using Avalonia.Interactivity;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui;

/// <summary>Review mode lives in <see cref="ReviewViewModel"/> and its view; these are the window's entry points and its host side.</summary>
internal sealed partial class MainWindow : IReviewHost
{
    private ReviewViewModel _reviewVm = null!;

    internal bool IsInReviewMode => _reviewVm.IsOpen;

    private async void BtnReviewPhotos_Click(object? sender, RoutedEventArgs e) => await OpenReviewModeAsync();

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
            _ = NavigateToScanIndexAsync(scanIndex);
            return;
        }

        var engine = ScanSessions[CurrentIndex].Activate();
        ShowScan(engine);
        _cropsVm.ShowPhotos(Math.Max(0, photoIndex));
        _cropsVm.UpdateCoverage();
    }
}
