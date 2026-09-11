using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Emgu.CV;
using Emgu.CV.Structure;
using PhotoCropper;
using PhotoCropper.Models;
using System.Diagnostics.CodeAnalysis;
using PhotoCropperGui.Services;

namespace PhotoCropperGui;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Avalonia Window lifecycle is managed by OnClosed override")]
internal sealed partial class MainWindow : Window
{
    private int currentIndex;
    private readonly List<PhotoCropperEngine> OriginalPhotos = [];
    private readonly UndoRedoHistory undoHistory = new();
    private bool isLoading;

    public MainWindow()
    {
        InitializeComponent();

        // Register key down handler in the Tunnel phase to prevent focused controls from hijacking keys
        AddHandler(KeyDownEvent, Window_KeyDown, RoutingStrategies.Tunnel);

        // Register Drag & Drop event handlers
        AddHandler(DragDrop.DragOverEvent, Window_DragOver);
        AddHandler(DragDrop.DropEvent, Window_Drop);

        PopulateLanguageMenu();
        ApplySettingsToUi();
    }

    private void Window_DragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async void Window_Drop(object? sender, DragEventArgs e)
    {
        if (e.DataTransfer == null) return;

        var imagePaths = new List<string>();
        var validExts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff", ".webp"
        };

        var asyncTransfer = e.DataTransfer as IAsyncDataTransfer;
        var files = asyncTransfer != null ? await asyncTransfer.TryGetFilesAsync() : null;
        if (files != null)
        {
            foreach (var file in files)
            {
                string localPath = file.Path.LocalPath;
                if (File.Exists(localPath) && validExts.Contains(Path.GetExtension(localPath)))
                {
                    imagePaths.Add(localPath);
                }
                else if (Directory.Exists(localPath))
                {
                    foreach (var ext in validExts)
                    {
                        imagePaths.AddRange(Directory.GetFiles(localPath, $"*{ext}", SearchOption.AllDirectories));
                    }
                }
            }
        }

        if (imagePaths.Count > 0)
        {
            await LoadScansFromPathsAsync(imagePaths.Distinct());
        }
    }

    private async Task LoadScansFromPathsAsync(IEnumerable<string> paths)
    {
        undoHistory.Clear();
        foreach (var photo in OriginalPhotos)
        {
            photo.Dispose();
        }
        OriginalPhotos.Clear();
        currentIndex = 0;

        var options = GetDetectionOptionsFromUi();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var photo = new PhotoCropperEngine(path);
            photo.ApplyOptions(options);
            OriginalPhotos.Add(photo);
        }
        await LoadPhotosToGuiAsync();
    }

    private async void BtnOpenFiles_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var fileResult = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = Application.Current?.FindResource("BtnOpenScans")?.ToString() ?? "Select Files",
            FileTypeFilter = [FilePickerFileTypes.ImageAll],
            AllowMultiple = true
        });

        if (fileResult.Count > 0)
        {
            await LoadScansFromPathsAsync(fileResult.Select(f => f.Path.LocalPath));
        }
    }

    private void ApplySettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldZoom.Value = settings.ZoomLevel;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;
        tglAdvanced.IsChecked = settings.AdvancedVisible;

        if (cbFormat != null)
        {
            cbFormat.SelectedIndex = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (sldJpegQuality != null)
        {
            sldJpegQuality.Value = settings.JpegQuality;
        }
        if (txtOutputDir != null)
        {
            txtOutputDir.Text = settings.CustomOutputDirectory ?? "";
        }
        if (pnlJpegQuality != null)
        {
            pnlJpegQuality.IsVisible = !string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase);
        }
    }

    private DetectionOptions GetDetectionOptionsFromUi()
    {
        return new DetectionOptions
        {
            BackgroundTolerance = sldSensitivity.Value,
            MinAreaFactor = sldMinArea.Value / 100.0,
            MaxAreaFactor = sldMaxArea.Value / 100.0,
            CannyLowThreshold = sldEdge.Value,
            CannyHighThreshold = sldEdge.Value * 2.5
        };
    }

    private void PopulateLanguageMenu()
    {
        if (menuLanguage == null) return;

        var languages = LocalizationManager.GetAvailableLanguages();
        foreach (var lang in languages)
        {
            var item = new MenuItem
            {
                Header = lang.Name,
                Tag = lang.Code,
                Focusable = false,
                IsTabStop = false
            };
            item.Click += (s, _) =>
            {
                if (s is MenuItem mi && mi.Tag is string code)
                {
                    LocalizationManager.SetLanguage(code);
                    FocusManager?.Focus(null);
                }
            };
            menuLanguage.Items.Add(item);
        }
    }

    private async Task ExecuteWithLoadingAsync(string statusText, Func<Task> action, string? completionText = null)
    {
        if (isLoading) return;
        isLoading = true;
        pnlLoadingOverlay.IsVisible = true;
        lblStatus.Text = statusText;

        try
        {
            await action();
            if (completionText != null)
            {
                lblStatus.Text = completionText;
            }
        }
        finally
        {
            pnlLoadingOverlay.IsVisible = false;
            isLoading = false;
        }
    }


    private async Task LoadPhotosToGuiAsync()
    {
        if (OriginalPhotos.Count == 0 || isLoading) return;

        string fileName = Path.GetFileName(OriginalPhotos[currentIndex].OriginalFilePath);
        string processingMsg = Application.Current?.FindResource("ProcessingScan")?.ToString() ?? "Processing...";

        await ExecuteWithLoadingAsync($"{processingMsg} {fileName}", async () =>
        {
            var currentPhoto = OriginalPhotos[currentIndex];
            currentPhoto.ApplyOptions(GetDetectionOptionsFromUi());

            if (currentPhoto.DetectedPhotos.Count == 0)
            {
                await Task.Run(() => currentPhoto.DetectPhotos());
            }

            img.Source = MatBitmapConverter.ToAvaloniaBitmap(currentPhoto.OriginalWithDetected);

            string scanCounterFormat = Application.Current?.FindResource("ScanCounter")?.ToString() ?? "Scan {0} of {1}";
            txtFileCounter.Text = string.Format(scanCounterFormat, currentIndex + 1, OriginalPhotos.Count);
            lblStatus.Text = fileName;

            LoadCroppedPhotosToSlider();
            UpdatePhotoCounterLabel();

            if (btnResetBackground != null)
            {
                btnResetBackground.IsEnabled = currentPhoto.CustomBackgroundColorHsv != null;
            }
        });
    }

    private void LoadCroppedPhotosToSlider()
    {
        slides.Items.Clear();

        foreach (var photo in OriginalPhotos[currentIndex].DetectedPhotos)
        {
            slides.Items.Add(MatBitmapConverter.ToAvaloniaBitmap(photo));
        }

        if (slides.Items.Count > 0)
        {
            slides.SelectedIndex = 0;
        }
    }

    private async void BtnPrevScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;
        currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
        await LoadPhotosToGuiAsync();
    }

    private async void BtnNextScan_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;
        currentIndex = (currentIndex + 1) % OriginalPhotos.Count;
        await LoadPhotosToGuiAsync();
    }

    private void BtnSaveImages_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;

        var settings = SettingsManager.Instance.Settings;
        int totalSaved = 0;
        foreach (var originalPhoto in OriginalPhotos)
        {
            originalPhoto.SaveDetectedPhotos(settings.CustomOutputDirectory, settings.PreferredFormat, settings.JpegQuality);
            totalSaved += originalPhoto.DetectedPhotos.Count;
        }

        string msgFormat = Application.Current?.FindResource("MsgSaved")?.ToString() ?? "Successfully saved {0} photos.";
        lblStatus.Text = string.Format(msgFormat, totalSaved);
    }

    private void BtnDelete_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;
        DeleteCurrentPhoto();
    }

    private void DeleteCurrentPhoto()
    {
        if (isLoading || OriginalPhotos.Count == 0 || slides == null) return;
        int photoIndex = slides.SelectedIndex;
        if (photoIndex < 0) return;

        var currentEngine = OriginalPhotos[currentIndex];
        var matToDelete = currentEngine.DetectedPhotos[photoIndex];
        undoHistory.PushDelete(currentIndex, photoIndex, matToDelete);

        currentEngine.DeletePhoto(photoIndex);

        int nextIndex = Math.Min(photoIndex, currentEngine.DetectedPhotos.Count - 1);
        LoadCroppedPhotosToSlider();
        if (nextIndex >= 0)
        {
            slides.SelectedIndex = nextIndex;
        }
        lblStatus.Text = Application.Current?.FindResource("MsgPhotoDeleted")?.ToString() ?? "Photo deleted.";
    }

    private async void BtnRotate_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;
        await RotateCurrentPhotoAsync();
    }

    private async Task RotateCurrentPhotoAsync()
    {
        if (isLoading || OriginalPhotos.Count == 0 || slides.SelectedIndex < 0) return;

        int photoIndex = slides.SelectedIndex;
        undoHistory.PushRotate(currentIndex, photoIndex);

        string rotatingMsg = Application.Current?.FindResource("MsgRotating")?.ToString() ?? "Rotating...";
        string rotatedMsg = Application.Current?.FindResource("MsgPhotoRotated")?.ToString() ?? "Photo rotated.";

        await ExecuteWithLoadingAsync(rotatingMsg, async () =>
        {
            await Task.Run(() => OriginalPhotos[currentIndex].RotatePhoto(photoIndex));
            LoadCroppedPhotosToSlider();
            slides.SelectedIndex = photoIndex;
        }, rotatedMsg);
    }

    private void BtnHelp_Click(object? sender, RoutedEventArgs e) => pnlHelpOverlay.IsVisible = true;
    private void BtnCloseHelp_Click(object? sender, RoutedEventArgs e) => pnlHelpOverlay.IsVisible = false;

    private async void SldSensitivity_PointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (isLoading || OriginalPhotos.Count == 0) return;

        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        SettingsManager.Instance.Save();

        var photo = OriginalPhotos[currentIndex];
        photo.ApplyOptions(GetDetectionOptionsFromUi());

        string reprocessingMsg = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing...";
        await ExecuteWithLoadingAsync(reprocessingMsg, async () =>
        {
            await Task.Run(() => photo.DetectPhotos());
            await LoadPhotosToGuiAsync();
            string msgFormat = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete. Found {0} photos.";
            lblStatus.Text = string.Format(msgFormat, photo.DetectedPhotos.Count);
        });
    }

    private void CbFormat_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cbFormat == null || pnlJpegQuality == null) return;

        bool isJpeg = cbFormat.SelectedIndex == 0;
        pnlJpegQuality.IsVisible = isJpeg;

        var settings = SettingsManager.Instance.Settings;
        settings.PreferredFormat = isJpeg ? "JPEG" : "PNG";
        SettingsManager.Instance.Save();
    }

    private void SldJpegQuality_PointerCaptureLost(object? sender, Avalonia.Input.PointerCaptureLostEventArgs e)
    {
        if (sldJpegQuality == null) return;
        SettingsManager.Instance.Settings.JpegQuality = (int)sldJpegQuality.Value;
        SettingsManager.Instance.Save();
    }

    private async void BtnBrowseDir_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folderResult = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = Application.Current?.FindResource("LblOutputDir")?.ToString() ?? "Select Export Folder",
            AllowMultiple = false
        });

        if (folderResult != null && folderResult.Count > 0)
        {
            var path = folderResult[0].Path.LocalPath;
            txtOutputDir.Text = path;
            SettingsManager.Instance.Settings.CustomOutputDirectory = path;
            SettingsManager.Instance.Save();
        }
    }

    private void BtnClearOutputDir_Click(object? sender, RoutedEventArgs e)
    {
        if (txtOutputDir == null) return;
        txtOutputDir.Text = "";
        SettingsManager.Instance.Settings.CustomOutputDirectory = null;
        SettingsManager.Instance.Save();
    }

    private void Slides_SelectionChanged(object? sender, SelectionChangedEventArgs e) => UpdatePhotoCounterLabel();

    private void UpdatePhotoCounterLabel()
    {
        if (lblPhotoInfo == null || slides == null || OriginalPhotos.Count == 0 || currentIndex >= OriginalPhotos.Count)
        {
            if (lblPhotoInfo != null) lblPhotoInfo.Text = "";
            return;
        }

        var currentPhoto = OriginalPhotos[currentIndex];
        if (currentPhoto.DetectedPhotos.Count == 0)
        {
            lblPhotoInfo.Text = "";
            return;
        }

        int current = slides.SelectedIndex + 1;
        int total = currentPhoto.DetectedPhotos.Count;
        string format = Application.Current?.FindResource("PhotoCounter")?.ToString() ?? "PHOTO {0} OF {1}";
        lblPhotoInfo.Text = string.Format(format, current, total);
    }

    private void BtnPreviousCroppedImage_Click(object? sender, RoutedEventArgs e)
    {
        if (!isLoading) slides.Previous();
    }

    private void BtnNextCroppedImage_Click(object? sender, RoutedEventArgs e)
    {
        if (!isLoading) slides.Next();
    }

    private async void PerformUndo()
    {
        if (OriginalPhotos.Count == 0 || !undoHistory.CanUndo) return;

        var action = undoHistory.Undo(OriginalPhotos);
        if (action != null)
        {
            if (action.ScanIndex != currentIndex && action.ScanIndex >= 0 && action.ScanIndex < OriginalPhotos.Count)
            {
                currentIndex = action.ScanIndex;
                await LoadPhotosToGuiAsync();
            }
            else
            {
                int selected = slides != null ? Math.Clamp(slides.SelectedIndex, 0, Math.Max(0, OriginalPhotos[currentIndex].DetectedPhotos.Count - 1)) : 0;
                LoadCroppedPhotosToSlider();
                if (slides != null && OriginalPhotos[currentIndex].DetectedPhotos.Count > 0)
                {
                    slides.SelectedIndex = selected;
                }
            }
            string undoFormat = Application.Current?.FindResource("MsgUndo")?.ToString() ?? "Undid {0}.";
            lblStatus.Text = string.Format(undoFormat, action.Description);
        }
    }

    private async void PerformRedo()
    {
        if (OriginalPhotos.Count == 0 || !undoHistory.CanRedo) return;

        var action = undoHistory.Redo(OriginalPhotos);
        if (action != null)
        {
            if (action.ScanIndex != currentIndex && action.ScanIndex >= 0 && action.ScanIndex < OriginalPhotos.Count)
            {
                currentIndex = action.ScanIndex;
                await LoadPhotosToGuiAsync();
            }
            else
            {
                int selected = slides != null ? Math.Clamp(slides.SelectedIndex, 0, Math.Max(0, OriginalPhotos[currentIndex].DetectedPhotos.Count - 1)) : 0;
                LoadCroppedPhotosToSlider();
                if (slides != null && OriginalPhotos[currentIndex].DetectedPhotos.Count > 0)
                {
                    slides.SelectedIndex = selected;
                }
            }
            string redoFormat = Application.Current?.FindResource("MsgRedo")?.ToString() ?? "Redid {0}.";
            lblStatus.Text = string.Format(redoFormat, action.Description);
        }
    }

    private async void Window_KeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (isLoading)
        {
            e.Handled = true;
            return;
        }

        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            if (e.Key == Avalonia.Input.Key.S)
            {
                BtnSaveImages_Click(null, new RoutedEventArgs());
                e.Handled = true;
                return;
            }
            if (e.Key == Avalonia.Input.Key.Z)
            {
                PerformUndo();
                e.Handled = true;
                return;
            }
            if (e.Key == Avalonia.Input.Key.Y)
            {
                PerformRedo();
                e.Handled = true;
                return;
            }
        }

        if (pnlHelpOverlay.IsVisible && e.Key == Avalonia.Input.Key.Escape)
        {
            pnlHelpOverlay.IsVisible = false;
            e.Handled = true;
            return;
        }

        if (isRefining)
        {
            switch (e.Key)
            {
                case Avalonia.Input.Key.Enter:
                case Avalonia.Input.Key.A:
                    AcceptRefine();
                    e.Handled = true;
                    break;
                case Avalonia.Input.Key.Back:
                case Avalonia.Input.Key.Escape:
                case Avalonia.Input.Key.C:
                    RejectRefine();
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (OriginalPhotos.Count == 0) return;

        var key = e.Key;
        if (key == Avalonia.Input.Key.OemPlus) key = Avalonia.Input.Key.Add;
        if (key == Avalonia.Input.Key.OemMinus) key = Avalonia.Input.Key.Subtract;
        if (key == Avalonia.Input.Key.OemTilde || key == Avalonia.Input.Key.Oem3) key = Avalonia.Input.Key.N;

        switch (key)
        {
            case Avalonia.Input.Key.Up:
            case Avalonia.Input.Key.PageUp:
                FocusManager?.Focus(null);
                currentIndex = (currentIndex - 1 + OriginalPhotos.Count) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Down:
            case Avalonia.Input.Key.PageDown:
                FocusManager?.Focus(null);
                currentIndex = (currentIndex + 1) % OriginalPhotos.Count;
                await LoadPhotosToGuiAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Left:
                FocusManager?.Focus(null);
                slides.Previous();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.Right:
                FocusManager?.Focus(null);
                slides.Next();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.R:
                FocusManager?.Focus(null);
                await RotateCurrentPhotoAsync();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.X:
            case Avalonia.Input.Key.Delete:
                FocusManager?.Focus(null);
                DeleteCurrentPhoto();
                e.Handled = true;
                break;

            case Avalonia.Input.Key.N:
                FocusManager?.Focus(null);
                StartRefineMode();
                e.Handled = true;
                break;
        }
    }

    private void ScrollOriginal_SizeChanged(object? sender, SizeChangedEventArgs e) => UpdateCropCanvasSize();

    private void SldZoom_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property.Name == "Value")
        {
            UpdateCropCanvasSize();
            if (sldZoom != null)
            {
                SettingsManager.Instance.Settings.ZoomLevel = sldZoom.Value;
                SettingsManager.Instance.Save();
            }
        }
    }

    private void ScrollOriginal_PointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(Avalonia.Input.KeyModifiers.Control))
        {
            double oldZoom = sldZoom.Value;
            double delta = e.Delta.Y > 0 ? 1.1 : 0.9;
            double newZoom = Math.Clamp(oldZoom * delta, sldZoom.Minimum, sldZoom.Maximum);
            
            if (newZoom != oldZoom)
            {
                sldZoom.Value = newZoom;
                double multiplier = newZoom / oldZoom;
                scrollOriginal.Offset = new Vector(
                    (scrollOriginal.Offset.X + e.GetPosition(scrollOriginal).X) * multiplier - e.GetPosition(scrollOriginal).X,
                    (scrollOriginal.Offset.Y + e.GetPosition(scrollOriginal).Y) * multiplier - e.GetPosition(scrollOriginal).Y
                );
            }
            e.Handled = true;
        }
    }

    private void UpdateCropCanvasSize()
    {
        if (img == null || scrollOriginal == null || cnvCrop == null || pnlOriginal == null) return;

        double availableW = scrollOriginal.Viewport.Width - 20;
        double availableH = scrollOriginal.Viewport.Height - 20;
        if (availableW <= 0 || availableH <= 0) return;

        img.Width = availableW;
        img.Height = availableH;

        double zoom = sldZoom.Value;
        double zoomedW = img.Bounds.Width * zoom;
        double zoomedH = img.Bounds.Height * zoom;

        if (zoomedW > 0 && zoomedH > 0)
        {
            pnlOriginal.Width = zoomedW;
            pnlOriginal.Height = zoomedH;
        }

        cnvCrop.Width = pnlOriginal.Width;
        cnvCrop.Height = pnlOriginal.Height;
    }

    private Point startPoint;
    private bool isDragging;

    private void PnlOriginal_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (OriginalPhotos.Count == 0) return;

        if (tglColorPicker?.IsChecked == true)
        {
            e.Handled = true;
            SampleBackgroundColorAtPointer(e.GetPosition(pnlOriginal));
            return;
        }

        UpdateCropCanvasSize();
        startPoint = e.GetPosition(pnlOriginal);
        isDragging = true;
        rectCrop.IsVisible = true;
        Canvas.SetLeft(rectCrop, startPoint.X);
        Canvas.SetTop(rectCrop, startPoint.Y);
        rectCrop.Width = 0;
        rectCrop.Height = 0;
    }

    private void PnlOriginal_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!isDragging) return;
        var normalized = CoordinateMapper.ComputeNormalizedRect(startPoint, e.GetPosition(pnlOriginal));
        Canvas.SetLeft(rectCrop, normalized.X);
        Canvas.SetTop(rectCrop, normalized.Y);
        rectCrop.Width = normalized.Width;
        rectCrop.Height = normalized.Height;
    }

    private void PnlOriginal_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!isDragging) return;
        isDragging = false;
        rectCrop.IsVisible = false;

        var rect = CoordinateMapper.ComputeNormalizedRect(startPoint, e.GetPosition(pnlOriginal));
        if (rect.Width < 5 || rect.Height < 5) return;

        ApplyManualCrop(rect);
    }

    private async void ApplyManualCrop(Rect uiRect)
    {
        var photo = OriginalPhotos[currentIndex];
        var imageRect = GetImageRectInsideControl();
        var originalSize = new System.Drawing.Size(photo.Original.Width, photo.Original.Height);
        var cropRect = CoordinateMapper.MapUiRectToImageRect(uiRect, imageRect, originalSize);

        if (cropRect.Width <= 10 || cropRect.Height <= 10) return;

        string extractingMsg = Application.Current?.FindResource("MsgExtractingCrop")?.ToString() ?? "Extracting manual crop...";
        string addedMsg = Application.Current?.FindResource("MsgManualCropAdded")?.ToString() ?? "Manual crop added.";

        await ExecuteWithLoadingAsync(extractingMsg, async () =>
        {
            int prevCount = photo.DetectedPhotos.Count;
            await Task.Run(() => photo.AddManualCrop(cropRect));
            if (photo.DetectedPhotos.Count > prevCount)
            {
                int newIndex = photo.DetectedPhotos.Count - 1;
                undoHistory.PushAdd(currentIndex, newIndex, photo.DetectedPhotos[newIndex]);
            }
            LoadCroppedPhotosToSlider();
            slides.SelectedIndex = photo.DetectedPhotos.Count - 1;
        }, addedMsg);
    }

    private Rect GetImageRectInsideControl()
    {
        if (img?.Source == null || pnlOriginal == null) return new Rect();

        double zoom = sldZoom.Value;
        double w = img.Bounds.Width * zoom;
        double h = img.Bounds.Height * zoom;
        double x = (pnlOriginal.Bounds.Width - w) / 2;
        double y = (pnlOriginal.Bounds.Height - h) / 2;

        return new Rect(x, y, w, h);
    }

    private bool isRefining;
    private System.Drawing.Rectangle currentRefineRect;
    private Point startRefinePoint;
    private bool isRefineDragging;

    private void BtnRefine_Click(object? sender, RoutedEventArgs e) => StartRefineMode();

    private void StartRefineMode()
    {
        if (OriginalPhotos.Count == 0 || slides == null || slides.SelectedIndex < 0) return;
        int photoIndex = slides.SelectedIndex;

        var photoCropper = OriginalPhotos[currentIndex];
        currentRefineRect = photoCropper.GetRefinedCropRect(photoIndex);

        if (currentRefineRect.IsEmpty || currentRefineRect.Width <= 10 || currentRefineRect.Height <= 10)
        {
            currentRefineRect = new System.Drawing.Rectangle(0, 0, photoCropper.DetectedPhotos[photoIndex].Width, photoCropper.DetectedPhotos[photoIndex].Height);
        }

        isRefining = true;
        pnlRefineOverlay.IsVisible = true;
        lblStatus.Text = Application.Current?.FindResource("MsgRefineModeHelp")?.ToString() ?? "Refinement mode active.";

        UpdateRefinePreview();
    }

    private void UpdateRefinePreview()
    {
        if (OriginalPhotos.Count == 0 || slides == null || slides.SelectedIndex < 0) return;
        int photoIndex = slides.SelectedIndex;

        var photoCropper = OriginalPhotos[currentIndex];
        using Mat previewMat = photoCropper.DetectedPhotos[photoIndex].Clone();

        System.Drawing.Rectangle drawRect = currentRefineRect;
        int thickness = 8;
        drawRect.Inflate(-thickness / 2, -thickness / 2);

        CvInvoke.Rectangle(previewMat, drawRect, new MCvScalar(0, 0, 255), thickness);
        imgRefine.Source = MatBitmapConverter.ToAvaloniaBitmap(previewMat);
    }

    private void PnlRefine_PointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (!isRefining) return;
        startRefinePoint = e.GetPosition(pnlRefineImage);
        isRefineDragging = true;
        rectRefineCrop.IsVisible = true;
        Canvas.SetLeft(rectRefineCrop, startRefinePoint.X);
        Canvas.SetTop(rectRefineCrop, startRefinePoint.Y);
        rectRefineCrop.Width = 0;
        rectRefineCrop.Height = 0;
    }

    private void PnlRefine_PointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (!isRefineDragging) return;
        var normalized = CoordinateMapper.ComputeNormalizedRect(startRefinePoint, e.GetPosition(pnlRefineImage));
        Canvas.SetLeft(rectRefineCrop, normalized.X);
        Canvas.SetTop(rectRefineCrop, normalized.Y);
        rectRefineCrop.Width = normalized.Width;
        rectRefineCrop.Height = normalized.Height;
    }

    private void PnlRefine_PointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (!isRefineDragging) return;
        isRefineDragging = false;
        rectRefineCrop.IsVisible = false;

        var uiRect = CoordinateMapper.ComputeNormalizedRect(startRefinePoint, e.GetPosition(pnlRefineImage));
        if (uiRect.Width < 5 || uiRect.Height < 5) return;

        var imageRect = GetRefineImageRectInsideControl();
        int photoIndex = slides.SelectedIndex;
        var photo = OriginalPhotos[currentIndex].DetectedPhotos[photoIndex];
        var photoSize = new System.Drawing.Size(photo.Width, photo.Height);

        currentRefineRect = CoordinateMapper.MapUiRectToImageRect(uiRect, imageRect, photoSize);
        UpdateRefinePreview();
    }

    private Rect GetRefineImageRectInsideControl()
    {
        if (imgRefine?.Source == null) return new Rect();

        var controlSize = pnlRefineImage.Bounds.Size;
        var imageSize = imgRefine.Source.Size;

        double availableWidth = controlSize.Width - 40;
        double availableHeight = controlSize.Height - 40;
        double scale = Math.Min(availableWidth / imageSize.Width, availableHeight / imageSize.Height);

        double w = imageSize.Width * scale;
        double h = imageSize.Height * scale;
        double x = 20 + (availableWidth - w) / 2;
        double y = 20 + (availableHeight - h) / 2;

        return new Rect(x, y, w, h);
    }

    private void BtnAcceptRefine_Click(object? sender, RoutedEventArgs e) => AcceptRefine();
    private void BtnRejectRefine_Click(object? sender, RoutedEventArgs e) => RejectRefine();

    private async void AcceptRefine()
    {
        if (!isRefining) return;
        int photoIndex = slides.SelectedIndex;

        string applyingMsg = Application.Current?.FindResource("MsgApplyingRefine")?.ToString() ?? "Applying refinement...";
        string successMsg = Application.Current?.FindResource("MsgRefineSuccess")?.ToString() ?? "Crop refined successfully.";

        var currentEngine = OriginalPhotos[currentIndex];
        var beforeMat = currentEngine.DetectedPhotos[photoIndex].Clone();

        await ExecuteWithLoadingAsync(applyingMsg, async () =>
        {
            await Task.Run(() => currentEngine.ApplyCropToPhoto(photoIndex, currentRefineRect));
            undoHistory.PushReplace(currentIndex, photoIndex, beforeMat, currentEngine.DetectedPhotos[photoIndex]);
            beforeMat.Dispose();
            CloseRefineMode();
            LoadCroppedPhotosToSlider();
            slides.SelectedIndex = photoIndex;
        }, successMsg);
    }

    private void RejectRefine()
    {
        if (!isRefining) return;
        CloseRefineMode();
        lblStatus.Text = Application.Current?.FindResource("MsgRefineCancelled")?.ToString() ?? "Refinement cancelled.";
    }

    private void CloseRefineMode()
    {
        isRefining = false;
        pnlRefineOverlay.IsVisible = false;
        imgRefine.Source = null;
    }

    private async void BtnResetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;

        SettingsManager.Instance.ResetDetectionDefaults();
        var settings = SettingsManager.Instance.Settings;
        sldSensitivity.Value = settings.BackgroundTolerance;
        sldMinArea.Value = settings.MinAreaFactor;
        sldMaxArea.Value = settings.MaxAreaFactor;
        sldEdge.Value = settings.CannyLowThreshold;

        if (OriginalPhotos.Count > 0)
        {
            var photo = OriginalPhotos[currentIndex];
            photo.ApplyOptions(GetDetectionOptionsFromUi());

            string reprocessingMsg = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing...";
            await ExecuteWithLoadingAsync(reprocessingMsg, async () =>
            {
                await Task.Run(() => photo.DetectPhotos());
                await LoadPhotosToGuiAsync();
                string msgFormat = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete. Found {0} photos.";
                lblStatus.Text = string.Format(msgFormat, photo.DetectedPhotos.Count);
            });
        }
    }

    private void TglColorPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlOriginal == null || tglColorPicker == null) return;
        pnlOriginal.Cursor = tglColorPicker.IsChecked == true 
            ? new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Cross) 
            : Avalonia.Input.Cursor.Default;
    }

    private async void SampleBackgroundColorAtPointer(Point uiPoint)
    {
        if (OriginalPhotos.Count == 0 || tglColorPicker == null || btnResetBackground == null) return;
        var photo = OriginalPhotos[currentIndex];
        var imageRect = GetImageRectInsideControl();
        var originalSize = new System.Drawing.Size(photo.Original.Width, photo.Original.Height);
        var pixel = CoordinateMapper.MapUiPointToImagePixel(uiPoint, imageRect, originalSize);

        pnlOriginal.Cursor = Avalonia.Input.Cursor.Default;
        tglColorPicker.IsChecked = false;

        string samplingMsg = Application.Current?.FindResource("MsgClickToSample")?.ToString() ?? "Sampling background color...";
        string completeMsg = Application.Current?.FindResource("MsgBackgroundSampled")?.ToString() ?? "Custom background color applied.";

        await ExecuteWithLoadingAsync(samplingMsg, async () =>
        {
            await Task.Run(() =>
            {
                photo.SetCustomBackgroundFromPixel(pixel.X, pixel.Y);
                photo.DetectPhotos();
            });
            btnResetBackground.IsEnabled = true;
            await LoadPhotosToGuiAsync();
        }, completeMsg);
    }

    private async void BtnResetBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (OriginalPhotos.Count == 0 || btnResetBackground == null) return;
        var photo = OriginalPhotos[currentIndex];

        photo.CustomBackgroundColorHsv = null;
        btnResetBackground.IsEnabled = false;

        string reprocessingMsg = Application.Current?.FindResource("MsgReprocessing")?.ToString() ?? "Reprocessing with automatic background...";
        string completeMsg = Application.Current?.FindResource("MsgDetectionComplete")?.ToString() ?? "Detection complete.";

        await ExecuteWithLoadingAsync(reprocessingMsg, async () =>
        {
            await Task.Run(() => photo.DetectPhotos());
            await LoadPhotosToGuiAsync();
        }, completeMsg);
    }

    protected override void OnClosed(EventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.BackgroundTolerance = sldSensitivity.Value;
        settings.ZoomLevel = sldZoom.Value;
        settings.MinAreaFactor = sldMinArea.Value;
        settings.MaxAreaFactor = sldMaxArea.Value;
        settings.CannyLowThreshold = sldEdge.Value;
        settings.AdvancedVisible = tglAdvanced.IsChecked ?? false;

        if (cbFormat != null)
        {
            settings.PreferredFormat = cbFormat.SelectedIndex == 1 ? "PNG" : "JPEG";
        }
        if (sldJpegQuality != null)
        {
            settings.JpegQuality = (int)sldJpegQuality.Value;
        }
        if (txtOutputDir != null)
        {
            settings.CustomOutputDirectory = string.IsNullOrEmpty(txtOutputDir.Text) ? null : txtOutputDir.Text;
        }

        SettingsManager.Instance.Save();

        base.OnClosed(e);
        undoHistory.Dispose();
        foreach (var photo in OriginalPhotos)
        {
            photo.Dispose();
        }
        OriginalPhotos.Clear();
    }
}
