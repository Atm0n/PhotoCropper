using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;

namespace PhotoCropper.Core.Tests.Extraction;

public class FaceOrientationServiceTests
{
    [Fact]
    public void IsModelAvailableShouldBeTrueWhenEmbeddedModelExists()
    {
        // Act
        bool isAvailable = FaceOrientationService.IsModelAvailable;

        // Assert
        isAvailable.ShouldBeTrue();
    }

    [Fact]
    public void DetectFaceRotationNullPhotoThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => FaceOrientationService.DetectFaceRotation(null!));
    }

    [Fact]
    public void DetectFaceRotationEmptyPhotoReturnsMinusOne()
    {
        // Arrange
        using Mat photo = new();

        // Act
        int rotation = FaceOrientationService.DetectFaceRotation(photo);

        // Assert
        rotation.ShouldBe(-1);
    }

    [Fact]
    public void DetectFaceRotationTooSmallPhotoReturnsMinusOne()
    {
        // Arrange
        using Mat photo = new Mat(10, 10, MatType.CV_8UC3);
        photo.SetTo(new Scalar(0, 0, 0));

        // Act
        int rotation = FaceOrientationService.DetectFaceRotation(photo);

        // Assert
        rotation.ShouldBe(-1);
    }

    [Fact]
    public void DetectFaceRotationNoFaceFoundReturnsMinusOne()
    {
        // Arrange - create a blank black image large enough to process
        using Mat photo = new Mat(200, 200, MatType.CV_8UC3);
        photo.SetTo(new Scalar(0, 0, 0));

        // Act
        int rotation = FaceOrientationService.DetectFaceRotation(photo);

        // Assert
        rotation.ShouldBe(-1); // No face -> returns -1
    }

    [Fact]
    public void ScoreFaceDataEmptyMatReturnsZero()
    {
        // Arrange
        using Mat faces = new();

        // Act
        float score = FaceOrientationService.ScoreFaceData(faces, 0.45f);

        // Assert
        score.ShouldBe(0f);
    }

    [Fact]
    public void ScoreFaceDataMockUprightFaceReturnsScore()
    {
        // Arrange
        // Create a 1D array to simulate YuNet output for 1 face (15 columns)
        float[] mockData = new float[15];
        mockData[14] = 0.9f; // Confidence score
        mockData[2] = 100f;  // Width
        mockData[3] = 100f;  // Height

        // Right eye
        mockData[4] = 30f;
        mockData[5] = 40f;
        // Left eye
        mockData[6] = 70f;
        mockData[7] = 40f;

        // Nose
        mockData[8] = 50f;
        mockData[9] = 60f;

        // Mouth right
        mockData[10] = 35f;
        mockData[11] = 80f;
        // Mouth left
        mockData[12] = 65f;
        mockData[13] = 80f;

        using Mat facesMat = new(1, 15, MatType.CV_32FC1);
        facesMat.SetData(mockData);

        // Act
        float score = FaceOrientationService.ScoreFaceData(facesMat, 0.45f);

        // Assert
        score.ShouldBe(0.9f);
    }

    [Fact]
    public void ScoreFaceDataSidewaysFaceReturnsZero()
    {
        // Arrange
        float[] mockData = new float[15];
        mockData[14] = 0.9f; // Confidence score
        mockData[2] = 100f;  // Width
        mockData[3] = 100f;  // Height

        // Face turned 90 degrees (eyes stacked vertically)
        // Right eye
        mockData[4] = 40f;
        mockData[5] = 30f;
        // Left eye
        mockData[6] = 40f;
        mockData[7] = 70f;

        // Nose
        mockData[8] = 60f;
        mockData[9] = 50f;

        // Mouth right
        mockData[10] = 80f;
        mockData[11] = 35f;
        // Mouth left
        mockData[12] = 80f;
        mockData[13] = 65f;

        using Mat facesMat = new(1, 15, MatType.CV_32FC1);
        facesMat.SetData(mockData);

        // Act
        float score = FaceOrientationService.ScoreFaceData(facesMat, 0.45f);

        // Assert
        score.ShouldBe(0f); // Because eyes are stacked vertically, fails geometric checks
    }

    // Face scores for the photo turned by 0, 90, 180 and 270 degrees; 0.45 is the detection threshold.
    [Theory]
    [InlineData(0f, 0f, 0.47f, 0f, -1)]    // one weak "face" in a crowd, only upside down: not trusted
    [InlineData(0f, 0f, 0f, 0f, -1)]       // no faces
    [InlineData(0.47f, 0f, 0f, 0f, 0)]     // a weak face upright keeps the photo as placed
    [InlineData(0f, 0.77f, 0f, 0f, 90)]    // a real face, lying on its side
    [InlineData(0.80f, 0.64f, 0f, 0f, 0)]  // upright wins when both look like faces
    [InlineData(0.51f, 0.49f, 2.19f, 0.73f, 180)] // a family, upside down
    public void ChooseFaceRotationShouldTurnOnlyForConfidentFaces(float up, float cw, float flip, float ccw, int expected) =>
        FaceOrientationService.ChooseFaceRotation([up, cw, flip, ccw], 0.45f).ShouldBe(expected);
}
