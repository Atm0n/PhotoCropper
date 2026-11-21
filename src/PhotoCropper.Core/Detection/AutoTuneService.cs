using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Detection;

public sealed class AutoTuneService : IDisposable
{
    internal static readonly double[] SweepTolerances = [8, 12, 16, 20, 25];
    internal static readonly double[] SweepCannyLows = [20, 30, 50, 70];
    internal static readonly double[] SweepMinAreaFactors = [0.005, 0.01, 0.02, 0.03];

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
    private Mat? _aroundPage;
    private Mat? _detHsv;
    private Scalar _avgBgColorHsv;
    private Scalar _bgBgr;

    private double _bestScore;
    private int _bestCount;
    private DetectionOptions _bestOptions;

    internal AutoTuneService(Mat source, DetectionOptions currentOptions, int minExpected, int maxExpected)
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
            Cv2.Resize(_source, rawDetMat, new Size(_scaledW, _scaledH), 0, 0, InterpolationFlags.Area);
        }
        else
        {
            _source.CopyTo(rawDetMat);
        }

        _detMat = ImageIo.NormalizeToBgr(rawDetMat);
        _detHsv = new Mat();
        Cv2.CvtColor(_detMat, _detHsv, ColorConversionCodes.BGR2HSV);

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
        _avgBgColorHsv = _currentOptions.CustomBackgroundColorHsv is { } picked ? CvInterop.ToCv(picked) : BackgroundAnalyzer.SampleBackgroundColor(_detHsv!);

        // A light album page on the bed: its paper is the background, as if picked with Pick Color.
        Scalar? lightPaper = _currentOptions.CustomBackgroundColorHsv == null ? BackgroundAnalyzer.LightPageColour(_detHsv!, _avgBgColorHsv) : null;
        if (lightPaper is { } paper) _avgBgColorHsv = paper;

        // An album page on a light bed: the page's colour (unless one was picked, or is a light page's paper) is the
        // one inside its edge.
        _aroundPage = BackgroundAnalyzer.FindBedAroundPage(_detMat!, _avgBgColorHsv, out Scalar background);
        if (_currentOptions.CustomBackgroundColorHsv == null && lightPaper == null) _avgBgColorHsv = background;
        _bgBgr = BackgroundAnalyzer.HsvToBgr(_avgBgColorHsv);
    }

    private void NeutralizeBezels()
    {
        bool painted = false;
        var margins = BackgroundAnalyzer.DetectBezelMargins(_detMat!, _avgBgColorHsv, _currentOptions.BackgroundTolerance);
        if (margins.Top > 0 || margins.Bottom > 0 || margins.Left > 0 || margins.Right > 0)
        {
            BackgroundAnalyzer.PaintBezels(_detMat!, margins, pad: 0, _bgBgr);
            painted = true;
        }
        // ...and the bed around it is background too.
        if (_aroundPage != null)
        {
            _detMat!.SetTo(_bgBgr, _aroundPage);
            _aroundPage.Dispose();
            _aroundPage = null;
            painted = true;
        }
        if (painted) Cv2.CvtColor(_detMat!, _detHsv!, ColorConversionCodes.BGR2HSV);
    }

    /// <summary>
    /// Tries every combination of the sweep's settings. Each combination's mask and score are independent, so they
    /// are worked out side by side; the best is then picked in the sweep's own order with the same rule as one by
    /// one (a later one only wins with a higher score), so the result is the same as before.
    /// </summary>
    private void PerformSweeps()
    {
        var edgeMaps = new Mat[SweepCannyLows.Length];
        try
        {
            Parallel.For(0, SweepCannyLows.Length, c =>
                edgeMaps[c] = ForegroundMaskGenerator.GeneratePrecomputedEdgeMap(_detMat!, SweepCannyLows[c], SweepCannyLows[c] * AppConstants.DefaultCannyHighRatio));

            // 1. HSV background subtraction: one mask per edge threshold and tolerance, scored at each minimum size.
            int tolerances = SweepTolerances.Length;
            var hsvResults = new (double Score, int Count)[SweepCannyLows.Length * tolerances][];
            Parallel.For(0, hsvResults.Length, i =>
            {
                int c = i / tolerances;
                double cannyLow = SweepCannyLows[c];
                using Mat foreground = new();
                ForegroundMaskGenerator.PopulateForegroundMask(
                    _detMat!, foreground, _avgBgColorHsv, SweepTolerances[i % tolerances], cannyLow, cannyLow * AppConstants.DefaultCannyHighRatio, edgeMaps[c], _detHsv!);
                hsvResults[i] = ScoreEachMinArea(foreground);
            });
            for (int i = 0; i < hsvResults.Length; i++)
            {
                double cannyLow = SweepCannyLows[i / tolerances];
                for (int m = 0; m < SweepMinAreaFactors.Length; m++)
                {
                    Consider(hsvResults[i][m], cannyLow, SweepMinAreaFactors[m], SweepTolerances[i % tolerances]);
                }
            }

            // 2. Otsu fallback, when too few photos were found.
            if (_bestCount < _minExpected)
            {
                bool isLightBg = _avgBgColorHsv.Val2 > 120;
                var otsuResults = new (double Score, int Count)[SweepCannyLows.Length][];
                Parallel.For(0, otsuResults.Length, c =>
                {
                    double cannyLow = SweepCannyLows[c];
                    using Mat foreground = new();
                    ForegroundMaskGenerator.PopulateOtsuForegroundMask(
                        _detMat!, foreground, cannyLow, cannyLow * AppConstants.DefaultCannyHighRatio, isLightBg, edgeMaps[c]);
                    otsuResults[c] = ScoreEachMinArea(foreground);
                });
                double tolerance = _bestOptions.BackgroundTolerance;
                for (int c = 0; c < otsuResults.Length; c++)
                {
                    for (int m = 0; m < SweepMinAreaFactors.Length; m++)
                    {
                        Consider(otsuResults[c][m], SweepCannyLows[c], SweepMinAreaFactors[m], tolerance);
                    }
                }
            }
        }
        finally
        {
            foreach (var mat in edgeMaps)
            {
                mat?.Dispose();
            }
        }
    }

    /// <summary>The photos found in <paramref name="foreground"/> scored at each of the sweep's minimum sizes.</summary>
    private (double Score, int Count)[] ScoreEachMinArea(Mat foreground)
    {
        var results = new (double Score, int Count)[SweepMinAreaFactors.Length];
        for (int m = 0; m < results.Length; m++)
        {
            var passCandidates = CandidateExtractor.ExtractCandidates(
                foreground, _scaledPad, _scaledW, _scaledH, SweepMinAreaFactors[m], _currentOptions.MaxAreaFactor, _detMat!, _bgBgr);
            var accepted = CandidateResolutionFilter.FilterCandidates(MapCandidatesToFullRes(passCandidates, _scale, _originalW, _originalH));
            results[m] = (CalculateScore(accepted), accepted.Count);
        }
        return results;
    }

    /// <summary>Keeps these settings when they score higher than the best so far.</summary>
    private void Consider((double Score, int Count) result, double cannyLow, double minArea, double tol)
    {
        if (result.Score <= _bestScore) return;
        _bestScore = result.Score;
        _bestCount = result.Count;
        _bestOptions = _currentOptions with
        {
            BackgroundTolerance = tol,
            CannyLowThreshold = cannyLow,
            CannyHighThreshold = cannyLow * AppConstants.DefaultCannyHighRatio,
            MinAreaFactor = minArea
        };
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
            var center = new Point2f((float)(cand.Rotated.Center.X * invScale), (float)(cand.Rotated.Center.Y * invScale));
            var size = new Size2f((float)(cand.Rotated.Size.Width * invScale), (float)(cand.Rotated.Size.Height * invScale));
            var fullRect = new RotatedRect(center, size, cand.Rotated.Angle);

            var bRect = fullRect.BoundingRect();
            int x = Math.Max(0, bRect.X);
            int y = Math.Max(0, bRect.Y);
            int w = Math.Min(originalW - x, bRect.Width);
            int h = Math.Min(originalH - y, bRect.Height);
            var rect = new Rect(x, y, w, h);

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
                Rotated = CvInterop.ToBox(fullRect),
                Rect = CvInterop.ToRectangle(rect),
                Area = fullRect.Size.Width * fullRect.Size.Height,
                ShapePoints = fullShape is null ? [] : CvInterop.ToPoints(fullShape)
            });
        }
        return fullCandidates;
    }

    internal double CalculateScore(IReadOnlyList<CropCandidate> accepted)
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
        _aroundPage?.Dispose();
    }
}
