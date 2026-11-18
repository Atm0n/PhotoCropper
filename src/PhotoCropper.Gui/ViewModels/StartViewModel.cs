using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;
using System.Collections.ObjectModel;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>What the start screen needs from the window.</summary>
internal interface IStartHost
{
    /// <summary>Recently opened project folders, newest first.</summary>
    IReadOnlyList<string> RecentProjectFolders { get; }

    /// <summary>The folder of the project that is open, or null at launch.</summary>
    string? OpenProjectFolder { get; }

    /// <summary>Where new projects are created unless another folder is chosen.</summary>
    string DefaultProjectsFolder { get; }

    /// <summary>Asks for a folder; null when cancelled.</summary>
    Task<string?> PickFolderAsync(string title);

    /// <summary>Opens a project (creating its folders if needed) and closes the start screen.</summary>
    /// <param name="restoreOnly">Go straight to importing photos to restore, in the Finish stage.</param>
    Task OpenProjectAsync(string folder, bool restoreOnly);

    void ForgetRecentProject(string folder);
}

/// <summary>One project in the start screen's recent list.</summary>
internal sealed partial class RecentProjectItem(string folder, string details, bool exists) : ObservableObject
{
    public string Folder { get; } = folder;
    public string Name { get; } = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder));
    public string Details { get; } = details;
    public bool Exists { get; } = exists;

    /// <summary>Its first scan, small.</summary>
    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }
}

/// <summary>
/// The start screen, shown at every launch and from the project button: create a project, open one, or create one
/// to restore photos that are already cropped. There is always a project open behind it.
/// </summary>
internal sealed partial class StartViewModel(IStartHost host) : ObservableObject
{
    /// <summary>
    /// Characters a project name can't contain: those Windows forbids in a folder name on every platform (Linux only
    /// forbids '/'), so a project stays usable when its folder is copied between systems.
    /// </summary>
    private static readonly char[] InvalidNameChars = [.. Path.GetInvalidFileNameChars().Union("\\/:*?\"<>|")];

    internal const int ThumbnailWidth = 160;

    public ObservableCollection<RecentProjectItem> Recent { get; } = [];

    [ObservableProperty]
    public partial bool IsOpen { get; set; }

    /// <summary>The "new project" form is shown.</summary>
    [ObservableProperty]
    public partial bool IsCreating { get; set; }

    /// <summary>The project being created is for restoring imported photos.</summary>
    [ObservableProperty]
    public partial bool IsRestoreOnly { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string NewName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string NewLocation { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string CreateError { get; set; } = string.Empty;

    /// <summary>A project is open, so the screen can be left without choosing one.</summary>
    public bool CanGoBack => host.OpenProjectFolder != null;

    public string BackText => host.OpenProjectFolder is { } folder
        ? LocalizationService.Format(ResourceKeys.BtnBackToProject, "Back to {0}", Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)))
        : string.Empty;

    public bool HasRecent => Recent.Count > 0;

    public void Open()
    {
        IsCreating = false;
        CreateError = string.Empty;
        OnPropertyChanged(nameof(CanGoBack));
        OnPropertyChanged(nameof(BackText));
        LoadRecent();
        IsOpen = true;
    }

    private void LoadRecent()
    {
        foreach (var old in Recent) old.Thumbnail?.Dispose();
        Recent.Clear();
        foreach (string folder in host.RecentProjectFolders)
        {
            bool exists = Directory.Exists(folder);
            var item = new RecentProjectItem(folder, exists ? Describe(folder) : LocalizationService.GetString(ResourceKeys.LblProjectMissing, "Folder not found"), exists);
            Recent.Add(item);
            if (exists) _ = LoadThumbnailAsync(item);
        }
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>"3 scans · 9 finished photos", from the files in the project's folders.</summary>
    internal static string Describe(string folder)
    {
        int scans = CountImages(ProjectWorkspaceService.GetRawScansDirectory(folder));
        int finals = CountImages(ProjectWorkspaceService.GetFinalDirectory(folder));
        return LocalizationService.Format(ResourceKeys.LblRecentProjectDetails, "{0} scans · {1} finished photos", scans, finals);
    }

    private static int CountImages(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.EnumerateFiles(dir).Count(IsImage) : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private static bool IsImage(string path) =>
        Path.GetExtension(path).ToUpperInvariant() is ".PNG" or ".JPG" or ".JPEG" or ".TIF" or ".TIFF" or ".BMP" or ".WEBP";

    private static async Task LoadThumbnailAsync(RecentProjectItem item)
    {
        try
        {
            string rawDir = ProjectWorkspaceService.GetRawScansDirectory(item.Folder);
            string? first = Directory.Exists(rawDir) ? Directory.EnumerateFiles(rawDir).Where(IsImage).Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault() : null;
            if (first == null) return;
            item.Thumbnail = await Task.Run(() =>
            {
                using var stream = File.OpenRead(first);
                return Bitmap.DecodeToWidth(stream, ThumbnailWidth);
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            System.Diagnostics.Trace.TraceWarning($"[PhotoCropper] Could not load project thumbnail '{item.Folder}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void NewProject() => StartCreating(restoreOnly: false);

    [RelayCommand]
    private void RestoreOnly() => StartCreating(restoreOnly: true);

    private void StartCreating(bool restoreOnly)
    {
        IsRestoreOnly = restoreOnly;
        NewLocation = host.DefaultProjectsFolder;
        NewName = SuggestName(NewLocation);
        CreateError = string.Empty;
        IsCreating = true;
    }

    /// <summary>"Album 2026-11-18", or "Album 2026-11-18 (2)" when that folder already exists.</summary>
    internal static string SuggestName(string location, DateTime? today = null)
    {
        string baseName = LocalizationService.Format(ResourceKeys.DefaultProjectName, "Album {0}", (today ?? DateTime.Today).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture));
        string name = baseName;
        for (int n = 2; Directory.Exists(Path.Combine(location, name)); n++) name = $"{baseName} ({n})";
        return name;
    }

    [RelayCommand]
    private void CancelCreate()
    {
        IsCreating = false;
        CreateError = string.Empty;
    }

    [RelayCommand]
    private async Task ChangeLocation()
    {
        string? folder = await host.PickFolderAsync(LocalizationService.GetString(ResourceKeys.LblProjectLocation, "Location"));
        if (folder != null) NewLocation = folder;
    }

    private bool CanCreate() => !string.IsNullOrWhiteSpace(NewName);

    /// <summary>Creates the project folder (it must not exist yet) and opens it.</summary>
    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task Create()
    {
        string name = NewName.Trim();
        if (name.IndexOfAny(InvalidNameChars) >= 0 || name is "." or "..")
        {
            CreateError = LocalizationService.GetString(ResourceKeys.MsgProjectNameInvalid, "A project name can't contain \\ / : * ? \" < > |");
            return;
        }

        string folder = Path.Combine(NewLocation, name);
        if (Directory.Exists(folder))
        {
            CreateError = LocalizationService.Format(ResourceKeys.MsgProjectExists, "There is already a folder called '{0}' there. Choose another name, or use Open project.", name);
            return;
        }

        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
        {
            CreateError = LocalizationService.Format(ResourceKeys.MsgProjectCreateFailed, "Could not create the project folder: {0}", ex.Message);
            return;
        }

        await OpenAndCloseAsync(folder, IsRestoreOnly);
    }

    [RelayCommand]
    private async Task OpenProject()
    {
        string? folder = await host.PickFolderAsync(LocalizationService.GetString(ResourceKeys.BtnOpenProject, "Open project"));
        if (folder != null) await OpenAndCloseAsync(folder, restoreOnly: false);
    }

    [RelayCommand]
    private async Task OpenRecent(RecentProjectItem? item)
    {
        if (item is { Exists: true }) await OpenAndCloseAsync(item.Folder, restoreOnly: false);
    }

    [RelayCommand]
    private void RemoveRecent(RecentProjectItem? item)
    {
        if (item == null) return;
        host.ForgetRecentProject(item.Folder);
        item.Thumbnail?.Dispose();
        Recent.Remove(item);
        OnPropertyChanged(nameof(HasRecent));
    }

    /// <summary>Leaves the screen for the project that is already open.</summary>
    [RelayCommand]
    public void Back()
    {
        if (IsCreating)
        {
            CancelCreate();
            return;
        }
        if (CanGoBack) IsOpen = false;
    }

    private async Task OpenAndCloseAsync(string folder, bool restoreOnly)
    {
        IsOpen = false;
        IsCreating = false;
        await host.OpenProjectAsync(folder, restoreOnly);
    }
}
