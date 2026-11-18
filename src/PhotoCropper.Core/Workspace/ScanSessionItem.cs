using PhotoCropper.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Core.Workspace;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Engine lifecycle is managed explicitly via Activate/Deactivate/Dispose methods")]
public sealed class ScanSessionItem : IDisposable
{
    public string FilePath { get; }
    public DetectionOptions Options { get; set; }
    public PhotoCropperEngine? Engine { get; private set; }
    public bool IsActive => Engine != null;
    public int PhotoCount => Engine?.DetectedPhotos.Count ?? CachedPhotoCount;
    public int CachedPhotoCount { get; private set; }
    public bool IsSaved { get; set; }

    /// <summary>
    /// The scan's boxes differ from a fresh detection, so its engine is kept loaded. Setting it to true (on every
    /// edit, even when already true) raises <see cref="Modified"/>, which the GUI uses to save the boxes.
    /// </summary>
    public bool IsModified
    {
        get => _isModified;
        set
        {
            _isModified = value;
            if (value) Modified?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool _isModified;

    /// <summary>Raised on each edit (see <see cref="IsModified"/>), possibly from a background thread.</summary>
    public event EventHandler? Modified;
    public bool IsAutoTuned { get; set; }
    public bool IsProcessing { get; set; }
    public PhotoExportMetadata Metadata { get; set; } = new();

    /// <summary>Files written by this scan's last export; replaced by the next export.</summary>
    public IReadOnlyList<PhotoCropper.Core.Export.ExportedFile> ExportedFiles { get; set; } = [];

    /// <summary>The boxes saved in session.json; a scan that has them is restored from them instead of re-detected.</summary>
    public IReadOnlyList<PhotoCropper.Core.Workspace.WorkspaceCropData>? SavedCrops { get; set; }

    private readonly Lock _lock = new();

    public ScanSessionItem(string filePath, DetectionOptions defaultOptions, bool isSaved = false, bool isModified = true, IEnumerable<PhotoCropper.Core.Workspace.WorkspaceCropData>? savedCrops = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(defaultOptions);

        FilePath = filePath;
        Options = defaultOptions with { };
        IsSaved = isSaved;
        _isModified = isModified;
        SavedCrops = savedCrops?.ToList();
    }

    public PhotoCropperEngine Activate()
    {
        lock (_lock)
        {
            if (Engine == null)
            {
                var engine = new PhotoCropperEngine(FilePath);
                engine.ApplyOptions(Options);

                // An empty saved list is kept as empty: every photo on the scan was deleted.
                if (SavedCrops != null && IsSaved && !IsModified)
                {
                    engine.RestoreFromSavedCrops(SavedCrops);
                }
                else
                {
                    engine.DetectPhotos();
                }

                CachedPhotoCount = engine.DetectedPhotos.Count;
                Engine = engine;
            }
        }
        return Engine;
    }

    public void Deactivate()
    {
        lock (_lock)
        {
            if (Engine != null && !IsProcessing)
            {
                Options = Engine.CurrentOptions with { };
                CachedPhotoCount = Engine.DetectedPhotos.Count;
                Engine.Dispose();
                Engine = null;
                IsAutoTuned = false;
            }
        }
    }

    public bool TryDeactivateIfUnmodified()
    {
        lock (_lock)
        {
            if (!IsModified && !IsProcessing && Engine != null)
            {
                Options = Engine.CurrentOptions with { };
                CachedPhotoCount = Engine.DetectedPhotos.Count;
                Engine.Dispose();
                Engine = null;
                IsAutoTuned = false;
                return true;
            }
            return false;
        }
    }

    public void DeactivateIfUnmodified()
    {
        TryDeactivateIfUnmodified();
    }

    public void Dispose()
    {
        Deactivate();
    }
}
