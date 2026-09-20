using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private bool HasUnsavedChanges()
    {
        return _sessionManager.HasUnsavedChanges();
    }

    private async void BtnSaveImages_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || !_sessionManager.HasScans) return;

        var settings = SettingsManager.Instance.Settings;
        if (settings.PromptBeforeExport)
        {
            await ShowExportSettingsAsync(triggerExportOnConfirm: true);
        }
        else
        {
            await SavePendingScansAsync(closeAfterSave: false);
        }
    }

    private async Task<bool> SavePendingScansAsync(bool closeAfterSave)
    {
        if (isLoading || !_sessionManager.HasScans) return false;

        var pendingSessions = _sessionManager.GetPendingExportSessions();
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
                PhotoExporter.ClearClaimedExportPaths();
                Parallel.ForEach(pendingSessions, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency }, (session) =>
                {
                    try
                    {
                        bool wasActive = session.IsActive;
                        var engine = session.Activate();
                        try
                        {
                            string scanTargetFolder = ExportPathResolver.ResolveOutputDirectory(
                                session.FilePath,
                                settings.CustomOutputDirectory,
                                settings.WorkDirectory);

                            var scanMetadata = settings.ApplyYearToAllScans
                                ? new PhotoExportMetadata { Year = settings.DefaultYear, Description = settings.DefaultDescription }
                                : session.Metadata;

                            engine.SaveDetectedPhotos(
                                scanTargetFolder,
                                settings.PreferredFormat,
                                settings.JpegQuality,
                                settings.FileNamePattern,
                                scanMetadata);

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
                                    entry.Metadata = scanMetadata;
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
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to export scan '{session.FilePath}': {ex.Message}");
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

            if (totalSavedPhotos > 0)
            {
                _notificationService.NotifyExportCompleted(totalSavedPhotos, targetOutputFolder);
            }
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

        var scanMetadata = settings.ApplyYearToAllScans
            ? new PhotoExportMetadata { Year = settings.DefaultYear, Description = settings.DefaultDescription }
            : ScanSessions[currentIndex].Metadata;

        string suggestedName = FileNameTemplateHelper.FormatFileName(
            settings.FileNamePattern,
            baseName,
            photoIndex + 1,
            engine.DetectedPhotos.Count,
            scanMetadata,
            defaultExt);

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
                if (scanMetadata.HasMetadata)
                {
                    PhotoExporter.EmbedPngMetadata(targetPath, scanMetadata);
                }
            }
            else
            {
                var parameters = new[] {
                    new KeyValuePair<Emgu.CV.CvEnum.ImwriteFlags, int>(Emgu.CV.CvEnum.ImwriteFlags.JpegQuality, settings.JpegQuality),
                    new KeyValuePair<Emgu.CV.CvEnum.ImwriteFlags, int>(Emgu.CV.CvEnum.ImwriteFlags.JpegOptimize, 1)
                };
                using var buf = new Emgu.CV.Util.VectorOfByte();
                Emgu.CV.CvInvoke.Imencode(".jpg", photoMat, buf, parameters);
                await File.WriteAllBytesAsync(targetPath, buf.ToArray());
                PhotoExporter.EmbedJpegDpi(targetPath, xDpi, yDpi);
                if (scanMetadata.HasMetadata)
                {
                    PhotoExporter.EmbedJpegMetadata(targetPath, scanMetadata);
                }
            }

            string exportMsg = $"Exported photo to {Path.GetFileName(targetPath)}";
            lblStatus.Text = exportMsg;
            _notificationService.ShowSuccess("PhotoCropper", exportMsg);
        }
    }

    private async void BtnSaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        await ShowExportSettingsAsync(triggerExportOnConfirm: false);
    }

    private async Task ShowExportSettingsAsync(bool triggerExportOnConfirm)
    {
        if (isLoading) return;

        string sampleName = ScanSessions.Count > 0 && currentIndex >= 0 && currentIndex < ScanSessions.Count
            ? Path.GetFileNameWithoutExtension(ScanSessions[currentIndex].FilePath)
            : "Scan001";

        var dialog = new Dialogs.ExportSettingsDialog(hasScans: _sessionManager.HasScans, sampleOriginal: sampleName);
        var result = await dialog.ShowDialog<Dialogs.ExportSettingsResult>(this);

        if (result == Dialogs.ExportSettingsResult.SaveAndExport || (triggerExportOnConfirm && result == Dialogs.ExportSettingsResult.SaveSettings))
        {
            await SavePendingScansAsync(closeAfterSave: false);
        }
    }
}
