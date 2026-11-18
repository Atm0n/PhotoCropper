using PhotoCropper.Core.Models;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Text.Json.Serialization;

namespace PhotoCropper.Core.Workspace;

public sealed class WorkspaceCropData
{
    public float CenterX { get; set; }
    public float CenterY { get; set; }
    public float Width { get; set; }
    public float Height { get; set; }
    public float Angle { get; set; }

    /// <summary>The user's manual clockwise rotation, applied after auto-orientation.</summary>
    public int RotationDegrees { get; set; }

    /// <summary>
    /// Clockwise rotation auto-orientation applied when the crop was saved. Restoring reuses it rather
    /// than re-detecting, so a photo comes back exactly as it was. Null in sessions saved before this
    /// was recorded, in which case orientation is detected again.
    /// </summary>
    public int? AutoRotationDegrees { get; set; }

    /// <summary>The engine's boxes, with each photo's manual and auto rotation, as saved in session.json.</summary>
    public static IReadOnlyList<WorkspaceCropData> FromEngine(PhotoCropperEngine engine)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var crops = new List<WorkspaceCropData>(engine.AcceptedCandidates.Count);
        for (int i = 0; i < engine.AcceptedCandidates.Count; i++)
        {
            var cand = engine.AcceptedCandidates[i];
            crops.Add(new WorkspaceCropData
            {
                CenterX = cand.Rotated.Center.X,
                CenterY = cand.Rotated.Center.Y,
                Width = cand.Rotated.Size.Width,
                Height = cand.Rotated.Size.Height,
                Angle = cand.Rotated.Angle,
                RotationDegrees = engine.PhotoRotations.TryGetValue(i, out int r) ? r : 0,
                AutoRotationDegrees = cand.OrientationDegrees
            });
        }
        return crops;
    }
}

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

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<WorkspaceCropData> FinalCrops { get; } = [];

    /// <summary>The files the last export of this scan wrote, replaced on re-export.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<PhotoCropper.Core.Export.ExportedFile> ExportedFiles { get; } = [];

    public bool IsProcessed { get; set; }
    public int ExtractedPhotoCount { get; set; }
    public PhotoExportMetadata? Metadata { get; set; }

    /// <summary>Next unused photo number for this scan; numbers are never reused, so photo IDs stay unique.</summary>
    public int NextPhotoNumber { get; set; } = 1;
}

public sealed class WorkspaceSessionState
{
    /// <summary>Current session format: 2.0 adds the RawScans → Cropped → Final pipeline (<see cref="Photos"/>).</summary>
    public const string CurrentVersion = "2.0";

    /// <summary>
    /// Format of this session. Defaults to "1.0" so files written before versions 2+ (which lack the field
    /// or say "1.0") are recognised and migrated; new sessions are stamped with <see cref="CurrentVersion"/>.
    /// </summary>
    public string Version { get; set; } = "1.0";
    public DateTime LastUpdatedUtc { get; set; } = DateTime.UtcNow;

    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<WorkspaceScanEntry> Scans { get; } = [];

    /// <summary>Every photo in the pipeline, from scans and imports.</summary>
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public Collection<ProjectPhoto> Photos { get; } = [];

    /// <summary>Next unused number for imported photos.</summary>
    public int NextImportNumber { get; set; } = 1;

    public static WorkspaceSessionState CreateNew() => new() { Version = CurrentVersion };
}
