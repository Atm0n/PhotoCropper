using OpenCvSharp;

namespace PhotoCropper.Core.Common;

/// <summary>Reading scans and bringing images to the 3-channel BGR that detection, extraction and restoration expect.</summary>
public static class ImageIo
{
    /// <summary>Reads an image file as BGR. The file is read even while another program has it open (e.g. still writing it).</summary>
    /// <exception cref="FileNotFoundException">The file doesn't exist.</exception>
    /// <exception cref="InvalidOperationException">The file is empty or isn't an image OpenCV can decode.</exception>
    public static Mat LoadBgr(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Scan file not found.", path);
        }

        var fileInfo = new FileInfo(path);
        if (fileInfo.Length == 0)
        {
            throw new InvalidOperationException($"The file '{path}' is empty (0 bytes).");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var ms = new MemoryStream((int)stream.Length);
        stream.CopyTo(ms);
        byte[] fileBytes = ms.ToArray();
        using Mat rawMat = Cv2.ImDecode(fileBytes, ImreadModes.AnyColor);
        if (rawMat.Empty() || rawMat.Width <= 0 || rawMat.Height <= 0)
        {
            throw new InvalidOperationException($"Failed to decode image from file '{path}'. The image format may be invalid or corrupt.");
        }
        return NormalizeToBgr(rawMat);
    }

    /// <summary>A 3-channel BGR copy of <paramref name="source"/> (grey and BGRA converted); an empty Mat for an empty source.</summary>
    public static Mat NormalizeToBgr(Mat source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.Empty() || source.Width <= 0 || source.Height <= 0)
        {
            return new Mat();
        }

        if (source.Channels() == 3)
        {
            return source.Clone();
        }

        Mat bgr = new();
        if (source.Channels() == 1)
        {
            Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
        }
        else if (source.Channels() == 4)
        {
            Cv2.CvtColor(source, bgr, ColorConversionCodes.BGRA2BGR);
        }
        else
        {
            source.CopyTo(bgr);
        }

        return bgr;
    }
}
