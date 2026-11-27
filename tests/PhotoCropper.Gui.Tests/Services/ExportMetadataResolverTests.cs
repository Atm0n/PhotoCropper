using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Services;

public sealed class ExportMetadataResolverTests
{
    private static PhotoExportMetadata ScanMetadata() => new()
    {
        Year = 1975,
        DateTaken = new DateTime(1975, 6, 14, 0, 0, 0, DateTimeKind.Unspecified),
        Description = "Beach trip",
        Photographer = "Studio Foto"
    };

    [Fact]
    public void ResolveWhenNotApplyingDefaultsShouldReturnScanMetadataUnchanged()
    {
        var scan = ScanMetadata();

        var result = ExportMetadataResolver.Resolve(scan, applyDefaultsToAllScans: false, 1990, "Default");

        result.ShouldBeSameAs(scan);
    }

    [Fact]
    public void ResolveWhenApplyingDefaultsShouldOverrideYearAndDescription()
    {
        var result = ExportMetadataResolver.Resolve(ScanMetadata(), applyDefaultsToAllScans: true, 1990, "Default");

        result.Year.ShouldBe(1990);
        result.Description.ShouldBe("Default");
    }

    [Fact]
    public void ResolveWhenApplyingDefaultsShouldKeepPhotographer()
    {
        var result = ExportMetadataResolver.Resolve(ScanMetadata(), applyDefaultsToAllScans: true, 1990, "Default");

        result.Photographer.ShouldBe("Studio Foto");
    }

    [Fact]
    public void ResolveWhenDateConflictsWithDefaultYearShouldDropDateSoDefaultYearIsEmbedded()
    {
        var result = ExportMetadataResolver.Resolve(ScanMetadata(), applyDefaultsToAllScans: true, 1990, null);

        result.DateTaken.ShouldBeNull();
        result.EffectiveDate!.Value.Year.ShouldBe(1990);
    }

    [Fact]
    public void ResolveWhenDateAgreesWithDefaultYearShouldKeepExactDate()
    {
        var result = ExportMetadataResolver.Resolve(ScanMetadata(), applyDefaultsToAllScans: true, 1975, null);

        result.DateTaken.ShouldBe(new DateTime(1975, 6, 14, 0, 0, 0, DateTimeKind.Unspecified));
    }

    [Fact]
    public void ResolveWhenNoDefaultYearShouldKeepExactDate()
    {
        var result = ExportMetadataResolver.Resolve(ScanMetadata(), applyDefaultsToAllScans: true, null, null);

        result.Year.ShouldBeNull();
        result.DateTaken.ShouldNotBeNull();
    }
}
