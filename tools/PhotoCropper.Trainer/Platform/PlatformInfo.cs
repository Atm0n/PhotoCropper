using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace PhotoCropper.Trainer.Platform;

/// <summary>An NVIDIA card as nvidia-smi reports it.</summary>
/// <param name="DriverVersion">e.g. "572.16".</param>
/// <param name="MemoryMiB">The card's memory.</param>
internal sealed record NvidiaGpu(string Name, string DriverVersion, int MemoryMiB)
{
    /// <summary>The oldest driver that runs CUDA 12.8, which the trainer's GPU build uses.</summary>
    public const int MinimumDriverMajor = 570;

    public int DriverMajor => int.TryParse(DriverVersion.Split('.')[0], CultureInfo.InvariantCulture, out int major) ? major : 0;

    public bool DriverIsRecentEnough => DriverMajor >= MinimumDriverMajor;
}

/// <summary>What this computer has, as far as training is concerned.</summary>
internal sealed record PlatformInfo(
    string OperatingSystem,
    string Architecture,
    string CpuName,
    int LogicalCores,
    long MemoryBytes,
    IReadOnlyList<string> GraphicsAdapters,
    NvidiaGpu? Nvidia)
{
    public static PlatformInfo Detect() => new(
        RuntimeInformation.OSDescription,
        RuntimeInformation.OSArchitecture.ToString(),
        DetectCpuName(),
        Environment.ProcessorCount,
        GC.GetGCMemoryInfo().TotalAvailableMemoryBytes,
        DetectGraphicsAdapters(),
        QueryNvidia());

    private static string DetectCpuName()
    {
        if (System.OperatingSystem.IsWindows())
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("ProcessorNameString") is string name) return name.Trim();
        }
        else if (File.Exists("/proc/cpuinfo"))
        {
            string? line = File.ReadLines("/proc/cpuinfo").FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal));
            if (line != null) return line[(line.IndexOf(':', StringComparison.Ordinal) + 1)..].Trim();
        }
        return "unknown processor";
    }

    /// <summary>The display adapters Windows knows, integrated ones included.</summary>
    private static List<string> DetectGraphicsAdapters()
    {
        var names = new List<string>();
        if (!System.OperatingSystem.IsWindows()) return names;

        // The display adapter device class.
        using var adapters = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
        if (adapters == null) return names;
        foreach (string sub in adapters.GetSubKeyNames().Where(n => n.All(char.IsAsciiDigit)))
        {
            using var adapter = adapters.OpenSubKey(sub);
            if (adapter?.GetValue("DriverDesc") is string description && !names.Contains(description, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(description.Trim());
            }
        }
        return names;
    }

    /// <summary>The first NVIDIA card, via nvidia-smi (installed with the NVIDIA driver); null when there is none.</summary>
    private static NvidiaGpu? QueryNvidia()
    {
        try
        {
            var start = new ProcessStartInfo("nvidia-smi", "--query-gpu=name,driver_version,memory.total --format=csv,noheader,nounits")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(start);
            if (process == null) return null;
            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(10_000) || process.ExitCode != 0) return null;

            string? first = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
            if (first == null) return null;
            string[] fields = first.Split(',', StringSplitOptions.TrimEntries);
            if (fields.Length < 3) return null;
            int memory = int.TryParse(fields[2], CultureInfo.InvariantCulture, out int mib) ? mib : 0;
            return new NvidiaGpu(fields[0], fields[1], memory);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return null; // nvidia-smi isn't installed: no NVIDIA driver.
        }
    }
}
