using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using System.Diagnostics.CodeAnalysis;

namespace PhotoCropper.Gui.Services;

[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Engine lifecycle is managed explicitly via Activate/Deactivate/Dispose methods")]
internal sealed class ScanSessionItem : IDisposable
{
    public string FilePath { get; }
    public DetectionOptions Options { get; set; }
    public PhotoCropperEngine? Engine { get; private set; }
    public bool IsActive => Engine != null;
    public int PhotoCount => Engine?.DetectedPhotos.Count ?? CachedPhotoCount;
    public int CachedPhotoCount { get; private set; }

    public ScanSessionItem(string filePath, DetectionOptions defaultOptions)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        ArgumentNullException.ThrowIfNull(defaultOptions);

        FilePath = filePath;
        Options = defaultOptions with { };
    }

    public PhotoCropperEngine Activate()
    {
        if (Engine == null)
        {
            Engine = new PhotoCropperEngine(FilePath);
            Engine.ApplyOptions(Options);
            Engine.DetectPhotos();
            CachedPhotoCount = Engine.DetectedPhotos.Count;
        }
        return Engine;
    }

    public void Deactivate()
    {
        if (Engine != null)
        {
            Options = Engine.CurrentOptions with { };
            CachedPhotoCount = Engine.DetectedPhotos.Count;
            Engine.Dispose();
            Engine = null;
        }
    }

    public void Dispose()
    {
        Deactivate();
    }
}
