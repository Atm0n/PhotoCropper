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
    /// The picture inside a print's white (or cream) border, or null when the photo has no such border. The crop may
    /// also hold a sliver of scanner bed or a band of album page outside the print: everything light around the
    /// picture is trimmed, up to where the picture starts. Photos without a border are never cut into.
    /// </summary>
    /// <remarks>
    /// Each side is followed inwards over lines (the middle 80% of each, clear of the corners and of a tilted print's
    /// other sides) that are mostly light: the bed, a page, the border, each a band, with a thin line between them (a
    /// page's or print's edge, or its shadow) bridged when the next band is as bright. A band ends at the picture: a
    /// sharp straight edge, or lines turning clearly darker (a pale sky). A side light past a fifth of the photo is no
    /// border. Thresholds were set on a 126-photo album: 18 of its 22 bordered prints are trimmed, and none of the
    /// 74 photos without a border.
    /// </remarks>
    public static Rect? GetWhiteBorderRect(Mat photo)
    {
        ArgumentNullException.ThrowIfNull(photo);
        if (photo.Empty() || photo.Width < 40 || photo.Height < 40) return null;

        using Mat bgr = new();
        if (photo.Channels() == 1) Cv2.CvtColor(photo, bgr, ColorConversionCodes.GRAY2BGR);
        else if (photo.Channels() == 4) Cv2.CvtColor(photo, bgr, ColorConversionCodes.BGRA2BGR);
        else photo.CopyTo(bgr);

        if (FindBorder(bgr) is { } rect) return rect;

        // A box drawn loosely around a print on a dark (or coloured) album page holds a ring of that page all round the
        // print, and the print's border no longer reaches the crop's edges. Painting the ring in the border's colour
        // turns it into the case above: a print on a light bed.
        using Mat filled = FillPageSurround(bgr);
        return filled.Empty() ? null : FindBorder(filled);
    }

    /// <summary>
    /// A copy of <paramref name="bgr"/> with the album page around the print painted in the print's border colour, or
    /// an empty Mat when there is none. The page is everything that isn't light, connected to the crop's edges: around
    /// a bordered print the light border keeps it to a thin ring along every side, while in a photo without a border
    /// dark parts of the picture reach the edges and run on into its middle.
    /// </summary>
    private static Mat FillPageSurround(Mat bgr)
    {
        int w = bgr.Width, h = bgr.Height;

        using Mat hsv = new();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        using Mat lightFrame = new();
        Cv2.InRange(hsv, new Scalar(0, 0, MinFrameBrightness), new Scalar(180, MaxFrameSaturation, 255), lightFrame);
        using Mat notLight = new();
        Cv2.BitwiseNot(lightFrame, notLight);
        using Mat labels = new();
        Cv2.ConnectedComponents(notLight, labels, PixelConnectivity.Connectivity8);
        var touching = new HashSet<int>();
        for (int x = 0; x < w; x++) { touching.Add(labels.At<int>(0, x)); touching.Add(labels.At<int>(h - 1, x)); }
        for (int y = 0; y < h; y++) { touching.Add(labels.At<int>(y, 0)); touching.Add(labels.At<int>(y, w - 1)); }
        touching.Remove(0);
        using Mat surround = new(h, w, MatType.CV_8UC1, Scalar.All(0));
        foreach (int label in touching)
        {
            using Mat part = new();
            Cv2.InRange(labels, new Scalar(label), new Scalar(label), part);
            Cv2.BitwiseOr(surround, part, surround);
        }

        // It is only a thin ring (all round a loosely boxed print, or wedges at the corners of a tilted one in a tight
        // box): it never reaches the middle of the photo.
        static double Covered(Mat area) => Cv2.CountNonZero(area) / (double)area.Total();
        if (Cv2.CountNonZero(surround) == 0) return new Mat();
        Rect inner = new((int)(w * MaxPageDepthShare), (int)(h * MaxPageDepthShare), w - 2 * (int)(w * MaxPageDepthShare), h - 2 * (int)(h * MaxPageDepthShare));
        using (Mat center = new(surround, inner))
        {
            if (Covered(center) > MaxPageInsideShare) return new Mat();
        }

        // The print's border colour: light, little coloured pixels just inside the page.
        using Mat ring = new();
        Cv2.Dilate(surround, ring, Cv2.GetStructuringElement(MorphShapes.Rect, new Size(9, 9)));
        Cv2.Subtract(ring, surround, ring);
        using Mat frameRing = new();
        Cv2.BitwiseAnd(lightFrame, ring, frameRing);
        if (Cv2.CountNonZero(frameRing) < Cv2.CountNonZero(ring) * MinLightFrameShare) return new Mat();
        Scalar frameColor = Cv2.Mean(bgr, frameRing);

        var filled = bgr.Clone();
        filled.SetTo(frameColor, surround);
        return filled;
    }

    // An album page around a print: at most this deep (as a share of the photo's size), with at most this share of the
    // inside covered, and a light frame (border) along at least this share of its inner edge.
    private const double MaxPageDepthShare = 0.15;
    private const double MaxPageInsideShare = 0.02;
    private const double MinLightFrameShare = 0.4;

    private static Rect? FindBorder(Mat bgr)
    {
        using Mat hsv = new();
        Cv2.CvtColor(bgr, hsv, ColorConversionCodes.BGR2HSV);
        int w = bgr.Width, h = bgr.Height;

        // The frame's brightness, from patches at the corners and the middle of each side. A rounded or worn corner
        // (or a torn side) can show the bed or the picture: two of the eight may be off, the rest must be light and
        // little coloured (white, cream, yellowed, or a pink album page).
        int s = Math.Max(5, Math.Min(w, h) / 60);
        Rect[] patches =
        [
            new(0, 0, s, s), new(w - s, 0, s, s), new(0, h - s, s, s), new(w - s, h - s, s, s),
            new((w - s) / 2, 0, s, s), new((w - s) / 2, h - s, s, s), new(0, (h - s) / 2, s, s), new(w - s, (h - s) / 2, s, s),
        ];
        var light = patches.Select(p =>
        {
            using Mat patch = new(hsv, p);
            return Cv2.Mean(patch);
        }).Where(m => m.Val2 >= MinFrameBrightness && m.Val1 <= MaxFrameSaturation).Select(m => m.Val2).Order().ToList();
        if (light.Count < patches.Length - MaxOffFramePatches) return null;
        double frame = light[light.Count / 2];

        using Mat lightMask = new();
        Cv2.InRange(hsv, new Scalar(0, 0, frame - FrameValueTolerance), new Scalar(180, MaxFrameSaturation, 255), lightMask);
        using Mat value = new();
        Cv2.ExtractChannel(hsv, value, 2);
        using Mat gray = new();
        Cv2.CvtColor(bgr, gray, ColorConversionCodes.BGR2GRAY);
        using Mat gradX = new();
        using Mat gradY = new();
        Cv2.Sobel(gray, gradX, MatType.CV_32F, 1, 0);
        Cv2.Sobel(gray, gradY, MatType.CV_32F, 0, 1);
        using Mat absX = new();
        using Mat absY = new();
        Cv2.ConvertScaleAbs(gradX, absX);
        Cv2.ConvertScaleAbs(gradY, absY);

        // Per-line averages over the middle 80% of each line: share of light pixels, brightness, and how sharply the
        // brightness changes across the line (low in a smooth band, a peak at the picture's straight edge).
        float[] Profile(Mat source, bool rows)
        {
            Rect middle = rows ? new Rect(w / 10, 0, w - w / 5, h) : new Rect(0, h / 10, w, h - h / 5);
            using Mat part = new(source, middle);
            using Mat reduced = new();
            Cv2.Reduce(part, reduced, rows ? ReduceDimension.Column : ReduceDimension.Row, ReduceTypes.Avg, MatType.CV_32FC1.Depth);
            reduced.GetArray(out float[] values);
            return values;
        }
        float[] lightRows = Profile(lightMask, rows: true), lightColumns = Profile(lightMask, rows: false);
        float[] valueRows = Profile(value, rows: true), valueColumns = Profile(value, rows: false);
        float[] edgeRows = Profile(absY, rows: true), edgeColumns = Profile(absX, rows: false);

        // How deep the frame goes from one side: 0 when there is none, -1 when it is light too far in (no border).
        int Depth(bool rows, bool fromEnd)
        {
            int count = rows ? h : w;
            float[] lightShare = rows ? lightRows : lightColumns;
            float[] brightness = rows ? valueRows : valueColumns;
            float[] sharpness = rows ? edgeRows : edgeColumns;
            int Index(int d) => fromEnd ? count - 1 - d : d;
            double Value(int d) => brightness[Index(d)];
            double Edge(int d) => sharpness[Index(d)];
            bool IsLight(int d) => d < count && lightShare[Index(d)] / 255.0 >= FrameLineShare;

            int limit = (int)(count * MaxFrameShare);
            int search = Math.Max(4, (int)(count * FrameSearchShare));
            int minRun = Math.Max(MinFramePixels, (int)(count * MinFrameShare));
            int gap = Math.Max(MinFramePixels, (int)(count * FrameGapShare));
            bool BandAt(int d, double atLeast) => d + minRun <= count && Enumerable.Range(d, minRun).All(k => IsLight(k) && Value(k) >= atLeast);

            // The outermost band starts near the edge (a dark sliver of bed or a shadow may come first).
            int start = Enumerable.Range(0, search).FirstOrDefault(d => BandAt(d, frame - FrameValueTolerance), -1);
            if (start < 0) return 0;

            double reference = 0;
            int bandStart = start;
            while (true)
            {
                // The band's own texture (grain), so the picture's edge stands out against it.
                var grain = Enumerable.Range(bandStart + 1, 2 * minRun).Where(k => k < count).Select(Edge).Order().ToList();
                double sharp = Math.Max(MinPictureEdge, grain[grain.Count / 2] * PictureEdgeFactor);

                double sum = 0;
                int e = bandStart;
                while (e < limit && IsLight(e))
                {
                    if (e - bandStart >= minRun)
                    {
                        double mean = sum / (e - bandStart);
                        if (Edge(e) >= sharp) break;
                        if (Enumerable.Range(e, minRun).All(k => k < count && Value(k) < mean - PictureDarkerStep)) break;
                    }
                    sum += Value(e);
                    e++;
                }
                if (e >= limit) return -1;
                if (e - bandStart < minRun) return bandStart == start ? 0 : bandStart;
                reference = Math.Max(reference, sum / (e - bandStart));

                // Another band just inside, past a thin line, and as bright: the next frame (page, then border).
                int next = Enumerable.Range(e + 1, gap).FirstOrDefault(k => BandAt(k, reference - NextBandTolerance), -1);
                if (next < 0) return e;
                bandStart = next;
            }
        }

        int top = Depth(rows: true, fromEnd: false), bottom = Depth(rows: true, fromEnd: true);
        int left = Depth(rows: false, fromEnd: false), right = Depth(rows: false, fromEnd: true);
        if (top < 0 || bottom < 0 || left < 0 || right < 0) return null;

        // The frame goes all the way round, and is clearly there on at least two sides: a pale background along one
        // edge, with only the scan's light sliver on the others, is no border.
        int minDepth = Math.Max(MinFramePixels, (int)(Math.Min(w, h) * MinFrameShare));
        if (Math.Min(Math.Min(top, bottom), Math.Min(left, right)) < minDepth) return null;
        int wide = (top >= h * WideFrameShare ? 1 : 0) + (bottom >= h * WideFrameShare ? 1 : 0)
            + (left >= w * WideFrameShare ? 1 : 0) + (right >= w * WideFrameShare ? 1 : 0);
        if (wide < 2) return null;

        // A couple of pixels more, past the soft edge between border and picture.
        top += BorderEdgeMargin; bottom += BorderEdgeMargin; left += BorderEdgeMargin; right += BorderEdgeMargin;
        return new Rect(left, top, w - left - right, h - top - bottom);
    }

    // What counts as the frame around a print's picture (bed, album page, white border): light (HSV value) and little
    // coloured, at most a fifth of each side deep.
    private const double MinFrameBrightness = 170;
    private const double MaxFrameSaturation = 80;
    private const int MaxOffFramePatches = 2;
    private const double FrameValueTolerance = 30;
    private const double FrameLineShare = 0.7;
    private const double MaxFrameShare = 0.2;
    private const double FrameSearchShare = 0.06;
    private const int MinFramePixels = 3;
    private const double MinFrameShare = 0.005;
    private const double FrameGapShare = 0.015;
    private const double NextBandTolerance = 12;
    private const double WideFrameShare = 0.012;

    // Where the picture starts: a sharp edge (against the band's grain), or lines turning clearly darker.
    private const double MinPictureEdge = 12;
    private const double PictureEdgeFactor = 3;
    private const double PictureDarkerStep = 15;
    private const int BorderEdgeMargin = 2;

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
