using Avalonia.Threading;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Diagnostics;

namespace PhotoCropper.Gui;

/// <summary>
/// Saves crop edits automatically: each edit (a scan's <see cref="ScanSessionItem.Modified"/>) schedules a write of
/// that scan's boxes to session.json shortly afterwards, so closing the app or a crash loses nothing and there is
/// no Save button. The Cropped/ PNGs are still written when moving to Finish.
/// </summary>
internal sealed partial class MainWindow
{
    /// <summary>How long after the last edit the boxes are written, so a drag doesn't write on every mouse move.</summary>
    private static readonly TimeSpan AutosaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly HashSet<ScanSessionItem> _autosavePending = [];
    private DispatcherTimer? _autosaveTimer;

    private void InitializeAutosave()
    {
        _autosaveTimer = new DispatcherTimer { Interval = AutosaveDelay };
        _autosaveTimer.Tick += (_, _) => RunAutosave();

        // Edits made by background work (the look-ahead auto-tune) arrive off the UI thread.
        _sessionManager.SessionModified += session => Dispatcher.UIThread.Post(() => ScheduleAutosave(session));
    }

    private void ScheduleAutosave(ScanSessionItem session)
    {
        if (WorkDirectory == null || _autosaveTimer == null) return;
        _autosavePending.Add(session);
        _autosaveTimer.Stop();
        _autosaveTimer.Start();
    }

    private void RunAutosave()
    {
        // While other work runs (detection, saving crops, navigation) the engines may be in use: try again later.
        if (isLoading || _isNavigating || _autosavePending.Any(s => s.IsProcessing)) return;
        _autosaveTimer?.Stop();
        FlushAutosave();
    }

    /// <summary>Writes the boxes of every edited scan now (also before closing or switching project).</summary>
    private void FlushAutosave()
    {
        _autosaveTimer?.Stop();
        if (_autosavePending.Count == 0) return;

        var sessions = _autosavePending.ToList();
        _autosavePending.Clear();

        var workspace = Workspace;
        string? workDir = WorkDirectory;
        if (workspace == null || workDir == null) return;

        bool changed = false;
        foreach (var session in sessions)
        {
            // Scans that were removed or unloaded meanwhile have nothing new to save.
            if (!ScanSessions.Contains(session) || session.Engine is not { } engine) continue;

            var entry = FindEntry(workspace, workDir, session.FilePath);
            if (entry == null) continue;

            var crops = BuildCropData(engine);
            lock (workspace)
            {
                entry.FinalCrops.Clear();
                foreach (var crop in crops) entry.FinalCrops.Add(crop);
            }

            // Reloading the scan (after a restart, or once it is unloaded) restores these boxes.
            session.SavedCrops = crops;
            session.IsSaved = true;
            changed = true;
        }

        if (!changed) return;
        try
        {
            lock (workspace) ProjectWorkspaceService.SaveSession(workDir, workspace);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceError($"[PhotoCropper] Autosave failed: {ex.Message}");
            ShowStatus(LocalizationService.Format(ResourceKeys.MsgAutosaveFailed, "Could not save your changes: {0}", ex.Message));
        }
    }

    private static WorkspaceScanEntry? FindEntry(WorkspaceSessionState workspace, string workDir, string scanPath)
    {
        string relative = Path.GetRelativePath(workDir, scanPath);
        string name = Path.GetFileName(scanPath);
        return workspace.Scans.FirstOrDefault(s => string.Equals(s.RelativePath, relative, StringComparison.OrdinalIgnoreCase))
            ?? workspace.Scans.FirstOrDefault(s => string.Equals(Path.GetFileName(s.RelativePath), name, StringComparison.OrdinalIgnoreCase));
    }
}
