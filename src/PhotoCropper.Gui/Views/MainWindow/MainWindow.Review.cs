using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed record ReviewPhotoItem(int ScanIndex, int PhotoIndex);

internal sealed partial class MainWindow
{
    private readonly List<ReviewPhotoItem> _reviewItems = [];
    private int _reviewCurrentIndex = -1;
    private Bitmap? _reviewCurrentBitmap;
    private int _reviewSessionRotations;
    private int _reviewSessionDeletions;

    internal bool IsInReviewMode => pnlReviewOverlay?.IsVisible == true;
    private bool IsAtSentinel => _reviewCurrentIndex == _reviewItems.Count;

    internal static IReadOnlyList<ReviewPhotoItem> FlattenReviewItems(IReadOnlyList<ScanSessionItem> sessions, int currentIndex, out int initialReviewIndex, int selectedPhotoIndex = 0)
    {
        var items = new List<ReviewPhotoItem>();
        initialReviewIndex = 0;

        for (int s = 0; s < sessions.Count; s++)
        {
            var session = sessions[s];
            int count = session.PhotoCount;
            for (int p = 0; p < count; p++)
            {
                if (s == currentIndex && p == selectedPhotoIndex)
                {
                    initialReviewIndex = items.Count;
                }
                items.Add(new ReviewPhotoItem(s, p));
            }
        }

        return items;
    }

    private async void BtnReviewPhotos_Click(object? sender, RoutedEventArgs e)
    {
        await OpenReviewModeAsync();
    }

    private async Task OpenReviewModeAsync()
    {
        if (isLoading || _isNavigating || ScanSessions.Count == 0) return;

        // Reset per-session tracking counters
        _reviewSessionRotations = 0;
        _reviewSessionDeletions = 0;

        // Ensure current scan is activated and build flat list of all photos
        _reviewItems.Clear();
        int initialIndex = -1;

        for (int s = 0; s < ScanSessions.Count; s++)
        {
            var session = ScanSessions[s];
            int count = session.IsActive ? session.Engine!.DetectedPhotos.Count : session.CachedPhotoCount;
            // If active, use actual count; if not active yet, activate if it's the current session
            if (s == CurrentIndex && !session.IsActive)
            {
                var engine = session.Activate();
                count = engine.DetectedPhotos.Count;
            }

            for (int p = 0; p < count; p++)
            {
                if (s == CurrentIndex && initialIndex == -1 && slides?.SelectedIndex == p)
                {
                    initialIndex = _reviewItems.Count;
                }
                _reviewItems.Add(new ReviewPhotoItem(s, p));
            }
        }

        if (_reviewItems.Count == 0)
        {
            ShowAppError(
                LocalizationService.GetString(ResourceKeys.TitleReviewMode, "Photo Review"),
                LocalizationService.GetString(ResourceKeys.MsgReviewEmpty, "No detected photos to review across any scans."));
            return;
        }

        _reviewCurrentIndex = initialIndex >= 0 ? initialIndex : 0;
        pnlReviewOverlay.IsVisible = true;
        await LoadReviewPhotoAtCurrentIndexAsync();
    }

    private void CloseReviewMode()
    {
        if (pnlReviewOverlay == null) return;
        pnlReviewOverlay.IsVisible = false;

        // Reset sentinel panel visibility for next open
        if (pnlReviewSentinel != null) pnlReviewSentinel.IsVisible = false;
        if (brdReviewPhoto != null) brdReviewPhoto.IsVisible = true;

        if (_reviewCurrentBitmap != null)
        {
            var bmp = _reviewCurrentBitmap;
            _reviewCurrentBitmap = null;
            if (imgReview != null) imgReview.Source = null;
            DeferDispose(bmp);
        }

        // Resolve effective index: if at sentinel, sync to the last real photo
        int effectiveIndex = Math.Min(_reviewCurrentIndex, _reviewItems.Count - 1);

        // Synchronize main window view with the current scan
        if (_reviewItems.Count > 0 && effectiveIndex >= 0)
        {
            var item = _reviewItems[effectiveIndex];
            if (item.ScanIndex != CurrentIndex)
            {
                _ = NavigateToScanIndexAsync(item.ScanIndex);
            }
            else
            {
                LoadCroppedPhotosToSlider();
                if (slides != null && item.PhotoIndex < slides.Items.Count)
                {
                    slides.SelectedIndex = item.PhotoIndex;
                }
                if (lstGallery != null && item.PhotoIndex < lstGallery.Items.Count)
                {
                    lstGallery.SelectedIndex = item.PhotoIndex;
                }
                UpdateSelectionUi();
            }
        }
        else if (ScanSessions.Count > 0 && CurrentIndex >= 0 && CurrentIndex < ScanSessions.Count)
        {
            LoadCroppedPhotosToSlider();
        }
    }

    private async Task LoadReviewPhotoAtCurrentIndexAsync()
    {
        if (_reviewItems.Count == 0 || _reviewCurrentIndex < 0)
        {
            if (imgReview != null) imgReview.Source = null;
            if (txtReviewCounter != null) txtReviewCounter.Text = "";
            if (txtReviewScanInfo != null) txtReviewScanInfo.Text = "";
            return;
        }

        // Sentinel position: index == Count means "end of loop" slide
        if (IsAtSentinel)
        {
            ShowReviewSentinel();
            return;
        }

        // Clear sentinel if going back to a real photo
        if (pnlReviewSentinel != null) pnlReviewSentinel.IsVisible = false;
        if (brdReviewPhoto != null) brdReviewPhoto.IsVisible = true;

        var item = _reviewItems[_reviewCurrentIndex];
        var session = ScanSessions[item.ScanIndex];
        var engine = session.Activate();

        if (item.PhotoIndex >= engine.DetectedPhotos.Count)
        {
            // Index out of bounds (might happen if deleted)
            _reviewCurrentIndex = Math.Clamp(_reviewCurrentIndex, 0, _reviewItems.Count - 1);
            if (_reviewCurrentIndex >= 0 && _reviewCurrentIndex < _reviewItems.Count)
            {
                await LoadReviewPhotoAtCurrentIndexAsync();
            }
            return;
        }

        var mat = engine.DetectedPhotos[item.PhotoIndex];
        var newBmp = MatBitmapConverter.ToAvaloniaBitmap(mat);

        var oldBmp = _reviewCurrentBitmap;
        _reviewCurrentBitmap = newBmp;
        if (imgReview != null)
        {
            imgReview.Source = newBmp;
        }
        if (oldBmp != null)
        {
            DeferDispose(oldBmp);
        }

        if (txtReviewCounter != null)
        {
            txtReviewCounter.Text = LocalizationService.Format(
                ResourceKeys.PhotoCounter,
                "PHOTO {0} OF {1}",
                _reviewCurrentIndex + 1,
                _reviewItems.Count);
        }

        if (txtReviewScanInfo != null)
        {
            string scanFileName = System.IO.Path.GetFileName(session.FilePath);
            txtReviewScanInfo.Text = $"{scanFileName} • {mat.Width} × {mat.Height} px";
        }
    }

    private void ShowReviewSentinel()
    {
        if (brdReviewPhoto != null) brdReviewPhoto.IsVisible = false;
        if (pnlReviewSentinel != null) pnlReviewSentinel.IsVisible = true;

        // Release current bitmap
        if (_reviewCurrentBitmap != null)
        {
            var old = _reviewCurrentBitmap;
            _reviewCurrentBitmap = null;
            if (imgReview != null) imgReview.Source = null;
            DeferDispose(old);
        }

        int totalPhotos = _reviewItems.Count;
        int totalScans = 0;
        if (totalPhotos > 0)
        {
            var seen = new System.Collections.Generic.HashSet<int>();
            foreach (var ri in _reviewItems) seen.Add(ri.ScanIndex);
            totalScans = seen.Count;
        }

        if (txtSentinelStats != null)
        {
            var statsText = LocalizationService.Format(
                ResourceKeys.ReviewSentinelStats,
                "{0} photos · {1} scans · {2} rotated · {3} deleted",
                totalPhotos, totalScans, _reviewSessionRotations, _reviewSessionDeletions);
            txtSentinelStats.Text = statsText;
        }

        if (txtReviewCounter != null)
        {
            txtReviewCounter.Text = LocalizationService.Format(
                ResourceKeys.PhotoCounter,
                "PHOTO {0} OF {1}",
                _reviewItems.Count,
                _reviewItems.Count);
        }

        if (txtReviewScanInfo != null)
        {
            txtReviewScanInfo.Text = LocalizationService.GetString(
                ResourceKeys.ReviewSentinelSubtitle, "All photos reviewed");
        }
    }

    // Navigation uses Count+1 total slots: positions 0..Count-1 are real photos, Count is the sentinel
    private async Task ReviewNextPhotoAsync()
    {
        if (_reviewItems.Count == 0) return;
        _reviewCurrentIndex = (_reviewCurrentIndex + 1) % (_reviewItems.Count + 1);
        await LoadReviewPhotoAtCurrentIndexAsync();
    }

    private async Task ReviewPrevPhotoAsync()
    {
        if (_reviewItems.Count == 0) return;
        _reviewCurrentIndex = (_reviewCurrentIndex - 1 + _reviewItems.Count + 1) % (_reviewItems.Count + 1);
        await LoadReviewPhotoAtCurrentIndexAsync();
    }

    private async Task ReviewRotateClockwiseAsync()
    {
        if (_reviewItems.Count == 0 || _reviewCurrentIndex < 0 || IsAtSentinel) return;

        var item = _reviewItems[_reviewCurrentIndex];
        var session = ScanSessions[item.ScanIndex];
        session.IsModified = true;

        var engine = session.Activate();
        if (item.PhotoIndex < engine.DetectedPhotos.Count)
        {
            engine.RotatePhoto(item.PhotoIndex);
            _reviewSessionRotations++;
            if (item.ScanIndex == CurrentIndex)
            {
                undoHistory.PushRotate(CurrentIndex, item.PhotoIndex);
            }
            await LoadReviewPhotoAtCurrentIndexAsync();
        }
    }

    private async Task ReviewRotateCounterClockwiseAsync()
    {
        if (_reviewItems.Count == 0 || _reviewCurrentIndex < 0 || IsAtSentinel) return;

        var item = _reviewItems[_reviewCurrentIndex];
        var session = ScanSessions[item.ScanIndex];
        session.IsModified = true;

        var engine = session.Activate();
        if (item.PhotoIndex < engine.DetectedPhotos.Count)
        {
            engine.RotatePhotoCounterClockwise(item.PhotoIndex);
            _reviewSessionRotations++;
            await LoadReviewPhotoAtCurrentIndexAsync();
        }
    }

    private async Task ReviewDeleteCurrentPhotoAsync()
    {
        if (_reviewItems.Count == 0 || _reviewCurrentIndex < 0 || IsAtSentinel) return;

        var item = _reviewItems[_reviewCurrentIndex];
        var session = ScanSessions[item.ScanIndex];
        session.IsModified = true;

        var engine = session.Activate();
        if (item.PhotoIndex < engine.DetectedPhotos.Count)
        {
            var matToDelete = engine.DetectedPhotos[item.PhotoIndex];
            var candToDelete = item.PhotoIndex < (engine.AcceptedCandidates?.Count ?? 0)
                ? engine.AcceptedCandidates?[item.PhotoIndex]
                : null;

            if (item.ScanIndex == CurrentIndex)
            {
                undoHistory.PushDelete(CurrentIndex, item.PhotoIndex, matToDelete, candToDelete);
            }

            engine.DeletePhoto(item.PhotoIndex);
            _reviewSessionDeletions++;

            // Remove from _reviewItems and shift subsequent indices in the same scan
            _reviewItems.RemoveAt(_reviewCurrentIndex);
            for (int i = 0; i < _reviewItems.Count; i++)
            {
                var ri = _reviewItems[i];
                if (ri.ScanIndex == item.ScanIndex && ri.PhotoIndex > item.PhotoIndex)
                {
                    _reviewItems[i] = new ReviewPhotoItem(ri.ScanIndex, ri.PhotoIndex - 1);
                }
            }

            if (_reviewItems.Count == 0)
            {
                CloseReviewMode();
                return;
            }

            // Clamp: deletion shrinks Count by 1, so an index that was valid may now be at/past end
            if (_reviewCurrentIndex > _reviewItems.Count)
            {
                _reviewCurrentIndex = _reviewItems.Count - 1;
            }

            await LoadReviewPhotoAtCurrentIndexAsync();
        }
    }

    internal async ValueTask<bool> HandleReviewKey(KeyEventArgs e)
    {
        if (!IsInReviewMode) return false;

        switch (e.Key)
        {
            case Key.Escape:
            case Key.E:
                CloseReviewMode();
                e.Handled = true;
                return true;

            case Key.R:
                await ReviewRotateClockwiseAsync();
                e.Handled = true;
                return true;

            case Key.L:
                await ReviewRotateCounterClockwiseAsync();
                e.Handled = true;
                return true;

            case Key.Right:
            case Key.Space:
            case Key.Enter:
                await ReviewNextPhotoAsync();
                e.Handled = true;
                return true;

            case Key.Left:
            case Key.Back:
                await ReviewPrevPhotoAsync();
                e.Handled = true;
                return true;

            case Key.Delete:
            case Key.X:
                await ReviewDeleteCurrentPhotoAsync();
                e.Handled = true;
                return true;
        }

        return true;
    }

    private void BtnReviewClose_Click(object? sender, RoutedEventArgs e) => CloseReviewMode();
    private async void BtnReviewPrev_Click(object? sender, RoutedEventArgs e) => await ReviewPrevPhotoAsync();
    private async void BtnReviewNext_Click(object? sender, RoutedEventArgs e) => await ReviewNextPhotoAsync();
    private async void BtnReviewRotateCw_Click(object? sender, RoutedEventArgs e) => await ReviewRotateClockwiseAsync();
    private async void BtnReviewRotateCcw_Click(object? sender, RoutedEventArgs e) => await ReviewRotateCounterClockwiseAsync();
    private async void BtnReviewDelete_Click(object? sender, RoutedEventArgs e) => await ReviewDeleteCurrentPhotoAsync();

    private async void PnlReviewOverlay_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (!IsInReviewMode) return;
        if (e.Delta.Y > 0)
        {
            await ReviewPrevPhotoAsync();
        }
        else if (e.Delta.Y < 0)
        {
            await ReviewNextPhotoAsync();
        }
        e.Handled = true;
    }

    private void PnlReviewOverlay_DoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (!IsInReviewMode) return;
        // Double tapping functions exactly like "Edit in Scanner (E)"
        CloseReviewMode();
        e.Handled = true;
    }
}
