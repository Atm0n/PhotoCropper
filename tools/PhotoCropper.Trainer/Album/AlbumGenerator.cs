using OpenCvSharp;

namespace PhotoCropper.Trainer.Album;

/// <summary>A photo on a generated page: its rotated box in page pixels (the whole print, white border included).</summary>
internal sealed record PhotoBox(float CenterX, float CenterY, float Width, float Height, float Angle);

/// <summary>A generated album page: the scan, its photo mask and the photos' boxes.</summary>
internal sealed class AlbumPage(Mat image, Mat mask, IReadOnlyList<PhotoBox> boxes) : IDisposable
{
    /// <summary>The scan: Size × Size, 8-bit BGR. A scan that isn't square is centred on black.</summary>
    public Mat Image { get; } = image;

    /// <summary>255 on photo pixels, 0 elsewhere. Neighbouring photos are kept apart by a thin gap.</summary>
    public Mat Mask { get; } = mask;

    public IReadOnlyList<PhotoBox> Boxes { get; } = boxes;

    public void Dispose()
    {
        Image.Dispose();
        Mask.Dispose();
    }
}

/// <summary>
/// Makes practice album pages: photos on album paper (or straight on the scanner bed), as they look on real scans.
/// Everything varies: the bed, the page and its paper, how many photos there are and how they are laid out, their
/// size, tilt, borders, fading and toning, shadows, photo corners, captions and plain paper cut-outs (which aren't
/// photos), then the scanner's own lighting, blur, noise and JPEG compression. A page is defined by its seed alone.
/// </summary>
internal sealed class AlbumGenerator(PhotoLibrary library, int size)
{
    /// <summary>Pages are drawn at this many times their final size, then scaled down (smooth, anti-aliased edges).</summary>
    private const int Supersample = 2;

    /// <summary>The gap kept between neighbouring photos in the mask, in final pixels (half on each photo).</summary>
    public const float MaskGap = Core.Detection.AiPhotoDetector.MaskGap;

    public int Size { get; } = size;

    private static readonly (Scalar Colour, double Weight)[] LightPapers =
    [
        (Bgr(238, 228, 205), 3), (Bgr(245, 240, 225), 2), (Bgr(235, 195, 175), 2), (Bgr(240, 210, 210), 1),
        (Bgr(215, 215, 210), 1), (Bgr(248, 248, 245), 1), (Bgr(240, 232, 190), 1), (Bgr(210, 220, 230), 1),
        (Bgr(215, 190, 150), 1),
    ];

    private static readonly (Scalar Colour, double Weight)[] DarkPapers =
    [
        (Bgr(25, 25, 25), 3), (Bgr(55, 55, 58), 2), (Bgr(85, 60, 45), 2), (Bgr(170, 135, 95), 1), (Bgr(55, 75, 55), 1),
        (Bgr(40, 45, 70), 1), (Bgr(95, 35, 40), 1), (Bgr(130, 130, 128), 1),
    ];

    private static readonly string[] Captions =
    [
        "Summer 1968", "Nadal 1974", "Barcelona", "Mama i papa", "1972", "La platja", "Sant Joan", "Our wedding",
        "Grandma's house", "1965", "Holidays", "Christmas", "The garden", "Agost 81", "Sitges",
    ];

    /// <summary>Generates page <paramref name="index"/> of the set made from <paramref name="seed"/>.</summary>
    public AlbumPage Generate(ulong seed, int index)
    {
        var rng = new Rng(seed + ((ulong)index * 0x9E3779B97F4A7C15UL));
        int longSide = Size * Supersample;
        double aspect = rng.Range(0.68, 0.80);
        bool portrait = rng.Chance(0.7);
        int width = portrait ? (int)(longSide * aspect) : longSide;
        int height = portrait ? longSide : (int)(longSide * aspect);

        using Mat canvas = ScannerBed(width, height, rng.Fork());
        using Mat mask = new(height, width, MatType.CV_8UC1, Scalar.All(0));

        // Where the photos go: an album page (most scans), or the scanner bed itself.
        Point2f[] area;
        Scalar paper;
        if (rng.Chance(0.82))
        {
            (area, paper) = DrawPage(canvas, rng.Fork());
        }
        else
        {
            float margin = longSide * 0.03f;
            area = [new(margin, margin), new(width - margin, margin), new(width - margin, height - margin), new(margin, height - margin)];
            paper = Cv2.Mean(canvas);
        }

        var boxes = PlacePhotos(area, width, height, rng.Fork());
        var photoRng = rng.Fork();
        foreach (var box in boxes) DrawPhoto(canvas, mask, box, paper, photoRng.Fork());

        DrawCutOutsAndCaptions(canvas, boxes, area, rng.Fork());
        ApplyScanner(canvas, rng.Fork());
        return Finish(canvas, mask, boxes, rng.Fork());
    }

    private static Scalar Bgr(int r, int g, int b) => new(b, g, r);

    private static Scalar Jitter(Scalar colour, Rng rng, double amount) => new(
        Math.Clamp(colour.Val0 + rng.Range(-amount, amount), 0, 255),
        Math.Clamp(colour.Val1 + rng.Range(-amount, amount), 0, 255),
        Math.Clamp(colour.Val2 + rng.Range(-amount, amount), 0, 255));

    /// <summary>The scanner bed seen where the page doesn't cover it: a white or grey lid, or black.</summary>
    private static Mat ScannerBed(int width, int height, Rng rng)
    {
        Scalar colour = rng.Pick<Scalar>(
        [
            (Scalar.All(rng.Range(232, 250)), 5),
            (Scalar.All(rng.Range(120, 195)), 2),
            (Scalar.All(rng.Range(8, 45)), 3),
        ]);
        var bed = new Mat(height, width, MatType.CV_32FC3, Jitter(colour, rng, 4));
        AddTexture(bed, rng, grain: rng.Range(0.8, 2.5), blotch: rng.Range(0, 3));

        // A dark strip along an edge or two: the scanner's frame (bezel).
        if (rng.Chance(0.25))
        {
            var dark = Scalar.All(rng.Range(5, 40));
            int strip = (int)(Math.Min(width, height) * rng.Range(0.01, 0.035));
            if (rng.Chance(0.6)) Cv2.Rectangle(bed, new Rect(0, 0, width, strip), dark, -1);
            if (rng.Chance(0.6)) Cv2.Rectangle(bed, new Rect(0, 0, strip, height), dark, -1);
            if (rng.Chance(0.3)) Cv2.Rectangle(bed, new Rect(width - strip, 0, strip, height), dark, -1);
        }
        return bed;
    }

    /// <summary>Adds fine grain and blotches to a float BGR image.</summary>
    private static void AddTexture(Mat image, Rng rng, double grain, double blotch)
    {
        using Mat fine = new(image.Size(), MatType.CV_32FC3);
        Noise.FillNormal(fine, rng, 0, grain);
        Cv2.Add(image, fine, image);
        if (blotch <= 0) return;
        using Mat blotches = Noise.Blotches(image.Size(), rng, blotch, rng.Range(48, 128));
        using Mat blotches3 = new();
        Cv2.Merge([blotches, blotches, blotches], blotches3);
        Cv2.Add(image, blotches3, image);
    }

    /// <summary>Draws the album page: the whole scan, or a page lying on the bed. Returns where photos may go and the paper's colour.</summary>
    private static (Point2f[] Area, Scalar Paper) DrawPage(Mat canvas, Rng rng)
    {
        int width = canvas.Width, height = canvas.Height;
        bool light = rng.Chance(0.55);
        Scalar paper = Jitter(rng.Pick(light ? LightPapers : DarkPapers), rng, 9);

        RotatedRect page;
        if (rng.Chance(0.45))
        {
            // Larger than the bed: the page fills the scan.
            page = new RotatedRect(new Point2f(width / 2f, height / 2f), new Size2f(width * 1.2f, height * 1.2f), 0);
        }
        else
        {
            float w = width * (float)rng.Range(0.72, 0.97), h = height * (float)rng.Range(0.72, 0.97);
            var centre = new Point2f(width / 2f + (float)rng.Range(-0.08, 0.08) * width, height / 2f + (float)rng.Range(-0.08, 0.08) * height);
            page = new RotatedRect(centre, new Size2f(w, h), (float)Math.Clamp(rng.Normal(0, 1.5), -5, 5));
        }

        using Mat sheet = new(height, width, MatType.CV_32FC3, paper);
        AddTexture(sheet, rng, grain: rng.Range(1.0, 4.5), blotch: rng.Range(1, 6));
        if (rng.Chance(0.15)) DrawPattern(sheet, paper, rng);
        if (rng.Chance(0.12))
        {
            // A printed frame line near the page's edge.
            var inset = new RotatedRect(page.Center, new Size2f(page.Size.Width * 0.92f, page.Size.Height * 0.92f), page.Angle);
            Cv2.Polylines(sheet, [ToPoints(inset.Points())], true, Jitter(paper, rng, 60), Math.Max(1, (int)rng.Range(1, 4)), LineTypes.AntiAlias);
        }

        using Mat pageMask = new(height, width, MatType.CV_8UC1, Scalar.All(0));
        Cv2.FillConvexPoly(pageMask, ToPoints(page.Points()), Scalar.All(255), LineTypes.AntiAlias);
        // A soft shadow along the page's edge on the bed.
        if (page.Size.Width < width) Shade(canvas, page.Points(), rng.Range(3, 10), rng.Range(0.15, 0.35), rng.Range(2, 6));
        sheet.CopyTo(canvas, pageMask);

        if (rng.Chance(0.15)) DrawGlare(canvas, rng);

        float inside = (float)rng.Range(0.03, 0.07);
        var area = new RotatedRect(page.Center, new Size2f(page.Size.Width * (1 - inside), page.Size.Height * (1 - inside)), page.Angle);
        return (Clip(area.Points(), width, height), paper);
    }

    private static Point2f[] Clip(Point2f[] points, int width, int height) =>
        [.. points.Select(p => new Point2f(Math.Clamp(p.X, 0, width - 1), Math.Clamp(p.Y, 0, height - 1)))];

    /// <summary>Faint dots or lines printed on decorative album paper.</summary>
    private static void DrawPattern(Mat sheet, Scalar paper, Rng rng)
    {
        Scalar ink = Jitter(paper, rng, 14);
        int step = (int)rng.Range(12, 40);
        int width = sheet.Width, height = sheet.Height;
        if (rng.Chance(0.5))
        {
            for (int y = step / 2; y < height; y += step)
            {
                for (int x = step / 2; x < width; x += step) Cv2.Circle(sheet, new Point(x, y), Math.Max(1, step / 8), ink, -1, LineTypes.AntiAlias);
            }
        }
        else
        {
            for (int d = -height; d < width; d += step) Cv2.Line(sheet, new Point(d, 0), new Point(d + height, height), ink, 1, LineTypes.AntiAlias);
        }
    }

    /// <summary>A soft bright band, as a plastic sleeve over the page reflects the scanner's lamp.</summary>
    private static void DrawGlare(Mat canvas, Rng rng)
    {
        using Mat band = new(canvas.Size(), MatType.CV_32FC1, Scalar.All(0));
        int x = (int)rng.Range(0, canvas.Width);
        int thickness = (int)(canvas.Width * rng.Range(0.04, 0.15));
        Cv2.Line(band, new Point(x, -10), new Point(x + (int)rng.Range(-canvas.Width / 3.0, canvas.Width / 3.0), canvas.Height + 10), Scalar.All(1), thickness);
        Cv2.GaussianBlur(band, band, new Size(0, 0), thickness * 0.6);
        using Mat band3 = new();
        Cv2.Merge([band, band, band], band3);
        Cv2.ScaleAdd(band3, rng.Range(12, 35), canvas, canvas);
    }

    /// <summary>Darkens a soft area: a shadow under the given outline, offset by <paramref name="offset"/>.</summary>
    private static void Shade(Mat canvas, Point2f[] outline, double blur, double strength, double offset)
    {
        using Mat shadow = new(canvas.Size(), MatType.CV_32FC1, Scalar.All(0));
        Cv2.FillConvexPoly(shadow, ToPoints([.. outline.Select(p => new Point2f(p.X + (float)offset, p.Y + (float)offset))]), Scalar.All(strength), LineTypes.AntiAlias);
        Cv2.GaussianBlur(shadow, shadow, new Size(0, 0), blur);
        using Mat keep = new();
        Cv2.Subtract(Scalar.All(1.0), shadow, keep);
        using Mat keep3 = new();
        Cv2.Merge([keep, keep, keep], keep3);
        Cv2.Multiply(canvas, keep3, canvas);
    }

    /// <summary>Chooses the photos' boxes: a loose grid (as many albums are laid out) or scattered, never overlapping.</summary>
    private static List<RotatedRect> PlacePhotos(Point2f[] area, int width, int height, Rng rng)
    {
        int count = rng.Pick<int>([(1, 10), (2, 20), (3, 20), (4, 25), (5, 10), (6, 10), (7, 3), (8, 2)]);
        var bounds = Cv2.BoundingRect(area);
        float shortSide = Math.Min(bounds.Width, bounds.Height);
        bool grid = rng.Chance(0.5);
        double gapFraction = rng.Chance(0.15) ? 0 : rng.Range(0.01, 0.05);
        bool allowOffEdge = rng.Chance(0.08);
        var boxes = new List<RotatedRect>();

        if (grid)
        {
            int columns = count switch { 1 => 1, 2 => rng.Chance(0.5) ? 1 : 2, 3 or 4 => 2, 5 or 6 => rng.Chance(0.5) ? 2 : 3, _ => 3 };
            int rows = (int)Math.Ceiling((double)count / columns);
            float cellW = bounds.Width / (float)columns, cellH = bounds.Height / (float)rows;
            for (int i = 0; i < count; i++)
            {
                int c = i % columns, r = i / columns;
                float fill = (float)rng.Range(0.68, 0.94);
                float aspect = (float)PhotoAspect(rng);
                float w = cellW * fill, h = w / aspect;
                if (h > cellH * fill)
                {
                    h = cellH * fill;
                    w = h * aspect;
                }
                var centre = new Point2f(
                    bounds.X + (cellW * (c + 0.5f)) + (float)rng.Range(-0.06, 0.06) * cellW,
                    bounds.Y + (cellH * (r + 0.5f)) + (float)rng.Range(-0.06, 0.06) * cellH);
                var box = new RotatedRect(centre, new Size2f(w, h), Tilt(rng));
                if (Fits(box, boxes, area, gapFraction * shortSide, checkArea: !allowOffEdge)) boxes.Add(box);
            }
        }
        else
        {
            for (int attempt = 0; attempt < 400 && boxes.Count < count; attempt++)
            {
                double scale = count <= 2 ? rng.Range(0.35, 0.75) : rng.Range(0.22, 0.5);
                float longEdge = (float)(shortSide * scale);
                float aspect = (float)PhotoAspect(rng);
                var sizeF = aspect >= 1 ? new Size2f(longEdge, longEdge / aspect) : new Size2f(longEdge * aspect, longEdge);
                var centre = new Point2f((float)rng.Range(bounds.Left, bounds.Right), (float)rng.Range(bounds.Top, bounds.Bottom));
                var box = new RotatedRect(centre, sizeF, Tilt(rng));
                if (Fits(box, boxes, area, gapFraction * shortSide, checkArea: !allowOffEdge)) boxes.Add(box);
            }
        }

        // A photo running off the scan's edge stays only if it's still mostly on it.
        boxes = [.. boxes.Where(b => VisibleFraction(b, width, height) > 0.5)];
        if (boxes.Count == 0)
        {
            // Every page has a photo: one in the middle of the area if none fitted.
            float aspect = (float)PhotoAspect(rng), longEdge = shortSide * (float)rng.Range(0.3, 0.6);
            var centre = new Point2f(bounds.X + (bounds.Width / 2f), bounds.Y + (bounds.Height / 2f));
            boxes.Add(new RotatedRect(centre, aspect >= 1 ? new Size2f(longEdge, longEdge / aspect) : new Size2f(longEdge * aspect, longEdge), Tilt(rng)));
        }
        return boxes;
    }

    /// <summary>A print's shape (width / height): mostly 3:2 and 4:3, square now and then; either way up.</summary>
    private static double PhotoAspect(Rng rng)
    {
        double aspect = rng.Pick<double>([(1.5, 5), (4.0 / 3, 4), (1.0, 1.5), (7.0 / 5, 2), (1.25, 1), (16.0 / 9, 0.5)]);
        aspect *= rng.Range(0.97, 1.03);
        return rng.Chance(0.4) ? 1 / aspect : aspect;
    }

    /// <summary>How crooked the photo is stuck: mostly nearly straight.</summary>
    private static float Tilt(Rng rng) => (float)rng.Pick<double>(
    [
        (rng.Range(-1.5, 1.5), 60),
        (rng.Range(-5, 5), 30),
        (rng.Range(-12, 12), 10),
    ]);

    private static bool Fits(RotatedRect box, List<RotatedRect> placed, Point2f[] area, double gap, bool checkArea)
    {
        if (checkArea && box.Points().Any(p => Cv2.PointPolygonTest(area, p, false) < 0)) return false;
        var padded = new RotatedRect(box.Center, new Size2f(box.Size.Width + (float)gap, box.Size.Height + (float)gap), box.Angle);
        return placed.All(other => Cv2.RotatedRectangleIntersection(padded, other, out _) == RectanglesIntersectTypes.None);
    }

    private static double VisibleFraction(RotatedRect box, int width, int height)
    {
        var scan = new RotatedRect(new Point2f(width / 2f, height / 2f), new Size2f(width, height), 0);
        if (Cv2.RotatedRectangleIntersection(box, scan, out var points) == RectanglesIntersectTypes.None || points.Length < 3) return 0;
        return Cv2.ContourArea(Cv2.ConvexHull(points)) / (box.Size.Width * box.Size.Height);
    }

    /// <summary>Draws one print (picture, maybe a border, rounded corners, a shadow, photo corners) and marks it in the mask.</summary>
    private void DrawPhoto(Mat canvas, Mat mask, RotatedRect box, Scalar paper, Rng rng)
    {
        int w = Math.Max(8, (int)Math.Round(box.Size.Width)), h = Math.Max(8, (int)Math.Round(box.Size.Height));
        using Mat print = RenderPrint(w, h, paper, rng, out Mat shape);
        using (shape)
        {
            if (rng.Chance(0.35)) Shade(canvas, box.Points(), rng.Range(2, 9), rng.Range(0.12, 0.4), rng.Range(1, 7));

            // Place the print: rotate it about its centre and move it onto the box.
            using Mat transform = Cv2.GetRotationMatrix2D(new Point2f(w / 2f, h / 2f), -box.Angle, 1.0);
            transform.Set(0, 2, transform.Get<double>(0, 2) + box.Center.X - (w / 2.0));
            transform.Set(1, 2, transform.Get<double>(1, 2) + box.Center.Y - (h / 2.0));
            using Mat placed = new();
            using Mat alpha = new();
            Cv2.WarpAffine(print, placed, transform, canvas.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
            Cv2.WarpAffine(shape, alpha, transform, canvas.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(0));
            Blend(canvas, placed, alpha);
        }

        // The mask: the whole box (a rounded or covered corner still belongs to the photo), a little smaller so
        // photos that touch stay apart.
        float shrink = 2 * MaskGap * Supersample;
        var inner = new RotatedRect(box.Center, new Size2f(Math.Max(1, box.Size.Width - shrink), Math.Max(1, box.Size.Height - shrink)), box.Angle);
        Cv2.FillConvexPoly(mask, ToPoints(inner.Points()), Scalar.All(255), LineTypes.Link8);

        if (rng.Chance(0.12)) DrawPhotoCorners(canvas, box, rng);
    }

    /// <summary>The print as a float BGR image, and its shape (1 inside, 0 outside; rounded corners).</summary>
    private Mat RenderPrint(int w, int h, Scalar paper, Rng rng, out Mat shape)
    {
        double borderFraction = rng.Chance(0.4) ? rng.Range(0.03, 0.08) : 0;
        int border = (int)(Math.Min(w, h) * borderFraction);
        int innerW = Math.Max(4, w - (2 * border)), innerH = Math.Max(4, h - (2 * border));

        using Mat picture = library.Take(rng, innerW, innerH);
        using Mat toned = Tone(picture, paper, rng);

        Scalar borderColour = rng.Chance(0.15) ? Jitter(paper, rng, 10) : Jitter(Scalar.All(rng.Range(222, 250)), rng, 6);
        var print = new Mat(h, w, MatType.CV_32FC3, borderColour);
        using (Mat inside = print[new Rect(border, border, innerW, innerH)]) toned.CopyTo(inside);

        shape = new Mat(h, w, MatType.CV_32FC1, Scalar.All(1));
        if (rng.Chance(0.15))
        {
            int radius = Math.Max(2, (int)(Math.Min(w, h) * rng.Range(0.02, 0.07)));
            shape.SetTo(Scalar.All(0));
            Cv2.Rectangle(shape, new Rect(radius, 0, w - (2 * radius), h), Scalar.All(1), -1);
            Cv2.Rectangle(shape, new Rect(0, radius, w, h - (2 * radius)), Scalar.All(1), -1);
            foreach (var corner in new[] { new Point(radius, radius), new Point(w - radius - 1, radius), new Point(radius, h - radius - 1), new Point(w - radius - 1, h - radius - 1) })
            {
                Cv2.Circle(shape, corner, radius, Scalar.All(1), -1, LineTypes.AntiAlias);
            }
        }
        return print;
    }

    /// <summary>
    /// Ages the picture as old prints are: black and white, sepia, faded colour, or fresh. Some are made to look like
    /// the page's paper, the hard case for detection.
    /// </summary>
    private static Mat Tone(Mat picture, Scalar paper, Rng rng)
    {
        var result = new Mat();
        picture.ConvertTo(result, MatType.CV_32FC3);
        string kind = rng.Pick<string>([("bw", 3), ("sepia", 2), ("faded", 3), ("fresh", 2)]);
        if (kind is "bw" or "sepia")
        {
            using Mat gray = new();
            Cv2.CvtColor(result, gray, ColorConversionCodes.BGR2GRAY);
            Scalar tint = kind == "sepia" ? new Scalar(rng.Range(0.68, 0.82), rng.Range(0.85, 0.93), 1.0) : new Scalar(1, 1, 1);
            using Mat b = new(), g = new(), r = new();
            gray.ConvertTo(b, MatType.CV_32FC1, tint.Val0);
            gray.ConvertTo(g, MatType.CV_32FC1, tint.Val1);
            gray.ConvertTo(r, MatType.CV_32FC1, tint.Val2);
            Cv2.Merge([b, g, r], result);
        }
        else if (kind == "faded")
        {
            // Part of the colour drains away...
            using Mat gray = new();
            Cv2.CvtColor(result, gray, ColorConversionCodes.BGR2GRAY);
            using Mat gray3 = new();
            Cv2.Merge([gray, gray, gray], gray3);
            double saturation = rng.Range(0.35, 0.75);
            Cv2.AddWeighted(result, saturation, gray3, 1 - saturation, 0, result);
            // ...and a cast is left (magenta, yellow or cyan), as dyes fade unevenly.
            Cv2.Add(result, new Scalar(rng.Range(-18, 18), rng.Range(-18, 18), rng.Range(-18, 18)), result);
        }

        // Contrast and brightness: old prints are often flat.
        double contrast = rng.Range(0.55, 1.1), lift = rng.Range(-10, 45);
        result.ConvertTo(result, MatType.CV_32FC3, contrast, ((1 - contrast) * 128) + lift);

        if (rng.Chance(0.2))
        {
            // Close to the paper's colour: faded photos on cream or salmon pages.
            using Mat paperImage = new(result.Size(), MatType.CV_32FC3, paper);
            double pull = rng.Range(0.15, 0.5);
            Cv2.AddWeighted(result, 1 - pull, paperImage, pull, 0, result);
        }
        return result;
    }

    /// <summary>canvas = canvas × (1 − alpha) + image × alpha.</summary>
    private static void Blend(Mat canvas, Mat image, Mat alpha)
    {
        using Mat alpha3 = new();
        Cv2.Merge([alpha, alpha, alpha], alpha3);
        using Mat difference = new();
        Cv2.Subtract(image, canvas, difference);
        Cv2.Multiply(difference, alpha3, difference);
        Cv2.Add(canvas, difference, canvas);
    }

    /// <summary>Triangular paper corners holding the print, over its corners (they aren't photo, but the box is).</summary>
    private static void DrawPhotoCorners(Mat canvas, RotatedRect box, Rng rng)
    {
        Scalar colour = rng.Pick<Scalar>([(Scalar.All(rng.Range(10, 40)), 3), (Bgr(200, 170, 90), 1), (Scalar.All(rng.Range(225, 250)), 1)]);
        float size = Math.Min(box.Size.Width, box.Size.Height) * (float)rng.Range(0.08, 0.15);
        Point2f[] corners = box.Points();
        for (int i = 0; i < 4; i++)
        {
            Point2f corner = corners[i], next = corners[(i + 1) % 4], previous = corners[(i + 3) % 4];
            Point2f a = Towards(corner, next, size), b = Towards(corner, previous, size);
            Point2f outward = new(corner.X - ((a.X + b.X) / 2 - corner.X) * 0.3f, corner.Y - ((a.Y + b.Y) / 2 - corner.Y) * 0.3f);
            Cv2.FillConvexPoly(canvas, ToPoints([outward, Towards(outward, a, size * 1.4f), Towards(outward, b, size * 1.4f)]), colour, LineTypes.AntiAlias);
        }
    }

    private static Point2f Towards(Point2f from, Point2f to, float distance)
    {
        float dx = to.X - from.X, dy = to.Y - from.Y;
        float length = MathF.Max(1e-3f, MathF.Sqrt((dx * dx) + (dy * dy)));
        return new Point2f(from.X + (dx / length * distance), from.Y + (dy / length * distance));
    }

    /// <summary>Things on the page that aren't photos: handwritten captions and plain paper cut-outs or labels.</summary>
    private static void DrawCutOutsAndCaptions(Mat canvas, List<RotatedRect> photos, Point2f[] area, Rng rng)
    {
        if (photos.Count > 0 && rng.Chance(0.3))
        {
            var below = rng.Pick(photos);
            var ink = rng.Pick<Scalar>([(Bgr(30, 30, 40), 3), (Bgr(30, 40, 120), 2), (Scalar.All(rng.Range(220, 250)), 1)]);
            double scale = below.Size.Width / 260.0;
            var at = new Point(below.Center.X - (below.Size.Width * 0.35), below.Center.Y + (below.Size.Height / 2) + (40 * scale));
            // Text is drawn on 8-bit images only: draw a stencil, then blend the ink through it.
            using Mat stencil = new(canvas.Size(), MatType.CV_8UC1, Scalar.All(0));
            Cv2.PutText(stencil, rng.Pick(Captions), at, rng.Chance(0.6) ? HersheyFonts.HersheyScriptSimplex : HersheyFonts.HersheyComplex, scale, Scalar.All(255), Math.Max(1, (int)(2 * scale)), LineTypes.AntiAlias);
            using Mat alpha = new();
            stencil.ConvertTo(alpha, MatType.CV_32FC1, 1 / 255.0);
            using Mat inkImage = new(canvas.Size(), MatType.CV_32FC3, ink);
            Blend(canvas, inkImage, alpha);
        }

        if (rng.Chance(0.12))
        {
            // A plain coloured cut-out or label: smooth, so not a photo.
            var bounds = Cv2.BoundingRect(area);
            var centre = new Point2f((float)rng.Range(bounds.Left, bounds.Right), (float)rng.Range(bounds.Top, bounds.Bottom));
            var cutOut = new RotatedRect(centre, new Size2f(bounds.Width * (float)rng.Range(0.08, 0.3), bounds.Height * (float)rng.Range(0.05, 0.2)), (float)rng.Range(-10, 10));
            var padded = new RotatedRect(cutOut.Center, new Size2f(cutOut.Size.Width + 10, cutOut.Size.Height + 10), cutOut.Angle);
            if (photos.All(p => Cv2.RotatedRectangleIntersection(padded, p, out _) == RectanglesIntersectTypes.None))
            {
                Cv2.FillConvexPoly(canvas, ToPoints(cutOut.Points()), new Scalar(rng.Range(30, 245), rng.Range(30, 245), rng.Range(30, 245)), LineTypes.AntiAlias);
            }
        }
    }

    /// <summary>The scanner's own marks: uneven lighting and a little blur.</summary>
    private static void ApplyScanner(Mat canvas, Rng rng)
    {
        if (rng.Chance(0.6))
        {
            // Light falls off towards one side or the edges.
            using Mat light = new(canvas.Size(), MatType.CV_32FC1);
            double strength = rng.Range(0.03, 0.12);
            bool radial = rng.Chance(0.5);
            int rows = canvas.Rows, cols = canvas.Cols;
            var values = new float[rows * cols];
            double cx = cols / 2.0, cy = rows / 2.0, maxR = Math.Sqrt((cx * cx) + (cy * cy));
            double angle = rng.Range(0, 2 * Math.PI);
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    double t = radial
                        ? Math.Sqrt(((x - cx) * (x - cx)) + ((y - cy) * (y - cy))) / maxR
                        : 0.5 + ((((x - cx) * Math.Cos(angle)) + ((y - cy) * Math.Sin(angle))) / (2 * maxR));
                    values[(y * cols) + x] = (float)(1 - (strength * t));
                }
            }
            Noise.Copy(values, light);
            using Mat light3 = new();
            Cv2.Merge([light, light, light], light3);
            Cv2.Multiply(canvas, light3, canvas);
        }
        double blur = rng.Range(0, 1.4);
        if (blur > 0.3) Cv2.GaussianBlur(canvas, canvas, new Size(0, 0), blur);
    }

    /// <summary>Scales the page down to its final size, centres it on a black square, and adds sensor noise and JPEG artefacts.</summary>
    private AlbumPage Finish(Mat canvas, Mat mask, List<RotatedRect> boxes, Rng rng)
    {
        int finalW = canvas.Width / Supersample, finalH = canvas.Height / Supersample;
        int offsetX = (Size - finalW) / 2, offsetY = (Size - finalH) / 2;

        using Mat small = new();
        Cv2.Resize(canvas, small, new Size(finalW, finalH), 0, 0, InterpolationFlags.Area);
        using Mat grain = new(small.Size(), MatType.CV_32FC3);
        Noise.FillNormal(grain, rng, 0, rng.Range(0.5, 3.0));
        Cv2.Add(small, grain, small);
        using Mat small8 = new();
        small.ConvertTo(small8, MatType.CV_8UC3);

        Cv2.ImEncode(".jpg", small8, out byte[] jpeg, [new ImageEncodingParam(ImwriteFlags.JpegQuality, rng.Range(55, 96))]);
        using Mat compressed = Cv2.ImDecode(jpeg, ImreadModes.Color);

        var photoBoxes = boxes.Select(b => new PhotoBox(
            (b.Center.X / Supersample) + offsetX, (b.Center.Y / Supersample) + offsetY,
            b.Size.Width / Supersample, b.Size.Height / Supersample, b.Angle)).ToList();

        // Freed here unless the page takes them over.
        Mat? image = new(Size, Size, MatType.CV_8UC3, Scalar.All(0));
        Mat? maskOut = null;
        try
        {
            using (Mat scan = image[new Rect(offsetX, offsetY, finalW, finalH)]) compressed.CopyTo(scan);
            maskOut = new Mat(Size, Size, MatType.CV_8UC1, Scalar.All(0));
            using Mat smallMask = new();
            Cv2.Resize(mask, smallMask, new Size(finalW, finalH), 0, 0, InterpolationFlags.Area);
            Cv2.Threshold(smallMask, smallMask, 127, 255, ThresholdTypes.Binary);
            using (Mat scan = maskOut[new Rect(offsetX, offsetY, finalW, finalH)]) smallMask.CopyTo(scan);

            var page = new AlbumPage(image, maskOut, photoBoxes);
            image = null;
            maskOut = null;
            return page;
        }
        finally
        {
            image?.Dispose();
            maskOut?.Dispose();
        }
    }

    private static Point[] ToPoints(Point2f[] points) => [.. points.Select(p => new Point((int)Math.Round(p.X), (int)Math.Round(p.Y)))];
}
