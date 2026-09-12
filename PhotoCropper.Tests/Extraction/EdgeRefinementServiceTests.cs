using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Extraction;
using System.Drawing;

namespace PhotoCropper.Tests.Extraction;

public sealed class EdgeRefinementServiceTests
{
    [Fact]
    public void GetRefinedCropRect_ShouldTrimOuterWhiteMargins()
    {
        // 1000x1000 photo with 80px white margin around a dark center
        using Mat photo = new(1000, 1000, DepthType.Cv8U, 3);
        photo.SetTo(new MCvScalar(255, 255, 255)); // White border
        CvInvoke.Rectangle(photo, new Rectangle(80, 80, 840, 840), new MCvScalar(30, 30, 30), -1);

        Rectangle refined = EdgeRefinementService.GetRefinedCropRect(photo);

        Assert.True(refined.X >= 70);
        Assert.True(refined.Y >= 70);
        Assert.True(refined.Width <= 860);
        Assert.True(refined.Height <= 860);
    }
}
