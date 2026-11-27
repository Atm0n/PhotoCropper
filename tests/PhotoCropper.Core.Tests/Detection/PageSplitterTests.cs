using OpenCvSharp;
using PhotoCropper.Core.Common;
using PhotoCropper.Core.Detection;
using PhotoCropper.Core.Models;
using PhotoCropper.TestHelpers;

namespace PhotoCropper.Core.Tests.Detection;

/// <summary>
/// The page step on its own: given the box detection takes for a whole light album page (what happens on real cream
/// pages, whose faded photos are close to the paper's colour), it finds the photos on the page; other boxes stay.
/// </summary>
public sealed class PageSplitterTests : IDisposable
{
    private readonly string _dir = TestImageFactory.CreateTempDirectory("PageSplitterTests");

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Your settings' tight tolerance and 5% minimum, and the defaults.</summary>
    public static TheoryData<double, double> Settings => new() { { 4, 0.05 }, { 30, 0.01 } };

    private static CropCandidate Box(Rect r) => new(
        [new System.Drawing.Point(r.X, r.Y), new System.Drawing.Point(r.Right, r.Y), new System.Drawing.Point(r.Right, r.Bottom), new System.Drawing.Point(r.X, r.Bottom)],
        CvInterop.ToRectangle(r),
        r.Width * r.Height,
        new RotatedBox(new System.Drawing.PointF(r.X + r.Width / 2f, r.Y + r.Height / 2f), new System.Drawing.SizeF(r.Width, r.Height), 0),
        r.Width * r.Height,
        1,
        1);

    [Theory]
    [MemberData(nameof(Settings))]
    public void SplitAWholeLightAlbumPageShouldGiveThePhotosOnIt(double tolerance, double minArea)
    {
        string path = Path.Combine(_dir, "light_page.png");
        TestImageFactory.CreateLightAlbumPageScan(path);
        using Mat scan = Cv2.ImRead(path);

        var result = PageSplitter.Split(scan, [Box(TestImageFactory.LightAlbumPage)], new DetectionOptions { BackgroundTolerance = tolerance, MinAreaFactor = minArea });

        result.Count.ShouldBe(TestImageFactory.LightAlbumPagePhotos.Length);
        foreach (var photo in TestImageFactory.LightAlbumPagePhotos)
        {
            result.ShouldContain(c =>
                Math.Abs(c.Rotated.Center.X - (photo.X + photo.Width / 2f)) < 15 && Math.Abs(c.Rotated.Center.Y - (photo.Y + photo.Height / 2f)) < 15 &&
                Math.Abs(Math.Max(c.Rotated.Size.Width, c.Rotated.Size.Height) - Math.Max(photo.Width, photo.Height)) < 30 &&
                Math.Abs(Math.Min(c.Rotated.Size.Width, c.Rotated.Size.Height) - Math.Min(photo.Width, photo.Height)) < 30);
        }
    }

    [Fact]
    public void SplitALargeSinglePhotoShouldLeaveItWhole()
    {
        // One big photo with structure all over and no paper in it: nothing to look inside.
        using Mat scan = new(2000, 1600, MatType.CV_8UC3, Scalar.All(244));
        var photo = new Rect(100, 100, 1400, 1800);
        using (Mat inside = new(scan, photo))
        {
            Cv2.Randn(inside, new Scalar(110, 120, 130), new Scalar(40, 40, 40));
        }

        var result = PageSplitter.Split(scan, [Box(photo)], new DetectionOptions { BackgroundTolerance = 4, MinAreaFactor = 0.05 });

        result.ShouldHaveSingleItem().Rotated.Size.Width.ShouldBe(photo.Width);
    }

    [Fact]
    public void LightPageColourACreamPageOnTheBedShouldGiveItsPaper()
    {
        string path = Path.Combine(_dir, "light_page_colour.png");
        TestImageFactory.CreateLightAlbumPageScan(path);
        using Mat scan = Cv2.ImRead(path);
        using Mat hsv = new();
        Cv2.CvtColor(scan, hsv, ColorConversionCodes.BGR2HSV);

        var paper = BackgroundAnalyzer.LightPageColour(hsv, BackgroundAnalyzer.SampleBackgroundColor(hsv));

        // The cream paper (BGR 196, 214, 236): light, and clearly more saturated than the white bed.
        var p = paper.ShouldNotBeNull();
        p.Val2.ShouldBeInRange(225, 245);
        p.Val1.ShouldBeInRange(30, 60);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LightPageColourNotALightPageShouldGiveNothing(bool loosePrints)
    {
        // Loose prints on the bed, and a dark grey page: neither is a light page to use as the background.
        string path = Path.Combine(_dir, loosePrints ? "not_light_prints.png" : "not_light_dark_page.png");
        if (loosePrints) TestImageFactory.CreatePrintsReachingTheEdgeScan(path);
        else TestImageFactory.CreateAlbumPageOnBedScan(path);
        using Mat scan = Cv2.ImRead(path);
        using Mat hsv = new();
        Cv2.CvtColor(scan, hsv, ColorConversionCodes.BGR2HSV);

        BackgroundAnalyzer.LightPageColour(hsv, BackgroundAnalyzer.SampleBackgroundColor(hsv)).ShouldBeNull();
    }

    [Fact]
    public void SplitABoxSmallerThanAPageShouldBeLeftAsItIs()
    {
        string path = Path.Combine(_dir, "light_page_small.png");
        TestImageFactory.CreateLightAlbumPageScan(path);
        using Mat scan = Cv2.ImRead(path);
        var photo = TestImageFactory.LightAlbumPagePhotos[0];

        var result = PageSplitter.Split(scan, [Box(photo)], new DetectionOptions());

        result.ShouldHaveSingleItem().Rect.ShouldBe(CvInterop.ToRectangle(photo));
    }
}
