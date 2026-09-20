using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private bool HasUnsavedChanges()
    {
        return ScanSessions.Any(s => !s.IsSaved || s.IsModified);
    }

    private async void BtnSaveImages_Click(object? sender, RoutedEventArgs e)
    {
        await SavePendingScansAsync(closeAfterSave: false);
    }

    private async Task<bool> SavePendingScansAsync(bool closeAfterSave)
    {
        if (isLoading || ScanSessions.Count == 0) return false;

        var pendingSessions = ScanSessions.Where(s => !s.IsSaved || s.IsModified).ToList();
        if (pendingSessions.Count == 0)
        {
            string alreadySavedMsg = Avalonia.Application.Current?.FindResource("MsgAllScansAlreadySaved")?.ToString()
                ?? "All {0} scans are already saved to 'Cropped'. No changes to export.";
            lblStatus.Text = string.Format(alreadySavedMsg, ScanSessions.Count);

            if (closeAfterSave)
            {
                _isExitingConfirmed = true;
                Close();
            }
            return true;
        }

        var settings = SettingsManager.Instance.Settings;
        int totalScans = pendingSessions.Count;
        int completedScans = 0;
        int totalSavedPhotos = 0;

        string savingMsg = Avalonia.Application.Current?.FindResource("MsgSavingProgress")?.ToString() ?? "Exporting scan {0} of {1} ({2} photos saved)...";
        string msgFormat = Avalonia.Application.Current?.FindResource("MsgSaved")?.ToString() ?? "Successfully saved {0} photos to 'cropped' folders.";

        string targetOutputFolder = ExportPathResolver.ResolveOutputDirectory(
            pendingSessions[0].FilePath,
            settings.CustomOutputDirectory,
            settings.WorkDirectory);

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        await ExecuteWithLoadingAsync(string.Format(savingMsg, 1, totalScans, 0), async () =>
        {
            await Task.Run(() =>
            {
                Parallel.ForEach(pendingSessions, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, (session) =>
                {
                    bool wasActive = session.IsActive;
                    var engine = session.Activate();
                    try
                    {
                        string scanTargetFolder = ExportPathResolver.ResolveOutputDirectory(
                            session.FilePath,
                            settings.CustomOutputDirectory,
                            settings.WorkDirectory);

                        engine.SaveDetectedPhotos(
                            scanTargetFolder,
                            settings.PreferredFormat,
                            settings.JpegQuality);

                        int savedCount = engine.DetectedPhotos.Count;
                        Interlocked.Add(ref totalSavedPhotos, savedCount);

                        session.IsSaved = true;
                        session.IsModified = false;

                        if (_workspaceSession != null)
                        {
                            var entry = _workspaceSession.Scans.FirstOrDefault(s =>
                                string.Equals(s.RelativePath, session.FilePath, StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Path.GetFileName(s.RelativePath), Path.GetFileName(session.FilePath), StringComparison.OrdinalIgnoreCase));
                            if (entry != null)
                            {
                                entry.IsProcessed = true;
                                entry.ExtractedPhotoCount = savedCount;
                            }
                        }
                    }
                    finally
                    {
                        if (!wasActive)
                        {
                            session.Deactivate();
                        }
                    }

                    int done = Interlocked.Increment(ref completedScans);
                    if (done % 15 == 0)
                    {
                        GC.Collect(1, GCCollectionMode.Optimized, false);
                    }

                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        lblStatus.Text = string.Format(savingMsg, done, totalScans, totalSavedPhotos);
                    });
                });

                if (_workspaceSession != null && !string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
                {
                    ProjectWorkspaceService.SaveSession(settings.WorkDirectory, _workspaceSession);
                }
            });

            PhotoCropper.Core.Utils.NotificationSound.PlayCompletionSound();
        }, string.Format(msgFormat, totalSavedPhotos));

        if (closeAfterSave)
        {
            _isExitingConfirmed = true;
            Close();
        }

        return true;
    }

    private async Task ExportSinglePhotoAsync(int photoIndex)
    {
        if (isLoading || ScanSessions.Count == 0 || photoIndex < 0) return;

        var engine = ScanSessions[currentIndex].Activate();
        if (photoIndex >= engine.DetectedPhotos.Count) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var settings = SettingsManager.Instance.Settings;
        string defaultExt = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        string baseName = Path.GetFileNameWithoutExtension(ScanSessions[currentIndex].FilePath);
        string suggestedName = $"{baseName}_{photoIndex + 1}{defaultExt}";

        var fileResult = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Avalonia.Application.Current?.FindResource("MenuExportSingle")?.ToString() ?? "Export Single Photo",
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExt
        });

        if (fileResult != null)
        {
            string targetPath = fileResult.Path.LocalPath;
            var photoMat = engine.DetectedPhotos[photoIndex];
            var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(ScanSessions[currentIndex].FilePath);

            if (targetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var buf = new Emgu.CV.Util.VectorOfByte();
                Emgu.CV.CvInvoke.Imencode(".png", photoMat, buf);
                await File.WriteAllBytesAsync(targetPath, buf.ToArray());
                PhotoExporter.EmbedPngDpi(targetPath, xDpi, yDpi);
            }
            else
            {
                var parameters = new[] {
                    new KeyValuePair<Emgu.CV.CvEnum.ImwriteFlags, int>(Emgu.CV.CvEnum.ImwriteFlags.JpegQuality, settings.JpegQuality)
                };
                using var buf = new Emgu.CV.Util.VectorOfByte();
                Emgu.CV.CvInvoke.Imencode(".jpg", photoMat, buf, parameters);
                await File.WriteAllBytesAsync(targetPath, buf.ToArray());
                PhotoExporter.EmbedJpegDpi(targetPath, xDpi, yDpi);
            }

            lblStatus.Text = $"Exported photo to {Path.GetFileName(targetPath)}";
        }
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

    private void SldJpegQuality_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
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
            Title = Avalonia.Application.Current?.FindResource("LblOutputDir")?.ToString() ?? "Select Export Folder",
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
}
