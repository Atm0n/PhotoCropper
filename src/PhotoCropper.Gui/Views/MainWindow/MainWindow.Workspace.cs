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
        // Every launch starts by choosing (or creating) a project.
        ShowStartScreen();

        _ = RefreshScannersAsync(CancellationToken.None).ContinueWith(t => System.Diagnostics.Trace.TraceError(t.Exception?.ToString()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        CheckForUpdatesOnStartupIfDue();
    }

    private async Task ResumeWorkspaceSessionAsync(string workDir)
    {
        _workspaceSession = ProjectWorkspaceService.LoadSession(workDir);
        if (_workspaceSession == null || _workspaceSession.Scans.Count == 0)
        {
            _workspaceSession = ProjectWorkspaceService.ReconstructSessionFromRawFiles(workDir);
        }
        else
        {
            _workspaceSession = ProjectWorkspaceService.SyncSessionWithRawFiles(workDir, _workspaceSession);
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
                lblStatus.Text = LocalizationService.Format(ResourceKeys.MsgProjectLoaded, "Project loaded: {0} scans.", rawPaths.Count);
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

            txtWorkDirBtn?.Text = projectName;
            if (btnWorkDir != null)
            {
                if (LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppButtonActionBrush, out var brush) && brush != null)
                {
                    btnWorkDir.Background = brush;
                }
                ToolTip.SetTip(btnWorkDir, tooltip);
            }

            Title = $"PhotoCropper - [{projectName}]";
        }
        else
        {
            string defaultBtn = LocalizationService.GetString(ResourceKeys.BtnWorkDir, "Folder");
            string defaultTip = LocalizationService.GetString(ResourceKeys.TipWorkDir, "Select a work directory for automatic raw scan staging and session recovery");

            txtWorkDirBtn?.Text = defaultBtn;
            if (btnWorkDir != null)
            {
                if (LocalizationService.TryGetResource<IBrush>(ResourceKeys.AppSubtleCardBrush, out var brush) && brush != null)
                {
                    btnWorkDir.Background = brush;
                }
                ToolTip.SetTip(btnWorkDir, defaultTip);
            }

            Title = LocalizationService.GetString(ResourceKeys.AppTitle, "PhotoCropper - Intelligent Photo Extractor");
        }
    }

    private void ClearActiveScansFromGui()
    {
        undoHistory.Clear();
        _sessionManager.Clear();
        ShowScan(null);
        _cropsVm.ClearPhotos();
        _cropsVm.UpdateScanCounter();
    }

    private async Task SwitchToProjectAsync(string newWorkDir)
    {
        var settings = SettingsManager.Instance.Settings;
        string? oldWorkDir = settings.WorkDirectory;

        // 1. Save existing session before switching
        FlushAutosave();
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
}
