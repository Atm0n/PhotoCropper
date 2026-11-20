using OpenCvSharp;
using PhotoCropper.Core.Extraction;
using PhotoCropper.TestHelpers;

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
        Scalar skyColor = new Scalar(235, 180, 70);
        // BGR Deep Blue/Cyan Water: Cyan/Deep-Blue/Teal (Hue in [65, 135], Sat >= 30, Val in [25, 175])
        // Let's use a nice ocean blue/cyan. BGR(200, 150, 0)
        // Let's check HSV: B=200, G=150, R=0 -> Cyan/Blue. H~105, S=255, V=200.
        // Wait, V in [25, 175] for water mask! So V must be <= 175.
        // Let's use BGR(150, 100, 0).
        Scalar waterColor = new Scalar(150, 100, 0);

        using Mat mat = new Mat(200, 200, MatType.CV_8UC3);
        switch (expectedRotation)
        {
            case 180: // Upside down: sky at bottom, water at top
                Cv2.Rectangle(mat, new Rect(0, 0, 200, 100), waterColor, -1);
                Cv2.Rectangle(mat, new Rect(0, 100, 200, 100), skyColor, -1);
                break;
            case 90: // Sideways: sky on left, water on right
                Cv2.Rectangle(mat, new Rect(0, 0, 100, 200), skyColor, -1);
                Cv2.Rectangle(mat, new Rect(100, 0, 100, 200), waterColor, -1);
                break;
            case 270: // Sideways: sky on right, water on left
                Cv2.Rectangle(mat, new Rect(0, 0, 100, 200), waterColor, -1);
                Cv2.Rectangle(mat, new Rect(100, 0, 100, 200), skyColor, -1);
                break;
            default: // Upright: sky at top, water at bottom
                Cv2.Rectangle(mat, new Rect(0, 0, 200, 100), skyColor, -1);
                Cv2.Rectangle(mat, new Rect(0, 100, 200, 100), waterColor, -1);
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
        using Mat mat = new Mat(200, 200, MatType.CV_8UC3);
        mat.SetTo(new Scalar(128, 128, 128)); // Solid gray image, no landscape features

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
        Scalar cloudColor = new Scalar(200, 200, 200);
        // Ground: BGR(40, 80, 40)
        Scalar groundColor = new Scalar(40, 80, 40);

        using Mat mat = new Mat(200, 200, MatType.CV_8UC3);
        // Upside down: cloud at bottom, ground at top -> Should return 180
        Cv2.Rectangle(mat, new Rect(0, 0, 200, 100), groundColor, -1);
        Cv2.Rectangle(mat, new Rect(0, 100, 200, 100), cloudColor, -1);

        // Act
        int rotation = AutoOrientationService.DetectLandscapeRotation(mat);

        // Assert
        rotation.ShouldBe(180);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    [InlineData(180)]
    public void DetectRequiredRotation_ThenOrientPhoto_ShouldLeaveThePhotoUpright(int turned)
    {
        using Mat photo = TestImageFactory.CreateSkyLandscapePhoto(200, 200, turned);
        AutoOrientationService.DetectRequiredRotation(photo).ShouldBe(turned);

        using Mat oriented = AutoOrientationService.OrientPhoto(photo);
        AutoOrientationService.DetectRequiredRotation(oriented).ShouldBe(0);
    }

    [Fact]
    public void DetectRequiredRotation_ShouldHandleBgraAndGreyscale()
    {
        // 4 channels: extracted photos have transparent margins.
        using Mat bgra = new(200, 200, MatType.CV_8UC4, new Scalar(200, 100, 50, 255));
        AutoOrientationService.DetectRequiredRotation(bgra).ShouldBe(0);

        using Mat gray = new(200, 200, MatType.CV_8UC1, new Scalar(128));
        AutoOrientationService.DetectRequiredRotation(gray).ShouldBe(0);
    }
}
