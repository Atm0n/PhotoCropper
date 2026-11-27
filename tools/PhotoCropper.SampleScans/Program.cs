using System.Globalization;
using PhotoCropper.TestHelpers;

// Writes synthetic scans (see SampleScanFactory) to a folder, with expected.txt listing how many photos each holds.
//   PhotoCropper.SampleScans <output folder> [--count N] [--seed S]
// The same seed always writes the same scans, so a smoke test can check the exact number of photos found.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("Usage: PhotoCropper.SampleScans <output folder> [--count N (default 3)] [--seed S (default 1)]");
    return args.Length == 0 ? 1 : 0;
}

string output = args[0];
int count = 3, seed = 1;
for (int i = 1; i < args.Length; i++)
{
    if (args[i] == "--count" && i + 1 < args.Length) count = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else if (args[i] == "--seed" && i + 1 < args.Length) seed = int.Parse(args[++i], CultureInfo.InvariantCulture);
    else
    {
        await Console.Error.WriteLineAsync($"Unknown option: {args[i]}");
        return 1;
    }
}

Directory.CreateDirectory(output);
var lines = new List<string>();
int total = 0;
for (int i = 0; i < count; i++)
{
    string path = Path.Combine(output, $"scan_{i + 1:0000}.png");
    var scan = SampleScanFactory.Create(path, seed + i);
    lines.Add($"{Path.GetFileName(path)} {scan.PhotoCount}");
    total += scan.PhotoCount;
}
lines.Add($"total {total}");
await File.WriteAllLinesAsync(Path.Combine(output, "expected.txt"), lines);

await Console.Out.WriteLineAsync($"Wrote {count} scans with {total} photos to {Path.GetFullPath(output)}");
return 0;
