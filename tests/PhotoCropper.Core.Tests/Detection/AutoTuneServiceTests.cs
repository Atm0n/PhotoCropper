using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Detection;

public sealed class AutoTuneServiceTests
{
    [Fact]
    public void Tune_NullSource_ShouldThrowArgumentNullException()
    {
        Should.Throw<ArgumentNullException>(() => AutoTuneService.Tune(null!, new DetectionOptions()));
    }

    [Fact]
    public void Tune_NullOptions_ShouldThrowArgumentNullException()
    {
        using var mat = new Mat(100, 100, DepthType.Cv8U, 3);
        Should.Throw<ArgumentNullException>(() => AutoTuneService.Tune(mat, null!));
    }

    [Fact]
    public void Tune_SyntheticScan_ShouldReturnResult()
    {
        using var scan = new Mat(400, 400, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(250, 250, 250)); // light background
        CvInvoke.Rectangle(scan, new Rectangle(50, 50, 120, 120), new MCvScalar(20, 20, 20), -1);
        CvInvoke.Rectangle(scan, new Rectangle(220, 220, 120, 120), new MCvScalar(30, 30, 30), -1);

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
}
