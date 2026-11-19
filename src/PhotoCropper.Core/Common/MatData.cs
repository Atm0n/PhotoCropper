using System.Runtime.InteropServices;
using OpenCvSharp;

namespace PhotoCropper.Core.Common;

/// <summary>
/// Copies an image's raw data (all channels, row by row) to or from a flat array. A view into a larger image (not
/// continuous in memory) is copied out first.
/// </summary>
/// <remarks>
/// OpenCvSharp's own calls don't fit these uses: <c>Mat.AsSpan</c> returns an empty span for a view into a larger
/// image instead of failing, and <c>GetArray</c>/<c>SetArray</c> want one element per pixel (<c>Vec3b[]</c> for
/// colour), rejecting flat byte arrays for multi-channel images and views.
/// </remarks>
public static class MatData
{
    public static void CopyDataTo(this Mat mat, byte[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Read(mat, destination.LongLength * sizeof(byte), m => Marshal.Copy(m.Data, destination, 0, destination.Length));
    }

    public static void CopyDataTo(this Mat mat, float[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Read(mat, destination.LongLength * sizeof(float), m => Marshal.Copy(m.Data, destination, 0, destination.Length));
    }

    public static void CopyDataTo(this Mat mat, int[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Read(mat, destination.LongLength * sizeof(int), m => Marshal.Copy(m.Data, destination, 0, destination.Length));
    }

    public static void CopyDataTo(this Mat mat, double[] destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Read(mat, destination.LongLength * sizeof(double), m => Marshal.Copy(m.Data, destination, 0, destination.Length));
    }

    public static void SetData(this Mat mat, byte[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write(mat, source.LongLength * sizeof(byte), m => Marshal.Copy(source, 0, m.Data, source.Length));
    }

    public static void SetData(this Mat mat, float[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write(mat, source.LongLength * sizeof(float), m => Marshal.Copy(source, 0, m.Data, source.Length));
    }

    public static void SetData(this Mat mat, short[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write(mat, source.LongLength * sizeof(short), m => Marshal.Copy(source, 0, m.Data, source.Length));
    }

    public static void SetData(this Mat mat, double[] source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Write(mat, source.LongLength * sizeof(double), m => Marshal.Copy(source, 0, m.Data, source.Length));
    }

    private static long ByteCount(Mat mat) => mat.Total() * mat.ElemSize();

    private static void Read(Mat mat, long bytes, Action<Mat> copy)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (bytes > ByteCount(mat)) throw new ArgumentException($"The array ({bytes} bytes) is larger than the image data ({ByteCount(mat)} bytes).", nameof(mat));
        if (mat.IsContinuous())
        {
            copy(mat);
            return;
        }
        using Mat continuous = mat.Clone();
        copy(continuous);
    }

    private static void Write(Mat mat, long bytes, Action<Mat> copy)
    {
        ArgumentNullException.ThrowIfNull(mat);
        if (bytes > ByteCount(mat)) throw new ArgumentException($"The array ({bytes} bytes) is larger than the image data ({ByteCount(mat)} bytes).", nameof(mat));
        if (!mat.IsContinuous()) throw new ArgumentException("The image must be continuous in memory to be written to.", nameof(mat));
        copy(mat);
    }
}
