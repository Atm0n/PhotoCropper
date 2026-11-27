using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using System.Text;

namespace PhotoCropper.Core.Tests.Export;

public sealed class ExifTextEncodingTests
{
    private static byte[] MinimalJpeg() => [0xFF, 0xD8, 0xFF, 0xD9];

    private static bool Contains(byte[] haystack, byte[] needle) =>
        haystack.AsSpan().IndexOf(needle) >= 0;

    [Fact]
    public void TextFieldBytesShouldKeepNonAsciiCharacters()
    {
        byte[] bytes = ExifMetadataWriter.TextFieldBytes("Josep Martí");

        Encoding.UTF8.GetString(bytes).ShouldBe("Josep Martí\0");
    }

    [Fact]
    public void InjectJpegMetadataShouldWriteNonAsciiArtistAndDescriptionIntact()
    {
        var metadata = new PhotoExportMetadata { Photographer = "Núria Puig", Description = "Festa a Sant Martí" };

        byte[] jpeg = ExifMetadataWriter.InjectJpegMetadata(MinimalJpeg(), metadata);

        Contains(jpeg, Encoding.UTF8.GetBytes("Núria Puig\0")).ShouldBeTrue();
        Contains(jpeg, Encoding.UTF8.GetBytes("Festa a Sant Martí\0")).ShouldBeTrue();
        Contains(jpeg, Encoding.ASCII.GetBytes("N?ria")).ShouldBeFalse();
    }

    [Fact]
    public void UserCommentBytesAsciiTextShouldUseAsciiCharacterCode()
    {
        byte[] bytes = ExifMetadataWriter.UserCommentBytes("Grandparents");

        Encoding.ASCII.GetString(bytes, 0, 8).ShouldBe("ASCII\0\0\0");
        Encoding.ASCII.GetString(bytes, 8, bytes.Length - 8).ShouldBe("Grandparents");
    }

    [Fact]
    public void UserCommentBytesNonAsciiTextShouldUseUnicodeCharacterCode()
    {
        byte[] bytes = ExifMetadataWriter.UserCommentBytes("Martí");

        Encoding.ASCII.GetString(bytes, 0, 8).ShouldBe("UNICODE\0");
        Encoding.Unicode.GetString(bytes, 8, bytes.Length - 8).ShouldBe("Martí");
    }
}
