using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Common;
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
            lblStatus.Text = LocalizationService.Format(
                ResourceKeys.MsgAllScansAlreadySaved,
                "All {0} scans are already saved to 'Cropped'. No changes to export.",
                ScanSessions.Count);

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

        string targetOutputFolder = ExportPathResolver.ResolveOutputDirectory(
            pendingSessions[0].FilePath,
            settings.CustomOutputDirectory,
            settings.WorkDirectory);

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        await ExecuteWithLoadingAsync(LocalizationService.Format(ResourceKeys.MsgSavingProgress, "Exporting scan {0} of {1} ({2} photos saved)...", 1, totalScans, 0), async ct =>
        {
            await Task.Run(() =>
            {
                PhotoExporter.ClearClaimedExportPaths();
                Parallel.ForEach(pendingSessions, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct }, (session) =>
                {
                    try
                    {
                        var engine = session.Activate();
                        bool wasActive = session.IsActive;
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
                                scanMetadata,
                                progressCallback: null,
                                cleanOldExports: true);

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
                                    
                                    entry.FinalCrops.Clear();
                                    foreach (var cand in engine.AcceptedCandidates)
                                    {
                                        entry.FinalCrops.Add(new PhotoCropper.Core.Workspace.WorkspaceCropData
                                        {
                                            CenterX = cand.Rotated.Center.X,
                                            CenterY = cand.Rotated.Center.Y,
                                            Width = cand.Rotated.Size.Width,
                                            Height = cand.Rotated.Size.Height,
                                            Angle = cand.Rotated.Angle
                                        });
                                    }
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
                        lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgSavingProgress, "Exporting scan {0} of {1} ({2} photos saved)...", done, totalScans, totalSavedPhotos);
                    });
                });

                if (_workspaceSession != null && !string.IsNullOrEmpty(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
                {
                    ProjectWorkspaceService.SaveSession(settings.WorkDirectory, _workspaceSession);
                }
            }, ct);

            if (totalSavedPhotos > 0)
            {
                _notificationService.NotifyExportCompleted(totalSavedPhotos, targetOutputFolder);
            }
        }, LocalizationService.Format(ResourceKeys.MsgSaved, "Successfully saved {0} photos to 'cropped' folders.", totalSavedPhotos));

        if (closeAfterSave)
        {
            _isExitingConfirmed = true;
            Close();
        }

        return true;
    }

    private async Task ExportSinglePhotoAsync(int photoIndex, CancellationToken cancellationToken = default)
    {
        if (isLoading || ScanSessions.Count == 0 || photoIndex < 0) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var engine = ScanSessions[CurrentIndex].Activate();
        if (photoIndex >= engine.DetectedPhotos.Count) return;

        var settings = SettingsManager.Instance.Settings;
        string defaultExt = string.Equals(settings.PreferredFormat, AppConstants.FormatPng, StringComparison.OrdinalIgnoreCase) ? AppConstants.ExtensionPng : AppConstants.ExtensionJpg;
        string baseName = Path.GetFileNameWithoutExtension(ScanSessions[CurrentIndex].FilePath);

        var scanMetadata = settings.ApplyYearToAllScans
            ? new PhotoExportMetadata { Year = settings.DefaultYear, Description = settings.DefaultDescription }
            : ScanSessions[CurrentIndex].Metadata;

        string suggestedName = FileNameTemplateHelper.FormatFileName(
            settings.FileNamePattern,
            baseName,
            photoIndex + 1,
            engine.DetectedPhotos.Count,
            scanMetadata,
            defaultExt);

        var fileResult = await topLevel.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = LocalizationService.GetString(ResourceKeys.MenuExportSingle, "Export Single Photo"),
            SuggestedFileName = suggestedName,
            DefaultExtension = defaultExt
        });

        if (fileResult != null)
        {
            string targetPath = fileResult.Path.LocalPath;
            var photoMat = engine.DetectedPhotos[photoIndex];
            var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(ScanSessions[CurrentIndex].FilePath);

            if (targetPath.EndsWith(AppConstants.ExtensionPng, StringComparison.OrdinalIgnoreCase))
            {
                using var buf = new Emgu.CV.Util.VectorOfByte();
                Emgu.CV.CvInvoke.Imencode(AppConstants.ExtensionPng, photoMat, buf);
                await File.WriteAllBytesAsync(targetPath, buf.ToArray(), cancellationToken);
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
                Emgu.CV.CvInvoke.Imencode(AppConstants.ExtensionJpg, photoMat, buf, parameters);
                await File.WriteAllBytesAsync(targetPath, buf.ToArray(), cancellationToken);
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

        string sampleName = ScanSessions.Count > 0 && CurrentIndex >= 0 && CurrentIndex < ScanSessions.Count
            ? Path.GetFileNameWithoutExtension(ScanSessions[CurrentIndex].FilePath)
            : "Scan001";

        var dialog = new Dialogs.ExportSettingsDialog(hasScans: _sessionManager.HasScans, sampleOriginal: sampleName);
        var result = await dialog.ShowDialog<Dialogs.ExportSettingsResult>(this);

        if (result == Dialogs.ExportSettingsResult.SaveAndExport || (triggerExportOnConfirm && result == Dialogs.ExportSettingsResult.SaveSettings))
        {
            await SavePendingScansAsync(closeAfterSave: false);
        }
    }
}
