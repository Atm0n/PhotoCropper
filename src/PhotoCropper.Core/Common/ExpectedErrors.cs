using System.Runtime.InteropServices;
using NAPS2.Scan.Exceptions;
using OpenCvSharp;

namespace PhotoCropper.Core.Common;

/// <summary>
/// The exceptions an operation is expected to fail with, as opposed to a bug. Code that carries on after one item
/// fails (a batch over files, a probe of optional scanner drivers) catches only these, so a defect such as a
/// <see cref="NullReferenceException"/> still surfaces instead of being reported as an ordinary failure.
/// </summary>
public static class ExpectedErrors
{
    /// <summary>A file or folder couldn't be read or written: missing, locked, not permitted, disk full or invalid data.</summary>
    public static bool IsFileError(Exception exception) =>
        exception is IOException or InvalidDataException or UnauthorizedAccessException;

    /// <summary>
    /// Loading, processing or saving an image failed: a file error, an image that doesn't decode (reported as
    /// <see cref="InvalidOperationException"/>), or OpenCV failing on it.
    /// </summary>
    public static bool IsImageError(Exception exception) =>
        IsFileError(exception) || exception is InvalidOperationException or OpenCVException or OpenCvSharpException;

    /// <summary>
    /// A scanner, its driver or the NAPS2 worker failed or isn't there: NAPS2's own errors, a driver that timed out,
    /// a COM/Win32 error from WIA or TWAIN, a driver library that is missing or of the wrong bitness (no TWAIN DSM
    /// installed, a 32-bit driver in a 64-bit process), a broken worker pipe, or a network scanner that can't be reached.
    /// </summary>
    public static bool IsScannerError(Exception exception) =>
        exception is ScanDriverException
            or InvalidOperationException
            or TimeoutException
            or IOException
            or ExternalException
            or DllNotFoundException
            or EntryPointNotFoundException
            or BadImageFormatException
            or HttpRequestException
            or Grpc.Core.RpcException;
}
