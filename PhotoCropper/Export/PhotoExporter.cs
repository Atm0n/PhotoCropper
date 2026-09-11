using Emgu.CV;
using Emgu.CV.CvEnum;

namespace PhotoCropper.Export;

public static class PhotoExporter
{
    public static (int XDpi, int YDpi) GetDpiFromSource(string sourceFilePath)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);
        if (!File.Exists(sourceFilePath)) return (300, 300);

        try
        {
            byte[] fileBytes = File.ReadAllBytes(sourceFilePath);
            return ExtractDpiFromBytes(fileBytes);
        }
        catch
        {
            return (300, 300);
        }
    }

    public static (int XDpi, int YDpi) ExtractDpiFromBytes(ReadOnlySpan<byte> data)
    {
        // Check JPEG JFIF / Exif headers
        if (data.Length > 20 && data[0] == 0xFF && data[1] == 0xD8)
        {
            int offset = 2;
            while (offset + 4 < data.Length)
            {
                if (data[offset] != 0xFF) break;
                byte marker = data[offset + 1];
                if (marker == 0xDA || marker == 0xD9) break; // SOS or EOI

                int length = (data[offset + 2] << 8) | data[offset + 3];
                if (offset + 2 + length > data.Length) break;

                // APP0 (JFIF)
                if (marker == 0xE0 && length >= 16)
                {
                    if (data[offset + 4] == 'J' && data[offset + 5] == 'F' && data[offset + 6] == 'I' && data[offset + 7] == 'F')
                    {
                        byte units = data[offset + 11];
                        int xDens = (data[offset + 12] << 8) | data[offset + 13];
                        int yDens = (data[offset + 14] << 8) | data[offset + 15];

                        if (units == 1 && xDens > 0 && yDens > 0) // Dots per inch
                        {
                            return (xDens, yDens);
                        }
                        if (units == 2 && xDens > 0 && yDens > 0) // Dots per cm -> convert to DPI
                        {
                            return ((int)Math.Round(xDens * 2.54), (int)Math.Round(yDens * 2.54));
                        }
                    }
                }
                offset += 2 + length;
            }
        }

        // Check PNG pHYs chunk
        if (data.Length > 30 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47)
        {
            int offset = 8;
            while (offset + 12 <= data.Length)
            {
                int chunkLen = (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
                if (chunkLen < 0 || offset + 12 + chunkLen > data.Length) break;

                if (data[offset + 4] == 'p' && data[offset + 5] == 'H' && data[offset + 6] == 'Y' && data[offset + 7] == 's' && chunkLen >= 9)
                {
                    int ppux = (data[offset + 8] << 24) | (data[offset + 9] << 16) | (data[offset + 10] << 8) | data[offset + 11];
                    int ppuy = (data[offset + 12] << 24) | (data[offset + 13] << 16) | (data[offset + 14] << 8) | data[offset + 15];
                    byte unit = data[offset + 16];

                    if (unit == 1 && ppux > 0 && ppuy > 0) // Meters -> convert to DPI
                    {
                        int xDpi = (int)Math.Round(ppux * 0.0254);
                        int yDpi = (int)Math.Round(ppuy * 0.0254);
                        return (xDpi, yDpi);
                    }
                }

                offset += 12 + chunkLen;
            }
        }

        return (300, 300);
    }

    public static void SavePhotos(
        IReadOnlyList<Mat> photos, 
        string originalFilePath, 
        string? customOutputFolder = null, 
        string format = "JPEG", 
        int jpegQuality = 90,
        Action<int, int>? progressCallback = null)
    {
        ArgumentNullException.ThrowIfNull(photos);
        ArgumentNullException.ThrowIfNull(originalFilePath);
        ArgumentNullException.ThrowIfNull(format);

        string outputFolder;
        if (!string.IsNullOrEmpty(customOutputFolder))
        {
            outputFolder = customOutputFolder;
        }
        else
        {
            string? directory = Path.GetDirectoryName(originalFilePath);
            if (string.IsNullOrEmpty(directory)) return;
            outputFolder = Path.Combine(directory, "cropped");
        }

        Directory.CreateDirectory(outputFolder);

        string baseFileName = Path.GetFileNameWithoutExtension(originalFilePath);
        string extension = string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase) ? ".png" : ".jpg";
        var (xDpi, yDpi) = GetDpiFromSource(originalFilePath);

        int saveCounter = 1;
        for (int i = 0; i < photos.Count; i++)
        {
            if (photos[i].IsEmpty) continue;
            string fileName = Path.Combine(outputFolder, $"{baseFileName}_{saveCounter++}{extension}");
            
            if (string.Equals(format, "PNG", StringComparison.OrdinalIgnoreCase))
            {
                photos[i].Save(fileName);
                EmbedPngDpi(fileName, xDpi, yDpi);
            }
            else
            {
                KeyValuePair<ImwriteFlags, int>[] parameters = [
                    new KeyValuePair<ImwriteFlags, int>(ImwriteFlags.JpegQuality, jpegQuality)
                ];
                CvInvoke.Imwrite(fileName, photos[i], parameters);
                EmbedJpegDpi(fileName, xDpi, yDpi);
            }

            progressCallback?.Invoke(i + 1, photos.Count);
        }
    }

    public static void EmbedJpegDpi(string filePath, int xDpi, int yDpi)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) return;

        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 4 || bytes[0] != 0xFF || bytes[1] != 0xD8) return;

        // If JFIF marker already exists at byte index 2
        if (bytes.Length >= 18 && bytes[2] == 0xFF && bytes[3] == 0xE0 &&
            bytes[6] == 'J' && bytes[7] == 'F' && bytes[8] == 'I' && bytes[9] == 'F')
        {
            bytes[13] = 1; // dots per inch
            bytes[14] = (byte)((xDpi >> 8) & 0xFF);
            bytes[15] = (byte)(xDpi & 0xFF);
            bytes[16] = (byte)((yDpi >> 8) & 0xFF);
            bytes[17] = (byte)(yDpi & 0xFF);
            File.WriteAllBytes(filePath, bytes);
            return;
        }

        // Insert new JFIF APP0 segment right after SOI (FF D8)
        byte[] jfifHeader = [
            0xFF, 0xE0,             // APP0
            0x00, 0x10,             // Length: 16 bytes
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, // Identifier
            0x01, 0x01,             // Version 1.1
            0x01,                   // Units: 1 = dots per inch
            (byte)((xDpi >> 8) & 0xFF), (byte)(xDpi & 0xFF), // X density
            (byte)((yDpi >> 8) & 0xFF), (byte)(yDpi & 0xFF), // Y density
            0x00, 0x00              // Thumbnail dimensions
        ];

        byte[] newBytes = new byte[bytes.Length + jfifHeader.Length];
        newBytes[0] = bytes[0];
        newBytes[1] = bytes[1];
        Buffer.BlockCopy(jfifHeader, 0, newBytes, 2, jfifHeader.Length);
        Buffer.BlockCopy(bytes, 2, newBytes, 2 + jfifHeader.Length, bytes.Length - 2);

        File.WriteAllBytes(filePath, newBytes);
    }

    public static void EmbedPngDpi(string filePath, int xDpi, int yDpi)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        if (!File.Exists(filePath)) return;

        byte[] bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < 33 || bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47) return;

        // Convert DPI to pixels per meter
        int ppux = (int)Math.Round(xDpi / 0.0254);
        int ppuy = (int)Math.Round(yDpi / 0.0254);

        // Find IHDR chunk length to insert pHYs right after IHDR
        int ihdrLen = (bytes[8] << 24) | (bytes[9] << 16) | (bytes[10] << 8) | bytes[11];
        int insertPos = 8 + 12 + ihdrLen; // 8 (signature) + 12 (IHDR len, type, crc) + ihdrLen

        // 9 data bytes: 4 ppux, 4 ppuy, 1 unit (1 = meters)
        byte[] chunkData = [
            (byte)'p', (byte)'H', (byte)'Y', (byte)'s',
            (byte)((ppux >> 24) & 0xFF), (byte)((ppux >> 16) & 0xFF), (byte)((ppux >> 8) & 0xFF), (byte)(ppux & 0xFF),
            (byte)((ppuy >> 24) & 0xFF), (byte)((ppuy >> 16) & 0xFF), (byte)((ppuy >> 8) & 0xFF), (byte)(ppuy & 0xFF),
            0x01
        ];

        uint crc = CalculatePngCrc(chunkData);

        byte[] physChunk = [
            0x00, 0x00, 0x00, 0x09, // Data length = 9
            chunkData[0], chunkData[1], chunkData[2], chunkData[3],
            chunkData[4], chunkData[5], chunkData[6], chunkData[7],
            chunkData[8], chunkData[9], chunkData[10], chunkData[11],
            chunkData[12],
            (byte)((crc >> 24) & 0xFF), (byte)((crc >> 16) & 0xFF), (byte)((crc >> 8) & 0xFF), (byte)(crc & 0xFF)
        ];

        byte[] newBytes = new byte[bytes.Length + physChunk.Length];
        Buffer.BlockCopy(bytes, 0, newBytes, 0, insertPos);
        Buffer.BlockCopy(physChunk, 0, newBytes, insertPos, physChunk.Length);
        Buffer.BlockCopy(bytes, insertPos, newBytes, insertPos + physChunk.Length, bytes.Length - insertPos);

        File.WriteAllBytes(filePath, newBytes);
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
