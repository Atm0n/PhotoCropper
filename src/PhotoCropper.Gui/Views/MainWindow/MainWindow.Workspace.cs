using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui;

internal sealed partial class MainWindow
{
    private WorkspaceSessionState? _workspaceSession;

    private async void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var workDir = SettingsManager.Instance.Settings.WorkDirectory;
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir) && ProjectWorkspaceService.HasRecoverableSession(workDir) && ScanSessions.Count == 0)
        {
            await ResumeWorkspaceSessionAsync(workDir);
        }

        _ = RefreshScannersAsync();
        CheckForUpdatesOnStartupIfDue();
    }

    private async Task ResumeWorkspaceSessionAsync(string workDir)
    {
        _workspaceSession = ProjectWorkspaceService.LoadSession(workDir);
        if (_workspaceSession == null || _workspaceSession.Scans.Count == 0)
        {
            _workspaceSession = ProjectWorkspaceService.ReconstructSessionFromRawFiles(workDir);
        }

        if (_workspaceSession?.Scans.Count > 0)
        {
            var rawPaths = _workspaceSession.Scans
                .Select(s => Path.IsPathRooted(s.RelativePath) ? s.RelativePath : Path.Combine(workDir, s.RelativePath))
                .Where(File.Exists)
                .ToList();

            if (rawPaths.Count > 0)
            {
                await LoadScansFromPathsAsync(rawPaths);
                lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgScanSuccess, "Workspace: loaded {0} scans.", rawPaths.Count);
            }
        }
    }

    private void UpdateWorkspaceUi(string? workDir)
    {
        if (!string.IsNullOrEmpty(workDir) && Directory.Exists(workDir))
        {
            string cleanDir = Path.TrimEndingDirectorySeparator(workDir);
            string projectName = Path.GetFileName(cleanDir);
            if (string.IsNullOrEmpty(projectName))
            {
                projectName = cleanDir;
            }

            string tooltip = LocalizationService.Format(
                ResourceKeys.TipActiveProject,
                "Active Project: {0}\nPath: {1}\n\nClick to switch project or work directory.",
                projectName,
                cleanDir);

            if (txtWorkDirBtn != null)
            {
                txtWorkDirBtn.Text = projectName;
            }
            if (btnWorkDir != null)
            {
                if (LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppButtonActionBrush, out var brush) && brush != null)
                {
                    btnWorkDir.Background = brush;
                }
                ToolTip.SetTip(btnWorkDir, tooltip);
            }
            if (lblCurrentProject != null)
            {
                lblCurrentProject.Text = projectName;
                ToolTip.SetTip(lblCurrentProject, tooltip);
            }

            Title = $"PhotoCropper - [{projectName}]";
        }
        else
        {
            string defaultBtn = LocalizationService.GetString(ResourceKeys.BtnWorkDir, "Folder");
            string noProject = LocalizationService.GetString(ResourceKeys.LblNoProject, "No Project");
            string defaultTip = LocalizationService.GetString(ResourceKeys.TipWorkDir, "Select a work directory for automatic raw scan staging and session recovery");

            if (txtWorkDirBtn != null)
            {
                txtWorkDirBtn.Text = defaultBtn;
            }
            if (btnWorkDir != null)
            {
                if (LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppSubtleCardBrush, out var brush) && brush != null)
                {
                    btnWorkDir.Background = brush;
                }
                ToolTip.SetTip(btnWorkDir, defaultTip);
            }
            if (lblCurrentProject != null)
            {
                lblCurrentProject.Text = noProject;
                ToolTip.SetTip(lblCurrentProject, defaultTip);
            }

            Title = "PhotoCropper - Intelligent Photo Extractor";
        }
    }

    private void LblCurrentProject_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        BtnWorkDir_Click(sender, e);
    }

    private void ClearActiveScansFromGui()
    {
        undoHistory.Clear();
        _sessionManager.Clear();
        SetMainImage(null);
        ClearGalleryBitmaps();
        if (txtFileCounter != null)
        {
            txtFileCounter.Text = LocalizationService.GetString(ResourceKeys.TxtNoFiles, "No files loaded");
        }
        if (lblPhotoInfo != null) lblPhotoInfo.Text = "";
        UpdateSelectionUi();
    }

    private async Task SwitchToProjectAsync(string newWorkDir)
    {
        var settings = SettingsManager.Instance.Settings;
        string? oldWorkDir = settings.WorkDirectory;

        // 1. Save existing session before switching
        if (!string.IsNullOrEmpty(oldWorkDir) && Directory.Exists(oldWorkDir) && _workspaceSession != null)
        {
            ProjectWorkspaceService.SaveSession(oldWorkDir, _workspaceSession);
        }

        // 2. Clear current scans from GUI so previous photos do not linger
        ClearActiveScansFromGui();

        // 3. Initialize workspace for new folder
        ProjectWorkspaceService.InitializeWorkspace(newWorkDir);
        settings.WorkDirectory = newWorkDir;
        SettingsManager.Instance.Save();
        UpdateWorkspaceUi(newWorkDir);

        // 4. If new folder has an existing session, load it; otherwise show clean project ready
        if (ProjectWorkspaceService.HasRecoverableSession(newWorkDir))
        {
            await ResumeWorkspaceSessionAsync(newWorkDir);
        }
        else
        {
            _workspaceSession = new WorkspaceSessionState();
            string cleanName = Path.GetFileName(Path.TrimEndingDirectorySeparator(newWorkDir));
            lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgNewProjectReady, "Project '{0}' ready. Click Scan or Open Files to begin.", cleanName);
        }
    }

    private async void BtnNewProject_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.GetString(ResourceKeys.BtnNewProject, "Select Folder for New Batch"),
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string chosenDir = folders[0].Path.LocalPath;
            await SwitchToProjectAsync(chosenDir);
        }
    }

    private async void BtnWorkDir_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider == null) return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = LocalizationService.GetString(ResourceKeys.BtnWorkDir, "Select Work Directory"),
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            string chosenDir = folders[0].Path.LocalPath;
            await SwitchToProjectAsync(chosenDir);
        }
    }
}
