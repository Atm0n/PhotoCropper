using OpenCvSharp;

namespace PhotoCropper.Core.Extraction;

public static class EdgeRefinementService
{
    public static Rect GetRefinedCropRect(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);

        if (photo.Empty()) return default;

        using Mat gray = new();
        if (photo.Channels() == 1)
        {
            photo.CopyTo(gray);
        }
        else if (photo.Channels() == 4)
        {
            Cv2.CvtColor(photo, gray, ColorConversionCodes.BGRA2GRAY);
        }
        else
        {
            Cv2.CvtColor(photo, gray, ColorConversionCodes.BGR2GRAY);
        }

        int s = 5;
        if (gray.Width <= s * 2 || gray.Height <= s * 2) return new Rect(0, 0, photo.Width, photo.Height);

        double bgGray = EstimateAverageBackgroundShade(gray);
        using Mat mask = BuildRefinementMask(gray, bgGray);

        Rect contentBox = Cv2.BoundingRect(mask);

        // Safety: Abort if content box is too small (<20% of photo area)
        if (contentBox.Width < photo.Width * 0.2 || contentBox.Height < photo.Height * 0.2)
        {
            return new Rect(0, 0, photo.Width, photo.Height);
        }

        // Shave 2 pixels to guarantee cutting inside the gradient edge of the margin
        contentBox.Inflate(-2, -2);

        int x = Math.Max(0, contentBox.X);
        int y = Math.Max(0, contentBox.Y);
        int w = Math.Max(10, contentBox.Width);
        int h = Math.Max(10, contentBox.Height);

        Rect finalRect = new(x, y, w, h);
        finalRect = finalRect.Intersect(new Rect(0, 0, photo.Width, photo.Height));

        return finalRect;
    }

    public static Mat ApplyCrop(Mat photo, Rect rect)
    {
        ArgumentNullException.ThrowIfNull(photo);

        rect = rect.Intersect(new Rect(0, 0, photo.Width, photo.Height));
        if (rect.Width <= 10 || rect.Height <= 10) return photo.Clone();

        using Mat subMat = new(photo, rect);
        return subMat.Clone();
    }

    private static double EstimateAverageBackgroundShade(Mat grayImage)
    {
        int s = 5;
        if (grayImage.Width <= s * 2 || grayImage.Height <= s * 2) return 255.0;

        using Mat mTl = new(grayImage, new Rect(0, 0, s, s));
        using Mat mTr = new(grayImage, new Rect(grayImage.Width - s, 0, s, s));
        using Mat mBl = new(grayImage, new Rect(0, grayImage.Height - s, s, s));
        using Mat mBr = new(grayImage, new Rect(grayImage.Width - s, grayImage.Height - s, s, s));

        var tl = Cv2.Mean(mTl).Val0;
        var tr = Cv2.Mean(mTr).Val0;
        var bl = Cv2.Mean(mBl).Val0;
        var br = Cv2.Mean(mBr).Val0;

        return (tl + tr + bl + br) / 4.0;
    }

    private static Mat BuildRefinementMask(Mat grayImage, double bgGray)
    {
        Mat mask = new();
        // Tolerance of 20 shades of grey to catch shadows/gradients in the scanner margin
        double lower = Math.Max(0, bgGray - 20);
        double upper = Math.Min(255, bgGray + 20);

        Cv2.InRange(grayImage, new Scalar(lower), new Scalar(upper), mask);

        // Invert: Photo is white (255), background margin is black (0)
        Cv2.BitwiseNot(mask, mask);

        // Remove scanner noise/dust from the black margin
        using Mat openKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(7, 7), new Point(-1, -1));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Open, openKernel, new Point(-1, -1), 1, BorderTypes.Default, new Scalar());

        // Solidify the photo area
        using Mat closeKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(21, 21), new Point(-1, -1));
        Cv2.MorphologyEx(mask, mask, MorphTypes.Close, closeKernel, new Point(-1, -1), 3, BorderTypes.Default, new Scalar());

        return mask;
    }
}
