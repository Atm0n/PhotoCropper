using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Extraction;
using PhotoCropper.TestHelpers;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Extraction;

public class AutoOrientationServiceTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DetectLandscapeRotation_SkyAndVegetation_ShouldReturnCorrectRotation(int expectedRotation)
    {
        // Act
        using Mat landscape = TestImageFactory.CreateSkyLandscapePhoto(orientation: expectedRotation);
        int rotation = AutoOrientationService.DetectLandscapeRotation(landscape);

        // Assert
        rotation.ShouldBe(expectedRotation);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    [InlineData(270)]
    public void DetectLandscapeRotation_SkyAndWater_ShouldReturnCorrectRotation(int expectedRotation)
    {
        // Arrange
        // BGR Blue Sky (same as TestImageFactory)
        MCvScalar skyColor = new MCvScalar(235, 180, 70);
        // BGR Deep Blue/Cyan Water: Cyan/Deep-Blue/Teal (Hue in [65, 135], Sat >= 30, Val in [25, 175])
        // Let's use a nice ocean blue/cyan. BGR(200, 150, 0)
        // Let's check HSV: B=200, G=150, R=0 -> Cyan/Blue. H~105, S=255, V=200.
        // Wait, V in [25, 175] for water mask! So V must be <= 175.
        // Let's use BGR(150, 100, 0).
        MCvScalar waterColor = new MCvScalar(150, 100, 0);

        using Mat mat = new Mat(200, 200, DepthType.Cv8U, 3);
        switch (expectedRotation)
        {
            case 180: // Upside down: sky at bottom, water at top
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, 200, 100), waterColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(0, 100, 200, 100), skyColor, -1);
                break;
            case 90: // Sideways: sky on left, water on right
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, 100, 200), skyColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(100, 0, 100, 200), waterColor, -1);
                break;
            case 270: // Sideways: sky on right, water on left
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, 100, 200), waterColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(100, 0, 100, 200), skyColor, -1);
                break;
            default: // Upright: sky at top, water at bottom
                CvInvoke.Rectangle(mat, new Rectangle(0, 0, 200, 100), skyColor, -1);
                CvInvoke.Rectangle(mat, new Rectangle(0, 100, 200, 100), waterColor, -1);
                break;
        }

        // Act
        int rotation = AutoOrientationService.DetectLandscapeRotation(mat);

        // Assert
        rotation.ShouldBe(expectedRotation);
    }

    [Fact]
    public void DetectLandscapeRotation_AmbiguousScene_ShouldReturnZero()
    {
        // Arrange
        using Mat mat = new Mat(200, 200, DepthType.Cv8U, 3);
        mat.SetTo(new MCvScalar(128, 128, 128)); // Solid gray image, no landscape features

        // Act
        int rotation = AutoOrientationService.DetectLandscapeRotation(mat);

        // Assert
        rotation.ShouldBe(0); // If no obvious landscape orientation is found, preserves natural 0 rotation
    }

    [Fact]
    public void DetectLandscapeRotation_OvercastSkyAndGround_ShouldDetectOrientation()
    {
        // Overcast / Cloudy White Sky: S <= 45, V >= 165
        // BGR(200, 200, 200) -> V=200, S=0 (Cloudy white)
        MCvScalar cloudColor = new MCvScalar(200, 200, 200);
        // Ground: BGR(40, 80, 40)
        MCvScalar groundColor = new MCvScalar(40, 80, 40);

        using Mat mat = new Mat(200, 200, DepthType.Cv8U, 3);
        // Upside down: cloud at bottom, ground at top -> Should return 180
        CvInvoke.Rectangle(mat, new Rectangle(0, 0, 200, 100), groundColor, -1);
        CvInvoke.Rectangle(mat, new Rectangle(0, 100, 200, 100), cloudColor, -1);

        // Act
        int rotation = AutoOrientationService.DetectLandscapeRotation(mat);

        // Assert
        rotation.ShouldBe(180);
    }
}
