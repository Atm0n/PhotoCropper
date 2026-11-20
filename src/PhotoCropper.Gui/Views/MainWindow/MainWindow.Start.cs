using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using PhotoCropper.Gui.ViewModels;

namespace PhotoCropper.Gui;

/// <summary>
/// The start screen's host side: it covers the window at every launch and when the project button is clicked, and
/// a project is opened (or created) from it.
/// </summary>
internal sealed partial class MainWindow : IStartHost
{
    private StartViewModel _startVm = null!;

    /// <summary>The project shown behind the start screen; null until one has been opened in this run.</summary>
    private string? _openProject;

    /// <summary>Keeps <see cref="_openProject"/> from being opened by another window at the same time.</summary>
    private ProjectLock? _projectLock;

    internal bool IsStartScreenOpen => _startVm.IsOpen;

    private void InitializeStartScreen()
    {
        _startVm = new StartViewModel(this);
        viewStart.DataContext = _startVm;

        // Settings from before the start screen only know the last project: list it so it is one click away.
        var settings = SettingsManager.Instance.Settings;
        if (settings.RecentProjectFolders.Count == 0 && !string.IsNullOrWhiteSpace(settings.WorkDirectory) && Directory.Exists(settings.WorkDirectory))
        {
            settings.RecentProjectFolders = RecentProjects.Add([], settings.WorkDirectory);
        }
    }

    private void ShowStartScreen()
    {
        FlushAutosave();
        _startVm.Open();
    }

    private void BtnWorkDir_Click(object? sender, RoutedEventArgs e) => ShowStartScreen();

    // ---- IStartHost ----

    IReadOnlyList<string> IStartHost.RecentProjectFolders => SettingsManager.Instance.Settings.RecentProjectFolders;

    string? IStartHost.OpenProjectFolder => _openProject;

    string IStartHost.DefaultProjectsFolder
    {
        get
        {
            string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
            if (string.IsNullOrWhiteSpace(pictures)) pictures = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(pictures, "PhotoCropper");
        }
    }

    async Task<string?> IStartHost.PickFolderAsync(string title)
    {
        if (StorageProvider is not { } storage) return null;
        var folders = await storage.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return folders.Count > 0 ? folders[0].Path.LocalPath : null;
    }

    async Task<bool> IStartHost.OpenProjectAsync(string folder, bool restoreOnly)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.RecentProjectFolders = RecentProjects.Add(settings.RecentProjectFolders, folder);
        SettingsManager.Instance.Save();

        // The project that is already open: just go back to it.
        if (!RecentProjects.SameFolder(folder, _openProject))
        {
            if (ProjectLock.TryAcquire(folder) is not { } projectLock) return false;
            await SwitchToProjectAsync(folder);
            _projectLock?.Dispose(); // the previous project, saved by the switch
            _projectLock = projectLock;
            _openProject = folder;
            ApplyStage(AppStage.Scan);
        }

        if (restoreOnly)
        {
            await SetStageAsync(AppStage.Finish);
            if (_finishStageVm != null) await _finishStageVm.ImportPhotosCommand.ExecuteAsync(null);
        }
        return true;
    }

    void IStartHost.ForgetRecentProject(string folder)
    {
        var settings = SettingsManager.Instance.Settings;
        settings.RecentProjectFolders = RecentProjects.Remove(settings.RecentProjectFolders, folder);
        SettingsManager.Instance.Save();
    }
}
