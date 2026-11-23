using Avalonia;
using PhotoCropper.Gui.Views.Stages;
using Edges = PhotoCropper.Gui.Views.Stages.CompareImageViewer.TrimEdges;

namespace PhotoCropper.Gui.Tests.Views;

/// <summary>Adjusting the trim by hand in the Finish editor: which edges the pointer takes, and how a drag moves them.</summary>
public sealed class TrimAdjustTests
{
    private static readonly Rect Frame = new(100, 50, 300, 200); // left 100, top 50, right 400, bottom 250

    [Theory]
    [InlineData(102, 150, (int)(Edges.Left))]
    [InlineData(395, 150, (int)(Edges.Right))]
    [InlineData(250, 47, (int)(Edges.Top))]
    [InlineData(250, 258, (int)(Edges.Bottom))]
    [InlineData(98, 52, (int)(Edges.Left | Edges.Top))]          // a corner: two edges at once
    [InlineData(404, 246, (int)(Edges.Right | Edges.Bottom))]
    [InlineData(250, 150, (int)(Edges.None))]                    // inside: nothing (it pans)
    [InlineData(102, 400, (int)(Edges.None))]                    // in line with an edge but far beyond its end
    public void EdgesNear_ShouldTakeTheEdgesUnderThePointer(double x, double y, int expected)
    {
        // The edges as an int: the enum is internal to the viewer.
        CompareImageViewer.EdgesNear(new Point(x, y), Frame, grab: 10).ShouldBe((Edges)expected);
    }

    [Fact]
    public void MoveEdges_ShouldMoveOnlyTheDraggedEdges()
    {
        var moved = CompareImageViewer.MoveEdges(Frame, Edges.Left | Edges.Top, new Point(130, 70), new Size(500, 300));

        moved.ShouldBe(new Rect(new Point(130, 70), new Point(400, 250)));
    }

    [Fact]
    public void MoveEdges_ShouldStayOnThePhotoAndKeepAMinimumSize()
    {
        var size = new Size(500, 300);

        CompareImageViewer.MoveEdges(Frame, Edges.Right, new Point(900, 0), size).Right.ShouldBe(500);
        CompareImageViewer.MoveEdges(Frame, Edges.Top, new Point(0, -40), size).Top.ShouldBe(0);
        // Dragged past the opposite edge: stops 5% of the photo short of it.
        CompareImageViewer.MoveEdges(Frame, Edges.Left, new Point(480, 0), size).Left.ShouldBe(400 - 500 * 0.05);
    }
}
