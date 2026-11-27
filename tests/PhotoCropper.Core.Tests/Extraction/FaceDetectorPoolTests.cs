using OpenCvSharp;
using PhotoCropper.Core.Extraction;

namespace PhotoCropper.Core.Tests.Extraction;

[CollectionDefinition(nameof(FaceDetectorPoolSerialGroup), DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xUnit collection definitions must be public")]
public sealed class FaceDetectorPoolSerialGroup;

/// <summary>
/// The detector pool is process-wide, so these tests run on their own, not in parallel with other tests.
/// </summary>
[Collection(nameof(FaceDetectorPoolSerialGroup))]
public sealed class FaceDetectorPoolTests
{
    private static Mat Photo()
    {
        var photo = new Mat(200, 200, MatType.CV_8UC3);
        photo.SetTo(new Scalar(120, 140, 160));
        return photo;
    }

    [Fact]
    public void DetectFaceRotationRepeatedCallsShouldReuseOneDetector()
    {
        FaceOrientationService.ReleaseCachedDetectors();
        using var photo = Photo();

        for (int i = 0; i < 5; i++)
        {
            FaceOrientationService.DetectFaceRotation(photo);
        }

        FaceOrientationService.IdleDetectorCount.ShouldBe(1);
    }

    [Fact]
    public void DetectFaceRotationManyParallelCallsShouldKeepThePoolBounded()
    {
        FaceOrientationService.ReleaseCachedDetectors();
        using var photo = Photo();

        Parallel.For(0, FaceOrientationService.MaxIdleDetectors * 4, _ => FaceOrientationService.DetectFaceRotation(photo));

        // Bounded by the cap (plus a small race allowance), not by how many threads ever ran detection.
        FaceOrientationService.IdleDetectorCount.ShouldBeLessThanOrEqualTo(FaceOrientationService.MaxIdleDetectors + Environment.ProcessorCount);
        FaceOrientationService.IdleDetectorCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ReleaseCachedDetectorsShouldEmptyThePool()
    {
        using var photo = Photo();
        FaceOrientationService.DetectFaceRotation(photo);

        FaceOrientationService.ReleaseCachedDetectors();

        FaceOrientationService.IdleDetectorCount.ShouldBe(0);
    }
}
