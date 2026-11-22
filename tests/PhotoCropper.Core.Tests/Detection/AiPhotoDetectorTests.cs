using OpenCvSharp;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Tests.Extraction;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Detection;

/// <summary>
/// AI detection with a stand-in model ("bright pixels are photo", made by the trainer's test-model command): the
/// scan's preparation, the mask's boxes and the engine's use of them, and the fallback when no model is installed.
/// The model's location is shared, so these run with the other AI model tests, not alongside other tests.
/// </summary>
[Collection(nameof(AiModelSerialGroup))]
public sealed class AiPhotoDetectorTests : IDisposable
{
    private static readonly string TestModel = Path.Combine(AppContext.BaseDirectory, "Detection", "bright-is-photo.onnx");
    private readonly string _originalModelDir = AiModelStore.ModelDirectory;
    private readonly string _dir = TestImageFactory.CreateTempDirectory("AiPhotoDetectorTests");

    // Two light prints on a dark page: one straight, one tilted.
    private static readonly RotatedRect Straight = new(new Point2f(300, 280), new Size2f(480, 360), 0);
    private static readonly RotatedRect Tilted = new(new Point2f(880, 650), new Size2f(460, 340), 12);

    public AiPhotoDetectorTests()
    {
        // No installed model can leak in from this computer's profile.
        AiModelStore.ModelDirectory = Path.Combine(_dir, "models");
        AiPhotoDetector.ModelOverride = null;
    }

    public void Dispose()
    {
        AiPhotoDetector.ModelOverride = null;
        AiModelStore.ModelDirectory = _originalModelDir;
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private static Mat Scan()
    {
        var scan = new Mat(900, 1200, MatType.CV_8UC3, Scalar.All(25));
        foreach (var print in new[] { Straight, Tilted })
        {
            Cv2.FillConvexPoly(scan, [.. print.Points().Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))], Scalar.All(235));
        }
        return scan;
    }

    private static double Iou(RotatedRect a, RotatedBox b)
    {
        var other = new RotatedRect(new Point2f(b.Center.X, b.Center.Y), new Size2f(b.Size.Width, b.Size.Height), b.Angle);
        if (Cv2.RotatedRectangleIntersection(a, other, out var points) == RectanglesIntersectTypes.None || points.Length < 3) return 0;
        double intersection = Cv2.ContourArea(Cv2.ConvexHull(points));
        return intersection / ((a.Size.Width * a.Size.Height) + (other.Size.Width * other.Size.Height) - intersection);
    }

    [Fact]
    public void FindPhotos_ShouldBoxEachPrintTheModelMarks()
    {
        AiPhotoDetector.ModelOverride = TestModel;
        using var scan = Scan();

        var boxes = AiPhotoDetector.FindPhotos(scan);

        boxes.Count.ShouldBe(2);
        boxes.Max(b => Iou(Straight, b)).ShouldBeGreaterThan(0.9);
        boxes.Max(b => Iou(Tilted, b)).ShouldBeGreaterThan(0.9);
    }

    [Fact]
    public void FindPhotos_WithoutAModel_ShouldFindNothingAndSaySo()
    {
        using var scan = Scan();

        AiPhotoDetector.IsAvailable.ShouldBeFalse();
        AiPhotoDetector.FindPhotos(scan).ShouldBeEmpty();
    }

    [Fact]
    public void DetectPhotos_WithAiDetection_ShouldExtractThePhotosTheModelFinds()
    {
        AiPhotoDetector.ModelOverride = TestModel;
        string path = Path.Combine(_dir, "scan.png");
        using (var scan = Scan()) Cv2.ImWrite(path, scan);
        using var engine = new PhotoCropperEngine(path) { AutoOrientPhotos = false, UseAiDetection = true };

        engine.DetectPhotos();

        engine.UsesAiDetection.ShouldBeTrue();
        engine.DetectedPhotos.Count.ShouldBe(2);
        engine.AcceptedCandidates.Max(c => Iou(Tilted, c.Rotated)).ShouldBeGreaterThan(0.9);
        // The tilted print comes out straightened, about its own size.
        engine.DetectedPhotos.ShouldContain(p => (Math.Abs(p.Width - 460) < 30 && Math.Abs(p.Height - 340) < 30) || (Math.Abs(p.Width - 340) < 30 && Math.Abs(p.Height - 460) < 30));
    }

    [Fact]
    public void DetectPhotos_WithAiDetectionButNoModel_ShouldDetectAsBefore()
    {
        string path = Path.Combine(_dir, "scan.png");
        using (var scan = Scan()) Cv2.ImWrite(path, scan);
        using var classic = new PhotoCropperEngine(path) { AutoOrientPhotos = false };
        using var asked = new PhotoCropperEngine(path) { AutoOrientPhotos = false, UseAiDetection = true };

        classic.DetectPhotos();
        asked.DetectPhotos();

        asked.UsesAiDetection.ShouldBeFalse();
        asked.AcceptedCandidates.Select(c => c.Rotated).ShouldBe(classic.AcceptedCandidates.Select(c => c.Rotated));
    }

    [Fact]
    public void AutoTune_WithAiDetection_ShouldKeepTheSettingsAndDetect()
    {
        AiPhotoDetector.ModelOverride = TestModel;
        string path = Path.Combine(_dir, "scan.png");
        using (var scan = Scan()) Cv2.ImWrite(path, scan);
        using var engine = new PhotoCropperEngine(path) { AutoOrientPhotos = false, UseAiDetection = true };
        var before = engine.CurrentOptions;

        var result = engine.AutoTune();

        result.Improved.ShouldBeFalse();
        result.PhotoCount.ShouldBe(2);
        engine.CurrentOptions.ShouldBe(before);
        engine.DetectedPhotos.Count.ShouldBe(2);
    }
}
