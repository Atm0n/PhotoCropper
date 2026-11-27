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
    public void DetectLandscapeRotationSkyAndVegetationShouldReturnCorrectRotation(int expectedRotation)
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
    public void DetectLandscapeRotationSkyAndWaterShouldReturnCorrectRotation(int expectedRotation)
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
    public void DetectLandscapeRotationAmbiguousSceneShouldReturnZero()
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
    public void DetectLandscapeRotationOvercastSkyAndGroundShouldDetectOrientation()
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
    public void DetectRequiredRotationThenOrientPhotoShouldLeaveThePhotoUpright(int turned)
    {
        using Mat photo = TestImageFactory.CreateSkyLandscapePhoto(200, 200, turned);
        AutoOrientationService.DetectRequiredRotation(photo).ShouldBe(turned);

        using Mat oriented = AutoOrientationService.OrientPhoto(photo);
        AutoOrientationService.DetectRequiredRotation(oriented).ShouldBe(0);
    }

    [Fact]
    public void DetectRequiredRotationShouldHandleBgraAndGreyscale()
    {
        // 4 channels: extracted photos have transparent margins.
        using Mat bgra = new(200, 200, MatType.CV_8UC4, new Scalar(200, 100, 50, 255));
        AutoOrientationService.DetectRequiredRotation(bgra).ShouldBe(0);

        using Mat gray = new(200, 200, MatType.CV_8UC1, new Scalar(128));
        AutoOrientationService.DetectRequiredRotation(gray).ShouldBe(0);
    }

    // Scores measured on real photos (for 0, 90, 180, 270 degrees). Upright photos whose scene features weakly or
    // narrowly suggested a turn used to be turned the wrong way.
    [Theory]
    [InlineData(-26.4, 19.5, 26.4, -19.5)] // faded colour print of a fair: weakly "upside down"
    [InlineData(-1.0, 20.7, 1.0, -20.7)]   // Paris street: weakly "sideways"
    [InlineData(56.6, -61.8, -56.6, 61.8)] // crowd at a fair: sideways barely beats upright
    [InlineData(0.0, 0.0, 0.0, 0.0)]       // indoors, no sky or vegetation
    public void ChooseLandscapeRotationWeakOrNarrowEvidenceShouldKeepThePhotoAsPlaced(double up, double cw, double flip, double ccw) =>
        AutoOrientationService.ChooseLandscapeRotation([up, cw, flip, ccw]).ShouldBe(0);

    [Theory]
    [InlineData(255.0, -66.2, -255.0, 66.2, 0)]  // valley under a sky, upright
    [InlineData(-10.7, 70.6, 10.7, -70.6, 90)]   // farm under a sky, lying on its side
    [InlineData(-246.1, -20.7, 246.1, 20.7, 180)] // church under a sky, upside down
    public void ChooseLandscapeRotationClearSkyShouldTurnItUpright(double up, double cw, double flip, double ccw, int expected) =>
        AutoOrientationService.ChooseLandscapeRotation([up, cw, flip, ccw]).ShouldBe(expected);

    [Theory]
    [InlineData("faded-fair.jpg")]
    [InlineData("fair-crowd.jpg")]
    [InlineData("paris-street.jpg")]
    public void DetectRequiredRotationUprightPhotoWithoutClearSkyOrFacesShouldKeepItUpright(string file)
    {
        using Mat photo = LoadPhoto(file);
        AutoOrientationService.DetectRequiredRotation(photo).ShouldBe(0);
    }

    [Theory]
    [InlineData("valley-landscape.jpg", 90)]
    [InlineData("valley-landscape.jpg", 180)]
    [InlineData("couple-portrait.jpg", 90)]
    [InlineData("couple-portrait.jpg", 270)]
    public void DetectRequiredRotationTurnedPhotoWithSkyOrFacesShouldTurnItBack(string file, int turned)
    {
        using Mat upright = LoadPhoto(file);
        using Mat photo = new();
        Cv2.Rotate(upright, photo, turned switch
        {
            90 => RotateFlags.Rotate90Clockwise,
            180 => RotateFlags.Rotate180,
            _ => RotateFlags.Rotate90Counterclockwise,
        });

        AutoOrientationService.DetectRequiredRotation(photo).ShouldBe((360 - turned) % 360);
    }

    /// <summary>Small copies of public-domain photos; see OrientationPhotos/CREDITS.md.</summary>
    private static Mat LoadPhoto(string file)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Extraction", "OrientationPhotos", file);
        var photo = Cv2.ImRead(path);
        photo.Empty().ShouldBeFalse(path);
        return photo;
    }
}
