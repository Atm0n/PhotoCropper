using System;
using System.Collections.Generic;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using PhotoCropper.Core.Models;
using System.Drawing;

namespace PhotoCropper.Core.Detection;

public sealed class AutoTuneService : IDisposable
{
    private static readonly double[] SweepTolerances = [8, 12, 16, 20, 25];
    private static readonly double[] SweepCannyLows = [20, 30, 50, 70];
    private static readonly double[] SweepMinAreaFactors = [0.005, 0.01, 0.02, 0.03];

    private readonly Mat _source;
    private readonly DetectionOptions _currentOptions;
    private readonly int _minExpected;
    private readonly int _maxExpected;

    private int _originalW;
    private int _originalH;
    private double _scale;
    private int _scaledW;
    private int _scaledH;
    private int _scaledPad;

    private Mat? _detMat;
    private Mat? _detHsv;
    private MCvScalar _avgBgColorHsv;
    private MCvScalar _bgBgr;

    private double _bestScore;
    private int _bestCount;
    private DetectionOptions _bestOptions;

    private AutoTuneService(Mat source, DetectionOptions currentOptions, int minExpected, int maxExpected)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _currentOptions = currentOptions ?? throw new ArgumentNullException(nameof(currentOptions));
        _minExpected = minExpected;
        _maxExpected = maxExpected;
        _bestOptions = currentOptions with { };
    }

    public static AutoTuneResult Tune(Mat source, DetectionOptions currentOptions, int minExpected = 1, int maxExpected = int.MaxValue)
    {
        using var service = new AutoTuneService(source, currentOptions, minExpected, maxExpected);
        return service.Execute();
    }

    private AutoTuneResult Execute()
    {
        InitializeDimensions();

        using Mat rawDetMat = new();
        if (_scale < 0.999)
        {
            CvInvoke.Resize(_source, rawDetMat, new Size(_scaledW, _scaledH), 0, 0, Inter.Area);
        }
        else
        {
            _source.CopyTo(rawDetMat);
        }

        _detMat = PhotoCropperEngine.NormalizeToBgr(rawDetMat);
        _detHsv = new Mat();
        CvInvoke.CvtColor(_detMat, _detHsv, ColorConversion.Bgr2Hsv);

        PrepareBackground();
        NeutralizeBezels();

        double baselineScore = EvaluateConfiguration(
            _currentOptions.BackgroundTolerance, 
            _currentOptions.CannyLowThreshold, 
            _currentOptions.CannyHighThreshold, 
            _currentOptions.MinAreaFactor, 
            out int baselineCount);

        _bestScore = baselineScore;
        _bestCount = baselineCount;

        PerformSweeps();

        bool improved = _bestScore > baselineScore + 0.01;
        return new AutoTuneResult(_bestOptions, _bestCount, _bestScore, improved);
    }

    private void InitializeDimensions()
    {
        _originalW = _source.Width;
        _originalH = _source.Height;
        _scale = Math.Min(1.0, 2000.0 / Math.Max(_originalW, _originalH));
        _scaledW = (int)Math.Round(_originalW * _scale);
        _scaledH = (int)Math.Round(_originalH * _scale);
        int pad = (int)Math.Round(Math.Max(_originalW, _originalH) * 0.05);
        _scaledPad = (int)Math.Round(pad * _scale);
    }

    private void PrepareBackground()
    {
        _avgBgColorHsv = _currentOptions.CustomBackgroundColorHsv ?? BackgroundAnalyzer.SampleBackgroundColor(_detHsv!);
        _bgBgr = BackgroundAnalyzer.HsvToBgr(_avgBgColorHsv);
    }

    private void NeutralizeBezels()
    {
        var (Top, Bottom, Left, Right) = BackgroundAnalyzer.DetectBezelMargins(_detMat!, _avgBgColorHsv, _currentOptions.BackgroundTolerance);
        if (Top > 0 || Bottom > 0 || Left > 0 || Right > 0)
        {
            if (Top > 0) CvInvoke.Rectangle(_detMat, new Rectangle(0, 0, _detMat!.Width, Top), _bgBgr, -1);
            if (Bottom > 0) CvInvoke.Rectangle(_detMat, new Rectangle(0, _detMat!.Height - Bottom, _detMat!.Width, Bottom), _bgBgr, -1);
            if (Left > 0) CvInvoke.Rectangle(_detMat, new Rectangle(0, 0, Left, _detMat!.Height), _bgBgr, -1);
            if (Right > 0) CvInvoke.Rectangle(_detMat, new Rectangle(_detMat!.Width - Right, 0, Right, _detMat!.Height), _bgBgr, -1);
            CvInvoke.CvtColor(_detMat, _detHsv, ColorConversion.Bgr2Hsv);
        }
    }

    private void PerformSweeps()
    {
        var edgeMaps = new List<Mat>();
        try
        {
            var edgeMapDict = new Dictionary<double, Mat>();
            foreach (double cannyLow in SweepCannyLows)
            {
                var map = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(_detMat!, cannyLow, cannyLow * 2.5);
                edgeMaps.Add(map);
                edgeMapDict[cannyLow] = map;
            }

            using Mat foreground = new();

            // 1. HSV Background Subtraction Sweep
            foreach (double cannyLow in SweepCannyLows)
            {
                Mat edgeMap = edgeMapDict[cannyLow];
                double cannyHigh = cannyLow * 2.5;

                foreach (double tol in SweepTolerances)
                {
                    ForegroundMaskGenerator.PopulateForegroundMask(
                        _detMat!, foreground, _avgBgColorHsv, tol, cannyLow, cannyHigh, edgeMap, _detHsv!);

                    foreach (double minArea in SweepMinAreaFactors)
                    {
                        EvaluateCandidates(foreground, cannyLow, cannyHigh, minArea, tol);
                    }
                }
            }

            // 2. Otsu Fallback Sweep
            if (_bestCount < _minExpected)
            {
                bool isLightBg = _avgBgColorHsv.V2 > 120;
                foreach (double cannyLow in SweepCannyLows)
                {
                    Mat edgeMap = edgeMapDict[cannyLow];
                    double cannyHigh = cannyLow * 2.5;

                    ForegroundMaskGenerator.PopulateOtsuForegroundMask(
                        _detMat!, foreground, cannyLow, cannyHigh, isLightBg, edgeMap);

                    foreach (double minArea in SweepMinAreaFactors)
                    {
                        EvaluateCandidates(foreground, cannyLow, cannyHigh, minArea, _bestOptions.BackgroundTolerance);
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
    }

    private void EvaluateCandidates(Mat foreground, double cannyLow, double cannyHigh, double minArea, double tol)
    {
        var passCandidates = CandidateExtractor.ExtractCandidates(
            foreground, _scaledPad, _scaledW, _scaledH, minArea, _currentOptions.MaxAreaFactor, _detMat!, _bgBgr);

        var fullCandidates = MapCandidatesToFullRes(passCandidates, _scale, _originalW, _originalH);
        var accepted = CandidateResolutionFilter.FilterCandidates(fullCandidates);

        double score = CalculateScore(accepted);
        if (score > _bestScore)
        {
            _bestScore = score;
            _bestCount = accepted.Count;
            _bestOptions = _currentOptions with
            {
                BackgroundTolerance = tol,
                CannyLowThreshold = cannyLow,
                CannyHighThreshold = cannyHigh,
                MinAreaFactor = minArea
            };
        }
    }

    private double EvaluateConfiguration(
        double tol, double cannyLow, double cannyHigh, double minArea, out int count)
    {
        using Mat edgeMap = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(_detMat!, cannyLow, cannyHigh);
        using Mat foreground = new();

        ForegroundMaskGenerator.PopulateForegroundMask(
            _detMat!, foreground, _avgBgColorHsv, tol, cannyLow, cannyHigh, edgeMap, _detHsv!);

        var passCandidates = CandidateExtractor.ExtractCandidates(
            foreground, _scaledPad, _scaledW, _scaledH, minArea, _currentOptions.MaxAreaFactor, _detMat!, _bgBgr);

        var fullCandidates = MapCandidatesToFullRes(passCandidates, _scale, _originalW, _originalH);
        var accepted = CandidateResolutionFilter.FilterCandidates(fullCandidates);

        count = accepted.Count;
        return CalculateScore(accepted);
    }

    private static List<CropCandidate> MapCandidatesToFullRes(IReadOnlyList<CropCandidate> candidates, double scale, int originalW, int originalH)
    {
        var fullCandidates = new List<CropCandidate>(candidates.Count);
        double invScale = 1.0 / scale;

        foreach (var cand in candidates)
        {
            var center = new PointF((float)(cand.Rotated.Center.X * invScale), (float)(cand.Rotated.Center.Y * invScale));
            var size = new SizeF((float)(cand.Rotated.Size.Width * invScale), (float)(cand.Rotated.Size.Height * invScale));
            var fullRect = new RotatedRect(center, size, cand.Rotated.Angle);

            var bRect = fullRect.MinAreaRect();
            int x = Math.Max(0, bRect.X);
            int y = Math.Max(0, bRect.Y);
            int w = Math.Min(originalW - x, bRect.Width);
            int h = Math.Min(originalH - y, bRect.Height);
            var rect = new Rectangle(x, y, w, h);

            Point[]? fullShape = null;
            if (cand.ShapePoints != null && cand.ShapePoints.Length > 0)
            {
                fullShape = new Point[cand.ShapePoints.Length];
                for (int i = 0; i < cand.ShapePoints.Length; i++)
                {
                    fullShape[i] = new Point(
                        (int)Math.Round(cand.ShapePoints[i].X * invScale),
                        (int)Math.Round(cand.ShapePoints[i].Y * invScale));
                }
            }

            fullCandidates.Add(new CropCandidate
            {
                Rotated = fullRect,
                Rect = rect,
                Area = fullRect.Size.Width * fullRect.Size.Height,
                ShapePoints = fullShape ?? []
            });
        }
        return fullCandidates;
    }

    private double CalculateScore(IReadOnlyList<CropCandidate> accepted)
    {
        if (accepted.Count == 0)
        {
            return 0;
        }

        double score = 0;

        if (accepted.Count >= _minExpected && accepted.Count <= _maxExpected)
        {
            score += 1000; 
        }
        else if (accepted.Count < _minExpected)
        {
            score += (accepted.Count * 100); 
        }
        else 
        {
            score += (1000 - ((accepted.Count - _maxExpected) * 50)); 
        }

        double totalArea = 0;
        double minAspectRatio = double.MaxValue;
        double maxAspectRatio = 0;

        foreach (var c in accepted)
        {
            totalArea += c.Area;
            double w = c.Rotated.Size.Width;
            double h = c.Rotated.Size.Height;
            double ar = Math.Max(w, h) / Math.Min(w, h);
            minAspectRatio = Math.Min(minAspectRatio, ar);
            maxAspectRatio = Math.Max(maxAspectRatio, ar);
        }

        double imageArea = _originalW * _originalH;
        double areaRatio = totalArea / imageArea;

        score += (areaRatio * 100);

        if (accepted.Count > 1)
        {
            double arDiff = maxAspectRatio - minAspectRatio;
            score -= (arDiff * 10); 
        }

        return score;
    }

    public void Dispose()
    {
        _detMat?.Dispose();
        _detHsv?.Dispose();
    }
}
