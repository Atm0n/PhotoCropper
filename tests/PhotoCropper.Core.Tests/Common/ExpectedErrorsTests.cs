using System.ComponentModel;
using NAPS2.Scan.Exceptions;
using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Scanning;

namespace PhotoCropper.Core.Tests.Common;

/// <summary>
/// Batches and scanner probes carry on after the failures these name, so a bug must never count as one:
/// it would be reported as an ordinary failed file instead of surfacing.
/// </summary>
public sealed class ExpectedErrorsTests
{
    public static TheoryData<Exception> Bugs() => new()
    {
        // Thrown for real: the runtime reserves these types, so they aren't constructed by hand.
        Should.Throw<NullReferenceException>(() => _ = NullString()!.Length),
        Should.Throw<IndexOutOfRangeException>(() => _ = Array.Empty<int>()[Index()]),
        new ArgumentNullException("value"),
        new ArgumentOutOfRangeException("index"),
        new ArgumentException("bad argument"),
        new InvalidCastException(),
        new NotImplementedException(),
        new KeyNotFoundException(),
        new FormatException(),
    };

    private static string? NullString() => null;

    private static int Index() => 0;

    public static TheoryData<Exception> FileErrors() => new()
    {
        new IOException("disk full"),
        new FileNotFoundException("missing"),
        new DirectoryNotFoundException("gone"),
        new InvalidDataException("corrupt"),
        new PartialExportException("half written", new IOException()),
        new UnauthorizedAccessException("read-only"),
    };

    public static TheoryData<Exception> ImageOnlyErrors() => new()
    {
        new InvalidOperationException("Failed to decode image"),
        new OpenCVException(ErrorCode.StsError, "func", "err", "file", 1),
        new OpenCvSharpException("wrapper"),
    };

    public static TheoryData<Exception> ScannerErrors() => new()
    {
        new DeviceOfflineException(),
        new ScanDriverUnknownException(new InvalidOperationException()),
        new ScannerNotFoundException("not found"),
        new TimeoutException(),
        new IOException("worker pipe broken"),
        new Win32Exception(5), // an ExternalException, like the COMException WIA throws (reserved, so not built here)
        new HttpRequestException("eSCL unreachable"),
        new Grpc.Core.RpcException(Grpc.Core.Status.DefaultCancelled),
    };

    [Theory]
    [MemberData(nameof(Bugs))]
    public void BugsShouldNotCountAsAnyExpectedError(Exception bug)
    {
        ExpectedErrors.IsFileError(bug).ShouldBeFalse();
        ExpectedErrors.IsImageError(bug).ShouldBeFalse();
        ExpectedErrors.IsScannerError(bug).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(FileErrors))]
    public void FileErrorsShouldCountAsFileAndImageErrors(Exception error)
    {
        ExpectedErrors.IsFileError(error).ShouldBeTrue();
        ExpectedErrors.IsImageError(error).ShouldBeTrue();
    }

    [Theory]
    [MemberData(nameof(ImageOnlyErrors))]
    public void DecodeAndOpenCvErrorsShouldCountAsImageErrorsOnly(Exception error)
    {
        ExpectedErrors.IsImageError(error).ShouldBeTrue();
        ExpectedErrors.IsFileError(error).ShouldBeFalse();
    }

    [Theory]
    [MemberData(nameof(ScannerErrors))]
    public void DriverWorkerAndNetworkFailuresShouldCountAsScannerErrors(Exception error)
    {
        ExpectedErrors.IsScannerError(error).ShouldBeTrue();
    }

    [Fact]
    public void CancellationShouldNotCountAsAScannerError()
    {
        // Callers rethrow cancellation before these filters run; being matched here would swallow it if they didn't.
        ExpectedErrors.IsScannerError(new OperationCanceledException()).ShouldBeFalse();
    }
}
