using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Emgu.CV.Structure;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PointF = System.Drawing.PointF;
using Size = System.Drawing.Size;
using SizeF = System.Drawing.SizeF;
using UiPoint = Avalonia.Point;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>
/// The Scan stage's large preview: the selected scan with the photos found on it outlined, so a crooked or
/// overlapping page can be spotted and rescanned at once. A new scan is selected as soon as it arrives.
/// </summary>
internal sealed partial class ScanStageViewModel
{
    /// <summary>Longest side of the preview image, in pixels.</summary>
    internal const int PreviewSize = 1800;

    private CancellationTokenSource? _previewCts;

    /// <summary>The list is being rebuilt: the list box clears its selection meanwhile, which must not clear the preview.</summary>
    private bool _rebuildingList;

    /// <summary>The file <see cref="Preview"/> shows, so selecting the same scan again (the list is rebuilt on every refresh) doesn't decode it again.</summary>
    private (string Path, DateTime WriteTime, Size Size)? _previewOf;

    [ObservableProperty]
    public partial ScanRowViewModel? SelectedScan { get; set; }

    [ObservableProperty]
    public partial Bitmap? Preview { get; set; }

    /// <summary>The photos' outlines on <see cref="Preview"/>, as corners from 0 to 1 of the image's width and height.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<UiPoint[]>? PreviewBoxes { get; set; }

    /// <summary>What the preview shows: how many photos were found, or that they are being looked for.</summary>
    [ObservableProperty]
    public partial string PreviewStatus { get; set; } = string.Empty;

    public bool HasSelection => SelectedScan != null;

    public string SelectedFileName => SelectedScan?.FileName ?? string.Empty;

    /// <summary>The selected scan's Discard button is waiting for its confirming click.</summary>
    public bool IsSelectedConfirmingDiscard => SelectedScan?.IsConfirmingDiscard ?? false;

    private void OnSelectedRowChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScanRowViewModel.IsConfirmingDiscard)) OnPropertyChanged(nameof(IsSelectedConfirmingDiscard));
    }

    /// <summary>Picks the row to select after the list was rebuilt: a scan that just arrived, else the same scan as before.</summary>
    /// <remarks>A discarded scan's place goes to the scan after it (or the last one). A newly opened project starts at its first scan.</remarks>
    private void RestoreSelection(IReadOnlyCollection<string> previousPaths, string? previousSelection, int previousIndex)
    {
        var known = previousPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        // None of the scans were listed before: another project was opened, so start at its first scan.
        bool sameProject = Scans.Any(r => known.Contains(r.FilePath));
        var added = sameProject ? Scans.LastOrDefault(r => !known.Contains(r.FilePath)) : null;

        SelectedScan = added
            ?? Scans.FirstOrDefault(r => string.Equals(r.FilePath, previousSelection, StringComparison.OrdinalIgnoreCase))
            ?? (Scans.Count == 0 ? null : Scans[Math.Clamp(previousIndex, 0, Scans.Count - 1)]);
    }

    partial void OnSelectedScanChanged(ScanRowViewModel? oldValue, ScanRowViewModel? newValue)
    {
        if (oldValue != null) oldValue.PropertyChanged -= OnSelectedRowChanged;
        if (newValue != null) newValue.PropertyChanged += OnSelectedRowChanged;
        OnPropertyChanged(nameof(SelectedFileName));
        OnPropertyChanged(nameof(IsSelectedConfirmingDiscard));
    }

    partial void OnSelectedScanChanged(ScanRowViewModel? value)
    {
        if (_rebuildingList) return;
        OnPropertyChanged(nameof(HasSelection));
        foreach (var other in Scans) if (other != value) other.IsConfirmingDiscard = false;
        _ = LoadPreviewAsync(value);
    }

    private async Task LoadPreviewAsync(ScanRowViewModel? row)
    {
        if (_previewCts != null) await _previewCts.CancelAsync();
        _previewCts?.Dispose();
        _previewCts = null;

        if (row == null || !File.Exists(row.FilePath))
        {
            ReplacePreview(null);
            _previewOf = null;
            PreviewBoxes = null;
            PreviewStatus = string.Empty;
            return;
        }

        // Another scan: don't leave the previous scan's outlines on it while it loads.
        if (_previewOf is not { } current || !string.Equals(current.Path, row.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            PreviewBoxes = null;
            PreviewStatus = string.Empty;
        }

        var cts = new CancellationTokenSource();
        _previewCts = cts;
        var token = cts.Token;
        var session = row.Index < _host.Scans.Count ? _host.Scans[row.Index] : null;

        try
        {
            // The image first, so the page can be checked straight away; the outlines follow.
            DateTime fileTime = File.GetLastWriteTimeUtc(row.FilePath);
            Size size;
            if (_previewOf is { } shown && string.Equals(shown.Path, row.FilePath, StringComparison.OrdinalIgnoreCase) && shown.WriteTime == fileTime && Preview != null)
            {
                size = shown.Size;
            }
            else
            {
                var (bitmap, decodedSize) = await Task.Run(() => DecodePreview(row.FilePath), token);
                if (token.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return;
                }
                ReplacePreview(bitmap);
                size = decodedSize;
                _previewOf = (row.FilePath, fileTime, size);
            }

            List<PointF[]>? boxes = KnownBoxes(session, row.FilePath);
            if (boxes == null && session != null)
            {
                PreviewBoxes = null;
                PreviewStatus = LocalizationService.GetString(ResourceKeys.ScanPreviewDetecting, "Looking for photos…");
                // The Crops stage's own detection, so both show the same photos (and the scan is ready there).
                var crops = await _host.PrepareScanAsync(row.Index, token);
                if (token.IsCancellationRequested) return;
                boxes = [.. crops.Select(ToCorners)];
            }
            if (token.IsCancellationRequested) return;

            PreviewBoxes = boxes == null || size.Width <= 0 || size.Height <= 0
                ? null
                : [.. boxes.Select(b => b.Select(p => new UiPoint((double)p.X / size.Width, (double)p.Y / size.Height)).ToArray())];
            int count = boxes?.Count ?? 0;
            PreviewStatus = PhotoCountText(count);

            // The list was built before the count was known (e.g. the editor loaded the scan in the background).
            row.PhotosText = PreviewStatus;
            row.IsFlagged = count == 0;
        }
        catch (OperationCanceledException)
        {
            // Another scan was selected meanwhile.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or Emgu.CV.Util.CvException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not preview scan '{row.FilePath}': {ex.Message}");
            if (!token.IsCancellationRequested) PreviewStatus = string.Empty;
        }
    }

    /// <summary>Decodes a scan at preview size, with its full pixel size (which the outlines are measured in).</summary>
    private static (Bitmap Bitmap, Size Size) DecodePreview(string path)
    {
        var info = SixLabors.ImageSharp.Image.Identify(path);
        using var stream = File.OpenRead(path);
        var bitmap = info.Width >= info.Height
            ? Bitmap.DecodeToWidth(stream, Math.Min(PreviewSize, info.Width))
            : Bitmap.DecodeToHeight(stream, Math.Min(PreviewSize, info.Height));
        return (bitmap, new Size(info.Width, info.Height));
    }

    /// <summary>The scan's boxes if they are already known: open in the editor, or saved in the project.</summary>
    private List<PointF[]>? KnownBoxes(ScanSessionItem? session, string path)
    {
        if (session?.Engine is { } engine)
        {
            // Still being prepared (or not auto-tuned yet): PrepareScanAsync waits for it and finishes the job.
            if (session.IsProcessing || !session.IsAutoTuned) return null;
            return [.. engine.AcceptedCandidates.Select(c => c.Rotated.GetVertices())];
        }

        IEnumerable<WorkspaceCropData>? saved = null;
        if (_host.Workspace is { } workspace && _host.WorkDirectory is { } workDir)
        {
            string relative = Path.GetRelativePath(workDir, path);
            var entry = workspace.Scans.FirstOrDefault(s => string.Equals(s.RelativePath, relative, StringComparison.OrdinalIgnoreCase));
            if (entry is { HasSavedCrops: true }) saved = entry.FinalCrops;
        }
        saved ??= session?.SavedCrops;

        return saved?.Select(ToCorners).ToList();
    }

    private static PointF[] ToCorners(WorkspaceCropData c) =>
        new RotatedRect(new PointF(c.CenterX, c.CenterY), new SizeF(c.Width, c.Height), c.Angle).GetVertices();

    private void ReplacePreview(Bitmap? bitmap)
    {
        var old = Preview;
        Preview = bitmap;
        if (old != null && !ReferenceEquals(old, bitmap)) DeferredDisposal.Later(old);
    }
}
