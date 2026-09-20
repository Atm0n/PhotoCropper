using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using Shouldly;
using Xunit;

namespace PhotoCropper.Core.Tests.Export;

public class FileNameTemplateHelperTests
{
    [Fact]
    public void FormatFileName_DefaultPattern_ProducesOriginalAndIndex()
    {
        string result = FileNameTemplateHelper.FormatFileName(
            template: null,
            originalFileNameWithoutExt: "Scan001",
            photoIndex: 1,
            totalPhotos: 3,
            metadata: null,
            extension: ".jpg");

        result.ShouldBe("Scan001_1.jpg");
    }

    [Fact]
    public void FormatFileName_ZeroPadded_ProducesPaddedIndex()
    {
        string result = FileNameTemplateHelper.FormatFileName(
            template: "{original}_{index:02}",
            originalFileNameWithoutExt: "FamilyAlbum",
            photoIndex: 5,
            totalPhotos: 12,
            metadata: null,
            extension: ".jpg");

        result.ShouldBe("FamilyAlbum_05.jpg");
    }

    [Fact]
    public void FormatFileName_ThreeDigitPadded_ProducesThreeDigitIndex()
    {
        string result = FileNameTemplateHelper.FormatFileName(
            template: "{original}_{index:000}",
            originalFileNameWithoutExt: "Doc",
            photoIndex: 7,
            totalPhotos: 50,
            metadata: null,
            extension: ".png");

        result.ShouldBe("Doc_007.png");
    }

    [Fact]
    public void FormatFileName_WithYearAndMetadata_IncludesYear()
    {
        var metadata = new PhotoExportMetadata
        {
            Year = 1985
        };

        string result = FileNameTemplateHelper.FormatFileName(
            template: "{year}_{original}_{index:02}",
            originalFileNameWithoutExt: "Summer",
            photoIndex: 2,
            totalPhotos: 4,
            metadata: metadata,
            extension: ".jpg");

        result.ShouldBe("1985_Summer_02.jpg");
    }

    [Fact]
    public void FormatFileName_WithYearPatternButNoMetadataYear_CleansLeadingSeparator()
    {
        var metadata = new PhotoExportMetadata(); // No year set

        string result = FileNameTemplateHelper.FormatFileName(
            template: "{year}_{original}_{index:02}",
            originalFileNameWithoutExt: "Summer",
            photoIndex: 2,
            totalPhotos: 4,
            metadata: metadata,
            extension: ".jpg");

        // Leading underscore should be cleanly removed
        result.ShouldBe("Summer_02.jpg");
    }

    [Fact]
    public void FormatFileName_WithDateToken_IncludesFormattedDate()
    {
        var metadata = new PhotoExportMetadata
        {
            DateTaken = new DateTime(1974, 8, 15)
        };

        string result = FileNameTemplateHelper.FormatFileName(
            template: "{date}_{index:02}",
            originalFileNameWithoutExt: "Trip",
            photoIndex: 1,
            totalPhotos: 2,
            metadata: metadata,
            extension: ".png");

        result.ShouldBe("1974-08-15_01.png");
    }

    [Fact]
    public void FormatFileName_SanitizesInvalidPathCharacters()
    {
        string result = FileNameTemplateHelper.FormatFileName(
            template: "{original}_photo:?*{index}",
            originalFileNameWithoutExt: "Test",
            photoIndex: 3,
            totalPhotos: 3,
            metadata: null,
            extension: ".jpg");

        result.ShouldNotContain(":");
        result.ShouldNotContain("?");
        result.ShouldNotContain("*");
        result.ShouldEndWith(".jpg");
    }

    [Fact]
    public void FormatFileName_TotalToken_ReplacesTotalCount()
    {
        string result = FileNameTemplateHelper.FormatFileName(
            template: "{original}_photo_{index}_of_{total}",
            originalFileNameWithoutExt: "Batch1",
            photoIndex: 2,
            totalPhotos: 5,
            metadata: null,
            extension: ".jpg");

        result.ShouldBe("Batch1_photo_2_of_5.jpg");
    }

    [Fact]
    public void FormatPreview_GeneratesCleanPreview()
    {
        var meta = new PhotoExportMetadata { Year = 1990 };
        string preview = FileNameTemplateHelper.FormatPreview("{year}_{original}_{index:02}", sampleMetadata: meta);
        preview.ShouldBe("1990_Scan001_01.jpg");
    }
}
