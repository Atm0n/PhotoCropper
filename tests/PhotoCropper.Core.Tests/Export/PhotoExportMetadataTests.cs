using PhotoCropper.Core.Models;
using Shouldly;
using Xunit;

namespace PhotoCropper.Core.Tests.Export;

public class PhotoExportMetadataTests
{
    [Fact]
    public void EffectiveDate_WhenDateTakenSpecified_ReturnsDateTaken()
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
    public void EffectiveDate_WhenOnlyYearSpecified_ReturnsJanFirst()
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
    public void FormattedExifDate_StandardFormat_MatchesExifSpec()
    {
        var meta = new PhotoExportMetadata
        {
            Year = 1975
        };

        meta.FormattedExifDate.ShouldBe("1975:01:01 00:00:00");
    }

    [Fact]
    public void HasMetadata_WhenEmpty_ReturnsFalse()
    {
        var meta = new PhotoExportMetadata();
        meta.HasMetadata.ShouldBeFalse();
    }

    [Fact]
    public void HasMetadata_WhenYearOrDescSet_ReturnsTrue()
    {
        var meta = new PhotoExportMetadata { Description = "Family in Madrid" };
        meta.HasMetadata.ShouldBeTrue();
    }
}
