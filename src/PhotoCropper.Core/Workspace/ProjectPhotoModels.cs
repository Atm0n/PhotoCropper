using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Workspace;

/// <summary>
/// A file inside the project folder, recorded with its size and write time when the pipeline wrote it.
/// Paths are relative to the project folder so a project can be moved or synced.
/// </summary>
public sealed class ProjectFile
{
    public string RelativePath { get; init; } = string.Empty;
    public long Length { get; init; }
    public DateTime LastWriteTimeUtc { get; init; }

    public static ProjectFile FromDisk(string workDirectory, string fullPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(fullPath);
        var info = new FileInfo(fullPath);
        return new ProjectFile
        {
            RelativePath = Path.GetRelativePath(workDirectory, info.FullName),
            Length = info.Length,
            LastWriteTimeUtc = info.LastWriteTimeUtc
        };
    }

    public string GetFullPath(string workDirectory) => Path.GetFullPath(Path.Combine(workDirectory, RelativePath));

    /// <summary>
    /// True when the file still exists exactly as the pipeline wrote it. A missing, replaced or edited file
    /// is not the pipeline's to overwrite or delete.
    /// </summary>
    public bool IsUnchangedOnDisk(string workDirectory)
    {
        if (string.IsNullOrEmpty(RelativePath)) return false;
        var info = new FileInfo(GetFullPath(workDirectory));
        return info.Exists && info.Length == Length && info.LastWriteTimeUtc == LastWriteTimeUtc;
    }

    /// <summary>True when both records describe the same file content (same path, size and write time).</summary>
    public bool IsSameVersionAs(ProjectFile? other) =>
        other != null &&
        string.Equals(RelativePath, other.RelativePath, StringComparison.OrdinalIgnoreCase) &&
        Length == other.Length &&
        LastWriteTimeUtc == other.LastWriteTimeUtc;
}

/// <summary>
/// One stroke of the repair brush on a cropped photo: the area under it is filled in from its surroundings.
/// Coordinates are fractions of the photo's width and height (0..1), so a stroke applies the same way to the
/// preview and to the full-size photo.
/// </summary>
public sealed class RepairStroke
{
    /// <summary>Brush radius as a fraction of the photo's width.</summary>
    public float Radius { get; init; }

    /// <summary>The stroke's points as x0, y0, x1, y1, …, each a fraction of the photo's width or height.</summary>
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public System.Collections.ObjectModel.Collection<float> Points { get; } = [];

    [System.Text.Json.Serialization.JsonIgnore]
    public int PointCount => Points.Count / 2;
}

/// <summary>How strongly the automatic restoration corrects a photo.</summary>
public enum RestorationStrength
{
    Gentle,
    Normal,
    Strong
}

/// <summary>Whether a photo is treated as a colour or a black-and-white print.</summary>
public enum PhotoColorMode
{
    /// <summary>Detect black-and-white and sepia prints and treat them as <see cref="BlackAndWhiteKeepTone"/>.</summary>
    Auto,

    Color,

    /// <summary>Black and white, keeping its tone (e.g. sepia): no white balance or saturation boost.</summary>
    BlackAndWhiteKeepTone,

    /// <summary>Black and white, made neutral grey.</summary>
    BlackAndWhiteNeutral
}

/// <summary>
/// Appearance fixes applied when a cropped photo is turned into its final version. Every new setting defaults to
/// "no change", so settings saved before they existed (only RestoreColors/RemoveDust) keep their meaning.
/// </summary>
public sealed record RestorationSettings
{
    /// <summary>Automatic restoration (white balance, contrast, saturation, as enabled below) at <see cref="Strength"/>.</summary>
    public bool RestoreColors { get; init; }

    /// <summary>Dust, hair and scratch removal; independent of the colour settings.</summary>
    public bool RemoveDust { get; init; }

    public RestorationStrength Strength { get; init; } = RestorationStrength.Normal;

    /// <summary>Part of the automatic restoration: neutralise colour casts (yellowing, fading).</summary>
    public bool AutoWhiteBalance { get; init; } = true;

    /// <summary>Part of the automatic restoration: stretch levels and recover local contrast.</summary>
    public bool AutoContrast { get; init; } = true;

    /// <summary>Part of the automatic restoration: revive faded colours.</summary>
    public bool AutoSaturation { get; init; } = true;

    public PhotoColorMode ColorMode { get; init; } = PhotoColorMode.Auto;

    /// <summary>Manual adjustments, each from -100 to 100 (0 = unchanged), applied after the automatic ones.</summary>
    public int Brightness { get; init; }
    public int Contrast { get; init; }
    public int Warmth { get; init; }
    public int Tint { get; init; }
    public int Saturation { get; init; }

    /// <summary>True when any manual adjustment is set.</summary>
    public bool HasManualAdjustments => Brightness != 0 || Contrast != 0 || Warmth != 0 || Tint != 0 || Saturation != 0;

    /// <summary>True when applying these settings would change the photo at all.</summary>
    public bool ChangesPhoto =>
        RestoreColors || RemoveDust || HasManualAdjustments || ColorMode == PhotoColorMode.BlackAndWhiteNeutral;

    public static RestorationSettings None { get; } = new();

    // ---- Presets: starting points that can then be tweaked per photo ----

    /// <summary>Faded colour print: automatic colour, contrast and saturation recovery, plus dust removal.</summary>
    public static RestorationSettings FadedColorPrint { get; } = new() { RestoreColors = true, RemoveDust = true, ColorMode = PhotoColorMode.Color };

    /// <summary>Black-and-white print: contrast recovery and dust removal, made neutral grey.</summary>
    public static RestorationSettings BlackAndWhite { get; } = new() { RestoreColors = true, RemoveDust = true, ColorMode = PhotoColorMode.BlackAndWhiteNeutral };

    /// <summary>Sepia print: contrast recovery and dust removal, keeping the warm tone.</summary>
    public static RestorationSettings Sepia { get; } = new() { RestoreColors = true, RemoveDust = true, ColorMode = PhotoColorMode.BlackAndWhiteKeepTone };

    /// <summary>Only remove dust and scratches; colours stay exactly as scanned.</summary>
    public static RestorationSettings OnlyCleanDust { get; } = new() { RemoveDust = true };

    /// <summary>A preset by name ("faded", "bw", "sepia", "dust", "none"); null if unknown.</summary>
    public static RestorationSettings? FromPresetName(string? name) => name?.Trim().ToUpperInvariant() switch
    {
        "FADED" => FadedColorPrint,
        "BW" => BlackAndWhite,
        "SEPIA" => Sepia,
        "DUST" => OnlyCleanDust,
        "NONE" => None,
        _ => null
    };
}

/// <summary>
/// One photo in the project and its history through the folder pipeline:
/// RawScans (scan) → Cropped/{Id}.png (geometry only) → Final/ (restored, with metadata).
/// </summary>
public sealed class ProjectPhoto
{
    /// <summary>Permanent ID, also the Cropped file name (e.g. "scan_0003_p02", "import_0001").</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>The scan this photo was cropped from, relative to the project; null for imported photos.</summary>
    public string? ScanRelativePath { get; init; }

    /// <summary>Original location of an imported photo; null for photos cropped from a scan.</summary>
    public string? ImportedFrom { get; init; }

    /// <summary>Stable number within its scan (or import), used as {index} in final file names.</summary>
    public int Number { get; init; }

    /// <summary>The box on the scan (with rotations) that produced the cropped photo; null for imported photos.</summary>
    public WorkspaceCropData? Crop { get; set; }

    /// <summary>The geometry-only photo in Cropped/.</summary>
    public ProjectFile? Cropped { get; set; }

    public RestorationSettings Restoration { get; set; } = RestorationSettings.None;

    /// <summary>Per-photo metadata; when null, the export's default metadata is used.</summary>
    public PhotoExportMetadata? Metadata { get; set; }

    /// <summary>The final photo in Final/.</summary>
    public ProjectFile? Final { get; set; }

    /// <summary>The Cropped file version the final photo was rendered from.</summary>
    public ProjectFile? FinalRenderedFrom { get; set; }

    /// <summary>The restoration settings the final photo was rendered with.</summary>
    public RestorationSettings? FinalRenderedWith { get; set; }

    /// <summary>Repair-brush strokes, applied to the cropped photo before restoration. Never written into Cropped/.</summary>
    [System.Text.Json.Serialization.JsonObjectCreationHandling(System.Text.Json.Serialization.JsonObjectCreationHandling.Populate)]
    public System.Collections.ObjectModel.Collection<RepairStroke> Repairs { get; } = [];

    /// <summary>
    /// Size of the cropped photo the strokes were painted on. If a re-crop changes the size, the strokes would no
    /// longer line up, so they are kept but not applied (see <see cref="AreRepairsOutdated"/>).
    /// </summary>
    public int RepairImageWidth { get; set; }
    public int RepairImageHeight { get; set; }

    /// <summary>Fingerprint of the strokes the final photo was rendered with.</summary>
    public string? FinalRenderedRepairs { get; set; }

    /// <summary>True when the strokes were painted on a cropped photo of a different size than <paramref name="width"/> × <paramref name="height"/>.</summary>
    public bool AreRepairsOutdated(int width, int height) =>
        Repairs.Count > 0 && (RepairImageWidth != width || RepairImageHeight != height);

    /// <summary>A short fingerprint of the current strokes ("" when there are none), to detect changes.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string RepairsFingerprint
    {
        get
        {
            if (Repairs.Count == 0) return string.Empty;
            var text = new System.Text.StringBuilder();
            text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{RepairImageWidth}x{RepairImageHeight};");
            foreach (var stroke in Repairs)
            {
                text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{stroke.Radius:R}:");
                foreach (float p in stroke.Points) text.Append(System.Globalization.CultureInfo.InvariantCulture, $"{p:R},");
                text.Append('|');
            }
            byte[] hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text.ToString()));
            return Convert.ToHexString(hash, 0, 8);
        }
    }

    /// <summary>
    /// True when the final photo is missing or no longer matches the cropped photo, restoration settings or repairs.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsFinalStale =>
        Final == null ||
        Cropped == null ||
        !Cropped.IsSameVersionAs(FinalRenderedFrom) ||
        FinalRenderedWith != Restoration ||
        (FinalRenderedRepairs ?? string.Empty) != RepairsFingerprint;
}
