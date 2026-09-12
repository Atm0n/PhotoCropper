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
    public void Tune_ShouldFindDifficultContrastPhoto()
    {
        using Mat scan = new(1000, 1000, DepthType.Cv8U, 3);
        scan.SetTo(new MCvScalar(240, 240, 240)); // Slightly off-white scanner background
        // Subtle low-contrast photo
        CvInvoke.Rectangle(scan, new Rectangle(150, 150, 400, 400), new MCvScalar(232, 230, 228), -1);

        var restrictiveOptions = new DetectionOptions
        {
            BackgroundTolerance = 5,
            CannyLowThreshold = 50
        };

        var result = AutoTuneService.Tune(scan, restrictiveOptions);

        result.ShouldNotBeNull();
        result.BestOptions.ShouldNotBeNull();
        result.PhotoCount.ShouldBeGreaterThanOrEqualTo(1);
        result.Score.ShouldBeGreaterThan(0);
    }
}
