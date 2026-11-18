using Avalonia.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using Emgu.CV.Structure;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;
using System.Drawing;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>What refine mode needs from the window.</summary>
internal interface IRefineHost
{
    /// <summary>Runs work behind the loading overlay; returns false if it failed or was skipped.</summary>
    Task<bool> RunBusyAsync(string message, Func<CancellationToken, Task> work);

    /// <summary>Disposes bitmaps once the UI has stopped drawing them.</summary>
    void DisposeLater(IReadOnlyCollection<IDisposable> items);

    /// <summary>A photo was re-cropped: show the scan's boxes and photos again, on this photo.</summary>
    void Refined(PhotoCropperEngine engine, int photoIndex);

    void ShowStatus(string message);
}

/// <summary>
/// Refine mode: draws a tighter crop inside one extracted photo (e.g. to trim a border detection left). Accepting
/// re-crops the photo, moves its box on the scan to match, and can be undone in one step.
/// </summary>
internal sealed partial class RefineViewModel(IRefineHost host, UndoRedoHistory undo) : ObservableObject
{
    private PhotoCropperEngine? _engine;
    private int _scanIndex = -1;
    private int _photoIndex = -1;
    private Action? _markModified;

    [ObservableProperty] private bool _isOpen;

    /// <summary>The photo with the chosen crop drawn on it.</summary>
    [ObservableProperty] private Bitmap? _preview;

    /// <summary>The chosen crop, in the photo's pixels.</summary>
    public Rectangle CropRect { get; private set; }

    /// <summary>Size of the photo being refined, for mapping the pointer onto it.</summary>
    public Size PhotoSize { get; private set; }

    /// <summary>Starts refining <paramref name="photoIndex"/> of the open scan, starting from its current refinement (or the whole photo).</summary>
    public bool Open(PhotoCropperEngine engine, int scanIndex, int photoIndex, Action markModified)
    {
        ArgumentNullException.ThrowIfNull(engine);
        if (photoIndex < 0 || photoIndex >= engine.DetectedPhotos.Count) return false;

        _engine = engine;
        _scanIndex = scanIndex;
        _photoIndex = photoIndex;
        _markModified = markModified;

        var photo = engine.DetectedPhotos[photoIndex];
        PhotoSize = new Size(photo.Width, photo.Height);
        var rect = engine.GetRefinedCropRect(photoIndex);
        CropRect = rect.IsEmpty || rect.Width <= 10 || rect.Height <= 10 ? new Rectangle(Point.Empty, PhotoSize) : rect;

        IsOpen = true;
        host.ShowStatus(LocalizationService.GetString(ResourceKeys.MsgRefineModeHelp, "Refinement mode active."));
        UpdatePreview();
        return true;
    }

    /// <summary>Sets the crop to a rectangle drawn on the photo (photo pixels); tiny rectangles are ignored.</summary>
    public void SetCrop(Rectangle rect)
    {
        if (!IsOpen || rect.Width < 2 || rect.Height < 2) return;
        rect.Intersect(new Rectangle(Point.Empty, PhotoSize));
        if (rect.IsEmpty) return;
        CropRect = rect;
        UpdatePreview();
    }

    /// <summary>Re-crops the photo to <see cref="CropRect"/>; its box on the scan moves with it, all in one undo step.</summary>
    [RelayCommand]
    public async Task AcceptAsync()
    {
        if (!IsOpen || _engine == null) return;
        var engine = _engine;
        int index = _photoIndex;
        int scan = _scanIndex;
        var rect = CropRect;

        using var beforePhoto = engine.DetectedPhotos[index].Clone();
        using var beforeRaw = index < engine.RawDetectedPhotos.Count ? engine.RawDetectedPhotos[index].Clone() : null;
        IReadOnlyList<CropCandidate> beforeBoxes = [.. engine.AcceptedCandidates];

        bool done = await host.RunBusyAsync(LocalizationService.GetString(ResourceKeys.MsgApplyingRefine, "Applying refinement..."), async ct =>
        {
            _markModified?.Invoke();
            await Task.Run(() => engine.ApplyCropToPhoto(index, rect), ct);
            undo.PushResizeCandidate(
                scan, index,
                beforePhoto, engine.DetectedPhotos[index],
                beforeBoxes, engine.AcceptedCandidates,
                "Crop Refinement",
                beforeRaw,
                index < engine.RawDetectedPhotos.Count ? engine.RawDetectedPhotos[index] : null);
        });

        if (!done) return;
        CloseOverlay();
        host.Refined(engine, index);
        host.ShowStatus(LocalizationService.GetString(ResourceKeys.MsgRefineSuccess, "Crop refined successfully."));
    }

    [RelayCommand]
    public void Reject()
    {
        if (!IsOpen) return;
        CloseOverlay();
        host.ShowStatus(LocalizationService.GetString(ResourceKeys.MsgRefineCancelled, "Refinement cancelled."));
    }

    /// <summary>Refine's shortcuts (Enter/A accept, Esc/Backspace/C cancel). While open it takes every key.</summary>
    public async Task<bool> HandleKeyAsync(Key key)
    {
        if (!IsOpen) return false;
        switch (key)
        {
            case Key.Enter: await AcceptAsync(); break;
            case Key.Escape: Reject(); break;
        }
        return true;
    }

    private void CloseOverlay()
    {
        IsOpen = false;
        _engine = null;
        _markModified = null;
        ReplacePreview(null);
    }

    private void UpdatePreview()
    {
        if (_engine == null) return;
        using var preview = _engine.DetectedPhotos[_photoIndex].Clone();
        const int thickness = 8;
        var outline = CropRect;
        outline.Inflate(-thickness / 2, -thickness / 2);
        CvInvoke.Rectangle(preview, outline, new MCvScalar(0, 0, 255), thickness);
        ReplacePreview(MatBitmapConverter.ToAvaloniaBitmap(preview));
    }

    private void ReplacePreview(Bitmap? bitmap)
    {
        var old = Preview;
        Preview = bitmap;
        if (old != null) host.DisposeLater([old]);
    }
}
