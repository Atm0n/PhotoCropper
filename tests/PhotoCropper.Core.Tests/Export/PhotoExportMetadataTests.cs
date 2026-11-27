using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Tests.Export;

public class PhotoExportMetadataTests
{
    [Fact]
    public void EffectiveDateWhenDateTakenSpecifiedReturnsDateTaken()
    {
        var dt = new DateTime(1968, 5, 20);
        var meta = new PhotoExportMetadata
        {
            Year = 1970, // ignored when DateTaken is set
            DateTaken = dt
        };

        meta.EffectiveDate.ShouldBe(dt);
    }

    [Fact]
    public void EffectiveDateWhenOnlyYearSpecifiedReturnsJanFirst()
    {
        var meta = new PhotoExportMetadata
        {
            Year = 1985
        };

        var effective = meta.EffectiveDate;
        effective.ShouldNotBeNull();
        effective.Value.Year.ShouldBe(1985);
        effective.Value.Month.ShouldBe(1);
        effective.Value.Day.ShouldBe(1);
    }

    [Fact]
    public void FormattedExifDateStandardFormatMatchesExifSpec()
    {
        var meta = new PhotoExportMetadata
        {
            Year = 1975
        };

        meta.FormattedExifDate.ShouldBe("1975:01:01 00:00:00");
    }

    [Fact]
    public void HasMetadataWhenEmptyReturnsFalse()
    {
        var meta = new PhotoExportMetadata();
        meta.HasMetadata.ShouldBeFalse();
    }

    [Fact]
    public void HasMetadataWhenYearOrDescSetReturnsTrue()
    {
        var meta = new PhotoExportMetadata { Description = "Family in Madrid" };
        meta.HasMetadata.ShouldBeTrue();
    }
}
