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
    /// <summary>The engine's boxes, with each photo's manual and auto rotation, as saved in session.json.</summary>
    private static List<WorkspaceCropData> BuildCropData(PhotoCropperEngine engine) => [.. WorkspaceCropData.FromEngine(engine)];

    /// <summary>
    /// Builds a bullet list of failed scans for the error dialog, capped so a large batch of
    /// failures (e.g. an unwritable output folder) doesn't overflow the dialog.
    /// </summary>
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

                string exportMsg = LocalizationService.Format(ResourceKeys.MsgPhotoExported, "Exported photo to {0}", Path.GetFileName(targetPath));
                lblStatus.Text = exportMsg;
                _notificationService.ShowSuccess("PhotoCropper", exportMsg);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                string errorMsg = LocalizationService.Format(ResourceKeys.MsgPhotoExportFailed, "Failed to export photo: {0}", ex.Message);
                lblStatus.Text = errorMsg;
                _notificationService.ShowError("PhotoCropper", errorMsg);
            }
        }
    }

    private async void BtnSaveSettings_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;

        string sampleName = ScanSessions.Count > 0 && CurrentIndex >= 0 && CurrentIndex < ScanSessions.Count
            ? Path.GetFileNameWithoutExtension(ScanSessions[CurrentIndex].FilePath)
            : "Scan001";

        var dialog = new Dialogs.ExportSettingsDialog(sampleOriginal: sampleName);
        await dialog.ShowDialog<Dialogs.ExportSettingsResult>(this);
    }
}
