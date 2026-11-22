using OpenCvSharp;
using PhotoCropper.Core.Detection;

namespace PhotoCropper.Core.Tests.Detection;

/// <summary>
/// Photos found on a light page by texture and seams: prints with detail on smooth cream paper, including prints
/// stuck edge to edge, which come out as one area and must be divided along the straight seam between them.
/// </summary>
public sealed class TextureSeamSplitterTests
{
    private static readonly Scalar CreamBgr = new(196, 214, 236);

    private static Scalar PaperHsv()
    {
        using Mat one = new(1, 1, MatType.CV_8UC3, CreamBgr);
        using Mat hsv = new();
        Cv2.CvtColor(one, hsv, ColorConversionCodes.BGR2HSV);
        var p = hsv.At<Vec3b>(0, 0);
        return new Scalar(p.Item0, p.Item1, p.Item2);
    }

    /// <summary>A cream page with the given prints on it, each a picture with detail around its own brightness.</summary>
    private static Mat Page(params (Rect Rect, double Brightness)[] prints)
    {
        var page = new Mat(1400, 1000, MatType.CV_8UC3, CreamBgr);
        using (Mat noise = new(page.Size(), MatType.CV_8UC3))
        {
            Cv2.Randn(noise, Scalar.All(0), Scalar.All(2));
            Cv2.Add(page, noise, page);
        }
        foreach (var (rect, brightness) in prints)
        {
            using Mat inside = new(page, rect);
            inside.SetTo(new Scalar(brightness * 0.8, brightness * 0.9, brightness));
            // Blobs of light and shade, as a picture has: detail in every tile, but no long straight lines. Spread
            // evenly by the R2 low-discrepancy sequence, so the picture is the same on every run.
            for (int i = 1; i <= 120; i++)
            {
                var centre = new Point((int)(i * 0.7548776662 % 1 * rect.Width), (int)(i * 0.5698402910 % 1 * rect.Height));
                double shade = brightness + (i * 37 % 100) - 50;
                Cv2.Circle(inside, centre, 8 + (i * 13 % 22), new Scalar(shade * 0.8, shade * 0.9, shade), -1);
            }
        }
        return page;
    }

    private static void ShouldMatch(RotatedRect piece, Rect print)
    {
        piece.Center.X.ShouldBeInRange(print.X + print.Width / 2f - 30, print.X + print.Width / 2f + 30);
        piece.Center.Y.ShouldBeInRange(print.Y + print.Height / 2f - 30, print.Y + print.Height / 2f + 30);
        Math.Max(piece.Size.Width, piece.Size.Height).ShouldBeInRange(Math.Max(print.Width, print.Height) - 60, Math.Max(print.Width, print.Height) + 40);
        Math.Min(piece.Size.Width, piece.Size.Height).ShouldBeInRange(Math.Min(print.Width, print.Height) - 60, Math.Min(print.Width, print.Height) + 40);
    }

    [Fact]
    public void FindPieces_TwoPrintsStuckEdgeToEdge_ShouldDivideThemAtTheSeam()
    {
        // Together the two make one print-shaped area (800 x 1120): only the straight seam tells them apart.
        var top = new Rect(100, 100, 800, 560);
        var bottom = new Rect(100, 660, 800, 560);
        using Mat page = Page((top, 170), (bottom, 90));

        var pieces = TextureSeamSplitter.FindPieces(page, PaperHsv(), minArea: 0.05 * page.Width * page.Height);

        pieces.Count.ShouldBe(2);
        ShouldMatch(pieces.MinBy(p => p.Center.Y), top);
        ShouldMatch(pieces.MaxBy(p => p.Center.Y), bottom);
    }

    [Fact]
    public void FindPieces_OnePrint_ShouldGiveItWhole()
    {
        var print = new Rect(150, 200, 600, 840);
        using Mat page = Page((print, 140));

        var pieces = TextureSeamSplitter.FindPieces(page, PaperHsv(), minArea: 0.05 * page.Width * page.Height);

        ShouldMatch(pieces.ShouldHaveSingleItem(), print);
    }

    [Fact]
    public void FindPieces_BarePaper_ShouldGiveNothing()
    {
        using Mat page = Page();

        TextureSeamSplitter.FindPieces(page, PaperHsv(), minArea: 0.05 * page.Width * page.Height).ShouldBeEmpty();
    }
}
