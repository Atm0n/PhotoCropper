using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;

namespace PhotoCropper.Core.Tests.Extraction;

public class FaceOrientationServiceTests
{
    [Fact]
    public void IsModelAvailable_ShouldBeTrue_WhenEmbeddedModelExists()
    {
        // Act
        bool isAvailable = FaceOrientationService.IsModelAvailable;

        // Assert
        isAvailable.ShouldBeTrue();
    }

    [Fact]
    public void DetectFaceRotation_NullPhoto_ThrowsArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => FaceOrientationService.DetectFaceRotation(null!));
    }

    [Fact]
    public void DetectFaceRotation_EmptyPhoto_ReturnsMinusOne()
    {
        // Arrange
        using Mat photo = new();

        // Act
        int rotation = FaceOrientationService.DetectFaceRotation(photo);

        // Assert
        rotation.ShouldBe(-1);
    }

    [Fact]
    public void DetectFaceRotation_TooSmallPhoto_ReturnsMinusOne()
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
    public void DetectFaceRotation_NoFaceFound_ReturnsMinusOne()
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
    public void ScoreFaceData_EmptyMat_ReturnsZero()
    {
        // Arrange
        using Mat faces = new();

        // Act
        float score = FaceOrientationService.ScoreFaceData(faces, 0.45f);

        // Assert
        score.ShouldBe(0f);
    }

    [Fact]
    public void ScoreFaceData_MockUprightFace_ReturnsScore()
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
    public void ScoreFaceData_SidewaysFace_ReturnsZero()
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
}
