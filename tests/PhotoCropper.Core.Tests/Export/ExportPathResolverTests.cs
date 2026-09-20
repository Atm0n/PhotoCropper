using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using Shouldly;

namespace PhotoCropper.Core.Tests.Export;

public sealed class ExportPathResolverTests : IDisposable
{
    private readonly string _tempDir;

    public ExportPathResolverTests()
    {
        _tempDir = TestImageFactory.CreateTempDirectory("ExportPathResolverTests");
    }

    [Fact]
    public void ResolveOutputDirectory_WithCustomOutput_ShouldReturnCustomOutput()
    {
        string scanPath = Path.Combine(_tempDir, "sample.jpg");
        string customOutput = Path.Combine(_tempDir, "custom_cropped");

        string resolved = ExportPathResolver.ResolveOutputDirectory(scanPath, customOutputFolder: customOutput);

        resolved.ShouldBe(customOutput);
    }

    [Fact]
    public void ResolveOutputDirectory_WithWorkspaceDirectory_ShouldReturnWorkspaceCropped()
    {
        string workDir = Path.Combine(_tempDir, "ProjectWorkDir");
        Directory.CreateDirectory(workDir);
        string scanPath = Path.Combine(workDir, ProjectWorkspaceService.RawScansFolderName, "scan1.jpg");

        string resolved = ExportPathResolver.ResolveOutputDirectory(scanPath, workDirectory: workDir);

        resolved.ShouldBe(Path.Combine(workDir, ProjectWorkspaceService.CroppedFolderName));
    }

    [Fact]
    public void ResolveOutputDirectory_InRawScansFolder_ShouldReturnSiblingCropped()
    {
        string projectDir = Path.Combine(_tempDir, "MyScanProject");
        string rawDir = Path.Combine(projectDir, ProjectWorkspaceService.RawScansFolderName);
        Directory.CreateDirectory(rawDir);
        string scanPath = Path.Combine(rawDir, "scan01.tif");

        string resolved = ExportPathResolver.ResolveOutputDirectory(scanPath);

        resolved.ShouldBe(Path.Combine(projectDir, ProjectWorkspaceService.CroppedFolderName));
    }

    [Fact]
    public void ResolveOutputDirectory_DefaultFolder_ShouldReturnChildCropped()
    {
        string scanFolder = Path.Combine(_tempDir, "Photos");
        Directory.CreateDirectory(scanFolder);
        string scanPath = Path.Combine(scanFolder, "scan01.tif");

        string resolved = ExportPathResolver.ResolveOutputDirectory(scanPath);

        resolved.ShouldBe(Path.Combine(scanFolder, "cropped"));
    }

    [Fact]
    public void IsScanExported_WhenFilesExist_ShouldReturnTrue()
    {
        string scanPath = Path.Combine(_tempDir, "scan_family.jpg");
        string croppedDir = Path.Combine(_tempDir, "cropped");
        Directory.CreateDirectory(croppedDir);

        // Create exported file matching pattern
        File.WriteAllText(Path.Combine(croppedDir, "scan_family_1.jpg"), "fake photo");

        bool isExported = ExportPathResolver.IsScanExported(scanPath);

        isExported.ShouldBeTrue();
    }

    [Fact]
    public void IsScanExported_WhenNoFilesExist_ShouldReturnFalse()
    {
        string scanPath = Path.Combine(_tempDir, "scan_unexported.jpg");

        bool isExported = ExportPathResolver.IsScanExported(scanPath);

        isExported.ShouldBeFalse();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try
            {
                Directory.Delete(_tempDir, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
