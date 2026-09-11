using PhotoCropper;
using PhotoCropper.Export;
using PhotoCropper.Models;
using PhotoCropperCli.Models;
using System.Diagnostics;
using System.Globalization;

namespace PhotoCropperCli.Services;

internal static class BatchProcessor
{
    public static int Execute(CliOptions options, IReadOnlyList<string> scanFiles)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(scanFiles);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== PhotoCropper CLI - Unattended Batch Extractor ===");
        Console.ResetColor();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Found {0} scan(s) to process.", scanFiles.Count));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Output Format: {0} (Quality: {1})", options.Format, options.JpegQuality));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Detection: Tolerance={0}, MinSize={1:0}%, MaxSize={2:0}%", options.Tolerance, options.MinAreaFactor * 100, options.MaxAreaFactor * 100));
        if (options.AutoOrient)
        {
            Console.WriteLine("Auto-Orientation: Enabled [Experimental]");
        }
        Console.WriteLine();

        var detectionOptions = new DetectionOptions
        {
            BackgroundTolerance = options.Tolerance,
            MinAreaFactor = options.MinAreaFactor,
            MaxAreaFactor = options.MaxAreaFactor,
            CannyLowThreshold = options.CannyLow,
            CannyHighThreshold = options.CannyLow * 2.5,
            AutoOrientPhotos = options.AutoOrient
        };

        int totalExtracted = 0;
        int errorCount = 0;
        var totalStopwatch = Stopwatch.StartNew();

        for (int i = 0; i < scanFiles.Count; i++)
        {
            string scanPath = scanFiles[i];
            string fileName = Path.GetFileName(scanPath);
            Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Processing {2}... ", i + 1, scanFiles.Count, fileName));

            var fileStopwatch = Stopwatch.StartNew();
            try
            {
                using var engine = new PhotoCropperEngine(scanPath);
                engine.ApplyOptions(detectionOptions);
                engine.DetectPhotos();

                int photoCount = engine.DetectedPhotos.Count;
                if (photoCount > 0)
                {
                    PhotoExporter.SavePhotos(
                        engine.DetectedPhotos,
                        scanPath,
                        options.OutputDirectory,
                        options.Format,
                        options.JpegQuality);

                    totalExtracted += photoCount;
                    fileStopwatch.Stop();

                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write(string.Format(CultureInfo.InvariantCulture, "✓ {0} photo(s) extracted ", photoCount));
                    Console.ResetColor();
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "({0}ms)", fileStopwatch.ElapsedMilliseconds));

                    if (options.Verbose)
                    {
                        for (int p = 0; p < photoCount; p++)
                        {
                            var mat = engine.DetectedPhotos[p];
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "    -> Photo #{0}: {1}x{2} px", p + 1, mat.Width, mat.Height));
                        }
                    }
                }
                else
                {
                    fileStopwatch.Stop();
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "No photos detected ({0}ms)", fileStopwatch.ElapsedMilliseconds));
                    Console.ResetColor();
                }
            }
            catch (Exception ex)
            {
                fileStopwatch.Stop();
                errorCount++;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "FAILED: {0}", ex.Message));
                Console.ResetColor();
            }
        }

        totalStopwatch.Stop();
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Batch Complete ===");
        Console.ResetColor();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Scans Processed : {0}", scanFiles.Count));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Photos Extracted: {0}", totalExtracted));
        if (errorCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Errors          : {0}", errorCount));
            Console.ResetColor();
        }
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Total Time      : {0:F2}s", totalStopwatch.Elapsed.TotalSeconds));

        return errorCount == 0 ? 0 : 1;
    }
}
