using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;

namespace PhotoCropper.DatasetGenerator;

internal static class Program
{
    private static readonly HttpClient HttpClient = new HttpClient();
    private static readonly Random Rng = new Random();

    public static async Task Main(string[] args)
    {
        Console.WriteLine("=================================================");
        Console.WriteLine(" PhotoCropper Realistic Dataset Generator (C#)");
        Console.WriteLine("=================================================");

        string baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dataset");
        string rawPhotosDir = Path.Combine(baseDir, "raw_photos");
        string trainImagesDir = Path.Combine(baseDir, "train", "images");
        string trainLabelsDir = Path.Combine(baseDir, "train", "labels");
        string valImagesDir = Path.Combine(baseDir, "val", "images");
        string valLabelsDir = Path.Combine(baseDir, "val", "labels");

        Directory.CreateDirectory(rawPhotosDir);
        Directory.CreateDirectory(trainImagesDir);
        Directory.CreateDirectory(trainLabelsDir);
        Directory.CreateDirectory(valImagesDir);
        Directory.CreateDirectory(valLabelsDir);

        int targetRawPhotos = 120;
        int totalScansToGenerate = 800; // 640 train, 160 val

        await DownloadSamplePhotosAsync(rawPhotosDir, targetRawPhotos);

        var photoFiles = Directory.GetFiles(rawPhotosDir, "*.jpg");
        if (photoFiles.Length == 0)
        {
            Console.WriteLine("[!] Error: No raw photos available.");
            return;
        }

        Console.WriteLine($"\n[*] Loaded {photoFiles.Length} source photos.");
        Console.WriteLine($"[*] Generating {totalScansToGenerate} realistic non-overlapping scans...");

        for (int i = 0; i < totalScansToGenerate; i++)
        {
            bool isVal = (i % 5 == 0); // 20% validation split
            string imgDir = isVal ? valImagesDir : trainImagesDir;
            string lblDir = isVal ? valLabelsDir : trainLabelsDir;
            string filename = $"scan_{i:D5}";

            GenerateSyntheticScan(photoFiles, imgDir, lblDir, filename);

            if ((i + 1) % 50 == 0 || i == totalScansToGenerate - 1)
            {
                Console.WriteLine($"[*] Generated {i + 1}/{totalScansToGenerate} scans ({(i + 1) * 100 / totalScansToGenerate}%)...");
            }
        }

        // Generate data.yaml for Ultralytics YOLO-OBB training
        string yamlPath = Path.Combine(baseDir, "data.yaml");
        string yamlContent = $@"path: {baseDir.Replace('\\', '/')}
train: train/images
val: val/images

names:
  0: photo
";
        await File.WriteAllTextAsync(yamlPath, yamlContent);

        Console.WriteLine("\n[+] Dataset generation complete!");
        Console.WriteLine($"[+] Config saved to: {yamlPath}");
    }

    private static async Task DownloadSamplePhotosAsync(string targetDir, int count)
    {
        var existing = Directory.GetFiles(targetDir, "*.jpg");
        int needed = count - existing.Length;
        if (needed <= 0)
        {
            Console.WriteLine($"[*] Already have {existing.Length} source photos in cache.");
            return;
        }

        Console.WriteLine($"[*] Downloading {needed} source photos from Lorem Picsum...");
        for (int i = 0; i < needed; i++)
        {
            try
            {
                int width = Rng.Next(900, 1500);
                int height = Rng.Next(900, 1500);
                string url = $"https://picsum.photos/{width}/{height}";

                byte[] data = await HttpClient.GetByteArrayAsync(new Uri(url));
                string filePath = Path.Combine(targetDir, $"photo_{existing.Length + i:D4}.jpg");
                await File.WriteAllBytesAsync(filePath, data);

                if ((i + 1) % 10 == 0 || i == needed - 1)
                {
                    Console.WriteLine($"    Downloaded {i + 1}/{needed} photos...");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    Warning downloading image: {ex.Message}");
            }
        }
    }

    private static void GenerateSyntheticScan(string[] photoFiles, string imgDir, string lblDir, string filename)
    {
        // Flatbed scanner dimensions (A4 size)
        bool landscapeBed = Rng.NextDouble() > 0.5;
        int bedW = landscapeBed ? 2400 : 1700;
        int bedH = landscapeBed ? 1700 : 2400;

        // Background scanner color (mostly white/off-white flatbed glass, occasionally dark lid)
        MCvScalar bgColor;
        double bgType = Rng.NextDouble();
        if (bgType < 0.75)
        {
            byte v = (byte)Rng.Next(238, 255);
            byte g = (byte)Math.Clamp(v - Rng.Next(0, 4), 0, 255);
            byte r = (byte)Math.Clamp(v - Rng.Next(0, 4), 0, 255);
            bgColor = new MCvScalar(v, g, r);
        }
        else if (bgType < 0.90)
        {
            byte v = (byte)Rng.Next(15, 40);
            bgColor = new MCvScalar(v, v, v);
        }
        else
        {
            byte b = (byte)Rng.Next(215, 235);
            byte g = (byte)Rng.Next(220, 240);
            byte r = (byte)Rng.Next(225, 245);
            bgColor = new MCvScalar(b, g, r);
        }

        using var scanBed = new Mat(bedH, bedW, DepthType.Cv8U, 3);
        scanBed.SetTo(bgColor);

        AddScannerBedImperfections(scanBed);

        // Realistic number of photos placed side-by-side (1 to 4)
        int targetPhotos = Rng.Next(1, 5);
        var placedBoxes = new List<RotatedRect>();
        var annotations = new List<string>();

        int attempts = 0;
        while (placedBoxes.Count < targetPhotos && attempts < 40)
        {
            attempts++;
            string sourcePath = photoFiles[Rng.Next(photoFiles.Length)];
            using var photoMat = CvInvoke.Imread(sourcePath, ImreadModes.AnyColor);
            if (photoMat.IsEmpty) continue;

            // Standard photo print aspect ratios (4x6, 5x7, square)
            int targetW = Rng.Next(500, 850);
            int targetH = (int)(photoMat.Height * ((double)targetW / photoMat.Width));
            if (targetH > 1050)
            {
                targetH = 1050;
                targetW = (int)(photoMat.Width * ((double)targetH / photoMat.Height));
            }

            using var resizedPhoto = new Mat();
            CvInvoke.Resize(photoMat, resizedPhoto, new Size(targetW, targetH), 0, 0, Inter.Area);

            using var finalPhoto = new Mat();
            if (Rng.NextDouble() > 0.35)
            {
                int border = Rng.Next(10, 28);
                CvInvoke.CopyMakeBorder(resizedPhoto, finalPhoto, border, border, border, border, BorderType.Constant, new MCvScalar(250, 250, 250));
            }
            else
            {
                resizedPhoto.CopyTo(finalPhoto);
            }

            ApplyPhotoImperfections(finalPhoto);

            // REALISTIC TILT ANGLES:
            // 75% of photos are almost straight (-4° to +4°)
            // 20% have a slight human tilt (-12° to +12°)
            // 5% are rotated landscape/portrait (~90° +- 4°)
            float angleDeg;
            double angleRoll = Rng.NextDouble();
            if (angleRoll < 0.75)
            {
                angleDeg = (float)((Rng.NextDouble() * 8.0) - 4.0);
            }
            else if (angleRoll < 0.95)
            {
                angleDeg = (float)((Rng.NextDouble() * 24.0) - 12.0);
            }
            else
            {
                float baseAngle = Rng.NextDouble() > 0.5 ? 90.0f : -90.0f;
                angleDeg = baseAngle + (float)((Rng.NextDouble() * 8.0) - 4.0);
            }

            int margin = 60;
            int cx = Rng.Next(finalPhoto.Width / 2 + margin, bedW - finalPhoto.Width / 2 - margin);
            int cy = Rng.Next(finalPhoto.Height / 2 + margin, bedH - finalPhoto.Height / 2 - margin);

            var candidateRect = new RotatedRect(new PointF(cx, cy), new SizeF(finalPhoto.Width, finalPhoto.Height), angleDeg);

            // Check non-overlapping collision with previously placed photos (with padding)
            if (CheckOverlap(candidateRect, placedBoxes, padding: 45))
            {
                continue;
            }

            placedBoxes.Add(candidateRect);
            OverlayRotatedPhoto(scanBed, finalPhoto, candidateRect);

            // Save YOLO-OBB 4-corner normalized polygon
            PointF[] vertices = candidateRect.GetVertices();
            string labelLine = FormattableString.Invariant($"0 {vertices[0].X / bedW:F6} {vertices[0].Y / bedH:F6} {vertices[1].X / bedW:F6} {vertices[1].Y / bedH:F6} {vertices[2].X / bedW:F6} {vertices[2].Y / bedH:F6} {vertices[3].X / bedW:F6} {vertices[3].Y / bedH:F6}");
            annotations.Add(labelLine);
        }

        string imagePath = Path.Combine(imgDir, $"{filename}.jpg");
        string labelPath = Path.Combine(lblDir, $"{filename}.txt");

        CvInvoke.Imwrite(imagePath, scanBed);
        File.WriteAllLines(labelPath, annotations);
    }

    private static bool CheckOverlap(RotatedRect candidate, List<RotatedRect> existingBoxes, int padding)
    {
        var candPadded = new RotatedRect(candidate.Center, new SizeF(candidate.Size.Width + padding, candidate.Size.Height + padding), candidate.Angle);
        Rectangle candBound = CvInvoke.BoundingRectangle(new Emgu.CV.Util.VectorOfPoint(Array.ConvertAll(candPadded.GetVertices(), Point.Round)));

        foreach (var exist in existingBoxes)
        {
            var existPadded = new RotatedRect(exist.Center, new SizeF(exist.Size.Width + padding, exist.Size.Height + padding), exist.Angle);
            Rectangle existBound = CvInvoke.BoundingRectangle(new Emgu.CV.Util.VectorOfPoint(Array.ConvertAll(existPadded.GetVertices(), Point.Round)));

            if (candBound.IntersectsWith(existBound))
            {
                return true;
            }
        }
        return false;
    }

    private static void AddScannerBedImperfections(Mat bed)
    {
        int specks = Rng.Next(5, 20);
        for (int i = 0; i < specks; i++)
        {
            int sx = Rng.Next(bed.Width);
            int sy = Rng.Next(bed.Height);
            int r = Rng.Next(1, 3);
            byte dustColor = (byte)Rng.Next(100, 180);
            CvInvoke.Circle(bed, new Point(sx, sy), r, new MCvScalar(dustColor, dustColor, dustColor), -1);
        }
    }

    private static void ApplyPhotoImperfections(Mat photo)
    {
        double alpha = 0.90 + (Rng.NextDouble() * 0.2);
        int beta = Rng.Next(-10, 10);
        photo.ConvertTo(photo, DepthType.Cv8U, alpha, beta);

        if (Rng.NextDouble() < 0.15)
        {
            CvInvoke.GaussianBlur(photo, photo, new Size(3, 3), 0.7);
        }
    }

    private static void OverlayRotatedPhoto(Mat bed, Mat photo, RotatedRect rect)
    {
        int maxDim = (int)(Math.Sqrt(photo.Width * photo.Width + photo.Height * photo.Height) + 40);
        using var paddedPhoto = new Mat(maxDim, maxDim, DepthType.Cv8U, 4);
        paddedPhoto.SetTo(new MCvScalar(0, 0, 0, 0));

        int offsetX = (maxDim - photo.Width) / 2;
        int offsetY = (maxDim - photo.Height) / 2;

        using var photoBgra = new Mat();
        CvInvoke.CvtColor(photo, photoBgra, ColorConversion.Bgr2Bgra);

        var roi = new Rectangle(offsetX, offsetY, photo.Width, photo.Height);
        using var targetRoi = new Mat(paddedPhoto, roi);
        photoBgra.CopyTo(targetRoi);

        var padCenter = new PointF(maxDim / 2.0f, maxDim / 2.0f);
        using var rotMatPadded = new Mat();
        CvInvoke.GetRotationMatrix2D(padCenter, rect.Angle, 1.0, rotMatPadded);

        using var rotatedCanvas = new Mat(maxDim, maxDim, DepthType.Cv8U, 4);
        CvInvoke.WarpAffine(paddedPhoto, rotatedCanvas, rotMatPadded, new Size(maxDim, maxDim), Inter.Linear, Warp.Default, BorderType.Constant, new MCvScalar(0, 0, 0, 0));

        int startX = (int)(rect.Center.X - maxDim / 2.0f);
        int startY = (int)(rect.Center.Y - maxDim / 2.0f);

        int srcX = 0, srcY = 0;
        int w = maxDim, h = maxDim;

        if (startX < 0) { srcX = -startX; w += startX; startX = 0; }
        if (startY < 0) { srcY = -startY; h += startY; startY = 0; }
        if (startX + w > bed.Width) { w = bed.Width - startX; }
        if (startY + h > bed.Height) { h = bed.Height - startY; }

        if (w <= 0 || h <= 0) return;

        var bedRoiRect = new Rectangle(startX, startY, w, h);
        var srcRoiRect = new Rectangle(srcX, srcY, w, h);

        using var bedRoi = new Mat(bed, bedRoiRect);
        using var srcRoi = new Mat(rotatedCanvas, srcRoiRect);

        BlendBgraOntoBgr(srcRoi, bedRoi);
    }

    private static unsafe void BlendBgraOntoBgr(Mat srcBgra, Mat dstBgr)
    {
        int rows = srcBgra.Rows;
        int cols = srcBgra.Cols;

        byte* srcPtr = (byte*)srcBgra.DataPointer;
        byte* dstPtr = (byte*)dstBgr.DataPointer;
        int srcStep = (int)srcBgra.Step;
        int dstStep = (int)dstBgr.Step;

        for (int y = 0; y < rows; y++)
        {
            byte* sRow = srcPtr + (y * srcStep);
            byte* dRow = dstPtr + (y * dstStep);

            for (int x = 0; x < cols; x++)
            {
                byte alpha = sRow[x * 4 + 3];
                if (alpha == 0) continue;

                if (alpha == 255)
                {
                    dRow[x * 3 + 0] = sRow[x * 4 + 0];
                    dRow[x * 3 + 1] = sRow[x * 4 + 1];
                    dRow[x * 3 + 2] = sRow[x * 4 + 2];
                }
                else
                {
                    float a = alpha / 255.0f;
                    float invA = 1.0f - a;

                    dRow[x * 3 + 0] = (byte)(sRow[x * 4 + 0] * a + dRow[x * 3 + 0] * invA);
                    dRow[x * 3 + 1] = (byte)(sRow[x * 4 + 1] * a + dRow[x * 3 + 1] * invA);
                    dRow[x * 3 + 2] = (byte)(sRow[x * 4 + 2] * a + dRow[x * 3 + 2] * invA);
                }
            }
        }
    }
}
