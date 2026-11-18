using Emgu.CV;
using Emgu.CV.CvEnum;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Export;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Workspace;

/// <summary>
/// Exporting through the folder pipeline (engine raw copy → Cropped PNG → restore → Final) must give the same
/// photos as the engine's own restored output, so moving project exports onto the pipeline changes nothing visible.
/// </summary>
public sealed class PipelineEquivalenceTests : IDisposable
{
    private readonly string _workDir = TestImageFactory.CreateTempDirectory("PipelineEquivalence");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PipelineFinal_ShouldMatchTheEnginesRestoredPhoto(bool restoreColors, bool removeDust)
    {
        ProjectWorkspaceService.InitializeWorkspace(_workDir);
        string scanPath = Path.Combine(ProjectWorkspaceService.GetRawScansDirectory(_workDir), "scan_0001.png");
        TestImageFactory.CreateStandardTwoPhotoScan(scanPath);

        using var engine = new PhotoCropperEngine(scanPath)
        {
            RestoreVintageColors = restoreColors,
            RemoveDustAndScratches = removeDust
        };
        engine.DetectPhotos();
        engine.DetectedPhotos.Count.ShouldBeGreaterThan(0);

        var session = WorkspaceSessionState.CreateNew();
        var scan = new WorkspaceScanEntry { RelativePath = Path.GetRelativePath(_workDir, scanPath) };
        session.Scans.Add(scan);

        var inputs = engine.AcceptedCandidates.Select((c, i) => new CroppedPhotoInput(
            engine.RawDetectedPhotos[i],
            new WorkspaceCropData { CenterX = c.Rotated.Center.X, CenterY = c.Rotated.Center.Y, Width = c.Rotated.Size.Width, Height = c.Rotated.Size.Height, Angle = c.Rotated.Angle }))
            .ToList();
        var restoration = new RestorationSettings { RestoreColors = restoreColors, RemoveDust = removeDust };
        var photos = PhotoPipelineService.SaveCroppedPhotos(_workDir, session, scan, inputs, restoration);

        for (int i = 0; i < photos.Count; i++)
        {
            PhotoPipelineService.RenderFinal(_workDir, session, photos[i], new FinalRenderOptions { Format = AppConstants.FormatPng });

            using var final = RestoreStage.ReadImage(photos[i].Final!.GetFullPath(_workDir));
            var expected = engine.DetectedPhotos[i];
            final.Size.ShouldBe(expected.Size);
            final.NumberOfChannels.ShouldBe(expected.NumberOfChannels);
            CvInvoke.Norm(final, expected, NormType.L1).ShouldBe(0, $"photo {i + 1} differs from the engine's output");
        }
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_workDir)) Directory.Delete(_workDir, true);
        }
        catch (IOException)
        {
        }
    }
}
