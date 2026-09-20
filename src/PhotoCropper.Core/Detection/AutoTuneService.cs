using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using Emgu.CV.Util;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

public static class AutoTuneService
{
    private static readonly double[] SweepTolerances = [15, 25, 35, 45, 55, 65, 75, 10, 85];
    private static readonly double[] SweepCannyLows = [10, 20, 30, 40];
    private static readonly double[] SweepMinAreaFactors = [0.005, 0.01, 0.05, 0.10];

    public static AutoTuneResult Tune(Mat source, DetectionOptions currentOptions)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(currentOptions);

        int originalW = source.Width;
        int originalH = source.Height;
        double scale = Math.Min(1.0, 2000.0 / Math.Max(originalW, originalH));
        int scaledW = (int)Math.Round(originalW * scale);
        int scaledH = (int)Math.Round(originalH * scale);

        using Mat detMat = new();
        if (scale < 0.999)
        {
            CvInvoke.Resize(source, detMat, new Size(scaledW, scaledH), 0, 0, Inter.Area);
        }
        else
        {
            source.CopyTo(detMat);
        }

        using Mat detHsv = new();
        CvInvoke.CvtColor(detMat, detHsv, ColorConversion.Bgr2Hsv);

        MCvScalar avgBgColorHsv = currentOptions.CustomBackgroundColorHsv ?? BackgroundAnalyzer.SampleBackgroundColor(detHsv);
        MCvScalar bgBgr = BackgroundAnalyzer.HsvToBgr(avgBgColorHsv);
        var bezel = BackgroundAnalyzer.DetectBezelMargins(detMat, avgBgColorHsv, currentOptions.BackgroundTolerance);
        if (bezel.Top > 0 || bezel.Bottom > 0 || bezel.Left > 0 || bezel.Right > 0)
        {
            if (bezel.Top > 0) CvInvoke.Rectangle(detMat, new Rectangle(0, 0, detMat.Width, bezel.Top), bgBgr, -1);
            if (bezel.Bottom > 0) CvInvoke.Rectangle(detMat, new Rectangle(0, detMat.Height - bezel.Bottom, detMat.Width, bezel.Bottom), bgBgr, -1);
            if (bezel.Left > 0) CvInvoke.Rectangle(detMat, new Rectangle(0, 0, bezel.Left, detMat.Height), bgBgr, -1);
            if (bezel.Right > 0) CvInvoke.Rectangle(detMat, new Rectangle(detMat.Width - bezel.Right, 0, bezel.Right, detMat.Height), bgBgr, -1);
            CvInvoke.CvtColor(detMat, detHsv, ColorConversion.Bgr2Hsv);
        }

        int pad = (int)Math.Round(Math.Max(originalW, originalH) * 0.05);
        int scaledPad = (int)Math.Round(pad * scale);

        double baselineScore = EvaluateConfiguration(detMat, detHsv, avgBgColorHsv, currentOptions.BackgroundTolerance, currentOptions.CannyLowThreshold, currentOptions.CannyHighThreshold, currentOptions.MinAreaFactor, currentOptions.MaxAreaFactor, scaledPad, scaledW, scaledH, originalW, originalH, scale, out int baselineCount);

        double bestScore = baselineScore;
        int bestCount = baselineCount;
        DetectionOptions bestOptions = currentOptions with { };

        var edgeMaps = new List<Mat>();
        try
        {
            var edgeMapDict = new Dictionary<double, Mat>();
            foreach (double cannyLow in SweepCannyLows)
            {
                var map = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(detMat, cannyLow, cannyLow * 2.5);
                edgeMaps.Add(map);
                edgeMapDict[cannyLow] = map;
            }

            using Mat foreground = new();

            foreach (double cannyLow in SweepCannyLows)
            {
                Mat edgeMap = edgeMapDict[cannyLow];
                double cannyHigh = cannyLow * 2.5;

                foreach (double tol in SweepTolerances)
                {
                    ForegroundMaskGenerator.PopulateForegroundMask(
                        detMat,
                        foreground,
                        avgBgColorHsv,
                        tol,
                        cannyLow,
                        cannyHigh,
                        edgeMap,
                        detHsv);

                    foreach (double minArea in SweepMinAreaFactors)
                    {
                        var passCandidates = CandidateExtractor.ExtractCandidates(
                            foreground,
                            scaledPad,
                            scaledW,
                            scaledH,
                            minArea,
                            currentOptions.MaxAreaFactor);

                        var fullCandidates = MapCandidatesToFullRes(passCandidates, scale, originalW, originalH);
                        var accepted = CandidateResolutionFilter.FilterCandidates(fullCandidates);

                        double score = CalculateScore(accepted, originalW, originalH);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestCount = accepted.Count;
                            bestOptions = currentOptions with
                            {
                                BackgroundTolerance = tol,
                                CannyLowThreshold = cannyLow,
                                CannyHighThreshold = cannyHigh,
                                MinAreaFactor = minArea
                            };
                        }
                    }
                }
            }
        }
        finally
        {
            foreach (var mat in edgeMaps)
            {
                mat.Dispose();
            }
        }

        bool improved = bestScore > baselineScore + 0.01;
        return new AutoTuneResult(bestOptions, bestCount, bestScore, improved);
    }

    private static double EvaluateConfiguration(
        Mat detMat,
        Mat detHsv,
        MCvScalar avgBgColorHsv,
        double tolerance,
        double cannyLow,
        double cannyHigh,
        double minAreaFactor,
        double maxAreaFactor,
        int scaledPad,
        int scaledW,
        int scaledH,
        int originalW,
        int originalH,
        double scale,
        out int count)
    {
        using Mat edgeMap = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(detMat, cannyLow, cannyHigh);
        using Mat foreground = new();
        ForegroundMaskGenerator.PopulateForegroundMask(
            detMat,
            foreground,
            avgBgColorHsv,
            tolerance,
            cannyLow,
            cannyHigh,
            edgeMap,
            detHsv);

        var passCandidates = CandidateExtractor.ExtractCandidates(
            foreground,
            scaledPad,
            scaledW,
            scaledH,
            minAreaFactor,
            maxAreaFactor);

        var fullCandidates = MapCandidatesToFullRes(passCandidates, scale, originalW, originalH);
        var accepted = CandidateResolutionFilter.FilterCandidates(fullCandidates);
        count = accepted.Count;
        return CalculateScore(accepted, originalW, originalH);
    }

    private static List<CropCandidate> MapCandidatesToFullRes(IReadOnlyList<CropCandidate> candidates, double scale, int originalW, int originalH)
    {
        if (scale >= 0.999)
        {
            return new List<CropCandidate>(candidates);
        }

        double invScale = 1.0 / scale;
        var fullCandidates = new List<CropCandidate>(candidates.Count);

        foreach (var cand in candidates)
        {
            PointF fullCenter = new((float)(cand.Rotated.Center.X * invScale), (float)(cand.Rotated.Center.Y * invScale));
            SizeF fullSize = new SizeF((float)(cand.Rotated.Size.Width * invScale), (float)(cand.Rotated.Size.Height * invScale));
            RotatedRect fullRr = new(fullCenter, fullSize, cand.Rotated.Angle);

            PointF[] fullVerts = fullRr.GetVertices();
            Point[] fullPoints = new Point[4];
            for (int p = 0; p < 4; p++)
            {
                fullPoints[p] = new Point(
                    Math.Clamp((int)Math.Round(fullVerts[p].X), 0, originalW - 1),
                    Math.Clamp((int)Math.Round(fullVerts[p].Y), 0, originalH - 1)
                );
            }

            using VectorOfPoint fullShape = new(fullPoints);
            double fullArea = (double)fullSize.Width * fullSize.Height;
            double score = fullArea * Math.Pow(cand.Rectangularity, 3) * Math.Pow(cand.Convexity, 2);

            fullCandidates.Add(new CropCandidate(
                fullPoints,
                CvInvoke.BoundingRectangle(fullShape),
                score,
                fullRr,
                fullArea,
                cand.Rectangularity,
                cand.Convexity));
        }

        return fullCandidates;
    }

    private static double CalculateScore(IReadOnlyList<CropCandidate> accepted, int originalW, int originalH)
    {
        if (accepted.Count == 0) return 0.0;

        double totalArea = (double)originalW * originalH;
        double coveredArea = 0.0;
        double scoreSum = 0.0;

        foreach (var cand in accepted)
        {
            coveredArea += cand.Area;
            scoreSum += cand.Score;
        }

        // Single candidate occupying virtually entire scan bed is likely background/border false positive
        if (accepted.Count == 1 && (coveredArea / totalArea) > 0.75)
        {
            scoreSum *= 0.001;
        }

        // Discrete bonus for separating into valid multiple photos
        if (accepted.Count >= 1 && accepted.Count <= 12)
        {
            scoreSum += accepted.Count * 1000.0;
        }

        return scoreSum;
    }
}
