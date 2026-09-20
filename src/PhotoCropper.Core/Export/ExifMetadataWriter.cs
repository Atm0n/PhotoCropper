using System.Buffers.Binary;
using System.Text;
using PhotoCropper.Core.Models;

namespace PhotoCropper.Core.Export;

public static class ExifMetadataWriter
{
    // Tag IDs
    private const ushort TagImageDescription = 0x010E;
    private const ushort TagSoftware = 0x0131;
    private const ushort TagDateTime = 0x0132;
    private const ushort TagExifIfdPointer = 0x8769;
    private const ushort TagDateTimeOriginal = 0x9003;
    private const ushort TagDateTimeDigitized = 0x9004;
    private const ushort TagUserComment = 0x9286;

    // TIFF Types
    private const ushort TypeAscii = 2;
    private const ushort TypeLong = 4;
    private const ushort TypeUndefined = 7;

    public static byte[] BuildExifApp1Segment(PhotoExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        string? exifDate = metadata.FormattedExifDate;
        string? description = metadata.Description;
        string software = "PhotoCropper";

        using MemoryStream ms = new();
        using BinaryWriter writer = new(ms, Encoding.ASCII);

        // APP1 Marker
        writer.Write((byte)0xFF);
        writer.Write((byte)0xE1);

        // Placeholder for segment length (2 bytes, big-endian)
        long lengthPos = ms.Position;
        writer.Write((byte)0);
        writer.Write((byte)0);

        // Exif Header: "Exif\0\0"
        writer.Write((byte)'E');
        writer.Write((byte)'x');
        writer.Write((byte)'i');
        writer.Write((byte)'f');
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);

        long tiffStartPos = ms.Position;

        // TIFF Header (Little Endian: "II*\0" + offset to IFD0 = 8)
        writer.Write((byte)'I');
        writer.Write((byte)'I');
        writer.Write((byte)0x2A);
        writer.Write((byte)0x00);
        writer.Write((uint)8); // IFD0 starts at byte 8 from tiffStartPos

        // IFD0 entries count
        var ifd0Entries = new List<(ushort Tag, ushort Type, uint Count, byte[]? ValueBytes)>();
        ifd0Entries.Add((TagSoftware, TypeAscii, (uint)software.Length + 1, Encoding.ASCII.GetBytes(software + "\0")));

        if (!string.IsNullOrEmpty(exifDate))
        {
            ifd0Entries.Add((TagDateTime, TypeAscii, (uint)exifDate.Length + 1, Encoding.ASCII.GetBytes(exifDate + "\0")));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            string descNullTerm = description + "\0";
            ifd0Entries.Add((TagImageDescription, TypeAscii, (uint)descNullTerm.Length, Encoding.ASCII.GetBytes(descNullTerm)));
        }

        // Exif IFD Pointer entry (we will calculate offset after IFD0)
        // Sub-IFD entries count
        var subIfdEntries = new List<(ushort Tag, ushort Type, uint Count, byte[]? ValueBytes)>();
        if (!string.IsNullOrEmpty(exifDate))
        {
            subIfdEntries.Add((TagDateTimeOriginal, TypeAscii, (uint)exifDate.Length + 1, Encoding.ASCII.GetBytes(exifDate + "\0")));
            subIfdEntries.Add((TagDateTimeDigitized, TypeAscii, (uint)exifDate.Length + 1, Encoding.ASCII.GetBytes(exifDate + "\0")));
        }

        if (!string.IsNullOrWhiteSpace(description))
        {
            // UserComment format: 8-byte charset prefix ("ASCII\0\0\0") + comment
            byte[] userCommentData = new byte[8 + Encoding.UTF8.GetByteCount(description)];
            Encoding.ASCII.GetBytes("ASCII\0\0\0", 0, 8, userCommentData, 0);
            Encoding.UTF8.GetBytes(description, 0, description.Length, userCommentData, 8);
            subIfdEntries.Add((TagUserComment, TypeUndefined, (uint)userCommentData.Length, userCommentData));
        }

        bool hasSubIfd = subIfdEntries.Count > 0;
        int ifd0Count = ifd0Entries.Count + (hasSubIfd ? 1 : 0);

        // Calculate offsets:
        // IFD0 header = 2 bytes (count) + ifd0Count * 12 bytes (entries) + 4 bytes (next IFD = 0)
        long ifd0Size = 2 + (ifd0Count * 12) + 4;
        long subIfdOffset = 8 + ifd0Size; // Relative to TIFF start
        long subIfdSize = hasSubIfd ? (2 + (subIfdEntries.Count * 12) + 4) : 0;
        long dataStartOffset = subIfdOffset + subIfdSize; // Relative to TIFF start

        using MemoryStream dataHeap = new();

        // Write IFD0
        writer.Write((ushort)ifd0Count);

        foreach (var entry in ifd0Entries)
        {
            writer.Write(entry.Tag);
            writer.Write(entry.Type);
            writer.Write(entry.Count);

            if (entry.ValueBytes != null && entry.ValueBytes.Length <= 4)
            {
                byte[] padded = new byte[4];
                Buffer.BlockCopy(entry.ValueBytes, 0, padded, 0, entry.ValueBytes.Length);
                writer.Write(padded);
            }
            else if (entry.ValueBytes != null)
            {
                uint valOffset = (uint)(dataStartOffset + dataHeap.Position);
                writer.Write(valOffset);
                dataHeap.Write(entry.ValueBytes, 0, entry.ValueBytes.Length);
            }
            else
            {
                writer.Write((uint)0);
            }
        }

        if (hasSubIfd)
        {
            // Exif IFD Pointer entry
            writer.Write(TagExifIfdPointer);
            writer.Write(TypeLong);
            writer.Write((uint)1);
            writer.Write((uint)subIfdOffset);
        }

        // IFD0 Next IFD pointer (0 = none)
        writer.Write((uint)0);

        // Write Sub-IFD if present
        if (hasSubIfd)
        {
            writer.Write((ushort)subIfdEntries.Count);
            foreach (var entry in subIfdEntries)
            {
                writer.Write(entry.Tag);
                writer.Write(entry.Type);
                writer.Write(entry.Count);

                if (entry.ValueBytes != null && entry.ValueBytes.Length <= 4)
                {
                    byte[] padded = new byte[4];
                    Buffer.BlockCopy(entry.ValueBytes, 0, padded, 0, entry.ValueBytes.Length);
                    writer.Write(padded);
                }
                else if (entry.ValueBytes != null)
                {
                    uint valOffset = (uint)(dataStartOffset + dataHeap.Position);
                    writer.Write(valOffset);
                    dataHeap.Write(entry.ValueBytes, 0, entry.ValueBytes.Length);
                }
                else
                {
                    writer.Write((uint)0);
                }
            }

            // Sub-IFD Next IFD pointer
            writer.Write((uint)0);
        }

        // Append data heap
        dataHeap.Position = 0;
        dataHeap.CopyTo(ms);

        // Update APP1 segment length (Big Endian)
        // Length covers itself (2 bytes) + Exif header (6 bytes) + TIFF data
        ushort totalApp1Length = (ushort)(ms.Length - lengthPos);
        ms.Position = lengthPos;
        writer.Write((byte)((totalApp1Length >> 8) & 0xFF));
        writer.Write((byte)(totalApp1Length & 0xFF));

        return ms.ToArray();
    }

    public static byte[] InjectJpegMetadata(ReadOnlySpan<byte> jpegBytes, PhotoExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (jpegBytes.Length < 4 || jpegBytes[0] != 0xFF || jpegBytes[1] != 0xD8)
        {
            return jpegBytes.ToArray();
        }

        byte[] app1Segment = BuildExifApp1Segment(metadata);

        // Find insertion index: after SOI (0xFF, 0xD8) and after APP0 (JFIF) if present
        int insertPos = 2;
        if (jpegBytes.Length > 4 && jpegBytes[2] == 0xFF && jpegBytes[3] == 0xE0)
        {
            int app0Length = (jpegBytes[4] << 8) | jpegBytes[5];
            insertPos = 4 + app0Length;
        }

        // If an existing APP1 segment is already present at insertPos, check and optionally skip/replace it
        if (insertPos + 4 <= jpegBytes.Length && jpegBytes[insertPos] == 0xFF && jpegBytes[insertPos + 1] == 0xE1)
        {
            int existingApp1Len = (jpegBytes[insertPos + 2] << 8) | jpegBytes[insertPos + 3];
            int removeEnd = insertPos + 2 + existingApp1Len;
            if (removeEnd <= jpegBytes.Length)
            {
                byte[] replaced = new byte[jpegBytes.Length - (removeEnd - insertPos) + app1Segment.Length];
                jpegBytes[..insertPos].CopyTo(replaced);
                Buffer.BlockCopy(app1Segment, 0, replaced, insertPos, app1Segment.Length);
                jpegBytes[removeEnd..].CopyTo(replaced.AsSpan(insertPos + app1Segment.Length));
                return replaced;
            }
        }

        byte[] output = new byte[jpegBytes.Length + app1Segment.Length];
        jpegBytes[..insertPos].CopyTo(output);
        Buffer.BlockCopy(app1Segment, 0, output, insertPos, app1Segment.Length);
        jpegBytes[insertPos..].CopyTo(output.AsSpan(insertPos + app1Segment.Length));
        return output;
    }

    public static byte[] BuildPngTextChunk(string keyword, string text)
    {
        ArgumentNullException.ThrowIfNull(keyword);
        ArgumentNullException.ThrowIfNull(text);

        byte[] keywordBytes = Encoding.ASCII.GetBytes(keyword);
        byte[] textBytes = Encoding.UTF8.GetBytes(text);

        // Chunk data: keyword + 0x00 + text
        int dataLength = keywordBytes.Length + 1 + textBytes.Length;
        byte[] chunkData = new byte[4 + dataLength]; // "tEXt" + data
        chunkData[0] = (byte)'t';
        chunkData[1] = (byte)'E';
        chunkData[2] = (byte)'X';
        chunkData[3] = (byte)'t';
        Buffer.BlockCopy(keywordBytes, 0, chunkData, 4, keywordBytes.Length);
        chunkData[4 + keywordBytes.Length] = 0x00;
        Buffer.BlockCopy(textBytes, 0, chunkData, 4 + keywordBytes.Length + 1, textBytes.Length);

        uint crc = CalculatePngCrc(chunkData);

        byte[] result = new byte[4 + chunkData.Length + 4]; // Length (4) + Type & Data (4+dataLength) + CRC (4)
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(0, 4), dataLength);
        Buffer.BlockCopy(chunkData, 0, result, 4, chunkData.Length);
        BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(4 + chunkData.Length, 4), crc);

        return result;
    }

    public static byte[] InjectPngMetadata(ReadOnlySpan<byte> pngBytes, PhotoExportMetadata metadata)
    {
        ArgumentNullException.ThrowIfNull(metadata);

        if (pngBytes.Length < 33 || pngBytes[0] != 0x89 || pngBytes[1] != 0x50 || pngBytes[2] != 0x4E || pngBytes[3] != 0x47)
        {
            return pngBytes.ToArray();
        }

        List<byte[]> chunksToInject = [];

        string? exifDate = metadata.FormattedDate("yyyy-MM-dd");
        if (!string.IsNullOrEmpty(exifDate))
        {
            chunksToInject.Add(BuildPngTextChunk("Creation Time", exifDate));
        }

        if (!string.IsNullOrWhiteSpace(metadata.Description))
        {
            chunksToInject.Add(BuildPngTextChunk("Description", metadata.Description));
            chunksToInject.Add(BuildPngTextChunk("Comment", metadata.Description));
        }

        chunksToInject.Add(BuildPngTextChunk("Software", "PhotoCropper"));

        if (chunksToInject.Count == 0)
        {
            return pngBytes.ToArray();
        }

        int totalInjectedBytes = chunksToInject.Sum(c => c.Length);

        // Find position right after IHDR chunk
        int ihdrLen = BinaryPrimitives.ReadInt32BigEndian(pngBytes.Slice(8, 4));
        int insertPos = 8 + 12 + ihdrLen; // 8 sig + 12 chunk wrapper + ihdrLen

        byte[] output = new byte[pngBytes.Length + totalInjectedBytes];
        pngBytes[..insertPos].CopyTo(output);

        int currentPos = insertPos;
        foreach (var chunk in chunksToInject)
        {
            Buffer.BlockCopy(chunk, 0, output, currentPos, chunk.Length);
            currentPos += chunk.Length;
        }

        pngBytes[insertPos..].CopyTo(output.AsSpan(currentPos));
        return output;
    }

    private static uint CalculatePngCrc(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? 0xEDB88320 ^ (crc >> 1) : crc >> 1;
            }
        }
        return ~crc;
    }
}
