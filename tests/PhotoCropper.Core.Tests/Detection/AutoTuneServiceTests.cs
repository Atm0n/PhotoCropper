using OpenCvSharp;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class AutoTuneServiceTests
{
    [Fact]
    public void TuneNullSourceShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => AutoTuneService.Tune(null!, new DetectionOptions()));
    }

    [Fact]
    public void TuneNullOptionsShouldThrowArgumentNullException()
    {
        using var mat = new Mat(100, 100, MatType.CV_8UC3);
        Should.Throw<ArgumentNullException>(() => AutoTuneService.Tune(mat, null!));
    }

    [Fact]
    public void TuneSyntheticScanShouldReturnResult()
    {
        using var scan = new Mat(400, 400, MatType.CV_8UC3);
        scan.SetTo(new Scalar(250, 250, 250)); // light background
        Cv2.Rectangle(scan, new Rect(50, 50, 120, 120), new Scalar(20, 20, 20), -1);
        Cv2.Rectangle(scan, new Rect(220, 220, 120, 120), new Scalar(30, 30, 30), -1);

        var options = new DetectionOptions
        {
            BackgroundTolerance = 30,
            MinAreaFactor = 0.01,
            MaxAreaFactor = 0.90
        };

        var result = AutoTuneService.Tune(scan, options);

        result.ShouldNotBeNull();
        result.BestOptions.ShouldNotBeNull();
        result.PhotoCount.ShouldBeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public void TuneWithExpectedBoundsShouldTargetSpecifiedRange()
    {
        using var scan = new Mat(400, 400, MatType.CV_8UC3);
        scan.SetTo(new Scalar(250, 250, 250));
        Cv2.Rectangle(scan, new Rect(50, 50, 120, 120), new Scalar(20, 20, 20), -1);
        Cv2.Rectangle(scan, new Rect(220, 220, 120, 120), new Scalar(30, 30, 30), -1);

        var options = new DetectionOptions
        {
            BackgroundTolerance = 30,
            MinAreaFactor = 0.01,
            MaxAreaFactor = 0.90
        };

        var result = AutoTuneService.Tune(scan, options, minExpected: 2, maxExpected: 2);

        result.ShouldNotBeNull();
        result.PhotoCount.ShouldBe(2);
    }

    [Fact]
    public void TuneBlankImageReturnsZeroCandidates()
    {
        // Edge case: all passes return 0 candidates
        using var scan = new Mat(400, 400, MatType.CV_8UC3);
        scan.SetTo(new Scalar(255, 255, 255)); // completely uniform background
        var options = new DetectionOptions();

        var result = AutoTuneService.Tune(scan, options, minExpected: 1, maxExpected: 2);

        result.ShouldNotBeNull();
        result.PhotoCount.ShouldBe(0);
        result.Improved.ShouldBeFalse();
    }

    [Fact]
    public void CalculateScoreWithinExpectedBoundsRanksHigherThanOutsideBounds()
    {
        // Scoring: does a better result rank higher?
        using var scan = new Mat(400, 400, MatType.CV_8UC3);
        var options = new DetectionOptions();
        using var service = new AutoTuneService(scan, options, minExpected: 2, maxExpected: 2);

        var goodCandidates = new List<CropCandidate>
        {
            new() { Area = 100, Rotated = new RotatedBox(new System.Drawing.PointF(10, 10), new System.Drawing.SizeF(10, 10), 0) },
            new() { Area = 100, Rotated = new RotatedBox(new System.Drawing.PointF(30, 30), new System.Drawing.SizeF(10, 10), 0) }
        };

        var badCandidates = new List<CropCandidate>
        {
            new() { Area = 100, Rotated = new RotatedBox(new System.Drawing.PointF(10, 10), new System.Drawing.SizeF(10, 10), 0) }
        };

        // Initialize internal dimensions before calling CalculateScore
        typeof(AutoTuneService).GetMethod("InitializeDimensions", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.Invoke(service, null);

        double goodScore = service.CalculateScore(goodCandidates);
        double badScore = service.CalculateScore(badCandidates);

        goodScore.ShouldBeGreaterThan(badScore);
    }

    [Fact]
    public void SweepCoverageShouldDefineExpectedParameterCombinations()
    {
        // Sweep coverage: does it try all parameter combos?
        AutoTuneService.SweepTolerances.Length.ShouldBeGreaterThan(0);
        AutoTuneService.SweepCannyLows.Length.ShouldBeGreaterThan(0);
        AutoTuneService.SweepMinAreaFactors.Length.ShouldBeGreaterThan(0);

        int totalCombos = AutoTuneService.SweepTolerances.Length *
                          AutoTuneService.SweepCannyLows.Length *
                          AutoTuneService.SweepMinAreaFactors.Length;

        // Ensure we are doing an exhaustive sweep
        totalCombos.ShouldBe(5 * 4 * 4); // 80 combinations
    }
}
