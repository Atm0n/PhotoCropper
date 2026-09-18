using PhotoCropper.Core.Models;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Text.Json.Serialization;

namespace PhotoCropper.Core.Workspace;

public sealed class WorkspaceScanEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public DateTime StagedAtUtc { get; set; } = DateTime.UtcNow;
    public DetectionOptions? CustomOptions { get; set; }

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<int> Rotations { get; } = [];

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<Rectangle> ManualCrops { get; } = [];

    public bool IsProcessed { get; set; }
    public int ExtractedPhotoCount { get; set; }
}

public sealed class WorkspaceSessionState
{
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<WorkspaceScanEntry> Scans { get; } = [];
}
