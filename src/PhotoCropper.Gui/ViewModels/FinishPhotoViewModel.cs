using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>One photo in the Finish stage: its cropped thumbnail, restoration choices and export status.</summary>
internal sealed partial class FinishPhotoViewModel : ObservableObject
{
    private readonly Action<FinishPhotoViewModel> _changed;

    public FinishPhotoViewModel(ProjectPhoto photo, Action<FinishPhotoViewModel> changed)
    {
        ArgumentNullException.ThrowIfNull(photo);
        ArgumentNullException.ThrowIfNull(changed);
        Photo = photo;
        _changed = changed;
    }

    public ProjectPhoto Photo { get; }

    /// <summary>What kind of print this is (black and white, sepia, faded or good colour), once looked at.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StyleText), nameof(HasStyle))]
    public partial PhotoStyle Style { get; set; }

    public bool HasStyle => Style != PhotoStyle.Unknown;

    public string StyleText => FinishStageViewModel.StyleName(Style);

    public string Title => Photo.Id;

    public string SourceText => Photo.ScanRelativePath != null
        ? Path.GetFileName(Photo.ScanRelativePath)
        : LocalizationService.GetString(ResourceKeys.FinishSourceImported, "Imported");

    [ObservableProperty]
    public partial Bitmap? Thumbnail { get; set; }

    // ---- Restoration settings (each change updates the photo's record and is saved shortly after) ----

    public bool RestoreColors
    {
        get => Photo.Restoration.RestoreColors;
        set => Update(s => s with { RestoreColors = value });
    }

    public bool RemoveDust
    {
        get => Photo.Restoration.RemoveDust;
        set => Update(s => s with { RemoveDust = value });
    }

    /// <summary>Leave out the print's white border in the final photo (the cropped photo keeps the whole print).</summary>
    public bool TrimWhiteBorders
    {
        get => Photo.Restoration.TrimWhiteBorders;
        set => Update(s => s with { TrimWhiteBorders = value });
    }

    /// <summary>Chosen in the grid (Ctrl+click, Shift+click or its tick) for the bulk actions.</summary>
    [ObservableProperty]
    public partial bool IsMarked { get; set; }

    partial void OnIsMarkedChanged(bool value) => MarkChanged?.Invoke(this);

    /// <summary>Raised when <see cref="IsMarked"/> changes, so the stage can count the selection.</summary>
    public event Action<FinishPhotoViewModel>? MarkChanged;

    /// <summary>0 = Gentle, 1 = Normal, 2 = Strong (bound to a ComboBox).</summary>
    public int StrengthIndex
    {
        get => (int)Photo.Restoration.Strength;
        set => Update(s => s with { Strength = (RestorationStrength)Math.Clamp(value, 0, 2) });
    }

    public bool AutoWhiteBalance
    {
        get => Photo.Restoration.AutoWhiteBalance;
        set => Update(s => s with { AutoWhiteBalance = value });
    }

    public bool AutoContrast
    {
        get => Photo.Restoration.AutoContrast;
        set => Update(s => s with { AutoContrast = value });
    }

    public bool AutoSaturation
    {
        get => Photo.Restoration.AutoSaturation;
        set => Update(s => s with { AutoSaturation = value });
    }

    /// <summary>0 = Auto, 1 = Colour, 2 = Black &amp; white (keep tone), 3 = Black &amp; white (neutral).</summary>
    public int ColorModeIndex
    {
        get => (int)Photo.Restoration.ColorMode;
        set => Update(s => s with { ColorMode = (PhotoColorMode)Math.Clamp(value, 0, 3) });
    }

    public double Brightness
    {
        get => Photo.Restoration.Brightness;
        set => Update(s => s with { Brightness = ToAdjustment(value) });
    }

    public double Contrast
    {
        get => Photo.Restoration.Contrast;
        set => Update(s => s with { Contrast = ToAdjustment(value) });
    }

    public double Warmth
    {
        get => Photo.Restoration.Warmth;
        set => Update(s => s with { Warmth = ToAdjustment(value) });
    }

    public double Tint
    {
        get => Photo.Restoration.Tint;
        set => Update(s => s with { Tint = ToAdjustment(value) });
    }

    public double Saturation
    {
        get => Photo.Restoration.Saturation;
        set => Update(s => s with { Saturation = ToAdjustment(value) });
    }

    private static int ToAdjustment(double value) => (int)Math.Round(Math.Clamp(value, -100, 100));

    /// <summary>
    /// Replaces the colour settings with a preset ("faded", "bw", "sepia", "dust", "none"). Whether the border is
    /// trimmed is about the print, not its colours, so it stays as it is.
    /// </summary>
    [RelayCommand]
    public void ApplyPreset(string? name)
    {
        var preset = RestorationSettings.FromPresetName(name);
        if (preset != null) Update(s => preset with { TrimWhiteBorders = s.TrimWhiteBorders });
    }

    /// <summary>Applies the preset that suits this kind of print (see <see cref="PhotoStyleClassifier.SuggestedPreset"/>).</summary>
    public void ApplySuggestedPreset() => ApplyPreset(PhotoStyleClassifier.SuggestedPreset(Style));

    /// <summary>Sets the manual adjustments back to 0, keeping the automatic settings.</summary>
    [RelayCommand]
    private void ResetAdjustments() =>
        Update(s => s with { Brightness = 0, Contrast = 0, Warmth = 0, Tint = 0, Saturation = 0 });

    /// <summary>Replaces this photo's settings (e.g. copied from another photo).</summary>
    public void ApplySettings(RestorationSettings settings) => Update(_ => settings);

    // ---- Repair brush ----

    public int RepairCount => Photo.Repairs.Count;

    public bool HasRepairs => Photo.Repairs.Count > 0;

    /// <summary>
    /// Adds a repair-brush stroke painted on the cropped photo of <paramref name="imageWidth"/> × <paramref name="imageHeight"/>.
    /// Strokes left from an earlier crop of a different size couldn't be applied anyway, so they are replaced.
    /// </summary>
    public void AddRepair(RepairStroke stroke, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(stroke);
        if (stroke.PointCount == 0) return;

        if (Photo.AreRepairsOutdated(imageWidth, imageHeight)) Photo.Repairs.Clear();
        Photo.RepairImageWidth = imageWidth;
        Photo.RepairImageHeight = imageHeight;
        Photo.Repairs.Add(stroke);
        OnRestorationChanged();
    }

    [RelayCommand]
    private void UndoRepair()
    {
        if (Photo.Repairs.Count == 0) return;
        Photo.Repairs.RemoveAt(Photo.Repairs.Count - 1);
        OnRestorationChanged();
    }

    [RelayCommand]
    private void ClearRepairs()
    {
        if (Photo.Repairs.Count == 0) return;
        Photo.Repairs.Clear();
        OnRestorationChanged();
    }

    private void Update(Func<RestorationSettings, RestorationSettings> change)
    {
        var updated = change(Photo.Restoration);
        if (updated == Photo.Restoration) return; // records compare by value: nothing changed
        Photo.Restoration = updated;
        OnRestorationChanged();
    }

    public bool IsStale => Photo.IsFinalStale;

    public string StatusText => Photo.Final == null
        ? LocalizationService.GetString(ResourceKeys.FinishStatusNew, "Not exported yet")
        : Photo.IsFinalStale
            ? LocalizationService.GetString(ResourceKeys.FinishStatusStale, "Changed since export")
            : LocalizationService.GetString(ResourceKeys.FinishStatusExported, "Exported");

    /// <summary>Re-reads the record after it was changed elsewhere (e.g. after exporting).</summary>
    public void NotifyRecordChanged()
    {
        // Every setting is read from the record, so refresh them all.
        OnPropertyChanged(string.Empty);
    }

    private void OnRestorationChanged()
    {
        NotifyRecordChanged();
        _changed(this);
    }
}
