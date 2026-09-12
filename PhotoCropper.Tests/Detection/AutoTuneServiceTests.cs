using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Detection;
using PhotoCropper.Models;
using System.Drawing;

namespace PhotoCropper.Tests.Detection;

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

        Assert.NotNull(result);
        Assert.NotNull(result.BestOptions);
        Assert.True(result.PhotoCount >= 1);
        Assert.True(result.Score > 0);
    }
}
