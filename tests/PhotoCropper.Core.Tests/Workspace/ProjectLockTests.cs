using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Workspace;

public sealed class ProjectLockTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("ProjectLockTests");

    public void Dispose()
    {
        try { Directory.Delete(_workDir, true); } catch (IOException) { }
    }

    [Fact]
    public void TryAcquire_ShouldRefuseAProjectThatIsAlreadyOpen()
    {
        using (var first = ProjectLock.TryAcquire(_workDir))
        {
            first.ShouldNotBeNull();
            ProjectLock.TryAcquire(_workDir).ShouldBeNull();
        }

        // Closing the project releases it (and removes the lock file).
        using var again = ProjectLock.TryAcquire(_workDir);
        again.ShouldNotBeNull();
    }

    [Fact]
    public void Dispose_ShouldLeaveNoLockFileBehind()
    {
        ProjectLock.TryAcquire(_workDir)!.Dispose();
        File.Exists(Path.Combine(_workDir, ProjectLock.FileName)).ShouldBeFalse();
    }
}
