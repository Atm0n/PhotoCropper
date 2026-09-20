using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Core.Workspace;
using System.Globalization;

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
                    new KeyValuePair<Emgu.CV.CvEnum.ImwriteFlags, int>(Emgu.CV.CvEnum.ImwriteFlags.JpegQuality, settings.JpegQuality)
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
        UpdateNamingPreview();
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

    private void CbNamingPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (cbNamingPreset == null || txtFileNamePattern == null) return;
        if (cbNamingPreset.SelectedItem is ComboBoxItem item && item.Content is string preset)
        {
            txtFileNamePattern.Text = preset;
        }
    }

    private void TxtFileNamePattern_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (txtFileNamePattern == null) return;
        string pattern = txtFileNamePattern.Text ?? "";
        SettingsManager.Instance.Settings.FileNamePattern = pattern;
        SettingsManager.Instance.Save();
        SyncNamingPresetDropdown(pattern);
        UpdateNamingPreview();
    }

    private void SyncNamingPresetDropdown(string pattern)
    {
        if (cbNamingPreset == null) return;
        for (int i = 0; i < cbNamingPreset.Items.Count; i++)
        {
            if (cbNamingPreset.Items[i] is ComboBoxItem item && string.Equals(item.Content?.ToString(), pattern, StringComparison.Ordinal))
            {
                if (cbNamingPreset.SelectedIndex != i)
                {
                    cbNamingPreset.SelectedIndex = i;
                }
                return;
            }
        }
        cbNamingPreset.SelectedIndex = -1;
    }

    private void BtnToken_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string token || txtFileNamePattern == null) return;

        int caret = txtFileNamePattern.CaretIndex;
        string current = txtFileNamePattern.Text ?? "";
        if (caret >= 0 && caret <= current.Length)
        {
            txtFileNamePattern.Text = current.Insert(caret, token);
            txtFileNamePattern.CaretIndex = caret + token.Length;
        }
        else
        {
            txtFileNamePattern.Text = current + token;
        }
    }

    private void TxtMetadata_TextChanged(object? sender, TextChangedEventArgs e)
    {
        var settings = SettingsManager.Instance.Settings;
        int? year = null;
        if (txtMetadataYear != null && int.TryParse(txtMetadataYear.Text, CultureInfo.InvariantCulture, out int y))
        {
            year = y;
        }
        string? desc = string.IsNullOrWhiteSpace(txtMetadataDesc?.Text) ? null : txtMetadataDesc.Text;

        settings.DefaultYear = year;
        settings.DefaultDescription = desc;
        SettingsManager.Instance.Save();

        if (settings.ApplyYearToAllScans)
        {
            foreach (var session in ScanSessions)
            {
                session.Metadata.Year = year;
                session.Metadata.Description = desc;
            }
        }
        else if (ScanSessions.Count > 0 && currentIndex >= 0 && currentIndex < ScanSessions.Count)
        {
            ScanSessions[currentIndex].Metadata.Year = year;
            ScanSessions[currentIndex].Metadata.Description = desc;
        }

        UpdateNamingPreview();
    }

    private void ChkApplyYearToAll_Click(object? sender, RoutedEventArgs e)
    {
        if (chkApplyYearToAll == null) return;
        SettingsManager.Instance.Settings.ApplyYearToAllScans = chkApplyYearToAll.IsChecked ?? true;
        SettingsManager.Instance.Save();
    }

    private void UpdateNamingPreview()
    {
        if (txtNamingPreview == null) return;
        string pattern = txtFileNamePattern?.Text ?? FileNameTemplateHelper.DefaultPattern;
        if (string.IsNullOrWhiteSpace(pattern)) pattern = FileNameTemplateHelper.DefaultPattern;

        string sampleOriginal = ScanSessions.Count > 0 && currentIndex >= 0 && currentIndex < ScanSessions.Count
            ? Path.GetFileNameWithoutExtension(ScanSessions[currentIndex].FilePath)
            : "Scan001";

        string ext = cbFormat?.SelectedIndex == 1 ? ".png" : ".jpg";

        var meta = new PhotoExportMetadata();
        if (txtMetadataYear != null && int.TryParse(txtMetadataYear.Text, CultureInfo.InvariantCulture, out int y))
        {
            meta.Year = y;
        }
        else if (SettingsManager.Instance.Settings.DefaultYear.HasValue)
        {
            meta.Year = SettingsManager.Instance.Settings.DefaultYear.Value;
        }

        if (txtMetadataDesc != null && !string.IsNullOrWhiteSpace(txtMetadataDesc.Text))
        {
            meta.Description = txtMetadataDesc.Text;
        }
        else if (!string.IsNullOrWhiteSpace(SettingsManager.Instance.Settings.DefaultDescription))
        {
            meta.Description = SettingsManager.Instance.Settings.DefaultDescription;
        }

        string previewFile = FileNameTemplateHelper.FormatPreview(pattern, sampleOriginal, 1, 4, meta, ext);
        string previewFmt = Avalonia.Application.Current?.FindResource("LblNamingPreview")?.ToString() ?? "Preview: {0}";
        txtNamingPreview.Text = string.Format(previewFmt, previewFile);
    }
}
