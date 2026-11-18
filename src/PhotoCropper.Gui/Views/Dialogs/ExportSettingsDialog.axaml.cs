using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;
using System.Globalization;

namespace PhotoCropper.Gui.Dialogs;

internal enum ExportSettingsResult
{
    Cancel,
    SaveSettings
}

internal sealed partial class ExportSettingsDialog : Window
{
    private readonly string _sampleOriginal;
    private bool _isInitializing;

    public ExportSettingsDialog() : this("Scan001")
    {
    }

    public ExportSettingsDialog(string sampleOriginal)
    {
        _sampleOriginal = string.IsNullOrWhiteSpace(sampleOriginal) ? "Scan001" : sampleOriginal;

        InitializeComponent();

        KeyDown += ExportSettingsDialog_KeyDown;
        Loaded += ExportSettingsDialog_Loaded;
    }

    private void ExportSettingsDialog_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close(ExportSettingsResult.Cancel);
            e.Handled = true;
        }
    }

    private void ExportSettingsDialog_Loaded(object? sender, RoutedEventArgs e)
    {
        _isInitializing = true;
        try
        {
            LoadSettingsToUi();
        }
        finally
        {
            _isInitializing = false;
        }
        UpdateNamingPreview();
    }

    private void LoadSettingsToUi()
    {
        var settings = SettingsManager.Instance.Settings;

        // Output
        txtOutputDir?.Text = settings.CustomOutputDirectory ?? "";

        // Format & Quality
        cbFormat?.SelectedIndex = string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (sldJpegQuality != null)
        {
            sldJpegQuality.Value = settings.JpegQuality;
            UpdateQualityDisplay();
        }
        pnlJpegQuality?.IsVisible = !string.Equals(settings.PreferredFormat, "PNG", StringComparison.OrdinalIgnoreCase);

        // Naming
        txtFileNamePattern?.Text = settings.FileNamePattern;
        SyncNamingPresetDropdown(settings.FileNamePattern);

        // Metadata
        txtMetadataYear?.Text = settings.DefaultYear?.ToString() ?? "";
        txtMetadataDesc?.Text = settings.DefaultDescription ?? "";
        chkApplyYearToAll?.IsChecked = settings.ApplyYearToAllScans;

        // Notifications & prompt
        chkShowNotifications?.IsChecked = settings.ShowNotifications;
        chkFlashTaskbar?.IsChecked = settings.FlashTaskbarOnCompletion;
    }

    private void SaveAllSettings()
    {
        var settings = SettingsManager.Instance.Settings;

        // Output & Format
        settings.CustomOutputDirectory = string.IsNullOrWhiteSpace(txtOutputDir?.Text) ? null : txtOutputDir.Text.Trim();
        settings.PreferredFormat = cbFormat?.SelectedIndex == 1 ? "PNG" : "JPEG";
        if (sldJpegQuality != null)
        {
            settings.JpegQuality = (int)Math.Round(sldJpegQuality.Value);
        }

        // Naming
        if (txtFileNamePattern != null && !string.IsNullOrWhiteSpace(txtFileNamePattern.Text))
        {
            settings.FileNamePattern = txtFileNamePattern.Text.Trim();
        }

        // Metadata
        if (txtMetadataYear != null && int.TryParse(txtMetadataYear.Text, CultureInfo.InvariantCulture, out int yearVal))
        {
            settings.DefaultYear = yearVal;
        }
        else
        {
            settings.DefaultYear = null;
        }
        settings.DefaultDescription = string.IsNullOrWhiteSpace(txtMetadataDesc?.Text) ? null : txtMetadataDesc.Text.Trim();
        settings.ApplyYearToAllScans = chkApplyYearToAll?.IsChecked ?? true;

        // Alerts & Prompts
        settings.ShowNotifications = chkShowNotifications?.IsChecked ?? true;
        settings.FlashTaskbarOnCompletion = chkFlashTaskbar?.IsChecked ?? true;

        SettingsManager.Instance.Save();
    }

    private async void BtnBrowseDir_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folderResult = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.GetString(ResourceKeys.LblOutputDir, "Select Export Folder"),
            AllowMultiple = false
        });

        if (folderResult != null && folderResult.Count > 0)
        {
            txtOutputDir?.Text = folderResult[0].Path.LocalPath;
        }
    }

    private void BtnClearOutputDir_Click(object? sender, RoutedEventArgs e)
    {
        txtOutputDir?.Text = "";
    }

    private void CbFormat_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || cbFormat == null || pnlJpegQuality == null) return;
        bool isJpeg = cbFormat.SelectedIndex == 0;
        pnlJpegQuality.IsVisible = isJpeg;
        UpdateNamingPreview();
    }

    private void SldJpegQuality_ValueChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        UpdateQualityDisplay();
    }

    private void UpdateQualityDisplay()
    {
        if (sldJpegQuality == null || txtQualityValue == null) return;
        int val = (int)Math.Round(sldJpegQuality.Value);
        txtQualityValue.Text = val >= 100 ? LocalizationService.GetString(ResourceKeys.LblQualityMax, "100% (Max)") : $"{val}%";
    }

    private void CbNamingPreset_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isInitializing || cbNamingPreset == null || txtFileNamePattern == null) return;
        if (cbNamingPreset.SelectedItem is ComboBoxItem item && item.Content is string preset)
        {
            txtFileNamePattern.Text = preset;
        }
    }

    private void TxtFileNamePattern_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_isInitializing || txtFileNamePattern == null) return;
        SyncNamingPresetDropdown(txtFileNamePattern.Text ?? "");
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
        if (_isInitializing) return;
        UpdateNamingPreview();
    }

    private void UpdateNamingPreview()
    {
        if (txtNamingPreview == null) return;
        string pattern = txtFileNamePattern?.Text ?? FileNameTemplateHelper.DefaultPattern;
        if (string.IsNullOrWhiteSpace(pattern)) pattern = FileNameTemplateHelper.DefaultPattern;

        string ext = cbFormat?.SelectedIndex == 1 ? ".png" : ".jpg";

        var meta = new PhotoExportMetadata();
        if (txtMetadataYear != null && int.TryParse(txtMetadataYear.Text, CultureInfo.InvariantCulture, out int y))
        {
            meta.Year = y;
        }
        if (txtMetadataDesc != null && !string.IsNullOrWhiteSpace(txtMetadataDesc.Text))
        {
            meta.Description = txtMetadataDesc.Text;
        }

        string previewFile = FileNameTemplateHelper.FormatPreview(pattern, _sampleOriginal, 1, 4, meta, ext);
        txtNamingPreview.Text = LocalizationService.Format(ResourceKeys.LblNamingPreview, "Preview: {0}", previewFile);
    }

    private void BtnCancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(ExportSettingsResult.Cancel);
    }

    private void BtnSavePreferences_Click(object? sender, RoutedEventArgs e)
    {
        SaveAllSettings();
        Close(ExportSettingsResult.SaveSettings);
    }
}
