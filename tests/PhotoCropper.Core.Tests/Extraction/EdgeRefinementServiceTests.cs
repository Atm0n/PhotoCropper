using OpenCvSharp;
using PhotoCropper.Core.Extraction;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class EdgeRefinementServiceTests
{
    [Fact]
    public void GetRefinedCropRectShouldTrimOuterWhiteMargins()
    {
        // 1000x1000 photo with 80px white margin around a dark center
        using Mat photo = new(1000, 1000, MatType.CV_8UC3);
        photo.SetTo(new Scalar(255, 255, 255)); // White border
        Cv2.Rectangle(photo, new Rect(80, 80, 840, 840), new Scalar(30, 30, 30), -1);

        Rect refined = EdgeRefinementService.GetRefinedCropRect(photo);

        // The dark centre is (80, 80) to (920, 920).
        refined.X.ShouldBeInRange(70, 90);
        refined.Y.ShouldBeInRange(70, 90);
        refined.Right.ShouldBeInRange(910, 930);
        refined.Bottom.ShouldBeInRange(910, 930);
    }
}
