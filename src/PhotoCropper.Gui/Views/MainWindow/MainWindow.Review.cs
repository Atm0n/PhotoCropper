using Avalonia.Interactivity;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui;

/// <summary>Review mode lives in <see cref="ReviewViewModel"/> and its view; these are the window's entry points and its host side.</summary>
internal sealed partial class MainWindow : IReviewHost
{
    private ReviewViewModel _reviewVm = null!;

    internal bool IsInReviewMode => _reviewVm.IsOpen;

    private Task OpenReviewModeAsync()
    {
        if (isLoading || _isNavigating || ScanSessions.Count == 0) return Task.CompletedTask;
        _reviewVm.Open(Math.Max(0, SelectedPhotoNumber));
        return Task.CompletedTask;
    }

    internal ValueTask<bool> HandleReviewKey(Avalonia.Input.KeyEventArgs e)
    {
        if (!_reviewVm.HandleKey(e.Key)) return ValueTask.FromResult(false);
        e.Handled = true;
        return ValueTask.FromResult(true);
    }

    // ---- IReviewHost ----

    IReadOnlyList<ScanSessionItem> IReviewHost.Scans => ScanSessions;

    int IReviewHost.CurrentScanIndex => CurrentIndex;

    void IReviewHost.DisposeLater(IReadOnlyCollection<IDisposable> items) => DeferDispose(items);

    void IReviewHost.ShowError(string title, string message) => ShowAppError(title, message);

    /// <summary>Back in the editor on the photo review ended on; photos may have been rotated or deleted meanwhile.</summary>
    void IReviewHost.ReviewClosed(int scanIndex, int photoIndex)
    {
        if (ScanSessions.Count == 0) return;
        if (scanIndex >= 0 && scanIndex != CurrentIndex)
        {
            _ = ShowReviewedScanAsync(scanIndex, photoIndex);
            return;
        }

        var engine = ScanSessions[CurrentIndex].Activate();
        ShowScan(engine);
        _cropsVm.ShowPhotos(Math.Max(0, photoIndex));
        _cropsVm.UpdateCoverage();
    }

    /// <summary>Opens the scan review ended on, then selects its photo (loading a scan selects the first one).</summary>
    private async Task ShowReviewedScanAsync(int scanIndex, int photoIndex)
    {
        try
        {
            await NavigateToScanIndexAsync(scanIndex);
            if (CurrentIndex == scanIndex) _cropsVm.SelectPhoto(Math.Max(0, photoIndex));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceError($"[PhotoCropper] Opening the reviewed scan failed: {ex}");
        }
    }
}
