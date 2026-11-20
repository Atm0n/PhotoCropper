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

    /// <summary>
    /// The picture inside a print's white (or cream) border, or null when the photo has no such border. All four
    /// corners must be light, little saturated and alike (a border, not a bright sky along one edge), and the border
    /// trimmed must be thin, so a photo without one is never cut into.
    /// </summary>
    public static Rect? GetWhiteBorderRect(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.Empty() || photo.Width < 40 || photo.Height < 40) return null;

        using Mat bgr = new();
        if (photo.Channels() == 1) Cv2.CvtColor(photo, bgr, ColorConversionCodes.GRAY2BGR);
        else if (photo.Channels() == 4) Cv2.CvtColor(photo, bgr, ColorConversionCodes.BGRA2BGR);
        else photo.CopyTo(bgr);
        using Mat hsv = new();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);

        int s = Math.Max(5, Math.Min(photo.Width, photo.Height) / 60);
        Rect[] corners =
        [
            new(0, 0, s, s), new(photo.Width - s, 0, s, s),
            new(0, photo.Height - s, s, s), new(photo.Width - s, photo.Height - s, s, s),
        ];
        var means = corners.Select(c =>
        {
            using Mat patch = new(hsv, c);
            return Cv2.Mean(patch);
        }).ToList();
        if (means.Any(m => m.Val2 < MinBorderBrightness || m.Val1 > MaxBorderSaturation)) return null;
        if (means.Max(m => m.Val2) - means.Min(m => m.Val2) > MaxBorderCornerSpread) return null;

        // Pixels of the border's colour: as light as the corners (give or take) and near-neutral.
        double borderValue = means.Average(m => m.Val2);
        using Mat border = new();
        Cv2.InRange(hsv, new Scalar(0, 0, borderValue - BorderValueTolerance), new Scalar(180, MaxBorderSaturation, 255), border);

        // Border pixels per row and per column (sums of a 0/255 mask).
        using Mat perRow = new();
        using Mat perColumn = new();
        Cv2.Reduce(border, perRow, ReduceDimension.Column, ReduceTypes.Sum, MatType.CV_32SC1.Depth);
        Cv2.Reduce(border, perColumn, ReduceDimension.Row, ReduceTypes.Sum, MatType.CV_32SC1.Depth);

        // From each edge inwards, the border goes on while most of the line is border-coloured; a slightly tilted
        // print still has most of each line in its border, and a white shirt in the picture doesn't fill a line.
        // The outermost lines can be a sliver of the bed and the print's own edge (a thin dark line, or its shadow),
        // so the border is the last run of border lines that starts within the outer few percent. It must end at a
        // clean edge (the picture starts within a few lines): a vignette or light sand fading out is no border.
        int Depth(bool rows, bool fromEnd)
        {
            int count = rows ? photo.Height : photo.Width;
            int length = rows ? photo.Width : photo.Height;
            double Share(int d)
            {
                int i = fromEnd ? count - 1 - d : d;
                return (rows ? perRow.Get<int>(i, 0) : perColumn.Get<int>(0, i)) / 255.0 / length;
            }
            bool IsBorder(int d) => Share(d) >= BorderLineShare;

            int limit = (int)(count * MaxBorderShare);
            int edge = Math.Min(limit, Math.Max(4, (int)(count * BorderEdgeShare)));
            int minRun = Math.Max(MinBorderPixels, (int)(count * MinBorderShare));
            int end = 0;
            for (int start = 0; start < edge; start++)
            {
                if (!IsBorder(start) || (start > 0 && IsBorder(start - 1))) continue; // not the start of a run
                int e = start;
                while (e < limit && IsBorder(e)) e++;
                if (e >= limit) return -1; // border-coloured beyond the limit: not a border
                if (e - start >= minRun) end = e;
            }
            if (end == 0) return 0; // no border on this side

            int step = Math.Max(MinBorderPixels, (int)(count * BorderStepShare));
            for (int d = end; d < Math.Min(count, end + step); d++)
            {
                if (Share(d) < BorderPictureShare) return end;
            }
            return -1; // fades out instead of ending at an edge
        }

        int top = Depth(rows: true, fromEnd: false), bottom = Depth(rows: true, fromEnd: true);
        int left = Depth(rows: false, fromEnd: false), right = Depth(rows: false, fromEnd: true);
        if (top < 0 || bottom < 0 || left < 0 || right < 0) return null;

        // A print's border goes all the way round, and three of its sides are alike: only one may be wider (a
        // Polaroid's, which can end up on any side once the photo is turned upright). Light content along an edge
        // (a white sheet, a pale wall) fails this: it doesn't frame the picture evenly on every side.
        int[] depths = [top, bottom, left, right];
        Array.Sort(depths);
        int minDepth = Math.Max(MinBorderPixels, (int)(Math.Min(photo.Width, photo.Height) * MinBorderShare));
        if (depths[0] < minDepth) return null;
        if (depths[2] > depths[0] * MaxBorderSideRatio + MinBorderPixels) return null;

        // A couple of pixels more, past the soft edge between border and picture.
        top += BorderEdgeMargin; bottom += BorderEdgeMargin; left += BorderEdgeMargin; right += BorderEdgeMargin;
        return new Rect(left, top, photo.Width - left - right, photo.Height - top - bottom);
    }

    // A print's border: light (HSV value), near-neutral, the same at every corner, and at most a quarter of each
    // side (a Polaroid's wide bottom edge included).
    private const double MinBorderBrightness = 170;
    private const double MaxBorderSaturation = 60;
    private const double MaxBorderCornerSpread = 35;
    private const double MaxBorderShare = 0.2;
    private const double BorderValueTolerance = 30;
    private const double BorderLineShare = 0.8;
    private const int MinBorderPixels = 3;
    private const double MinBorderShare = 0.005;
    private const double MaxBorderSideRatio = 2.5;
    private const int BorderEdgeMargin = 2;
    private const double BorderEdgeShare = 0.04;
    private const double BorderStepShare = 0.015;
    private const double BorderPictureShare = 0.5;

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
