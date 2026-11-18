using Avalonia.Interactivity;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

/// <summary>Window entry points (toolbar, menus, shortcuts) for Crops actions; the scan view itself is CropsStageView.</summary>
internal sealed partial class MainWindow
{
    private void BtnRefine_Click(object? sender, RoutedEventArgs e) => StartRefineMode();

    private void StartRefineMode()
    {
        if (ScanSessions.Count == 0 || SelectedPhotoNumber < 0) return;
        var session = ScanSessions[CurrentIndex];
        _refineVm.Open(session.Activate(), CurrentIndex, SelectedPhotoNumber, () => session.IsModified = true);
    }

    /// <summary>Puts the detection settings back to their defaults and detects the open scan again with them.</summary>
    private async void BtnResetDefaults_Click(object? sender, RoutedEventArgs e)
    {
        if (isLoading) return;
        SettingsManager.Instance.ResetDetectionDefaults();
        _cropsVm.LoadFromSettings();
        await _cropsVm.ReprocessAsync();
    }

    private async void BtnResetBackground_Click(object? sender, RoutedEventArgs e)
    {
        if (ScanSessions.Count == 0) return;
        await _cropsVm.ResetBackgroundColorAsync();
    }
}
