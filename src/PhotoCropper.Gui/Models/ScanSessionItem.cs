using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui.Models;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Engine lifecycle is managed explicitly via Activate/Deactivate/Dispose methods")]
internal sealed class ScanSessionItem : IDisposable
{
    public string FilePath { get; }
    public DetectionOptions Options { get; set; }
    public PhotoCropperEngine? Engine { get; private set; }
    public bool IsActive => Engine != null;
    public int PhotoCount => Engine?.DetectedPhotos.Count ?? CachedPhotoCount;
    public int CachedPhotoCount { get; private set; }
    public bool IsSaved { get; set; }
    public bool IsModified { get; set; }
    public bool IsAutoTuned { get; set; }
    public bool IsProcessing { get; set; }
    public PhotoExportMetadata Metadata { get; set; } = new();

    public IReadOnlyList<PhotoCropper.Core.Workspace.WorkspaceCropData>? SavedCrops { get; }

    private readonly object _lock = new();

    public ScanSessionItem(string filePath, DetectionOptions defaultOptions, bool isSaved = false, bool isModified = true, IEnumerable<PhotoCropper.Core.Workspace.WorkspaceCropData>? savedCrops = null)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(defaultOptions);

        FilePath = filePath;
        Options = defaultOptions with { };
        IsSaved = isSaved;
        IsModified = isModified;
        SavedCrops = savedCrops?.ToList();
    }

    public PhotoCropperEngine Activate()
    {
        if (Engine == null)
        {
            lock (_lock)
            {
                if (Engine == null)
                {
                    var engine = new PhotoCropperEngine(FilePath);
                    engine.ApplyOptions(Options);

                    if (SavedCrops != null && SavedCrops.Count > 0 && IsSaved && !IsModified)
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
