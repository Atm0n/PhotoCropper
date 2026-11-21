using OpenCvSharp;
using PhotoCropper.Core.Common;

namespace PhotoCropper.Core.Detection;

public static class BackgroundAnalyzer
{
    /// <summary>Size of the spots <see cref="SampleBackgroundColor(Mat)"/> samples.</summary>
    private const int SpotSize = 15;

    /// <summary>
    /// The eight spots <see cref="SampleBackgroundColor(Mat)"/> samples: corners and side middles, a little way in
    /// (about 3.5%) to step over the scanner's bezel and edge shadows. None on an image too small for them.
    /// </summary>
    private static Rect[] SampleSpots(Mat hsv)
    {
        const int s = SpotSize;
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return [];

        int halfW = hsv.Width / 2;
        int halfH = hsv.Height / 2;
        int insetX = Math.Clamp((int)(hsv.Width * 0.035), 5, Math.Max(5, halfW - s - 10));
        int insetY = Math.Clamp((int)(hsv.Height * 0.035), 5, Math.Max(5, halfH - s - 10));
        if (hsv.Width >= 400 && insetX < 25) insetX = 25;
        if (hsv.Height >= 400 && insetY < 25) insetY = 25;

        return
        [
            new(insetX, insetY, s, s),                               // Top-Left
            new(halfW - s / 2, insetY, s, s),                        // Top-Center
            new(hsv.Width - s - insetX, insetY, s, s),               // Top-Right
            new(insetX, halfH - s / 2, s, s),                        // Middle-Left
            new(hsv.Width - s - insetX, halfH - s / 2, s, s),        // Middle-Right
            new(insetX, hsv.Height - s - insetY, s, s),              // Bottom-Left
            new(halfW - s / 2, hsv.Height - s - insetY, s, s),       // Bottom-Center
            new(hsv.Width - s - insetX, hsv.Height - s - insetY, s, s) // Bottom-Right
        ];
    }

    /// <summary>
    /// Whether, among <see cref="SampleBackgroundColor(Mat)"/>'s spots, those of <paramref name="bed"/>'s colour are
    /// at least as many as any other group of spots that agree with each other. Spots on photos are each a different
    /// colour; a page under them gives a group of its own, larger than the bed's when it covers most of the scan.
    /// </summary>
    private static bool BedHasTheLargestSpotGroup(Mat hsv, Scalar bed)
    {
        var colours = new List<Scalar>();
        foreach (var r in SampleSpots(hsv))
        {
            using Mat m = new(hsv, r);
            colours.Add(Cv2.Mean(m));
        }
        if (colours.Count == 0) return false;

        // Saturation and brightness only: hue is noise on the light, neutral colours this compares.
        static bool Agree(Scalar a, Scalar b) => Math.Abs(a.Val1 - b.Val1) <= 20 && Math.Abs(a.Val2 - b.Val2) <= 20;
        int bedGroup = colours.Count(c => Agree(c, bed));
        int largestOther = colours.Where(c => !Agree(c, bed)).Select(c => colours.Count(o => !Agree(o, bed) && Agree(o, c))).DefaultIfEmpty(0).Max();
        return bedGroup > 0 && bedGroup >= largestOther;
    }

    public static Scalar SampleBackgroundColor(Mat hsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        var rects = SampleSpots(hsv);
        if (rects.Length == 0) return new Scalar();

        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();

        foreach (var r in rects)
        {
            using Mat m = new(hsv, r);
            var mean = Cv2.Mean(m);
            hVals.Add(mean.Val0);
            sVals.Add(mean.Val1);
            vVals.Add(mean.Val2);
        }

        hVals.Sort();
        sVals.Sort();
        vVals.Sort();

        // Calculate median for Hue, Saturation, and Value to robustly reject outliers
        double medianH = (hVals[3] + hVals[4]) / 2.0;
        double medianS = (sVals[3] + sVals[4]) / 2.0;
        double medianV = (vVals[3] + vVals[4]) / 2.0;

        return new Scalar(medianH, medianS, medianV);
    }

    /// <summary>
    /// Like <see cref="SampleBackgroundColor(Mat)"/>, for a scan whose photos are partly known: samples around the
    /// border but skips spots inside <paramref name="photos"/> (outlines in <paramref name="hsv"/> pixels) and spots
    /// with texture, so photos covering most of the border don't decide the colour. Falls back to the plain sample
    /// when fewer than two spots are left.
    /// </summary>
    public static Scalar SampleBackgroundColor(Mat hsv, IReadOnlyList<Point2f[]> photos)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        ArgumentNullException.ThrowIfNull(photos);
        const int s = 15;
        if (hsv.Width < s * 3 + 20 || hsv.Height < s * 3 + 20) return SampleBackgroundColor(hsv);

        int insetX = Math.Max(Math.Clamp((int)(hsv.Width * 0.035), 5, hsv.Width / 2 - s - 10), hsv.Width >= 400 ? 25 : 5);
        int insetY = Math.Max(Math.Clamp((int)(hsv.Height * 0.035), 5, hsv.Height / 2 - s - 10), hsv.Height >= 400 ? 25 : 5);
        int x0 = insetX, x1 = hsv.Width - s - insetX, y0 = insetY, y1 = hsv.Height - s - insetY;

        // Five spots along each side (corners shared).
        var spots = new List<Rect>();
        for (int i = 0; i <= 5; i++)
        {
            int x = x0 + (x1 - x0) * i / 5, y = y0 + (y1 - y0) * i / 5;
            spots.Add(new Rect(x, y0, s, s));
            spots.Add(new Rect(x, y1, s, s));
            if (i is > 0 and < 5)
            {
                spots.Add(new Rect(x0, y, s, s));
                spots.Add(new Rect(x1, y, s, s));
            }
        }

        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();
        foreach (var r in spots)
        {
            var centre = new Point2f(r.X + s / 2f, r.Y + s / 2f);
            if (photos.Any(p => Cv2.PointPolygonTest(p, centre, true) > -s)) continue; // on (or right next to) a photo
            using Mat m = new(hsv, r);
            Cv2.MeanStdDev(m, out Scalar mean, out Scalar spread);
            if (spread.Val2 > 12) continue; // textured: not plain background
            hVals.Add(mean.Val0);
            sVals.Add(mean.Val1);
            vVals.Add(mean.Val2);
        }
        if (hVals.Count < 2) return SampleBackgroundColor(hsv);
        return new Scalar(Median(hVals), Median(sVals), Median(vVals));
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int n = values.Count;
        return n % 2 == 1 ? values[n / 2] : (values[n / 2 - 1] + values[n / 2]) / 2;
    }

    public static Scalar SamplePixelBackgroundColor(Mat bgr, int x, int y, int sampleSize = 5)
    {
        ArgumentNullException.ThrowIfNull(bgr);
        if (bgr.Empty()) return new Scalar();

        x = Math.Clamp(x, 0, bgr.Width - 1);
        y = Math.Clamp(y, 0, bgr.Height - 1);

        int half = sampleSize / 2;
        int startX = Math.Max(0, x - half);
        int startY = Math.Max(0, y - half);
        int w = Math.Min(bgr.Width - startX, sampleSize);
        int h = Math.Min(bgr.Height - startY, sampleSize);

        using Mat sampledArea = new(bgr, new Rect(startX, startY, w, h));
        using Mat bgrSampled = ImageIo.NormalizeToBgr(sampledArea);
        using Mat hsvSampled = new();
        Cv2.CvtColor(bgrSampled, hsvSampled, ColorConversionCodes.BGR2HSV);
        return Cv2.Mean(hsvSampled);
    }

    public static Scalar HsvToBgr(Scalar hsvColor)
    {
        using Mat hsvPixel = new(1, 1, MatType.CV_8UC3);
        hsvPixel.SetTo(hsvColor);
        using Mat bgrPixel = new();
        Cv2.CvtColor(hsvPixel, bgrPixel, ColorConversionCodes.HSV2BGR);
        return Cv2.Mean(bgrPixel);
    }

    public static Mat CreateBackgroundMask(Mat hsv, Scalar avgColor, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(hsv);

        // If the background is neutral / light (low saturation, high value like scanner lids),
        // hue is unstable and noise-prone. In this regime, match based on low saturation
        // and allow an extended Value range to absorb CIS sensor lid shadows and off-white gradients.
        bool isNeutralLight = avgColor.Val1 < 65 && avgColor.Val2 > 110;

        Scalar lower;
        Scalar upper;

        if (isNeutralLight)
        {
            // Background tolerance scales realistically to physical scanner lid variance:
            // Scanner lids vary by ~10-25 levels due to CIS lighting falloff and texture.
            // Tolerance 0: tight match to lid (V within 8 levels)
            // Tolerance 25 (default): absorbs ~19 levels of shadow
            // Tolerance 100: absorbs up to ~53 levels of shadow without eating photo content (V stays >= 160)
            double maxS = Math.Clamp(avgColor.Val1 + 10.0 + (tolerance * 0.25), 25.0, 60.0);
            double minV = Math.Max(160.0, avgColor.Val2 - (8.0 + tolerance * 0.45));

            lower = new Scalar(0, 0, minV);
            upper = new Scalar(180, maxS, 255);
        }
        else
        {
            double hTol = tolerance * 0.4;
            double sTol = tolerance;
            double vTol = avgColor.Val2 > 128 ? tolerance * 1.5 : tolerance;

            // A dark grey or black background (an album page): its hue is the scanner's noise, and so is much of its
            // (small) saturation, which noise inflates on dark pixels. Any hue matches, and a little more saturation.
            bool neutralDark = avgColor.Val1 < 30;
            double sUp = neutralDark ? sTol + 15 : sTol;

            lower = new Scalar(neutralDark ? 0 : Math.Max(0, avgColor.Val0 - hTol), Math.Max(0, avgColor.Val1 - sTol), Math.Max(0, avgColor.Val2 - vTol));
            upper = new Scalar(neutralDark ? 180 : Math.Min(180, avgColor.Val0 + hTol), Math.Min(255, avgColor.Val1 + sUp), Math.Min(255, avgColor.Val2 + vTol));
        }

        Mat mask = new();
        Scalar lowerArray = lower;
        Scalar upperArray = upper;
        Cv2.InRange(hsv, lowerArray, upperArray, mask);
        return mask;
    }

    public static (int Top, int Bottom, int Left, int Right) DetectBezelMargins(Mat source, Scalar avgBgHsv, double tolerance = 30)
    {
        ArgumentNullException.ThrowIfNull(source);

        int w = source.Width;
        int h = source.Height;
        if (w < 40 || h < 40) return (0, 0, 0, 0);

        // Downscale for fast bezel detection if source is large
        double scale = Math.Min(1.0, 800.0 / Math.Max(w, h));
        int scaledW = (int)Math.Round(w * scale);
        int scaledH = (int)Math.Round(h * scale);

        using Mat rawSmallSource = new();
        if (scale < 0.999)
        {
            Cv2.Resize(source, rawSmallSource, new Size(scaledW, scaledH), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            source.CopyTo(rawSmallSource);
        }

        using Mat smallSource = ImageIo.NormalizeToBgr(rawSmallSource);

        using Mat hsv = new();
        Cv2.CvtColor(smallSource, hsv, ColorConversionCodes.BGR2HSV);
        using Mat bgMask = CreateBackgroundMask(hsv, avgBgHsv, tolerance);

        int maxBezelX = Math.Clamp(scaledW / 30, 4, 30);
        int maxBezelY = Math.Clamp(scaledH / 30, 4, 30);

        int topBezel = MeasureBezelDepth(bgMask, maxBezelY, scaledW, true, true);
        int bottomBezel = MeasureBezelDepth(bgMask, maxBezelY, scaledW, true, false);
        int leftBezel = MeasureBezelDepth(bgMask, maxBezelX, scaledH, false, true);
        int rightBezel = MeasureBezelDepth(bgMask, maxBezelX, scaledH, false, false);

        double invScale = 1.0 / scale;
        return (
            (int)Math.Round(topBezel * invScale),
            (int)Math.Round(bottomBezel * invScale),
            (int)Math.Round(leftBezel * invScale),
            (int)Math.Round(rightBezel * invScale)
        );
    }

    /// <summary>
    /// Paints the scanner's bezel margins (see <see cref="DetectBezelMargins"/>) over with the background colour, so
    /// they don't join photos into one shape. <paramref name="pad"/> is the padding added around the scan, if any.
    /// </summary>
    public static void PaintBezels(Mat image, (int Top, int Bottom, int Left, int Right) margins, int pad, Scalar backgroundBgr)
    {
        ArgumentNullException.ThrowIfNull(image);
        var (top, bottom, left, right) = margins;
        if (top > 0) Cv2.Rectangle(image, new Rect(0, 0, image.Width, pad + top), backgroundBgr, -1);
        if (bottom > 0) Cv2.Rectangle(image, new Rect(0, image.Height - pad - bottom, image.Width, pad + bottom), backgroundBgr, -1);
        if (left > 0) Cv2.Rectangle(image, new Rect(0, 0, pad + left, image.Height), backgroundBgr, -1);
        if (right > 0) Cv2.Rectangle(image, new Rect(image.Width - pad - right, 0, pad + right, image.Height), backgroundBgr, -1);
    }

    /// <summary>
    /// An album page lying on a light scanner bed, with the background sampled on the page: the ring of bed around
    /// the page (often too wide for <see cref="DetectBezelMargins"/>) would count as foreground and, as a closed
    /// outline around the whole page, get filled in together with every photo on it. And the sample, taken a little
    /// way in, lands on the page's edge, often darker than the rest of it (shadow, age), so a tight tolerance takes
    /// the whole page for foreground.
    /// <para>
    /// Prints lying on a light bed are the other case where the scan's outermost pixels are bed but the background
    /// sample isn't: photos close to the edge cover the spots it is taken at. Then the bed's colour is the background.
    /// </para>
    /// </summary>
    /// <param name="image">The scan (BGR, without padding).</param>
    /// <param name="backgroundHsv">The background sampled on the scan.</param>
    /// <param name="background">The colour to use as background: the page's, sampled inside its edge, for a page with
    /// bed all round it; the bed's when prints near the edge hid it from the sample; otherwise
    /// <paramref name="backgroundHsv"/>.</param>
    /// <returns>
    /// The area around the page, to paint as background: the bed-coloured area reaching the scan's edge and the
    /// page's edge next to it; or <see langword="null"/> when the scan isn't a page with bed all round it.
    /// </returns>
    public static Mat? FindBedAroundPage(Mat image, Scalar backgroundHsv, out Scalar background)
    {
        ArgumentNullException.ThrowIfNull(image);
        background = backgroundHsv;
        Scalar pageHsv;
        const int s = 9;
        int w = image.Width, h = image.Height;
        if (w < 100 || h < 100) return null;

        using Mat hsv = new();
        Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);

        // The bed's colour: the outermost few pixels all round, sampled in spots along each side.
        const int inset = 3;
        var hVals = new List<double>();
        var sVals = new List<double>();
        var vVals = new List<double>();
        for (int i = 0; i <= 6; i++)
        {
            int x = inset + (w - s - 2 * inset) * i / 6, y = inset + (h - s - 2 * inset) * i / 6;
            foreach (var r in new[] { new Rect(x, inset, s, s), new Rect(x, h - s - inset, s, s), new Rect(inset, y, s, s), new Rect(w - s - inset, y, s, s) })
            {
                using Mat m = new(hsv, r);
                var mean = Cv2.Mean(m);
                hVals.Add(mean.Val0);
                sVals.Add(mean.Val1);
                vVals.Add(mean.Val2);
            }
        }
        var bed = new Scalar(Median(hVals), Median(sVals), Median(vVals));

        // Only a light, neutral bed clearly unlike the background (which was then sampled on something else: the page).
        bool bedIsLight = bed.Val1 < 65 && bed.Val2 > 150;
        bool unlikeBackground = backgroundHsv.Val2 < bed.Val2 - 35 || backgroundHsv.Val1 > bed.Val1 + 30;
        if (!bedIsLight || !unlikeBackground) return null;

        // The bed-coloured pixels connected to the scan's edge.
        using Mat bedLike = CreateBackgroundMask(hsv, bed, 25);
        using Mat bedMask = KeepConnectedToEdge(bedLike);
        int bedArea = Cv2.CountNonZero(bedMask);

        // A ring around a page, not most of the scan (a bed the background sample missed for some other reason).
        if (bedArea == 0 || bedArea > 0.35 * w * h) return null;

        // ...and bed on every side: a photo reaching the scan's edge breaks the ring, and its light parts there (a
        // white floor or sky) would be taken for bed and painted out. Nothing is painted then. When the sample's spots
        // are mostly bed and the rest photos (each its own colour, their median one found nowhere on the scan), these
        // are prints on the bed and the bed's colour is the background. When most agree on another colour (a page
        // reaching the scan's edge), the sample stands.
        if (!BedAlongEverySide(bedMask))
        {
            if (BedHasTheLargestSpotGroup(hsv, bed)) background = bed;
            return null;
        }

        // The page: what the bed leaves. Its colour is sampled as the scan's background is, well inside its edge.
        int reach = Math.Max(5, (int)(Math.Min(w, h) * 0.05)) | 1;
        using (Mat inside = new())
        {
            Cv2.BitwiseNot(bedMask, inside);
            Rect page = Cv2.BoundingRect(inside);
            var core = new Rect(page.X + reach, page.Y + reach, page.Width - 2 * reach, page.Height - 2 * reach);
            if (core.Width < w / 3 || core.Height < h / 3) return null;
            using Mat pageArea = new(hsv, core);
            pageHsv = SampleBackgroundColor(pageArea);
        }
        background = pageHsv;

        // The page's edge, often darker (shadow, age), would still outline the page: near the bed, paper of the page's
        // colour or darker goes too. Not lighter: a print's light border close to the page's edge stays.
        using Mat near = new();
        using (Mat reachKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(reach, reach)))
        {
            Cv2.Dilate(bedMask, near, reachKernel);
        }
        bool neutral = pageHsv.Val1 < 40; // hue means little on grey paper
        double hTol = neutral ? 180 : 15;
        using Mat pageLike = new();
        Cv2.InRange(hsv,
            new Scalar(neutral ? 0 : Math.Max(0, pageHsv.Val0 - hTol), Math.Max(0, pageHsv.Val1 - 40), Math.Max(0, pageHsv.Val2 - 50)),
            new Scalar(neutral ? 180 : Math.Min(180, pageHsv.Val0 + hTol), Math.Min(255, pageHsv.Val1 + 40), Math.Min(255, pageHsv.Val2 + 8)),
            pageLike);
        Cv2.BitwiseAnd(pageLike, near, pageLike);
        Cv2.BitwiseOr(pageLike, bedMask, pageLike);
        Mat surround = KeepConnectedToEdge(pageLike);

        // What is left of the page's edge (a shadow line, a cut that isn't straight): a thin band more.
        int band = Math.Max(3, (int)(Math.Min(w, h) * 0.004)) | 1;
        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(band, band)))
        {
            Cv2.MorphologyEx(surround, surround, MorphTypes.Close, kernel);
            Cv2.Dilate(surround, surround, kernel);
        }
        return surround;
    }

    /// <summary>Whether <paramref name="bedMask"/> covers most of each side of the image (its outermost few pixels).</summary>
    private static bool BedAlongEverySide(Mat bedMask)
    {
        const double minShare = 0.8;
        int w = bedMask.Width, h = bedMask.Height, depth = 3;
        Rect[] sides = [new(0, 0, w, depth), new(0, h - depth, w, depth), new(0, 0, depth, h), new(w - depth, 0, depth, h)];
        foreach (var side in sides)
        {
            using Mat strip = new(bedMask, side);
            if (Cv2.CountNonZero(strip) < minShare * side.Width * side.Height) return false;
        }
        return true;
    }

    /// <summary>The parts of <paramref name="mask"/> that reach the image's edge.</summary>
    private static Mat KeepConnectedToEdge(Mat mask)
    {
        int w = mask.Width, h = mask.Height;
        // Flood each part reaching the edge with 128, then keep only those pixels.
        using Mat marked = mask.Clone();
        void Fill(int x, int y)
        {
            if (marked.At<byte>(y, x) == 255) Cv2.FloodFill(marked, new Point(x, y), Scalar.All(128), out _, Scalar.All(0), Scalar.All(0), FloodFillFlags.Link8);
        }
        for (int x = 0; x < w; x++) { Fill(x, 0); Fill(x, h - 1); }
        for (int y = 0; y < h; y++) { Fill(0, y); Fill(w - 1, y); }

        Mat kept = new();
        Cv2.Compare(marked, 128, kept, CmpTypes.EQ);
        return kept;
    }

    /// <summary>
    /// The paper of a light album page (cream, salmon, pink) lying on the scanner bed, when the background was
    /// sampled on the bed: the page then stands out from the bed far more than its faded photos stand out from the
    /// page, and detection takes the whole page for one photo. Its paper is the most common smooth colour that isn't
    /// the bed's, light and soft, and forms one large area: the outline round it covers at least 40% of the scan,
    /// which a sky or a wall in a photo lying on the bed doesn't. Use it as the background, as Pick Color would.
    /// </summary>
    /// <param name="hsv">The scan, in HSV.</param>
    /// <param name="bedHsv">The background sampled on the scan (the bed, when this applies).</param>
    /// <returns>The paper's colour, or <see langword="null"/> when the scan isn't a light page on the bed.</returns>
    public static Scalar? LightPageColour(Mat hsv, Scalar bedHsv)
    {
        ArgumentNullException.ThrowIfNull(hsv);
        int width = hsv.Width, height = hsv.Height;
        if (width < 100 || height < 100) return null;
        // Only when the background is the bed (light and neutral): otherwise it was sampled on a page already.
        if (bedHsv.Val1 >= 30 || bedHsv.Val2 <= 150) return null;

        // The most common smooth colour that isn't the bed's.
        int tile = Math.Max(16, Math.Min(width, height) / 70);
        var smooth = new List<Scalar>();
        for (int y = 0; y + tile <= height; y += tile)
        {
            for (int x = 0; x + tile <= width; x += tile)
            {
                using Mat m = new(hsv, new Rect(x, y, tile, tile));
                Cv2.MeanStdDev(m, out Scalar mean, out Scalar spread);
                if (spread.Val2 <= 8 && mean.Val1 > bedHsv.Val1 + 15) smooth.Add(mean);
            }
        }
        if (smooth.Count == 0) return null;
        var paper = new Scalar(Median([.. smooth.Select(c => c.Val0)]), Median([.. smooth.Select(c => c.Val1)]), Median([.. smooth.Select(c => c.Val2)]));
        if (paper.Val2 < 170 || paper.Val1 > 90) return null; // light and soft-coloured, as album paper is

        // One large area of that paper. Measured at a quarter of the size: its outline, not its detail, matters.
        using Mat small = new();
        Cv2.Resize(hsv, small, new Size(width / 4, height / 4), 0, 0, InterpolationFlags.Area);
        using Mat paperMask = new();
        Cv2.InRange(small,
            new Scalar(Math.Max(0, paper.Val0 - 12), Math.Max(0, paper.Val1 - 22), Math.Max(0, paper.Val2 - 28)),
            new Scalar(Math.Min(180, paper.Val0 + 12), Math.Min(255, paper.Val1 + 22), Math.Min(255, paper.Val2 + 20)),
            paperMask);
        using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
        {
            Cv2.MorphologyEx(paperMask, paperMask, MorphTypes.Open, kernel);
        }
        // The largest piece only: taking the outline round several pieces also took scans of loose prints for pages
        // (4 of 38 on a real set), while gaining no page.
        Cv2.FindContours(paperMask, out Point[][] areas, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
        if (areas.Length == 0) return null;
        var largest = areas.MaxBy(a => Cv2.ContourArea(a))!;
        double outline = Cv2.ContourArea(Cv2.ConvexHull(largest));
        return outline >= 0.4 * small.Width * small.Height ? paper : null;
    }

    private static int MeasureBezelDepth(Mat bgMask, int maxDepth, int length, bool isHorizontal, bool fromStart)
    {
        int bezel = 0;
        int crossLength = isHorizontal ? bgMask.Height : bgMask.Width;
        maxDepth = Math.Min(maxDepth, crossLength);

        for (int i = 0; i < maxDepth; i++)
        {
            int idx = fromStart ? i : crossLength - 1 - i;
            var rect = isHorizontal ? new Rect(0, idx, length, 1) : new Rect(idx, 0, 1, length);

            using Mat slice = new(bgMask, rect);
            int nonBg = length - Cv2.CountNonZero(slice);

            if (nonBg >= length * 0.70)
            {
                bezel = i + 1;
            }
            else if (nonBg < length * 0.40)
            {
                break;
            }
        }

        return bezel >= maxDepth ? 0 : bezel;
    }
}
