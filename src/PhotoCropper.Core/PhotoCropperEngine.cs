using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using System.Collections.ObjectModel;
using static PhotoCropper.Core.Detection.CropGeometry;
using DrawingPoint = System.Drawing.Point;
using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;

namespace PhotoCropper.Core;

public class PhotoCropperEngine : IDisposable
{
    static PhotoCropperEngine()
    {
        Cv2.SetLogLevel(LogLevel.ERROR);
    }

    private bool disposedValue;

    public string OriginalFilePath { get; }

    // Configurable Detection Parameters
    public double BackgroundTolerance { get; set; } = Common.AppConstants.DefaultBackgroundTolerance;
    public double MinAreaFactor { get; set; } = Common.AppConstants.DefaultMinAreaFactor;
    public double MaxAreaFactor { get; set; } = Common.AppConstants.DefaultMaxAreaFactor;
    public double CannyLowThreshold { get; set; } = Common.AppConstants.DefaultCannyLow;
    public double CannyHighThreshold { get; set; } = Common.AppConstants.DefaultCannyHigh;
    public ColorScalar? CustomBackgroundColorHsv { get; set; }
    public bool AutoOrientPhotos { get; set; } = true;
    public bool RestoreVintageColors { get; set; }
    public bool RemoveDustAndScratches { get; set; }
    public string BoundingBoxColor { get; set; } = Common.AppConstants.DefaultBoundingBoxColor;

    /// <summary>
    /// Find the photos with the trained network (<see cref="AiPhotoDetector"/>) instead of by colour. Has no effect
    /// while no model is installed. Auto-Tune doesn't apply to it.
    /// </summary>
    public bool UseAiDetection { get; set; }

    /// <summary>True when detection will use the trained network: it is asked for and a model is installed.</summary>
    public bool UsesAiDetection => UseAiDetection && AiPhotoDetector.IsAvailable;

    public Mat Original { get; set; }
    public Collection<Mat> DetectedPhotos { get; } = [];
    public Collection<Mat> RawDetectedPhotos { get; } = [];
    public Dictionary<int, int> PhotoRotations { get; } = [];
    public IReadOnlyList<CropCandidate> AcceptedCandidates { get; private set; } = [];

    public PhotoCropperEngine(string originalFilePath)
    {
        ArgumentNullException.ThrowIfNull(originalFilePath);
        OriginalFilePath = originalFilePath;
        Original = ImageIo.LoadBgr(originalFilePath);
    }

    public void ApplyOptions(DetectionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        BackgroundTolerance = options.BackgroundTolerance;
        MinAreaFactor = options.MinAreaFactor;
        MaxAreaFactor = options.MaxAreaFactor;
        CannyLowThreshold = options.CannyLowThreshold;
        CannyHighThreshold = options.CannyHighThreshold;
        CustomBackgroundColorHsv = options.CustomBackgroundColorHsv;
        AutoOrientPhotos = options.AutoOrientPhotos;
        UseAiDetection = options.UseAiDetection;
        RestoreVintageColors = options.RestoreVintageColors;
        RemoveDustAndScratches = options.RemoveDustAndScratches;
        BoundingBoxColor = options.BoundingBoxColor;
    }

    public DetectionOptions CurrentOptions => new()
    {
        BackgroundTolerance = BackgroundTolerance,
        MinAreaFactor = MinAreaFactor,
        MaxAreaFactor = MaxAreaFactor,
        CannyLowThreshold = CannyLowThreshold,
        CannyHighThreshold = CannyHighThreshold,
        CustomBackgroundColorHsv = CustomBackgroundColorHsv,
        AutoOrientPhotos = AutoOrientPhotos,
        RestoreVintageColors = RestoreVintageColors,
        RemoveDustAndScratches = RemoveDustAndScratches,
        BoundingBoxColor = BoundingBoxColor,
        UseAiDetection = UseAiDetection,
    };

    /// <summary>
    /// The settings and box list of the last <see cref="DetectPhotos"/>, while its photos are untouched (no box
    /// added, deleted, moved or turned since): detecting again with the same settings would give the same photos.
    /// </summary>
    private (DetectionOptions Options, IReadOnlyList<CropCandidate> Candidates)? _freshDetection;

    public AutoTuneResult AutoTune(int minExpected = 1, int maxExpected = int.MaxValue)
    {
        if (UsesAiDetection)
        {
            // Nothing to tune: the network has no colour settings. Detect, unless that was just done.
            bool detected = _freshDetection is { } last && ReferenceEquals(last.Candidates, AcceptedCandidates) && last.Options == CurrentOptions;
            if (!detected) DetectPhotos();
            return new AutoTuneResult(CurrentOptions, AcceptedCandidates.Count, 0, Improved: false);
        }

        var tuneResult = AutoTuneService.Tune(Original, CurrentOptions, minExpected, maxExpected);
        ApplyOptions(tuneResult.BestOptions);
        // Auto-Tune often keeps the settings the scan was just detected with: its photos are then already there.
        bool alreadyDetected = _freshDetection is { } fresh && ReferenceEquals(fresh.Candidates, AcceptedCandidates) && fresh.Options == CurrentOptions;
        if (!alreadyDetected) DetectPhotos();
        return tuneResult;
    }

    public double TotalDetectedAreaRatio
    {
        get
        {
            if (Original.Empty() || Original.Width <= 0 || Original.Height <= 0) return 0.0;
            double totalArea = (double)Original.Width * Original.Height;
            double coveredArea = 0.0;
            foreach (var candidate in AcceptedCandidates)
            {
                coveredArea += candidate.Area;
            }
            return Math.Clamp(coveredArea / totalArea, 0.0, 1.0);
        }
    }

    private void ResetState()
    {
        _freshDetection = null;
        foreach (var photo in DetectedPhotos) photo.Dispose();
        DetectedPhotos.Clear();

        foreach (var raw in RawDetectedPhotos) raw.Dispose();
        RawDetectedPhotos.Clear();

        // Rotations are keyed by photo index, so they belong to the photos being discarded here.
        // Keeping them would apply stale user rotations to newly detected photos.
        PhotoRotations.Clear();
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                Original?.Dispose();
                foreach (var photo in DetectedPhotos) photo.Dispose();
                DetectedPhotos.Clear();
                foreach (var raw in RawDetectedPhotos) raw.Dispose();
                RawDetectedPhotos.Clear();
            }
            disposedValue = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public void RestoreFromSavedCrops(IEnumerable<WorkspaceCropData> savedCrops)
    {
        ArgumentNullException.ThrowIfNull(savedCrops);

        ResetState();
        if (Original == null || Original.Empty() || Original.Width <= 0 || Original.Height <= 0)
        {
            return;
        }

        var savedList = savedCrops as IReadOnlyList<WorkspaceCropData> ?? [.. savedCrops];

        var candidates = new List<CropCandidate>();
        var knownOrientations = new List<int?>();
        foreach (var crop in savedList)
        {
            var rr = new RotatedBox(new System.Drawing.PointF(crop.CenterX, crop.CenterY), new System.Drawing.SizeF(crop.Width, crop.Height), crop.Angle);
            candidates.Add(new CropCandidate { Rotated = rr, Area = crop.Width * crop.Height, ShapePoints = [], Rect = new DrawingRectangle(0, 0, (int)crop.Width, (int)crop.Height) });
            knownOrientations.Add(crop.AutoRotationDegrees);
        }

        // Saved crops whose extraction comes back empty are dropped, so map each kept photo back to
        // the saved crop it came from rather than assuming photo i == saved crop i.
        // A box reaching past the scan's edge was extracted from the padded detection image: do the same, so the
        // restored photo matches the detected one pixel for pixel.
        using DetectionImage? prepared = Original.Channels() == 3 && candidates.Any(c => !IsInside(c.Rotated, Original))
            ? PhotoDetector.Prepare(Original, CurrentOptions)
            : null;
        var kept = ExtractAndProcessCandidates(candidates, prepared?.Padded, prepared?.Pad ?? 0, knownOrientations);
        AcceptedCandidates = [.. kept.Select(k => k.Candidate)];

        for (int photoIdx = 0; photoIdx < kept.Count; photoIdx++)
        {
            int rotationDegrees = NormalizeRightAngle(savedList[kept[photoIdx].SourceIndex].RotationDegrees);
            for (int r = 0; r < rotationDegrees / 90; r++)
            {
                RotatePhoto(photoIdx);
            }
        }
    }

    /// <summary>
    /// Runs the per-photo pipeline on a freshly extracted crop: auto-orientation, the user's manual rotation,
    /// the raw compare copy, then colour restoration. Takes ownership of <paramref name="extracted"/>.
    /// </summary>
    /// <param name="knownOrientation">Orientation to reuse instead of detecting it (e.g. from a saved session).</param>
    /// <returns>The processed photo, its raw (pre-restoration) copy, and the auto-orientation applied.</returns>
    private (Mat Photo, Mat Raw, int Orientation) ProcessExtractedPhoto(Mat extracted, int? knownOrientation = null, int userRotation = 0)
    {
        int orientation = 0;
        if (AutoOrientPhotos)
        {
            orientation = NormalizeRightAngle(knownOrientation ?? AutoOrientationService.DetectRequiredRotation(extracted));
        }

        int totalRotation = NormalizeRightAngle(orientation + userRotation);
        if (totalRotation != 0)
        {
            Mat rotated = new();
            Cv2.Rotate(extracted, rotated, ToRotateFlags(totalRotation));
            extracted.Dispose();
            extracted = rotated;
        }

        // The raw copy (used by hold-to-compare) must be taken before colour restoration.
        Mat raw = extracted.Clone();

        if (RestoreVintageColors)
        {
            Mat restored = PhotoRestorationService.RestoreColors(extracted, removeDust: RemoveDustAndScratches);
            if (!ReferenceEquals(restored, extracted))
            {
                extracted.Dispose();
                extracted = restored;
            }
        }

        return (extracted, raw, orientation);
    }

    /// <summary>
    /// Extracts every candidate in parallel and appends the non-empty results to
    /// <see cref="DetectedPhotos"/> / <see cref="RawDetectedPhotos"/>.
    /// </summary>
    /// <param name="knownOrientations">Optional per-candidate orientation to reuse instead of detecting it.</param>
    /// <returns>
    /// For each candidate that produced a photo, in order: its index in <paramref name="candidates"/> and the
    /// candidate with <see cref="CropCandidate.OrientationDegrees"/> filled in. Callers must use these to keep
    /// <see cref="AcceptedCandidates"/> aligned with <see cref="DetectedPhotos"/>.
    /// </returns>
    private List<(int SourceIndex, CropCandidate Candidate)> ExtractAndProcessCandidates(
        IReadOnlyList<CropCandidate> candidates,
        Mat? padded = null,
        int pad = 0,
        List<int?>? knownOrientations = null)
    {
        Mat?[] results = new Mat[candidates.Count];
        Mat?[] rawResults = new Mat[candidates.Count];
        int[] orientations = new int[candidates.Count];

        try
        {
            System.Threading.Tasks.Parallel.For(0, candidates.Count, i =>
            {
                // The padded image is only needed for a box that reaches past the scan's edge. Inside it, extracting
                // from the original gives the same pixels as restoring the saved box later, so a reopened project
                // doesn't rewrite its cropped photos (and re-render their finals) for sub-pixel differences.
                Mat extracted = (padded != null && !padded.Empty() && pad > 0 && !IsInside(candidates[i].Rotated, Original))
                    ? PhotoExtractionEngine.ExtractPhotoFromRotatedRect(CvInterop.ToCv(candidates[i].Rotated), Original, padded, pad)
                    : PhotoExtractionEngine.ExtractPhotoFromRotatedRect(CvInterop.ToCv(candidates[i].Rotated), Original);

                if (extracted.Empty())
                {
                    extracted.Dispose();
                    return;
                }

                int? known = knownOrientations != null && i < knownOrientations.Count ? knownOrientations[i] : null;
                (results[i], rawResults[i], orientations[i]) = ProcessExtractedPhoto(extracted, known);
            });
        }
        catch
        {
            // One failing extraction aborts the loop; free whatever the other workers produced.
            foreach (var mat in results) mat?.Dispose();
            foreach (var raw in rawResults) raw?.Dispose();
            throw;
        }

        var kept = new List<(int, CropCandidate)>(candidates.Count);
        for (int i = 0; i < results.Length; i++)
        {
            var mat = results[i];
            var raw = rawResults[i];
            if (mat != null && !mat.Empty() && raw != null && !raw.Empty())
            {
                DetectedPhotos.Add(mat);
                RawDetectedPhotos.Add(raw);
                kept.Add((i, candidates[i] with { OrientationDegrees = orientations[i] }));
            }
            else
            {
                mat?.Dispose();
                raw?.Dispose();
            }
        }

        return kept;
    }

    /// <summary>Long side, in pixels, at which overlay elements are drawn at their base size (≈ A4 at 300 DPI).</summary>
    private const double OverlayReferenceLongSide = 3500.0;

    /// <summary>Base distance from a box's top edge to its rotation handle, before <see cref="OverlayScale"/>.</summary>
    public const float RotationHandleBaseOffset = 60f;

    /// <summary>
    /// Factor applied to overlay sizes (lines, handles, badges) and to the matching hit-test distances, so they
    /// keep the same size relative to the scan. Fixed pixel sizes looked huge on 150 DPI scans and tiny at 1200 DPI.
    /// </summary>
    public double OverlayScale =>
        Original == null || Original.Empty()
            ? 1.0
            : Math.Clamp(Math.Max(Original.Width, Original.Height) / OverlayReferenceLongSide, 0.35, 6.0);

    /// <summary>Scales a base overlay size (in pixels at the reference resolution) to this scan.</summary>
    public int ScaleOverlay(double basePixels) => Math.Max(1, (int)Math.Round(basePixels * OverlayScale));

    public void DetectPhotos()
    {
        ResetState();

        if (Original == null || Original.Empty() || Original.Width <= 0 || Original.Height <= 0)
        {
            return;
        }

        if (Original.Channels() != 3)
        {
            var oldOriginal = Original;
            Original = ImageIo.NormalizeToBgr(oldOriginal);
            oldOriginal.Dispose();
        }

        var options = CurrentOptions;
        List<(int SourceIndex, CropCandidate Candidate)> kept;
        if (UsesAiDetection)
        {
            // Boxes can run past the scan's edge; extraction fills what is off the scan.
            kept = ExtractAndProcessCandidates([.. AiPhotoDetector.FindPhotos(Original).Select(ToCandidate)], null, 0);
        }
        else
        {
            using var prepared = PhotoDetector.Prepare(Original, options);
            var acceptedCandidates = PhotoDetector.FindCandidates(Original, prepared, options);
            // Only keep candidates that produced a photo, so AcceptedCandidates[i] always matches DetectedPhotos[i].
            kept = ExtractAndProcessCandidates(acceptedCandidates, prepared.Padded, prepared.Pad);
        }
        AcceptedCandidates = [.. kept.Select(k => k.Candidate)];
        _freshDetection = (options, AcceptedCandidates);
    }

    /// <summary>
    /// Shrinks each photo, and its box, to the picture inside a print's white border, where it has one (see
    /// <see cref="EdgeRefinementService.GetWhiteBorderRect"/>). For the CLI's one-step batch; the app and the
    /// project pipeline trim when restoring (<see cref="Workspace.RestorationSettings.TrimWhiteBorders"/>), so the
    /// cropped photo keeps the whole print.
    /// </summary>
    /// <returns>How many photos were trimmed.</returns>
    public int TrimWhiteBorders()
    {
        int trimmed = 0;
        for (int i = 0; i < DetectedPhotos.Count; i++)
        {
            // Measured on the raw copy: colour restoration can tint the border.
            Mat measured = i < RawDetectedPhotos.Count && RawDetectedPhotos[i].Size() == DetectedPhotos[i].Size()
                ? RawDetectedPhotos[i]
                : DetectedPhotos[i];
            if (EdgeRefinementService.GetWhiteBorderRect(measured) is not { } picture) continue;
            ApplyCropToPhoto(i, CvInterop.ToRectangle(picture));
            trimmed++;
        }
        return trimmed;
    }

    public void SetCustomBackgroundFromPixel(int x, int y)
    {
        CustomBackgroundColorHsv = CvInterop.ToColor(BackgroundAnalyzer.SamplePixelBackgroundColor(Original, x, y));
    }

    private void RotatePhotoInternal(int index, RotateFlags flags)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        _freshDetection = null; // a photo turned by hand: detecting again would turn it back

        Mat rotated = new();
        Cv2.Rotate(DetectedPhotos[index], rotated, flags);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = rotated;

        if (index < RawDetectedPhotos.Count)
        {
            Mat rawRotated = new();
            Cv2.Rotate(RawDetectedPhotos[index], rawRotated, flags);
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos[index] = rawRotated;
        }

        int delta = flags == RotateFlags.Rotate90Clockwise ? 90 : (flags == RotateFlags.Rotate90Counterclockwise ? 270 : 180);
        int currentRot = PhotoRotations.TryGetValue(index, out int r) ? r : 0;
        PhotoRotations[index] = (currentRot + delta) % 360;
    }

    public void RotatePhoto(int index) => RotatePhotoInternal(index, RotateFlags.Rotate90Clockwise);

    public void RotatePhotoCounterClockwise(int index) => RotatePhotoInternal(index, RotateFlags.Rotate90Counterclockwise);

    public void UpdateCandidates(IEnumerable<CropCandidate> newCandidates)
    {
        AcceptedCandidates = [.. newCandidates];
    }

    /// <summary>
    /// Re-extracts the photo for the (already edited) box at <paramref name="candIndex"/>.
    /// </summary>
    /// <returns>
    /// False when nothing could be extracted (index out of range, or the box was dragged too small). The photo is
    /// then left unchanged, so the caller must restore the box it edited or the two will no longer match.
    /// </returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessExtractedPhoto takes ownership of the extracted Mat")]
    public bool ApplyGrabHandleResize(int candIndex)
    {
        if (candIndex < 0 || candIndex >= AcceptedCandidates.Count || candIndex >= DetectedPhotos.Count) return false;

        var cand = AcceptedCandidates[candIndex];

        Mat extracted = PhotoExtractionEngine.ExtractPhotoFromRotatedRect(CvInterop.ToCv(cand.Rotated), Original);
        if (extracted.Empty())
        {
            // Box dragged too small to extract: keep the previous photo rather than storing an empty one.
            extracted.Dispose();
            return false;
        }

        // Re-apply the user's manual 90° rotations so resizing a box doesn't silently undo them.
        int userRotation = PhotoRotations.TryGetValue(candIndex, out int rot) ? rot : 0;
        var (photo, raw, orientation) = ProcessExtractedPhoto(extracted, userRotation: userRotation);
        cand = cand with { OrientationDegrees = orientation };

        DetectedPhotos[candIndex].Dispose();
        DetectedPhotos[candIndex] = photo;

        if (candIndex < RawDetectedPhotos.Count)
        {
            RawDetectedPhotos[candIndex].Dispose();
            RawDetectedPhotos[candIndex] = raw;
        }
        else
        {
            RawDetectedPhotos.Add(raw);
        }

        cand = cand.WithBox(cand.Rotated);

        var list = new List<CropCandidate>(AcceptedCandidates)
        {
            [candIndex] = cand
        };
        AcceptedCandidates = list;
        return true;
    }

    public void DeletePhoto(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;
        DetectedPhotos[index].Dispose();
        DetectedPhotos.RemoveAt(index);

        if (index < RawDetectedPhotos.Count)
        {
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos.RemoveAt(index);
        }

        PhotoRotations.Remove(index);
        ShiftRotations(from: index + 1, by: -1);

        // Always build a new list: AcceptedCandidates instances are handed out to callers (e.g. undo
        // history snapshots), so mutating one in place would silently change their recorded state.
        if (AcceptedCandidates != null && index < AcceptedCandidates.Count)
        {
            var newList = new List<CropCandidate>(AcceptedCandidates);
            newList.RemoveAt(index);
            AcceptedCandidates = newList;
        }
    }

    /// <summary>Inserts a photo, e.g. when undoing its deletion. Takes ownership of <paramref name="photo"/>.</summary>
    /// <param name="raw">The photo's raw (pre-restoration) compare copy; cloned. Defaults to a copy of <paramref name="photo"/>.</param>
    /// <param name="rotation">The photo's manual rotation, restored so later resizes, refinements and saves use it.</param>
    public void InsertPhoto(int index, Mat photo, CropCandidate? candidate = null, Mat? raw = null, int rotation = 0)
    {
        ArgumentNullException.ThrowIfNull(photo);

        int targetIndex = Math.Clamp(index, 0, DetectedPhotos.Count);
        DetectedPhotos.Insert(targetIndex, photo);

        int rawTargetIndex = Math.Clamp(index, 0, RawDetectedPhotos.Count);
        RawDetectedPhotos.Insert(rawTargetIndex, (raw ?? photo).Clone());

        ShiftRotations(from: targetIndex, by: 1);

        int normalizedRotation = NormalizeRightAngle(rotation);
        if (normalizedRotation != 0)
        {
            PhotoRotations[targetIndex] = normalizedRotation;
        }

        if (candidate.HasValue && AcceptedCandidates != null)
        {
            int candTargetIndex = Math.Clamp(index, 0, AcceptedCandidates.Count);
            var newList = new List<CropCandidate>(AcceptedCandidates);
            newList.Insert(candTargetIndex, candidate.Value);
            AcceptedCandidates = newList;
        }
    }

    /// <summary>Moves the manual rotations of photos from index <paramref name="from"/> on, after a photo is removed or inserted.</summary>
    private void ShiftRotations(int from, int by)
    {
        var shifted = PhotoRotations.ToDictionary(r => r.Key >= from ? r.Key + by : r.Key, r => r.Value);
        PhotoRotations.Clear();
        foreach (var (k, v) in shifted) PhotoRotations[k] = v;
    }

    public DrawingRectangle GetRefinedCropRect(int index)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return DrawingRectangle.Empty;
        return CvInterop.ToRectangle(EdgeRefinementService.GetRefinedCropRect(DetectedPhotos[index]));
    }

    public void ApplyCropToPhoto(int index, DrawingRectangle crop)
    {
        if (index < 0 || index >= DetectedPhotos.Count) return;

        Rect rect = CvInterop.ToCv(crop).Intersect(new Rect(0, 0, DetectedPhotos[index].Width, DetectedPhotos[index].Height));
        if (rect.Width <= 10 || rect.Height <= 10) return;

        // Move the box on the scan to the refined area too, so the overlay matches the photo and the
        // refinement is saved with the session and survives re-extraction (restore, export).
        if (index < AcceptedCandidates.Count)
        {
            var cand = AcceptedCandidates[index];
            int userRotation = PhotoRotations.TryGetValue(index, out int rot) ? rot : 0;
            int totalRotation = NormalizeRightAngle(cand.OrientationDegrees + userRotation);
            RotatedBox refined = MapPhotoCropToScan(cand.Rotated, totalRotation, CvInterop.ToSize(DetectedPhotos[index].Size()), CvInterop.ToRectangle(rect));
            var newList = new List<CropCandidate>(AcceptedCandidates) { [index] = cand.WithBox(refined) };
            AcceptedCandidates = newList;
        }

        // Crop the raw copy with the same rectangle so hold-to-compare keeps showing the same region.
        // Both copies share dimensions (restoration never resizes), but guard in case they diverge.
        if (index < RawDetectedPhotos.Count && RawDetectedPhotos[index].Size() == DetectedPhotos[index].Size())
        {
            Mat rawCropped = EdgeRefinementService.ApplyCrop(RawDetectedPhotos[index], rect);
            RawDetectedPhotos[index].Dispose();
            RawDetectedPhotos[index] = rawCropped;
        }

        Mat cropped = EdgeRefinementService.ApplyCrop(DetectedPhotos[index], rect);
        DetectedPhotos[index].Dispose();
        DetectedPhotos[index] = cropped;
    }

    public void AddManualCrop(DrawingRectangle area)
    {
        var (extracted, candidate) = PhotoExtractionEngine.ExtractManualCrop(
            Original,
            CvInterop.ToCv(area),
            CustomBackgroundColorHsv is { } picked ? CvInterop.ToCv(picked) : null,
            BackgroundTolerance,
            CannyLowThreshold,
            CannyHighThreshold);

        if (extracted.Empty() || !candidate.HasValue)
        {
            extracted.Dispose();
            return;
        }

        // Same pipeline as detected photos, so a manual crop looks the same now as after a restart.
        var (photo, raw, orientation) = ProcessExtractedPhoto(extracted);
        DetectedPhotos.Add(photo);
        RawDetectedPhotos.Add(raw);
        AcceptedCandidates = [.. AcceptedCandidates ?? [], candidate.Value with { OrientationDegrees = orientation }];
    }

    /// <summary>A candidate for a box found as a whole (by Find Photo or the trained network), not from a mask's outline.</summary>
    private static CropCandidate ToCandidate(RotatedBox box)
    {
        var shape = box.GetVertices().Select(v => new DrawingPoint((int)Math.Round(v.X), (int)Math.Round(v.Y))).ToArray();
        return new CropCandidate(
            shape,
            CvInterop.ToRectangle(Cv2.BoundingRect(CvInterop.ToCv(shape))),
            9999.0, box, (double)box.Size.Width * box.Size.Height, 1.0, 1.0);
    }

    /// <summary>
    /// Finds the photo at <paramref name="point"/> (scan pixels). Inside an existing box it re-fits that box;
    /// elsewhere it adds the photo found, processed like a detected one.
    /// </summary>
    /// <returns>The photo's index and whether it was added; index -1 when no photo was found there.</returns>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "ProcessExtractedPhoto takes ownership of the extracted Mat")]
    public (int Index, bool Added) FindPhotoAt(System.Drawing.PointF point)
    {
        var boxes = AcceptedCandidates ?? [];
        int inside = -1;
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            if (Cv2.PointPolygonTest(CvInterop.ToCv(boxes[i].Rotated).Points(), new Point2f(point.X, point.Y), false) >= 0)
            {
                inside = i;
                break;
            }
        }

        var others = boxes.Where((_, i) => i != inside).Select(b => b.Rotated).ToList();
        if (PhotoAtPointLocator.Locate(Original, point, CurrentOptions, others) is not { } found) return (-1, false);

        var candidate = ToCandidate(found);

        if (inside >= 0)
        {
            var before = boxes.ToList();
            var refitted = boxes.ToList();
            refitted[inside] = candidate with { OrientationDegrees = boxes[inside].OrientationDegrees };
            UpdateCandidates(refitted);
            if (ApplyGrabHandleResize(inside)) return (inside, false);
            UpdateCandidates(before);
            return (-1, false);
        }

        Mat extracted = PhotoExtractionEngine.ExtractPhotoFromRotatedRect(CvInterop.ToCv(found), Original);
        if (extracted.Empty())
        {
            extracted.Dispose();
            return (-1, false);
        }
        var (photo, raw, orientation) = ProcessExtractedPhoto(extracted);
        DetectedPhotos.Add(photo);
        RawDetectedPhotos.Add(raw);
        AcceptedCandidates = [.. boxes, candidate with { OrientationDegrees = orientation }];
        return (DetectedPhotos.Count - 1, true);
    }
}
