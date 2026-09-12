using PhotoCropper.Cli.Models;
using PhotoCropper.Core;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace PhotoCropper.Cli.Services;

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
        if (options.AutoTune)
        {
            Console.WriteLine("Auto-Tune: Enabled (Automatic Parameter Sweeping for Difficult Scans)");
        }
        if (options.CopyUndetectedDirectory != null)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Undetected Isolation Directory: '{0}'", options.CopyUndetectedDirectory));
        }
        if (options.AutoOrient)
        {
            Console.WriteLine("Auto-Orientation: Enabled (AI Face + Landscape)");
        }
        if (options.RestoreColors)
        {
            Console.WriteLine("Color-Restoration: Enabled (Auto-White Balance + LAB CLAHE + Vibrancy)");
        }
        if (options.RemoveDust)
        {
            Console.WriteLine("Dust-Inpainting: Enabled (Morphological Scratch & Dust Inpainting)");
        }
        Console.WriteLine();

        var detectionOptions = new DetectionOptions
        {
            BackgroundTolerance = options.Tolerance,
            MinAreaFactor = options.MinAreaFactor,
            MaxAreaFactor = options.MaxAreaFactor,
            CannyLowThreshold = options.CannyLow,
            CannyHighThreshold = options.CannyLow * 2.5,
            AutoOrientPhotos = options.AutoOrient,
            RestoreVintageColors = options.RestoreColors,
            RemoveDustAndScratches = options.RemoveDust
        };

        int totalExtracted = 0;
        int errorCount = 0;
        int completedCount = 0;
        var undetectedScans = new ConcurrentBag<string>();
        var totalStopwatch = Stopwatch.StartNew();
        var consoleLock = new object();

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Max(1, options.Threads)
        };

        Parallel.ForEach(scanFiles, parallelOptions, (scanPath) =>
        {
            string fileName = Path.GetFileName(scanPath);
            var fileStopwatch = Stopwatch.StartNew();

            try
            {
                using var engine = new PhotoCropperEngine(scanPath);
                engine.ApplyOptions(detectionOptions);
                engine.DetectPhotos();

                int photoCount = engine.DetectedPhotos.Count;
                bool wasAutoTuned = false;

                if (photoCount == 0 && options.AutoTune)
                {
                    var tuneResult = engine.AutoTune();
                    if (tuneResult.PhotoCount > 0)
                    {
                        photoCount = tuneResult.PhotoCount;
                        wasAutoTuned = true;
                    }
                }

                if (photoCount > 0)
                {
                    PhotoExporter.SavePhotos(
                        engine.DetectedPhotos,
                        scanPath,
                        options.OutputDirectory,
                        options.Format,
                        options.JpegQuality);

                    Interlocked.Add(ref totalExtracted, photoCount);
                    fileStopwatch.Stop();

                    int currentDone = Interlocked.Increment(ref completedCount);

                    lock (consoleLock)
                    {
                        Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Processing {2}... ", currentDone, scanFiles.Count, fileName));
                        Console.ForegroundColor = ConsoleColor.Green;
                        if (wasAutoTuned)
                        {
                            Console.Write(string.Format(CultureInfo.InvariantCulture, "⚡ {0} photo(s) auto-tuned & extracted ", photoCount));
                        }
                        else
                        {
                            Console.Write(string.Format(CultureInfo.InvariantCulture, "✓ {0} photo(s) extracted ", photoCount));
                        }
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
                }
                else
                {
                    undetectedScans.Add(scanPath);
                    fileStopwatch.Stop();
                    int currentDone = Interlocked.Increment(ref completedCount);

                    lock (consoleLock)
                    {
                        Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Processing {2}... ", currentDone, scanFiles.Count, fileName));
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "No photos detected ({0}ms)", fileStopwatch.ElapsedMilliseconds));
                        Console.ResetColor();
                    }
                }
            }
            catch (Exception ex)
            {
                fileStopwatch.Stop();
                Interlocked.Increment(ref errorCount);
                int currentDone = Interlocked.Increment(ref completedCount);

                lock (consoleLock)
                {
                    Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Processing {2}... ", currentDone, scanFiles.Count, fileName));
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "FAILED: {0}", ex.Message));
                    Console.ResetColor();
                }
            }
        });

        totalStopwatch.Stop();

        // Process undetected audit log & isolation
        if (!undetectedScans.IsEmpty)
        {
            WriteUndetectedAuditLog(undetectedScans, options, scanFiles.Count, scanFiles);
        }

        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=== Batch Complete ===");
        Console.ResetColor();
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Scans Processed : {0}", scanFiles.Count));
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Photos Extracted: {0}", totalExtracted));
        if (!undetectedScans.IsEmpty)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Undetected Scans: {0} (Logged to undetected_scans.txt)", undetectedScans.Count));
            if (options.CopyUndetectedDirectory != null)
            {
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "  -> Isolated copy saved to '{0}'", options.CopyUndetectedDirectory));
            }
            Console.ResetColor();
        }
        if (errorCount > 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Errors          : {0}", errorCount));
            Console.ResetColor();
        }
        Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Total Time      : {0:F2}s", totalStopwatch.Elapsed.TotalSeconds));

        // Interactive Post-Batch Auto-Tune Review Prompt
        if (!options.AutoTune && !options.NonInteractive && !undetectedScans.IsEmpty && !Console.IsInputRedirected)
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.Write(string.Format(CultureInfo.InvariantCulture, "⚠ {0} scan(s) had 0 photos detected. Would you like to run Auto-Tune on them now? [y/N]: ", undetectedScans.Count));
            Console.ResetColor();

            string? response = Console.ReadLine()?.Trim();
            if (string.Equals(response, "y", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(response, "yes", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Running Auto-Tune Pass on Undetected Scans ===");
                Console.ResetColor();

                var remainingUndetected = new ConcurrentBag<string>();
                int autoTunedRecovered = 0;
                int autoTuneDone = 0;
                var unlist = undetectedScans.ToList();

                Parallel.ForEach(unlist, parallelOptions, (scanPath) =>
                {
                    string fileName = Path.GetFileName(scanPath);
                    var fileStopwatch = Stopwatch.StartNew();

                    try
                    {
                        using var engine = new PhotoCropperEngine(scanPath);
                        engine.ApplyOptions(detectionOptions);
                        var tuneResult = engine.AutoTune();

                        int photoCount = engine.DetectedPhotos.Count;
                        if (photoCount > 0)
                        {
                            PhotoExporter.SavePhotos(
                                engine.DetectedPhotos,
                                scanPath,
                                options.OutputDirectory,
                                options.Format,
                                options.JpegQuality);

                            Interlocked.Add(ref totalExtracted, photoCount);
                            Interlocked.Increment(ref autoTunedRecovered);
                            fileStopwatch.Stop();

                            int current = Interlocked.Increment(ref autoTuneDone);
                            lock (consoleLock)
                            {
                                Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Auto-tuning {2}... ", current, unlist.Count, fileName));
                                Console.ForegroundColor = ConsoleColor.Green;
                                Console.Write(string.Format(CultureInfo.InvariantCulture, "⚡ {0} photo(s) recovered! (Tolerance: {1:0}, Edge: {2:0}) ", photoCount, tuneResult.BestOptions.BackgroundTolerance, tuneResult.BestOptions.CannyLowThreshold));
                                Console.ResetColor();
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "({0}ms)", fileStopwatch.ElapsedMilliseconds));
                            }
                        }
                        else
                        {
                            remainingUndetected.Add(scanPath);
                            fileStopwatch.Stop();
                            int current = Interlocked.Increment(ref autoTuneDone);
                            lock (consoleLock)
                            {
                                Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Auto-tuning {2}... ", current, unlist.Count, fileName));
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Still 0 photos ({0}ms)", fileStopwatch.ElapsedMilliseconds));
                                Console.ResetColor();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        fileStopwatch.Stop();
                        remainingUndetected.Add(scanPath);
                        int current = Interlocked.Increment(ref autoTuneDone);
                        lock (consoleLock)
                        {
                            Console.Write(string.Format(CultureInfo.InvariantCulture, "[{0}/{1}] Auto-tuning {2}... ", current, unlist.Count, fileName));
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "FAILED: {0}", ex.Message));
                            Console.ResetColor();
                        }
                    }
                });

                WriteUndetectedAuditLog(remainingUndetected, options, scanFiles.Count, scanFiles);

                Console.WriteLine();
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("=== Auto-Tune Review Summary ===");
                Console.ResetColor();
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Scans Recovered   : {0}", autoTunedRecovered));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Remaining Scans   : {0}", remainingUndetected.Count));
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture, "Total Photos Saved: {0}", totalExtracted));
            }
        }

        return errorCount == 0 ? 0 : 1;
    }

    private static void WriteUndetectedAuditLog(
        IEnumerable<string> undetectedScans,
        CliOptions options,
        int totalScansCount,
        IReadOnlyList<string> scanFiles)
    {
        var sortedUndetected = undetectedScans.OrderBy(x => x).ToList();
        string auditDir = options.OutputDirectory ?? (scanFiles.Count > 0 ? (Path.GetDirectoryName(scanFiles[0]) ?? ".") : ".");
        if (!Directory.Exists(auditDir))
        {
            Directory.CreateDirectory(auditDir);
        }
        string auditFile = Path.Combine(auditDir, "undetected_scans.txt");

        if (sortedUndetected.Count == 0)
        {
            if (File.Exists(auditFile))
            {
                File.Delete(auditFile);
            }
            return;
        }

        var logLines = new List<string>
        {
            "=== PhotoCropper Undetected Scans Audit Log ===",
            string.Format(CultureInfo.InvariantCulture, "Timestamp: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now),
            string.Format(CultureInfo.InvariantCulture, "Total Scans: {0} | Undetected Scans: {1}", totalScansCount, sortedUndetected.Count),
            "",
            "Files requiring manual review or auto-tuning:"
        };

        for (int i = 0; i < sortedUndetected.Count; i++)
        {
            logLines.Add(string.Format(CultureInfo.InvariantCulture, "{0}. {1}", i + 1, sortedUndetected[i]));
        }

        File.WriteAllLines(auditFile, logLines);

        if (options.CopyUndetectedDirectory != null)
        {
            Directory.CreateDirectory(options.CopyUndetectedDirectory);
            foreach (var undetectedFile in sortedUndetected)
            {
                if (File.Exists(undetectedFile))
                {
                    string destPath = Path.Combine(options.CopyUndetectedDirectory, Path.GetFileName(undetectedFile));
                    File.Copy(undetectedFile, destPath, overwrite: true);
                }
            }
        }
    }
}
