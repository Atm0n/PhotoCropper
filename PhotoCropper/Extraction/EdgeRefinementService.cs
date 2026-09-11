using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using System.Drawing;

namespace PhotoCropper.Extraction;

public static class EdgeRefinementService
{
    public static Rectangle GetRefinedCropRect(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (photo.IsEmpty) return Rectangle.Empty;

        using Mat gray = new();
        CvInvoke.CvtColor(photo, gray, ColorConversion.Bgr2Gray);

        int s = 5;
        if (gray.Width <= s * 2 || gray.Height <= s * 2) return new Rectangle(0, 0, photo.Width, photo.Height);

        double bgGray = EstimateAverageBackgroundShade(gray);
        using Mat mask = BuildRefinementMask(gray, bgGray);

        Rectangle contentBox = CvInvoke.BoundingRectangle(mask);

        // Safety: Abort if content box is too small (<20% of photo area)
        if (contentBox.Width < photo.Width * 0.2 || contentBox.Height < photo.Height * 0.2)
        {
            return new Rectangle(0, 0, photo.Width, photo.Height);
        }

        // Shave 2 pixels to guarantee cutting inside the gradient edge of the margin
        contentBox.Inflate(-2, -2);

        int x = Math.Max(0, contentBox.X);
        int y = Math.Max(0, contentBox.Y);
        int w = Math.Max(10, contentBox.Width);
        int h = Math.Max(10, contentBox.Height);

        Rectangle finalRect = new(x, y, w, h);
        finalRect.Intersect(new Rectangle(Point.Empty, photo.Size));

        return finalRect;
    }

    public static Mat ApplyCrop(Mat photo, Rectangle rect)
    {
        ArgumentNullException.ThrowIfNull(photo);

        rect.Intersect(new Rectangle(Point.Empty, photo.Size));
        if (rect.Width <= 10 || rect.Height <= 10) return photo.Clone();

        using Mat subMat = new(photo, rect);
        return subMat.Clone();
    }

    private static double EstimateAverageBackgroundShade(Mat grayImage)
    {
        int s = 5;
        if (grayImage.Width <= s * 2 || grayImage.Height <= s * 2) return 255.0;

        using Mat mTl = new(grayImage, new Rectangle(0, 0, s, s));
        using Mat mTr = new(grayImage, new Rectangle(grayImage.Width - s, 0, s, s));
        using Mat mBl = new(grayImage, new Rectangle(0, grayImage.Height - s, s, s));
        using Mat mBr = new(grayImage, new Rectangle(grayImage.Width - s, grayImage.Height - s, s, s));

        var tl = CvInvoke.Mean(mTl).V0;
        var tr = CvInvoke.Mean(mTr).V0;
        var bl = CvInvoke.Mean(mBl).V0;
        var br = CvInvoke.Mean(mBr).V0;

        return (tl + tr + bl + br) / 4.0;
    }

    private static Mat BuildRefinementMask(Mat grayImage, double bgGray)
    {
        Mat mask = new();
        // Tolerance of 20 shades of grey to catch shadows/gradients in the scanner margin
        double lower = Math.Max(0, bgGray - 20);
        double upper = Math.Min(255, bgGray + 20);

        using ScalarArray lowerArray = new(lower);
        using ScalarArray upperArray = new(upper);
        CvInvoke.InRange(grayImage, lowerArray, upperArray, mask);

        // Invert: Photo is white (255), background margin is black (0)
        CvInvoke.BitwiseNot(mask, mask);

        // Remove scanner noise/dust from the black margin
        using Mat openKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(7, 7), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Open, openKernel, new Point(-1, -1), 1, BorderType.Default, new MCvScalar());

        // Solidify the photo area
        using Mat closeKernel = CvInvoke.GetStructuringElement(MorphShapes.Rectangle, new Size(21, 21), new Point(-1, -1));
        CvInvoke.MorphologyEx(mask, mask, MorphOp.Close, closeKernel, new Point(-1, -1), 3, BorderType.Default, new MCvScalar());

        return mask;
    }
}
