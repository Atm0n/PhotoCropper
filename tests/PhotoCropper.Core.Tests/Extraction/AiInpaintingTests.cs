using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Extraction;
using PhotoCropper.Core.Workspace;
using PhotoCropper.TestHelpers;
using System.Net;
using System.Security.Cryptography;

namespace PhotoCropper.Core.Tests.Extraction;

[CollectionDefinition(nameof(AiModelSerialGroup), DisableParallelization = true)]
[System.Diagnostics.CodeAnalysis.SuppressMessage("Maintainability", "CA1515", Justification = "xUnit collection definitions must be public")]
public sealed class AiModelSerialGroup;

/// <summary>Stands in for LaMa: paints the hole a fixed colour and counts how often it runs.</summary>
internal sealed class FakeInpaintingModel : IInpaintingModel
{
    public int Calls { get; private set; }

    public int InputSize => 512;

    public Mat Inpaint(Mat bgr, Mat mask)
    {
        bgr.Size().ShouldBe(new Size(512, 512));
        mask.Size().ShouldBe(new Size(512, 512));
        Calls++;
        Mat result = bgr.Clone();
        result.SetTo(new Scalar(0, 200, 0), mask);
        return result;
    }
}

[Collection(nameof(AiModelSerialGroup))]
public sealed class AiInpaintingTests : IDisposable
{
    private readonly string _modelDir = TestImageFactory.CreateTempDirectory("AiInpaintingTests");
    private readonly string _originalModelDir = AiModelStore.ModelDirectory;
    private readonly FakeInpaintingModel _fake = new();

    public AiInpaintingTests()
    {
        AiModelStore.ModelDirectory = _modelDir; // no real model here
        AiInpainting.ClearCache();
    }

    private static Mat Grey(int width = 400, int height = 300, int channels = 3)
    {
        var mat = new Mat(height, width, MatType.CV_8UC(channels));
        mat.SetTo(channels == 4 ? new Scalar(128, 128, 128, 200) : new Scalar(128, 128, 128, 0));
        return mat;
    }

    private static RepairStroke Stroke(RepairMethod method, float radius, params float[] points)
    {
        var stroke = new RepairStroke { Radius = radius, Method = method };
        foreach (float p in points) stroke.Points.Add(p);
        return stroke;
    }

    private static Scalar MeanAt(Mat image, Rect area)
    {
        using var region = new Mat(image, area);
        return Cv2.Mean(region);
    }

    [Fact]
    public void AiStroke_ShouldReplaceOnlyThePaintedArea()
    {
        AiInpainting.ModelOverride = _fake;
        using var photo = Grey();

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [Stroke(RepairMethod.Ai, 0.05f, 0.5f, 0.5f)]);

        _fake.Calls.ShouldBe(1);
        MeanAt(repaired, new Rect(195, 145, 10, 10)).Val1.ShouldBeGreaterThan(190, "the painted spot comes from the model");
        MeanAt(repaired, new Rect(10, 10, 60, 60)).Val1.ShouldBe(128, 1, "far away nothing changes");
        MeanAt(repaired, new Rect(195, 145, 10, 10)).Val0.ShouldBeLessThan(15);
    }

    [Fact]
    public void AiStroke_ShouldKeepTransparencyAndWorkOnGreyscale()
    {
        AiInpainting.ModelOverride = _fake;
        using var withAlpha = Grey(channels: 4);
        using var grey = Grey(channels: 1);

        using var repairedAlpha = PhotoRepairService.ApplyRepairs(withAlpha, [Stroke(RepairMethod.Ai, 0.05f, 0.5f, 0.5f)]);
        using var repairedGrey = PhotoRepairService.ApplyRepairs(grey, [Stroke(RepairMethod.Ai, 0.05f, 0.5f, 0.5f)]);

        repairedAlpha.Channels().ShouldBe(4);
        MeanAt(repairedAlpha, new Rect(0, 0, 400, 300)).Val3.ShouldBe(200);
        repairedGrey.Channels().ShouldBe(1);
        Math.Abs(MeanAt(repairedGrey, new Rect(195, 145, 10, 10)).Val0 - 128).ShouldBeGreaterThan(5);
    }

    [Fact]
    public void AiStroke_WithoutTheModel_ShouldFallBackToQuickFill()
    {
        AiInpainting.ModelOverride = null;
        AiInpainting.IsAvailable.ShouldBeFalse();
        using var photo = Grey();
        Cv2.Line(photo, new Point(100, 150), new Point(300, 150), new Scalar(20, 20, 20), 3);

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [Stroke(RepairMethod.Ai, 0.015f, 0.25f, 0.5f, 0.75f, 0.5f)]);

        MeanAt(repaired, new Rect(120, 149, 160, 3)).Val0.ShouldBe(128, 6);
    }

    [Fact]
    public void RepeatedRenders_ShouldReuseTheModelsResult()
    {
        AiInpainting.ModelOverride = _fake;
        using var photo = Grey();
        RepairStroke[] strokes = [Stroke(RepairMethod.Ai, 0.05f, 0.5f, 0.5f)];

        using (PhotoRepairService.ApplyRepairs(photo, strokes)) { }
        using (PhotoRepairService.ApplyRepairs(photo, strokes)) { }

        _fake.Calls.ShouldBe(1);
    }

    [Fact]
    public void MixedStrokes_ShouldUseTheModelOnlyForAiStrokes()
    {
        AiInpainting.ModelOverride = _fake;
        using var photo = Grey();

        using var repaired = PhotoRepairService.ApplyRepairs(photo,
            [Stroke(RepairMethod.Quick, 0.02f, 0.1f, 0.1f), Stroke(RepairMethod.Ai, 0.02f, 0.9f, 0.9f)]);

        MeanAt(repaired, new Rect(38, 28, 4, 4)).Val1.ShouldBe(128, 3, "quick fill blends the grey around it");
        MeanAt(repaired, new Rect(358, 268, 4, 4)).Val1.ShouldBeGreaterThan(190, "AI fill comes from the model");
    }

    [Fact]
    public void FindAreas_ShouldGroupNearbyDamageAndKeepDistantDamageApart()
    {
        using var mask = new Mat(1000, 1000, MatType.CV_8UC1, Scalar.All(0));
        Cv2.Circle(mask, new Point(100, 100), 10, new Scalar(255), -1);
        Cv2.Circle(mask, new Point(140, 100), 10, new Scalar(255), -1); // close to the first
        Cv2.Circle(mask, new Point(900, 900), 10, new Scalar(255), -1); // far away

        var areas = AiInpainting.FindAreas(mask, new Size(1000, 1000));

        areas.Count.ShouldBe(2);
        areas.ShouldAllBe(a => a.Width == a.Height && a.Width >= 250, "squares with surroundings for context");
        areas.ShouldContain(a => a.Contains(new Rect(90, 90, 60, 20)));
        areas.ShouldContain(a => a.Contains(new Rect(890, 890, 20, 20)));
    }

    [Fact]
    public void Fingerprint_ShouldChangeWithTheMethodButNotForExistingQuickStrokes()
    {
        var photo = new ProjectPhoto { Id = "p", RepairImageWidth = 100, RepairImageHeight = 100 };
        photo.Repairs.Add(Stroke(RepairMethod.Quick, 0.01f, 0.5f, 0.5f));
        string quick = photo.RepairsFingerprint;

        photo.Repairs[0] = Stroke(RepairMethod.Ai, 0.01f, 0.5f, 0.5f);

        photo.RepairsFingerprint.ShouldNotBe(quick);
    }

    [Fact]
    public void Method_ShouldSurviveSavingAndLoading()
    {
        var stroke = Stroke(RepairMethod.Ai, 0.01f, 0.5f, 0.5f);

        string json = System.Text.Json.JsonSerializer.Serialize(stroke);
        var loaded = System.Text.Json.JsonSerializer.Deserialize<RepairStroke>(json)!;

        json.ShouldContain("\"Ai\"");
        loaded.Method.ShouldBe(RepairMethod.Ai);
        System.Text.Json.JsonSerializer.Deserialize<RepairStroke>("{\"Radius\":0.01}")!.Method.ShouldBe(RepairMethod.Quick, "older projects");
    }

    private sealed class BytesHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }

    [Fact]
    public async Task Download_ShouldInstallAVerifiedFile()
    {
        byte[] body = new byte[300_000];
        RandomNumberGenerator.Fill(body);
        string hash = Convert.ToHexStringLower(SHA256.HashData(body));
        string target = Path.Combine(_modelDir, "model.onnx");
        var reports = new List<double>();
        using var handler = new BytesHandler(body);
        using var client = new HttpClient(handler, disposeHandler: false);

        await AiModelStore.DownloadAsync(client, new Uri("https://example.invalid/model.onnx"), target, hash, body.Length,
            new SyncProgress(reports.Add), TestContext.Current.CancellationToken);

        (await File.ReadAllBytesAsync(target, TestContext.Current.CancellationToken)).ShouldBe(body);
        reports.Last().ShouldBe(1.0);
        File.Exists(target + ".part").ShouldBeFalse();
    }

    [Fact]
    public async Task Download_WithTheWrongChecksum_ShouldInstallNothing()
    {
        byte[] body = new byte[1000];
        string target = Path.Combine(_modelDir, "model.onnx");
        using var handler = new BytesHandler(body);
        using var client = new HttpClient(handler, disposeHandler: false);

        await Should.ThrowAsync<InvalidDataException>(() => AiModelStore.DownloadAsync(client, new Uri("https://example.invalid/m"), target,
            new string('0', 64), body.Length, null, TestContext.Current.CancellationToken));

        File.Exists(target).ShouldBeFalse();
        File.Exists(target + ".part").ShouldBeFalse();
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>
    /// Runs the real LaMa model when PHOTOCROPPER_LAMA_DIR points to a folder holding it (it is too large for CI):
    /// a hole in a smooth gradient should be filled with the gradient.
    /// </summary>
    [Fact]
    public void RealModel_ShouldContinueTheSurroundings()
    {
        string? dir = Environment.GetEnvironmentVariable("PHOTOCROPPER_LAMA_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(dir) || !File.Exists(Path.Combine(dir, AiModelStore.LamaFileName)),
            "Set PHOTOCROPPER_LAMA_DIR to a folder with lama_fp32.onnx to run this test.");
        AiModelStore.ModelDirectory = dir!;
        AiInpainting.ModelOverride = null;

        using var photo = new Mat(600, 800, MatType.CV_8UC3);
        byte[] pixels = new byte[600 * 800 * 3];
        for (int y = 0; y < 600; y++)
            for (int x = 0; x < 800; x++)
            {
                int i = (y * 800 + x) * 3;
                pixels[i] = (byte)(60 + x * 120 / 800);
                pixels[i + 1] = (byte)(90 + y * 100 / 600);
                pixels[i + 2] = 150;
            }
        photo.SetData(pixels);
        using var expected = photo.Clone();
        Cv2.Circle(photo, new Point(400, 300), 50, new Scalar(0, 0, 255), -1); // big red stain

        using var repaired = PhotoRepairService.ApplyRepairs(photo, [Stroke(RepairMethod.Ai, 0.07f, 0.5f, 0.5f)]);

        using var diff = new Mat();
        using var repairedCentre = new Mat(repaired, new Rect(360, 260, 80, 80));
        using var expectedCentre = new Mat(expected, new Rect(360, 260, 80, 80));
        Cv2.Absdiff(repairedCentre, expectedCentre, diff);
        var error = Cv2.Mean(diff);
        ((error.Val0 + error.Val1 + error.Val2) / 3).ShouldBeLessThan(12);
    }

    public void Dispose()
    {
        AiInpainting.ModelOverride = null;
        AiInpainting.ClearCache();
        AiModelStore.ModelDirectory = _originalModelDir;
        try
        {
            if (Directory.Exists(_modelDir)) Directory.Delete(_modelDir, true);
        }
        catch (IOException)
        {
        }
    }
}
