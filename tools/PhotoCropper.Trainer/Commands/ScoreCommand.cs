using System.Text.Json;
using OpenCvSharp;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;
using Spectre.Console;

namespace PhotoCropper.Trainer.Commands;

/// <summary>Options for <c>score</c>.</summary>
/// <param name="Key">A project session (session.json, or a saved answer key) whose scans have the correct boxes.</param>
/// <param name="Overlays">When set, each scan is drawn there with the correct boxes (green) and the model's (red).</param>
/// <param name="Boxes">When set, the model's boxes for every scan are written there (JSON), for further analysis.</param>
internal sealed record ScoreOptions(string Model, string Scans, string Key, string? Overlays, string? Boxes = null);

/// <summary>
/// Scores a model on real scans against their correct boxes, running it through the app's own detection
/// (<see cref="AiPhotoDetector"/>), so the score is what the app would do. Each correct box is paired with the model's box that
/// overlaps it most (each used once), and counts as found when they overlap by at least 90 % (IoU), the measure used
/// for PhotoCropper's own detection. Boxes of the model that match no photo are counted as extras.
/// </summary>
internal static class ScoreCommand
{
    private const double Found = 0.9, Close = 0.7, Matched = 0.5;

    public static int Run(IAnsiConsole console, ScoreOptions options)
    {
        var scans = ReadKey(options.Key);
        if (options.Overlays != null) Directory.CreateDirectory(options.Overlays);
        AiPhotoDetector.ModelOverride = Path.GetFullPath(options.Model);

        int total = 0, found = 0, close = 0, extras = 0, missing = 0;
        var worst = new List<(string Scan, int Found, int Photos, int Extras)>();
        var allBoxes = new Dictionary<string, List<float[]>>();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        console.Progress().AutoClear(true).Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn())
            .Start(context =>
            {
                var task = context.AddTask($"Scoring on {scans.Count} scans", maxValue: scans.Count);
                foreach (var (name, truth) in scans)
                {
                    string path = Path.Combine(options.Scans, name);
                    if (!File.Exists(path))
                    {
                        missing++;
                        task.Increment(1);
                        continue;
                    }
                    using Mat scan = Cv2.ImRead(path, ImreadModes.Color);
                    var predicted = AiPhotoDetector.FindPhotos(scan).Select(ToCv).ToList();
                    allBoxes[name] = [.. predicted.Select(b => new[] { b.Center.X, b.Center.Y, b.Size.Width, b.Size.Height, b.Angle })];

                    var ious = Pair(truth, predicted, out int unmatched);
                    int scanFound = ious.Count(i => i >= Found);
                    total += truth.Count;
                    found += scanFound;
                    close += ious.Count(i => i >= Close);
                    extras += unmatched;
                    if (scanFound < truth.Count || unmatched > 0) worst.Add((name, scanFound, truth.Count, unmatched));

                    if (options.Overlays != null) DrawOverlay(scan, truth, predicted, Path.Combine(options.Overlays, Path.ChangeExtension(name, ".jpg")));
                    task.Increment(1);
                }
            });

        if (options.Boxes != null) File.WriteAllText(options.Boxes, JsonSerializer.Serialize(allBoxes));
        var table = new Table().Border(TableBorder.Rounded).AddColumn("Scans").AddColumn(new TableColumn("Result").RightAligned());
        table.AddRow("Photos found (overlap ≥ 90 %)", $"[bold]{found} of {total}[/] ({(double)found / Math.Max(1, total):P0})");
        table.AddRow("Photos roughly found (≥ 70 %)", $"{close} of {total}");
        table.AddRow("Extra boxes (not a photo)", extras.ToString(System.Globalization.CultureInfo.InvariantCulture));
        table.AddRow("Time per scan", $"{watch.Elapsed.TotalSeconds / Math.Max(1, scans.Count - missing):F2} s");
        console.Write(table);
        if (missing > 0) console.MarkupLine($"[yellow]{missing} scans of the key weren't in {Markup.Escape(options.Scans)}.[/]");

        if (worst.Count > 0)
        {
            console.MarkupLine("Scans with misses or extras:");
            foreach (var (scan, scanFound, photos, scanExtras) in worst.OrderBy(w => w.Found - w.Photos - w.Extras).Take(20))
            {
                console.MarkupLine($"  {Markup.Escape(scan)}: {scanFound} of {photos} found{(scanExtras > 0 ? $", {scanExtras} extra" : string.Empty)}");
            }
        }
        return 0;
    }

    private static RotatedRect ToCv(RotatedBox box) =>
        new(new Point2f(box.Center.X, box.Center.Y), new Size2f(box.Size.Width, box.Size.Height), box.Angle);

    /// <summary>The scans and their correct boxes, from a session or answer key (Scans[].OriginalFileName, FinalCrops).</summary>
    internal static List<(string Name, List<RotatedRect> Boxes)> ReadKey(string path)
    {
        using var json = JsonDocument.Parse(File.ReadAllText(path));
        var scans = new List<(string, List<RotatedRect>)>();
        foreach (var scan in json.RootElement.GetProperty("Scans").EnumerateArray())
        {
            if (!scan.TryGetProperty("FinalCrops", out var crops) || crops.ValueKind != JsonValueKind.Array) continue;
            string name = scan.GetProperty("OriginalFileName").GetString() ?? string.Empty;
            var boxes = crops.EnumerateArray().Select(c => new RotatedRect(
                new Point2f(c.GetProperty("CenterX").GetSingle(), c.GetProperty("CenterY").GetSingle()),
                new Size2f(c.GetProperty("Width").GetSingle(), c.GetProperty("Height").GetSingle()),
                c.GetProperty("Angle").GetSingle())).ToList();
            scans.Add((name, boxes));
        }
        return scans;
    }

    /// <summary>For each correct box, the overlap with the model's box paired with it (0 when none); best pairs first.</summary>
    internal static List<double> Pair(List<RotatedRect> truth, List<RotatedRect> predicted, out int unmatched)
    {
        var pairs = new List<(int Truth, int Predicted, double Iou)>();
        for (int t = 0; t < truth.Count; t++)
        {
            for (int p = 0; p < predicted.Count; p++)
            {
                double iou = Iou(truth[t], predicted[p]);
                if (iou > 0) pairs.Add((t, p, iou));
            }
        }
        var result = new double[truth.Count];
        var pairedIou = new double[predicted.Count];
        var usedTruth = new HashSet<int>();
        var usedPredicted = new HashSet<int>();
        foreach (var (t, p, iou) in pairs.OrderByDescending(x => x.Iou))
        {
            if (usedTruth.Contains(t) || usedPredicted.Contains(p)) continue;
            usedTruth.Add(t);
            usedPredicted.Add(p);
            result[t] = iou;
            pairedIou[p] = iou;
        }
        // An extra: a box not paired with a photo it overlaps by at least half.
        unmatched = pairedIou.Count(iou => iou < Matched);
        return [.. result];
    }

    internal static double Iou(RotatedRect a, RotatedRect b)
    {
        if (Cv2.RotatedRectangleIntersection(a, b, out var points) == RectanglesIntersectTypes.None || points.Length < 3) return 0;
        double intersection = Cv2.ContourArea(Cv2.ConvexHull(points));
        double union = ((double)a.Size.Width * a.Size.Height) + ((double)b.Size.Width * b.Size.Height) - intersection;
        return union > 0 ? intersection / union : 0;
    }

    private static void DrawOverlay(Mat scan, List<RotatedRect> truth, List<RotatedRect> predicted, string path)
    {
        double scale = 1200.0 / Math.Max(scan.Width, scan.Height);
        using Mat shown = new();
        Cv2.Resize(scan, shown, new OpenCvSharp.Size(scan.Width * scale, scan.Height * scale), 0, 0, InterpolationFlags.Area);
        foreach (var (boxes, colour) in new[] { (truth, new Scalar(0, 200, 0)), (predicted, new Scalar(0, 0, 255)) })
        {
            foreach (var box in boxes)
            {
                Point[] corners = [.. box.Points().Select(p => new Point(p.X * scale, p.Y * scale))];
                Cv2.Polylines(shown, [corners], true, colour, 2, LineTypes.AntiAlias);
            }
        }
        Cv2.ImWrite(path, shown);
    }
}
