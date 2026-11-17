using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
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
        var photo = new Mat(200, 200, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(120, 140, 160));
        return photo;
    }

    [Fact]
    public void DetectFaceRotation_RepeatedCalls_ShouldReuseOneDetector()
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
    public void DetectFaceRotation_ManyParallelCalls_ShouldKeepThePoolBounded()
    {
        FaceOrientationService.ReleaseCachedDetectors();
        using var photo = Photo();

        Parallel.For(0, FaceOrientationService.MaxIdleDetectors * 4, _ => FaceOrientationService.DetectFaceRotation(photo));

        // Bounded by the cap (plus a small race allowance), not by how many threads ever ran detection.
        FaceOrientationService.IdleDetectorCount.ShouldBeLessThanOrEqualTo(FaceOrientationService.MaxIdleDetectors + Environment.ProcessorCount);
        FaceOrientationService.IdleDetectorCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void ReleaseCachedDetectors_ShouldEmptyThePool()
    {
        using var photo = Photo();
        FaceOrientationService.DetectFaceRotation(photo);

        FaceOrientationService.ReleaseCachedDetectors();

        FaceOrientationService.IdleDetectorCount.ShouldBe(0);
    }
}
