using System.Security.Cryptography;

namespace PhotoCropper.Core.Extraction;

/// <summary>
/// Optional AI models that are too large to ship with the app. They are downloaded once, verified against a known
/// SHA-256 checksum (a corrupted or tampered download is rejected), and stored per user.
/// </summary>
public static class AiModelStore
{
    /// <summary>
    /// LaMa ("Resolution-robust Large Mask Inpainting with Fourier Convolutions", Suvorov et al., Samsung AI; Apache-2.0),
    /// ONNX export by Carve (huggingface.co/Carve/LaMa-ONNX), pinned to a specific revision.
    /// </summary>
    public const string LamaFileName = "lama_fp32.onnx";

    public const string LamaDownloadUrl =
        "https://huggingface.co/Carve/LaMa-ONNX/resolve/c3c0c9e468934d62e79c329e35d82dd09ff8c444/lama_fp32.onnx";

    public const string LamaSha256 = "1faef5301d78db7dda502fe59966957ec4b79dd64e16f03ed96913c7a4eb68d6";

    public const long LamaSizeBytes = 208_044_816;

    /// <summary>Where downloaded models are kept (per user). Can be changed, e.g. by tests.</summary>
    public static string ModelDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PhotoCropper", "models");

    public static string LamaModelPath => Path.Combine(ModelDirectory, LamaFileName);

    /// <summary>True when the LaMa model has been downloaded (and has the expected size).</summary>
    public static bool IsLamaInstalled
    {
        get
        {
            var file = new FileInfo(LamaModelPath);
            return file.Exists && file.Length == LamaSizeBytes;
        }
    }

    /// <summary>Downloads the LaMa model, reporting progress from 0 to 1, and verifies it before installing it.</summary>
    public static Task DownloadLamaAsync(HttpClient client, IProgress<double>? progress = null, CancellationToken cancellationToken = default) =>
        DownloadAsync(client, new Uri(LamaDownloadUrl), LamaModelPath, LamaSha256, LamaSizeBytes, progress, cancellationToken);

    /// <summary>
    /// Downloads <paramref name="url"/> to a temporary file next to <paramref name="destination"/>, checks its
    /// SHA-256, and only then moves it into place. On any failure nothing is installed.
    /// </summary>
    /// <exception cref="InvalidDataException">The download does not match the expected checksum.</exception>
    internal static async Task DownloadAsync(HttpClient client, Uri url, string destination, string expectedSha256,
        long expectedSize, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        string partial = destination + ".part";

        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? expectedSize;

            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            {
                byte[] buffer = new byte[1 << 16];
                long received = 0;
                double lastReported = -1;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hash.AppendData(buffer, 0, read);
                    received += read;
                    double fraction = total > 0 ? Math.Min(1.0, (double)received / total) : 0;
                    if (fraction - lastReported >= 0.005)
                    {
                        progress?.Report(fraction);
                        lastReported = fraction;
                    }
                }
            }

            string actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"The downloaded model is corrupted or not the expected file (SHA-256 {actual}).");
            }

            File.Move(partial, destination, overwrite: true);
            progress?.Report(1.0);
        }
        finally
        {
            if (File.Exists(partial)) File.Delete(partial);
        }
    }
}
