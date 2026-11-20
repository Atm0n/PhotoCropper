using PhotoCropper.Core.Workspace;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>The three steps of the workflow, matching the project folders RawScans → Cropped → Final.</summary>
internal enum AppStage
{
    Scan,
    Crops,
    Finish
}

/// <summary>What the stage view-models need from the window, kept as an interface so they can be tested without UI.</summary>
internal interface IStageHost
{
    /// <summary>The open project folder, or null when no project is open.</summary>
    string? WorkDirectory { get; }

    /// <summary>The open project's session, or null when no project is open.</summary>
    WorkspaceSessionState? Workspace { get; }

    IReadOnlyList<Core.Workspace.ScanSessionItem> Scans { get; }

    Task ScanAsync();

    /// <summary>Opens the scanner settings (device, resolution, page size).</summary>
    Task ConfigureScannerAsync();

    /// <summary>Finds a scan's photos as the Crops stage does (it is then ready there) and returns their boxes.</summary>
    Task<IReadOnlyList<WorkspaceCropData>> PrepareScanAsync(int scanIndex, CancellationToken ct);

    Task ImportScansAsync();
    void OpenScanInCrops(int scanIndex);

    /// <summary>Deletes a scan from the project and disk, with its photos.</summary>
    Task DiscardScanAsync(int scanIndex);

    /// <summary>Scans a new page to replace a scan, keeping its place in the list.</summary>
    Task RescanAsync(int scanIndex);

    /// <summary>Asks the user for photo files to import (e.g. photos cropped elsewhere).</summary>
    Task<IReadOnlyList<string>> PickPhotosToImportAsync();

    /// <summary>How final photos are written (format, quality, naming pattern, default metadata, output folder).</summary>
    FinalRenderOptions GetRenderOptions();

    /// <summary>Restoration given to photos that are new to the pipeline.</summary>
    RestorationSettings DefaultRestoration { get; }

    /// <summary>Runs work behind the loading overlay; returns false if it failed or was skipped.</summary>
    Task<bool> RunBusyAsync(string message, Func<Task> work);

    void SaveWorkspace();
    void ShowStatus(string message);

    /// <summary>Tells the user a long export has finished: a popup and a taskbar flash, as the export settings allow.</summary>
    void NotifyExportCompleted(string message, bool succeeded);

    void OpenFolder(string path);
}
