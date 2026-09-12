using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Core.Extraction;
using System.Drawing;

namespace PhotoCropper.Core.Tests.Extraction;

public sealed class PhotoExtractionEngineTests
{
    [Fact]
    public void RegularizeNearRightAngles_ShouldSnapNearStraightAnglesPreservingDimensions()
    {
        // 1. Horizontal rectangle near 0 deg: SizeF(600, 400), Angle=0.8
        RotatedRect hRect = new(new PointF(500, 500), new SizeF(600, 400), 0.8f);
        RotatedRect hSnapped = PhotoExtractionEngine.RegularizeNearRightAngles(hRect);
        hSnapped.Angle.ShouldBe(0f);
        Math.Abs(hSnapped.Size.Width - 600).ShouldBeLessThan(5);
        Math.Abs(hSnapped.Size.Height - 400).ShouldBeLessThan(5);

        // 2. Vertical rectangle near 0 deg: SizeF(400, 600), Angle=-0.9
        RotatedRect vRect = new(new PointF(500, 500), new SizeF(400, 600), -0.9f);
        RotatedRect vSnapped = PhotoExtractionEngine.RegularizeNearRightAngles(vRect);
        vSnapped.Angle.ShouldBe(0f);
        Math.Abs(vSnapped.Size.Width - 400).ShouldBeLessThan(5);
        Math.Abs(vSnapped.Size.Height - 600).ShouldBeLessThan(5);

        // 3. Vertical rectangle near 90 deg: SizeF(600, 400), Angle=89.2
        RotatedRect vRect90 = new(new PointF(500, 500), new SizeF(600, 400), 89.2f);
        RotatedRect vSnapped90 = PhotoExtractionEngine.RegularizeNearRightAngles(vRect90);
        vSnapped90.Angle.ShouldBe(0f);
        // At Angle=0, the horizontal dimension should be ~400 and vertical should be ~600
        Math.Abs(vSnapped90.Size.Width - 400).ShouldBeLessThan(5);
        Math.Abs(vSnapped90.Size.Height - 600).ShouldBeLessThan(5);

        // 4. Truly tilted rectangle (25 deg): unchanged
        RotatedRect tilted = new(new PointF(500, 500), new SizeF(600, 400), 25.0f);
        RotatedRect tiltedRes = PhotoExtractionEngine.RegularizeNearRightAngles(tilted);
        tiltedRes.Angle.ShouldBe(25.0f);
    }

    [Fact]
    public void ExtractPhotoFromContour_ShouldPreserveNaturalOrientation()
    {
        // 1. Vertical photo on scan: 300 wide, 500 high
        using Mat scanMat = new(800, 800, DepthType.Cv8U, 3);
        scanMat.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(scanMat, new Rectangle(100, 100, 300, 500), new MCvScalar(50, 50, 50), -1);

        Point[] vPoints = [
            new Point(100, 100),
            new Point(400, 100),
            new Point(400, 600),
            new Point(100, 600)
        ];
        using VectorOfPoint vPoly = new(vPoints);
        using Mat vExtracted = PhotoExtractionEngine.ExtractPhotoFromContour(vPoly, scanMat);

        // Vertical photo should stay vertical (height > width)
        vExtracted.IsEmpty.ShouldBeFalse();
        vExtracted.Height.ShouldBeGreaterThan(vExtracted.Width);
        Math.Abs(vExtracted.Width - 300).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(vExtracted.Height - 500).ShouldBeLessThanOrEqualTo(2);

        // 2. Horizontal photo on scan: 500 wide, 300 high
        CvInvoke.Rectangle(scanMat, new Rectangle(200, 200, 500, 300), new MCvScalar(80, 80, 80), -1);
        Point[] hPoints = [
            new Point(200, 200),
            new Point(700, 200),
            new Point(700, 500),
            new Point(200, 500)
        ];
        using VectorOfPoint hPoly = new(hPoints);
        using Mat hExtracted = PhotoExtractionEngine.ExtractPhotoFromContour(hPoly, scanMat);

        // Horizontal photo should stay horizontal (width > height)
        hExtracted.IsEmpty.ShouldBeFalse();
        hExtracted.Width.ShouldBeGreaterThan(hExtracted.Height);
        Math.Abs(hExtracted.Width - 500).ShouldBeLessThanOrEqualTo(2);
        Math.Abs(hExtracted.Height - 300).ShouldBeLessThanOrEqualTo(2);
    }
}
