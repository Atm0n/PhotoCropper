using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Emgu.CV;
using Emgu.CV.Structure;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private Point startPoint;
    private bool isDragging;
    private bool isDraggingHandle;
    private bool isRotatingHandle;
    private double initialMouseAngle;
    private double initialRotatedAngle;
    private int dragCandIndex = -1;
    private int dragVertexIndex = -1;
    private bool isRefining;
    private System.Drawing.Rectangle currentRefineRect;
    private Point startRefinePoint;
    private bool isRefineDragging;

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

    private void ScrollOriginal_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            double oldZoom = sldZoom.Value;
            double delta = e.Delta.Y > 0 ? 1.1 : 0.9;
            double newZoom = Math.Clamp(oldZoom * delta, sldZoom.Minimum, sldZoom.Maximum);

            if (Math.Abs(newZoom - oldZoom) > 0.0001)
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

        if (img.Source is Avalonia.Media.Imaging.Bitmap bmp && bmp.PixelSize.Width > 0 && bmp.PixelSize.Height > 0)
        {
            double srcW = bmp.PixelSize.Width;
            double srcH = bmp.PixelSize.Height;

            double scale = Math.Min(availableW / srcW, availableH / srcH);
            double fitW = Math.Max(10, srcW * scale);
            double fitH = Math.Max(10, srcH * scale);

            img.Width = fitW;
            img.Height = fitH;

            double zoom = sldZoom?.Value ?? 1.0;
            double zoomedW = fitW * zoom;
            double zoomedH = fitH * zoom;

            pnlOriginal.Width = zoomedW;
            pnlOriginal.Height = zoomedH;
            cnvCrop.Width = zoomedW;
            cnvCrop.Height = zoomedH;
        }
        else
        {
            img.Width = availableW;
            img.Height = availableH;
            pnlOriginal.Width = availableW;
            pnlOriginal.Height = availableH;
            cnvCrop.Width = availableW;
            cnvCrop.Height = availableH;
        }
    }

    private void PnlOriginal_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ScanSessions.Count == 0) return;

        if (tglColorPicker?.IsChecked == true)
        {
            e.Handled = true;
            _ = SampleBackgroundColorAtPointerAsync(e.GetPosition(pnlOriginal));
            return;
        }

        UpdateCropCanvasSize();
        startPoint = e.GetPosition(pnlOriginal);

        var photo = ScanSessions[CurrentIndex].Activate();
        if (photo.AcceptedCandidates != null)
        {
            var pt = CoordinateMapper.MapUiPointToImagePixel(startPoint, GetImageRectInsideControl(), new System.Drawing.Size(photo.OriginalWithDetected.Width, photo.OriginalWithDetected.Height));
            for (int i = photo.AcceptedCandidates.Count - 1; i >= 0; i--)
            {
                var cand = photo.AcceptedCandidates[i];
                var vertices = cand.Rotated.GetVertices();
                var sortedVertices = (System.Drawing.PointF[])vertices.Clone();
                System.Array.Sort(sortedVertices, (a, b) => (b.X - b.Y).CompareTo(a.X - a.Y));
                var tr = sortedVertices[0];




                var brSorted = (System.Drawing.PointF[])vertices.Clone();
                System.Array.Sort(brSorted, (a, b) => (b.X + b.Y).CompareTo(a.X + a.Y));
                var br = brSorted[0];
                double rotateDist = Math.Sqrt(Math.Pow(br.X - pt.X, 2) + Math.Pow(br.Y - pt.Y, 2));
                if (rotateDist <= 45)
                {
                    isRotatingHandle = true;
                    dragCandIndex = i;
                    initialMouseAngle = Math.Atan2(pt.Y - cand.Rotated.Center.Y, pt.X - cand.Rotated.Center.X) * 180.0 / Math.PI;
                    initialRotatedAngle = cand.Rotated.Angle;
                    e.Handled = true;
                    return;
                }

                for (int j = 0; j < 4; j++)
                {
                    double dist = Math.Sqrt(Math.Pow(vertices[j].X - pt.X, 2) + Math.Pow(vertices[j].Y - pt.Y, 2));
                    if (dist <= 60)
                    {
                        isDraggingHandle = true;
                        dragCandIndex = i;
                        dragVertexIndex = j;
                        e.Handled = true;
                        return;
                    }
                }
            }
        }

        isDragging = true;
        rectCrop.IsVisible = true;
        Canvas.SetLeft(rectCrop, startPoint.X);
        Canvas.SetTop(rectCrop, startPoint.Y);
        rectCrop.Width = 0;
        rectCrop.Height = 0;
    }

    private void PnlOriginal_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (isRotatingHandle && dragCandIndex >= 0)
        {
            var photo = ScanSessions[CurrentIndex].Activate();
            var pt = CoordinateMapper.MapUiPointToImagePixel(e.GetPosition(pnlOriginal), GetImageRectInsideControl(), new System.Drawing.Size(photo.OriginalWithDetected.Width, photo.OriginalWithDetected.Height));
            var cand = photo.AcceptedCandidates[dragCandIndex];

            double currentMouseAngle = Math.Atan2(pt.Y - cand.Rotated.Center.Y, pt.X - cand.Rotated.Center.X) * 180.0 / Math.PI;
            double delta = currentMouseAngle - initialMouseAngle;
            double newAngle = initialRotatedAngle + delta;

            var newRotated = new Emgu.CV.Structure.RotatedRect(cand.Rotated.Center, cand.Rotated.Size, (float)newAngle);
            var updatedCand = cand with { Rotated = newRotated };

            var newList = new System.Collections.Generic.List<PhotoCropper.Core.Models.CropCandidate>(photo.AcceptedCandidates);
            newList[dragCandIndex] = updatedCand;

            photo.UpdateCandidates(newList);
            SetMainImage(photo.OriginalWithDetected);
            return;
        }
        if (isDraggingHandle && dragCandIndex >= 0)
        {
            var photo = ScanSessions[CurrentIndex].Activate();
            var pt = CoordinateMapper.MapUiPointToImagePixel(e.GetPosition(pnlOriginal), GetImageRectInsideControl(), new System.Drawing.Size(photo.OriginalWithDetected.Width, photo.OriginalWithDetected.Height));

            var cand = photo.AcceptedCandidates[dragCandIndex];
            var vertices = cand.Rotated.GetVertices();
            var oppVertex = vertices[(dragVertexIndex + 2) % 4];

            var newCenter = new System.Drawing.PointF((pt.X + oppVertex.X) / 2f, (pt.Y + oppVertex.Y) / 2f);

            double theta = cand.Rotated.Angle * Math.PI / 180.0;
            double dx = pt.X - oppVertex.X;
            double dy = pt.Y - oppVertex.Y;

            double ux = Math.Cos(theta);
            double uy = Math.Sin(theta);
            double vx = -Math.Sin(theta);
            double vy = Math.Cos(theta);

            float newW = (float)Math.Abs(dx * ux + dy * uy);
            float newH = (float)Math.Abs(dx * vx + dy * vy);

            var newRotated = new Emgu.CV.Structure.RotatedRect(newCenter, new System.Drawing.SizeF(newW, newH), cand.Rotated.Angle);
            var updatedCand = cand with { Rotated = newRotated };

            var newList = new System.Collections.Generic.List<PhotoCropper.Core.Models.CropCandidate>(photo.AcceptedCandidates);
            newList[dragCandIndex] = updatedCand;

            photo.UpdateCandidates(newList);
            SetMainImage(photo.OriginalWithDetected);
            return;
        }

        if (!isDragging) return;
        var normalized = CoordinateMapper.ComputeNormalizedRect(startPoint, e.GetPosition(pnlOriginal));
        Canvas.SetLeft(rectCrop, normalized.X);
        Canvas.SetTop(rectCrop, normalized.Y);
        rectCrop.Width = normalized.Width;
        rectCrop.Height = normalized.Height;
    }

    private void PnlOriginal_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {

        if (isRotatingHandle)
        {
            isRotatingHandle = false;
            if (dragCandIndex >= 0)
            {
                var photo = ScanSessions[CurrentIndex].Activate();
                photo.ApplyGrabHandleResize(dragCandIndex);
                ScanSessions[CurrentIndex].IsModified = true;
                LoadCroppedPhotosToSlider();
                UpdateDetectionCoverageLabel();
            }
            dragCandIndex = -1;
            return;
        }

        if (isDraggingHandle)
        {
            isDraggingHandle = false;
            if (dragCandIndex >= 0)
            {
                var ptRelease = e.GetPosition(pnlOriginal);
                var rectRelease = CoordinateMapper.ComputeNormalizedRect(startPoint, ptRelease);
                var photo = ScanSessions[CurrentIndex].Activate();

                if (rectRelease.Width < 5 && rectRelease.Height < 5 && dragVertexIndex >= 0)
                {
                    var cand = photo.AcceptedCandidates[dragCandIndex];
                    var vertices = cand.Rotated.GetVertices();
                    var sortedVertices = (System.Drawing.PointF[])vertices.Clone();
                    System.Array.Sort(sortedVertices, (a, b) => (b.X - b.Y).CompareTo(a.X - a.Y));
                    var tr = sortedVertices[0];
                    int trIndex = System.Array.IndexOf(vertices, tr);

                    if (dragVertexIndex == trIndex)
                    {
                        photo.DeletePhoto(dragCandIndex);
                        ScanSessions[CurrentIndex].IsModified = true;
                        SetMainImage(photo.OriginalWithDetected);
                        LoadCroppedPhotosToSlider();
                        UpdatePhotoCounterLabel();
                        UpdateDetectionCoverageLabel();
                        dragCandIndex = -1;
                        return;
                    }
                }

                photo.ApplyGrabHandleResize(dragCandIndex);
                ScanSessions[CurrentIndex].IsModified = true;
                LoadCroppedPhotosToSlider();
                UpdateDetectionCoverageLabel();
            }
            dragCandIndex = -1;
            return;
        }

        if (!isDragging) return;
        isDragging = false;
        rectCrop.IsVisible = false;
        var rect = CoordinateMapper.ComputeNormalizedRect(startPoint, e.GetPosition(pnlOriginal));

        if (rect.Width < 5 || rect.Height < 5)
        {
            // Click-to-Select / Delete
            var photo = ScanSessions[CurrentIndex].Activate();
            var imageRect = GetImageRectInsideControl();
            var originalSize = new System.Drawing.Size(photo.OriginalWithDetected.Width, photo.OriginalWithDetected.Height);

            var clickRectImg = CoordinateMapper.MapUiRectToImageRect(rect, imageRect, originalSize);
            var pt = new System.Drawing.PointF(clickRectImg.X, clickRectImg.Y);

            // Iterate candidates backwards to hit the top ones first
            for (int i = photo.AcceptedCandidates.Count - 1; i >= 0; i--)
            {
                var cand = photo.AcceptedCandidates[i];
                var vertices = cand.Rotated.GetVertices();
                System.Array.Sort(vertices, (a, b) => (b.X - b.Y).CompareTo(a.X - a.Y));
                var tr = vertices[0];

                // Check if click is on the Red X
                double dist = Math.Sqrt(Math.Pow(tr.X - pt.X, 2) + Math.Pow(tr.Y - pt.Y, 2));
                if (dist <= 60) // Slightly larger hit box for usability
                {
                    photo.DeletePhoto(i);
                    ScanSessions[CurrentIndex].IsModified = true;
                    // Force refresh image by recreating it
                    SetMainImage(photo.OriginalWithDetected);
                    LoadCroppedPhotosToSlider();
                    UpdatePhotoCounterLabel();
                    UpdateDetectionCoverageLabel();
                    return;
                }

                // Check if click is inside bounding box
                using (var vec = new Emgu.CV.Util.VectorOfPointF(cand.Rotated.GetVertices()))
                {
                    if (Emgu.CV.CvInvoke.PointPolygonTest(vec, pt, false) >= 0)
                    {
                        if (slides != null) slides.SelectedIndex = i;
                        return;
                    }
                }
            }
            return;
        }

        _ = ApplyManualCropAsync(rect);
    }

    private async Task ApplyManualCropAsync(Rect uiRect)
    {
        var photo = ScanSessions[CurrentIndex].Activate();
        var imageRect = GetImageRectInsideControl();
        var originalSize = new System.Drawing.Size(photo.Original.Width, photo.Original.Height);
        var cropRect = CoordinateMapper.MapUiRectToImageRect(uiRect, imageRect, originalSize);

        if (cropRect.Width <= 10 || cropRect.Height <= 10) return;

        string extractingMsg = LocalizationService.GetString(ResourceKeys.MsgExtractingCrop, "Extracting manual crop...");
        string addedMsg = LocalizationService.GetString(ResourceKeys.MsgManualCropAdded, "Manual crop added.");

        await ExecuteWithLoadingAsync(extractingMsg, async ct =>
        {
            int prevCount = photo.DetectedPhotos.Count;
            ScanSessions[CurrentIndex].IsModified = true;
            await Task.Run(() => photo.AddManualCrop(cropRect), ct);
            if (photo.DetectedPhotos.Count > prevCount)
            {
                int newIndex = photo.DetectedPhotos.Count - 1;
                undoHistory.PushAdd(CurrentIndex, newIndex, photo.DetectedPhotos[newIndex]);
            }
            SetMainImage(photo.OriginalWithDetected);
            LoadCroppedPhotosToSlider();
            slides.SelectedIndex = photo.DetectedPhotos.Count - 1;
        }, addedMsg);
    }

    private Rect GetImageRectInsideControl()
    {
        if (img?.Source == null || pnlOriginal == null) return new Rect();

        double zoom = sldZoom?.Value ?? 1.0;
        double w = (double.IsNaN(img.Width) ? img.Bounds.Width : img.Width) * zoom;
        double h = (double.IsNaN(img.Height) ? img.Bounds.Height : img.Height) * zoom;
        double x = (pnlOriginal.Bounds.Width - w) / 2;
        double y = (pnlOriginal.Bounds.Height - h) / 2;

        return new Rect(Math.Max(0, x), Math.Max(0, y), Math.Max(1, w), Math.Max(1, h));
    }

    private void BtnRefine_Click(object? sender, RoutedEventArgs e) => StartRefineMode();

    private void StartRefineMode()
    {
        if (ScanSessions.Count == 0 || slides == null || slides.SelectedIndex < 0) return;
        int photoIndex = slides.SelectedIndex;

        var photoCropper = ScanSessions[CurrentIndex].Activate();
        currentRefineRect = photoCropper.GetRefinedCropRect(photoIndex);

        if (currentRefineRect.IsEmpty || currentRefineRect.Width <= 10 || currentRefineRect.Height <= 10)
        {
            currentRefineRect = new System.Drawing.Rectangle(0, 0, photoCropper.DetectedPhotos[photoIndex].Width, photoCropper.DetectedPhotos[photoIndex].Height);
        }

        isRefining = true;
        pnlRefineOverlay.IsVisible = true;
        lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgRefineModeHelp, "Refinement mode active.");

        UpdateRefinePreview();
    }

    private void UpdateRefinePreview()
    {
        if (ScanSessions.Count == 0 || slides == null || slides.SelectedIndex < 0) return;
        int photoIndex = slides.SelectedIndex;

        var photoCropper = ScanSessions[CurrentIndex].Activate();
        using Mat previewMat = photoCropper.DetectedPhotos[photoIndex].Clone();

        System.Drawing.Rectangle drawRect = currentRefineRect;
        int thickness = 8;
        drawRect.Inflate(-thickness / 2, -thickness / 2);

        CvInvoke.Rectangle(previewMat, drawRect, new MCvScalar(0, 0, 255), thickness);
        SetRefineImage(previewMat);
    }

    private void PnlRefine_PointerPressed(object? sender, PointerPressedEventArgs e)
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

    private void PnlRefine_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!isRefineDragging) return;
        var normalized = CoordinateMapper.ComputeNormalizedRect(startRefinePoint, e.GetPosition(pnlRefineImage));
        Canvas.SetLeft(rectRefineCrop, normalized.X);
        Canvas.SetTop(rectRefineCrop, normalized.Y);
        rectRefineCrop.Width = normalized.Width;
        rectRefineCrop.Height = normalized.Height;
    }

    private void PnlRefine_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!isRefineDragging) return;
        isRefineDragging = false;
        rectRefineCrop.IsVisible = false;

        var uiRect = CoordinateMapper.ComputeNormalizedRect(startRefinePoint, e.GetPosition(pnlRefineImage));
        if (uiRect.Width < 5 || uiRect.Height < 5) return;

        var imageRect = GetRefineImageRectInsideControl();
        int photoIndex = slides.SelectedIndex;
        var photo = ScanSessions[CurrentIndex].Activate().DetectedPhotos[photoIndex];
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

    private async void BtnAcceptRefine_Click(object? sender, RoutedEventArgs e) => await AcceptRefineAsync();
    private void BtnRejectRefine_Click(object? sender, RoutedEventArgs e) => RejectRefine();

    private async Task AcceptRefineAsync()
    {
        if (!isRefining) return;
        int photoIndex = slides.SelectedIndex;

        string applyingMsg = LocalizationService.GetString(ResourceKeys.MsgApplyingRefine, "Applying refinement...");
        string successMsg = LocalizationService.GetString(ResourceKeys.MsgRefineSuccess, "Crop refined successfully.");

        var currentEngine = ScanSessions[CurrentIndex].Activate();
        var beforeMat = currentEngine.DetectedPhotos[photoIndex].Clone();

        await ExecuteWithLoadingAsync(applyingMsg, async ct =>
        {
            ScanSessions[CurrentIndex].IsModified = true;
            await Task.Run(() => currentEngine.ApplyCropToPhoto(photoIndex, currentRefineRect), ct);
            undoHistory.PushReplace(CurrentIndex, photoIndex, beforeMat, currentEngine.DetectedPhotos[photoIndex]);
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
        lblStatus.Text = LocalizationService.GetString(ResourceKeys.MsgRefineCancelled, "Refinement cancelled.");
    }

    private void CloseRefineMode()
    {
        isRefining = false;
        pnlRefineOverlay.IsVisible = false;
        SetRefineImage(null);
    }

    private async void BtnResetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;

        SettingsManager.Instance.ResetDetectionDefaults();
        var settings = SettingsManager.Instance.Settings;
        var defaultOptions = new DetectionOptions
        {
            BackgroundTolerance = settings.BackgroundTolerance,
            MinAreaFactor = settings.MinAreaFactor / 100.0,
            MaxAreaFactor = settings.MaxAreaFactor / 100.0,
            CannyLowThreshold = settings.CannyLowThreshold,
            CannyHighThreshold = settings.CannyLowThreshold * 2.5,
            AutoOrientPhotos = settings.AutoOrientPhotos,
            RestoreVintageColors = settings.RestoreVintageColors,
            RemoveDustAndScratches = settings.RemoveDustAndScratches
        };

        SyncUiWithScanOptions(defaultOptions);

        if (ScanSessions.Count > 0)
        {
            var photo = ScanSessions[CurrentIndex].Activate();
            photo.ApplyOptions(defaultOptions);

            string reprocessingMsg = LocalizationService.GetString(ResourceKeys.MsgReprocessing, "Reprocessing...");
            await ExecuteWithLoadingAsync(reprocessingMsg, async ct =>
            {
                await Task.Run(() => photo.DetectPhotos(), ct);
                SetMainImage(photo.OriginalWithDetected);
                LoadCroppedPhotosToSlider();
                UpdatePhotoCounterLabel();
                UpdateDetectionCoverageLabel();
                lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgDetectionComplete, "Detection complete. Found {0} photos.", photo.DetectedPhotos.Count);
            });
        }
    }

    private void TglColorPicker_Click(object? sender, RoutedEventArgs e)
    {
        if (pnlOriginal == null || tglColorPicker == null) return;
        pnlOriginal.Cursor = tglColorPicker.IsChecked == true
            ? new Cursor(StandardCursorType.Cross)
            : Cursor.Default;
    }

    private async Task SampleBackgroundColorAtPointerAsync(Point uiPoint)
    {
        if (ScanSessions.Count == 0 || tglColorPicker == null || btnResetBackground == null) return;
        var photo = ScanSessions[CurrentIndex].Activate();
        var imageRect = GetImageRectInsideControl();
        var originalSize = new System.Drawing.Size(photo.Original.Width, photo.Original.Height);
        var pixel = CoordinateMapper.MapUiPointToImagePixel(uiPoint, imageRect, originalSize);

        pnlOriginal.Cursor = Cursor.Default;
        tglColorPicker.IsChecked = false;

        string samplingMsg = LocalizationService.GetString(ResourceKeys.MsgClickToSample, "Sampling background color...");
        string completeMsg = LocalizationService.GetString(ResourceKeys.MsgBackgroundSampled, "Custom background color applied.");

        await ExecuteWithLoadingAsync(samplingMsg, async ct =>
        {
            ScanSessions[CurrentIndex].IsModified = true;
            await Task.Run(() =>
            {
                photo.SetCustomBackgroundFromPixel(pixel.X, pixel.Y);
                photo.DetectPhotos();
            }, ct);
            btnResetBackground.IsEnabled = true;
            await LoadPhotosToGuiAsync();
        }, completeMsg);
    }

    private async void BtnResetBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (ScanSessions.Count == 0 || btnResetBackground == null) return;
        var photo = ScanSessions[CurrentIndex].Activate();

        photo.CustomBackgroundColorHsv = null;
        ScanSessions[CurrentIndex].IsModified = true;
        btnResetBackground.IsEnabled = false;

        string reprocessingMsg = LocalizationService.GetString(ResourceKeys.MsgReprocessing, "Reprocessing with automatic background...");
        string completeMsg = LocalizationService.GetString(ResourceKeys.MsgDetectionComplete, "Detection complete.");

        await ExecuteWithLoadingAsync(reprocessingMsg, async ct =>
        {
            await Task.Run(() => photo.DetectPhotos(), ct);
            await LoadPhotosToGuiAsync();
        }, completeMsg);
    }
}
