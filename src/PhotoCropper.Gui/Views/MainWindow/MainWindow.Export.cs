using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Emgu.CV.CvEnum;
using Emgu.CV.Util;
using PhotoCropper.Core;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private bool HasUnsavedChanges()
    {
        return _sessionManager.HasUnsavedChanges();
    }

    private async void BtnSaveImages_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading || _isNavigating || !_sessionManager.HasScans) return;

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
        if (isLoading || _isNavigating || !_sessionManager.HasScans) return false;

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
        var failedScans = new System.Collections.Concurrent.ConcurrentQueue<(string FilePath, string Error)>();
        bool exportRanToCompletion = false;

        string targetOutputFolder = ExportPathResolver.ResolveOutputDirectory(
            pendingSessions[0].FilePath,
            settings.CustomOutputDirectory,
            settings.WorkDirectory);

        int maxConcurrency = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);

        // Scans that share a base name and export to the same folder (e.g. "photo.jpg" and "photo.png") produce
        // files whose names can't be told apart, so matching previous exports by name could delete the other
        // scan's photos. For those, only the per-scan record of written files is used for cleanup.
        var ambiguousScanNames = ScanSessions
            .GroupBy(s => (
                Folder: ExportPathResolver.ResolveOutputDirectory(s.FilePath, settings.CustomOutputDirectory, settings.WorkDirectory),
                Name: Path.GetFileNameWithoutExtension(s.FilePath)),
                new OutputNameComparer())
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .Select(s => s.FilePath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        await ExecuteWithLoadingAsync(LocalizationService.Format(ResourceKeys.MsgSavingProgress, "Exporting scan {0} of {1} ({2} photos saved)...", 1, totalScans, 0), async ct =>
        {
            await Task.Run(() =>
            {
                // Build a filename-keyed lookup before entering the parallel loop so that
                // each thread can resolve its own WorkspaceScanEntry without traversing
                // _workspaceSession.Scans concurrently (which would be a data race).
                Dictionary<string, WorkspaceScanEntry>? workspaceLookup = null;
                if (_workspaceSession != null)
                {
                    workspaceLookup = _workspaceSession.Scans
                        .Where(s => s != null)
                        .GroupBy(s => Path.GetFileName(s.RelativePath), StringComparer.OrdinalIgnoreCase)
                        .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
                }

                Parallel.ForEach(pendingSessions, new ParallelOptions { MaxDegreeOfParallelism = maxConcurrency, CancellationToken = ct }, (session) =>
                {
                    try
                    {
                        // Capture the state before activating: Activate() always loads the engine,
                        // so reading IsActive afterwards would always be true and never unload it.
                        bool wasActive = session.IsActive;
                        var engine = session.Activate();
                        try
                        {
                            string scanTargetFolder = ExportPathResolver.ResolveOutputDirectory(
                                session.FilePath,
                                settings.CustomOutputDirectory,
                                settings.WorkDirectory);

                            var scanMetadata = ExportMetadataResolver.Resolve(
                                session.Metadata,
                                settings.ApplyYearToAllScans,
                                settings.DefaultYear,
                                settings.DefaultDescription);

                            // Each session maps to exactly one WorkspaceScanEntry — resolved via the
                            // pre-built dict so no concurrent list traversal occurs inside the parallel body.
                            WorkspaceScanEntry? entry = null;
                            workspaceLookup?.TryGetValue(Path.GetFileName(session.FilePath), out entry);
                            var crops = BuildCropData(engine);

                            IReadOnlyList<ExportedFile> exportedFiles;
                            if (entry != null && _workspaceSession != null && !string.IsNullOrEmpty(settings.WorkDirectory))
                            {
                                // Project: RawScans → Cropped/{id}.png → Final/ (or the custom output folder).
                                exportedFiles = ExportScanThroughPipeline(
                                    settings.WorkDirectory, _workspaceSession, entry, engine, crops, session,
                                    new FinalRenderOptions
                                    {
                                        Format = settings.PreferredFormat,
                                        JpegQuality = settings.JpegQuality,
                                        FileNamePattern = settings.FileNamePattern,
                                        DefaultMetadata = scanMetadata,
                                        OutputDirectory = string.IsNullOrWhiteSpace(settings.CustomOutputDirectory) ? null : settings.CustomOutputDirectory
                                    });
                                RecordExportedFiles(session, workspaceLookup, exportedFiles);
                            }
                            else
                            {
                                // No project: export the finished photos directly, as before.
                                try
                                {
                                    exportedFiles = engine.SaveDetectedPhotos(
                                        scanTargetFolder,
                                        settings.PreferredFormat,
                                        settings.JpegQuality,
                                        settings.FileNamePattern,
                                        scanMetadata,
                                        progressCallback: null,
                                        cleanOldExports: true,
                                        previousExports: session.ExportedFiles,
                                        matchPreviousExportsByName: !ambiguousScanNames.Contains(session.FilePath));
                                }
                                catch (PartialExportException partial)
                                {
                                    // Record the files that did get written so the next export replaces them
                                    // instead of leaving orphans, then report the scan as failed.
                                    RecordExportedFiles(session, workspaceLookup, partial.WrittenFiles);
                                    throw;
                                }
                                RecordExportedFiles(session, workspaceLookup, exportedFiles);
                            }

                            int savedCount = exportedFiles.Count;
                            Interlocked.Add(ref totalSavedPhotos, savedCount);

                            session.IsSaved = true;
                            session.IsModified = false;

                            if (entry != null)
                            {
                                entry.IsProcessed = true;
                                entry.ExtractedPhotoCount = savedCount;
                                entry.Metadata = scanMetadata;

                                entry.FinalCrops.Clear();
                                foreach (var crop in crops) entry.FinalCrops.Add(crop);
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
                        Trace.TraceError($"[PhotoCropper] Failed to export scan '{session.FilePath}': {ex.GetType().Name}: {ex.Message}");
                        failedScans.Enqueue((session.FilePath, ex.Message));
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

            exportRanToCompletion = true;

            // Format the completion message only now that the export has finished; building it as an
            // argument to ExecuteWithLoadingAsync would capture totalSavedPhotos while it is still 0.
            lblStatus.Text = failedScans.IsEmpty
                ? LocalizationService.Format(ResourceKeys.MsgSaved, "Successfully saved {0} photos to 'cropped' folders.", totalSavedPhotos)
                : LocalizationService.Format(ResourceKeys.MsgExportFailedStatus, "Saved {0} photos; {1} scan(s) failed to export.", totalSavedPhotos, failedScans.Count);

            if (totalSavedPhotos > 0)
            {
                _notificationService.NotifyExportCompleted(totalSavedPhotos, targetOutputFolder);
            }
        });

        if (!failedScans.IsEmpty)
        {
            ShowAppError(
                LocalizationService.GetString(ResourceKeys.TitleExportFailed, "Some Scans Could Not Be Exported"),
                LocalizationService.Format(
                    ResourceKeys.MsgExportFailedDetails,
                    "{0} of {1} scans could not be exported. PhotoCropper stayed open so no work is lost.\n\n{2}",
                    failedScans.Count,
                    totalScans,
                    FormatFailedScanList([.. failedScans])));
        }

        // Only exit when every pending scan was exported. A failed scan, a cancelled run, or an error
        // reported by ExecuteWithLoadingAsync (e.g. the session file could not be written) keeps the
        // window open so the user can see what went wrong instead of losing unexported work.
        bool exportSucceeded = exportRanToCompletion && failedScans.IsEmpty;
        if (!exportSucceeded)
        {
            return false;
        }

        if (closeAfterSave)
        {
            _isExitingConfirmed = true;
            Close();
        }

        return true;
    }

    /// <summary>
    /// Builds a bullet list of failed scans for the error dialog, capped so a large batch of
    /// failures (e.g. an unwritable output folder) doesn't overflow the dialog.
    /// </summary>
    /// <summary>The engine's boxes, with each photo's manual and auto rotation, as saved in session.json.</summary>
    private static List<WorkspaceCropData> BuildCropData(PhotoCropperEngine engine)
    {
        var crops = new List<WorkspaceCropData>(engine.AcceptedCandidates.Count);
        for (int i = 0; i < engine.AcceptedCandidates.Count; i++)
        {
            var cand = engine.AcceptedCandidates[i];
            crops.Add(new WorkspaceCropData
            {
                CenterX = cand.Rotated.Center.X,
                CenterY = cand.Rotated.Center.Y,
                Width = cand.Rotated.Size.Width,
                Height = cand.Rotated.Size.Height,
                Angle = cand.Rotated.Angle,
                RotationDegrees = engine.PhotoRotations.TryGetValue(i, out int r) ? r : 0,
                AutoRotationDegrees = cand.OrientationDegrees
            });
        }
        return crops;
    }

    /// <summary>
    /// Exports a project scan through the folder pipeline: its geometry-only photos (the hold-to-compare copies)
    /// go to Cropped/{id}.png, then each is restored with the scan's settings and written as a final photo.
    /// </summary>
    /// <returns>The final files written.</returns>
    private static List<ExportedFile> ExportScanThroughPipeline(
        string workDirectory,
        WorkspaceSessionState workspace,
        WorkspaceScanEntry entry,
        PhotoCropperEngine engine,
        List<WorkspaceCropData> crops,
        ScanSessionItem session,
        FinalRenderOptions options)
    {
        int count = Math.Min(Math.Min(engine.RawDetectedPhotos.Count, engine.DetectedPhotos.Count), crops.Count);
        var inputs = new List<CroppedPhotoInput>(count);
        for (int i = 0; i < count; i++)
        {
            inputs.Add(new CroppedPhotoInput(engine.RawDetectedPhotos[i], crops[i]));
        }

        // New photos start with the scan's restoration setting; photos that already exist keep their own
        // (chosen per photo in the Finish stage).
        var restoration = new RestorationSettings
        {
            RestoreColors = engine.RestoreVintageColors,
            RemoveDust = engine.RemoveDustAndScratches
        };

        var photos = PhotoPipelineService.SaveCroppedPhotos(workDirectory, workspace, entry, inputs, restoration);

        // Exports made before the pipeline (moved to Final/ by the migration) would otherwise stay next to
        // the new ones. Remove the ones this scan recorded, if still unchanged.
        foreach (var legacy in session.ExportedFiles.Where(f => f.IsUnchangedOnDisk()))
        {
            bool isPipelineFile = photos.Any(p => p.Final != null &&
                string.Equals(p.Final.GetFullPath(workDirectory), Path.GetFullPath(legacy.Path), StringComparison.OrdinalIgnoreCase));
            if (!isPipelineFile)
            {
                try
                {
                    File.Delete(legacy.Path);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Trace.TraceWarning($"[PhotoCropper] Could not remove previous export '{legacy.Path}': {ex.Message}");
                }
            }
        }

        // Re-render every photo: the export settings (pattern, format, metadata) may have changed too.
        var written = new List<ExportedFile>(photos.Count);
        foreach (var photo in photos)
        {
            PhotoPipelineService.RenderFinal(workDirectory, workspace, photo, options);
            written.Add(ExportedFile.FromDisk(photo.Final!.GetFullPath(workDirectory)));
        }
        return written;
    }

    /// <summary>Stores the files an export wrote on the scan session and its workspace entry (persisted in session.json).</summary>
    private static void RecordExportedFiles(
        ScanSessionItem session,
        Dictionary<string, WorkspaceScanEntry>? workspaceLookup,
        IReadOnlyList<ExportedFile> files)
    {
        session.ExportedFiles = files;
        if (workspaceLookup != null && workspaceLookup.TryGetValue(Path.GetFileName(session.FilePath), out var entry))
        {
            entry.ExportedFiles.Clear();
            foreach (var file in files) entry.ExportedFiles.Add(file);
        }
    }

    /// <summary>Groups scans by (output folder, base file name), ignoring case and trailing separators.</summary>
    private sealed class OutputNameComparer : IEqualityComparer<(string Folder, string Name)>
    {
        private static string NormalizeFolder(string folder) =>
            string.IsNullOrEmpty(folder) ? string.Empty : Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));

        public bool Equals((string Folder, string Name) x, (string Folder, string Name) y) =>
            string.Equals(NormalizeFolder(x.Folder), NormalizeFolder(y.Folder), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Name, y.Name, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Folder, string Name) obj) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizeFolder(obj.Folder)),
            StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Name));
    }

    internal static string FormatFailedScanList(IReadOnlyList<(string FilePath, string Error)> failures, int maxShown = 10)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var lines = failures
            .Take(maxShown)
            .Select(f => $"• {Path.GetFileName(f.FilePath)}: {f.Error}")
            .ToList();

        if (failures.Count > maxShown)
        {
            lines.Add($"… (+{failures.Count - maxShown})");
        }

        return string.Join('\n', lines);
    }

    private async Task ExportSinglePhotoAsync(int photoIndex, CancellationToken cancellationToken = default)
    {
        if (isLoading || _isNavigating || ScanSessions.Count == 0 || photoIndex < 0) return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var engine = ScanSessions[CurrentIndex].Activate();
        if (photoIndex >= engine.DetectedPhotos.Count) return;

        var settings = SettingsManager.Instance.Settings;
        string defaultExt = string.Equals(settings.PreferredFormat, AppConstants.FormatPng, StringComparison.OrdinalIgnoreCase) ? AppConstants.ExtensionPng : AppConstants.ExtensionJpg;
        string baseName = Path.GetFileNameWithoutExtension(ScanSessions[CurrentIndex].FilePath);

        var scanMetadata = ExportMetadataResolver.Resolve(
            ScanSessions[CurrentIndex].Metadata,
            settings.ApplyYearToAllScans,
            settings.DefaultYear,
            settings.DefaultDescription);

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
            try
            {
                var (xDpi, yDpi) = PhotoExporter.GetDpiFromSource(ScanSessions[CurrentIndex].FilePath);

                // The format follows the extension chosen in the save dialog; encoding is shared with batch export.
                string format = targetPath.EndsWith(AppConstants.ExtensionPng, StringComparison.OrdinalIgnoreCase)
                    ? AppConstants.FormatPng
                    : AppConstants.FormatJpeg;
                byte[] fileBytes = PhotoExporter.EncodePhoto(engine.DetectedPhotos[photoIndex], format, settings.JpegQuality, scanMetadata, xDpi, yDpi);
                await File.WriteAllBytesAsync(targetPath, fileBytes, cancellationToken);

                string exportMsg = $"Exported photo to {Path.GetFileName(targetPath)}";
                lblStatus.Text = exportMsg;
                _notificationService.ShowSuccess("PhotoCropper", exportMsg);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                string errorMsg = $"Failed to export photo: {ex.Message}";
                lblStatus.Text = errorMsg;
                _notificationService.ShowError("PhotoCropper", errorMsg);
            }
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
