using System.Text;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Models;
using Shouldly;
using Xunit;

namespace PhotoCropper.Core.Tests.Export;

public class ExifMetadataWriterTests
{
    [Fact]
    public void BuildExifApp1Segment_ContainsValidHeaders()
    {
        var metadata = new PhotoExportMetadata
        {
            Year = 1982,
            Description = "Vintage holiday photo"
        };

        byte[] app1 = ExifMetadataWriter.BuildExifApp1Segment(metadata);

        app1.Length.ShouldBeGreaterThan(20);
        // APP1 marker
        app1[0].ShouldBe((byte)0xFF);
        app1[1].ShouldBe((byte)0xE1);

        // Length (bytes 2-3)
        int segLen = (app1[2] << 8) | app1[3];
        segLen.ShouldBe(app1.Length - 2);

        // "Exif\0\0"
        Encoding.ASCII.GetString(app1, 4, 6).ShouldBe("Exif\0\0");

        // TIFF header: "II*\0"
        app1[10].ShouldBe((byte)'I');
        app1[11].ShouldBe((byte)'I');
        app1[12].ShouldBe((byte)0x2A);
        app1[13].ShouldBe((byte)0x00);

        // Check that date and description strings are embedded
        string app1String = Encoding.ASCII.GetString(app1);
        app1String.ShouldContain("1982:01:01 00:00:00");
        app1String.ShouldContain("PhotoCropper");
        app1String.ShouldContain("Vintage holiday photo");
    }

    [Fact]
    public void InjectJpegMetadata_InsertsApp1AfterSOI()
    {
        // Minimal fake JPEG (SOI + EOI)
        byte[] fakeJpeg = [0xFF, 0xD8, 0xFF, 0xD9];
        var metadata = new PhotoExportMetadata
        {
            Year = 1970
        };

        byte[] output = ExifMetadataWriter.InjectJpegMetadata(fakeJpeg, metadata);

        output.Length.ShouldBeGreaterThan(fakeJpeg.Length);
        output[0].ShouldBe((byte)0xFF);
        output[1].ShouldBe((byte)0xD8); // SOI
        output[2].ShouldBe((byte)0xFF);
        output[3].ShouldBe((byte)0xE1); // APP1
        output[^2].ShouldBe((byte)0xFF);
        output[^1].ShouldBe((byte)0xD9); // EOI
    }

    [Fact]
    public void BuildPngTextChunk_CreatesValidChunk()
    {
        byte[] chunk = ExifMetadataWriter.BuildPngTextChunk("Creation Time", "1985-06-12");

        chunk.Length.ShouldBeGreaterThan(12);

        // Chunk type "tEXt"
        string chunkType = Encoding.ASCII.GetString(chunk, 4, 4);
        chunkType.ShouldBe("tEXt");

        // Keyword and text in payload
        string payload = Encoding.ASCII.GetString(chunk, 8, chunk.Length - 12);
        payload.ShouldContain("Creation Time");
        payload.ShouldContain("1985-06-12");
    }

    [Fact]
    public void InjectPngMetadata_InjectsTextChunksAfterIhdr()
    {
        // Minimal fake PNG: 8 bytes sig + 25 bytes IHDR + 12 bytes IEND = 45 bytes
        byte[] fakePng = new byte[45];
        // PNG Signature
        byte[] sig = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Buffer.BlockCopy(sig, 0, fakePng, 0, 8);
        // IHDR chunk: 13 bytes data len (0, 0, 0, 13)
        fakePng[8] = 0; fakePng[9] = 0; fakePng[10] = 0; fakePng[11] = 13;
        Encoding.ASCII.GetBytes("IHDR").CopyTo(fakePng, 12);
        // IEND chunk at byte 33
        Encoding.ASCII.GetBytes("IEND").CopyTo(fakePng, 37);

        var metadata = new PhotoExportMetadata
        {
            Year = 1999,
            Description = "Graduation"
        };

        byte[] result = ExifMetadataWriter.InjectPngMetadata(fakePng, metadata);

        result.Length.ShouldBeGreaterThan(fakePng.Length);
        // PNG signature preserved
        result[..8].ShouldBe(sig);
        // Contains metadata keywords
        string resultText = Encoding.ASCII.GetString(result);
        resultText.ShouldContain("Creation Time");
        resultText.ShouldContain("1999-01-01");
        resultText.ShouldContain("Graduation");
    }
}
