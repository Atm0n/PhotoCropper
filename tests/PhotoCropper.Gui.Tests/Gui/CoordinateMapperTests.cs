using PhotoCropper.Gui.Services;

namespace PhotoCropper.Gui.Tests.Gui;

public sealed class CoordinateMapperTests
{
    [Fact]
    public void ComputeNormalizedRect_TopLeftToBottomRight_ShouldProducePositiveDimensions()
    {
        var p1 = new Avalonia.Point(50, 60);
        var p2 = new Avalonia.Point(150, 200);

        var rect = CoordinateMapper.ComputeNormalizedRect(p1, p2);

        rect.X.ShouldBe(50);
        rect.Y.ShouldBe(60);
        rect.Width.ShouldBe(100);
        rect.Height.ShouldBe(140);
    }

    [Fact]
    public void ComputeNormalizedRect_BottomRightToTopLeft_ShouldProduceSamePositiveRect()
    {
        var p1 = new Avalonia.Point(150, 200);
        var p2 = new Avalonia.Point(50, 60);

        var rect = CoordinateMapper.ComputeNormalizedRect(p1, p2);

        rect.X.ShouldBe(50);
        rect.Y.ShouldBe(60);
        rect.Width.ShouldBe(100);
        rect.Height.ShouldBe(140);
    }

    [Fact]
    public void MapUiRectToImageRect_ValidScaling_ShouldMapCoordinatesAccurately()
    {
        var uiRect = new Avalonia.Rect(25, 25, 50, 50);
        var imageBounds = new Avalonia.Rect(0, 0, 100, 100);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var mapped = CoordinateMapper.MapUiRectToImageRect(uiRect, imageBounds, originalSize);

        mapped.X.ShouldBe(250);
        mapped.Y.ShouldBe(250);
        mapped.Width.ShouldBe(500);
        mapped.Height.ShouldBe(500);
    }

    [Fact]
    public void MapUiRectToImageRect_OutOfBounds_ShouldClampToImageDimensions()
    {
        var uiRect = new Avalonia.Rect(-50, -50, 200, 200);
        var imageBounds = new Avalonia.Rect(0, 0, 100, 100);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var mapped = CoordinateMapper.MapUiRectToImageRect(uiRect, imageBounds, originalSize);

        mapped.X.ShouldBe(0);
        mapped.Y.ShouldBe(0);
        mapped.Width.ShouldBe(1000);
        mapped.Height.ShouldBe(1000);
    }

    [Fact]
    public void MapUiRectToImageRect_EmptyOrZeroImageBounds_ShouldReturnEmptyRectangle()
    {
        var uiRect = new Avalonia.Rect(10, 10, 50, 50);
        var imageBounds = new Avalonia.Rect(0, 0, 0, 0);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var mapped = CoordinateMapper.MapUiRectToImageRect(uiRect, imageBounds, originalSize);

        mapped.ShouldBe(System.Drawing.Rectangle.Empty);
    }

    [Fact]
    public void MapUiPointToImagePixel_CenterPoint_ShouldMapCorrectly()
    {
        var uiPoint = new Avalonia.Point(50, 50);
        var imageBounds = new Avalonia.Rect(0, 0, 100, 100);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var pixel = CoordinateMapper.MapUiPointToImagePixel(uiPoint, imageBounds, originalSize);

        pixel.X.ShouldBe(500);
        pixel.Y.ShouldBe(500);
    }

    [Fact]
    public void MapUiPointToImagePixel_OutOfBoundsPoint_ShouldClampWithinPixelRange()
    {
        var uiPoint = new Avalonia.Point(200, 200);
        var imageBounds = new Avalonia.Rect(0, 0, 100, 100);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var pixel = CoordinateMapper.MapUiPointToImagePixel(uiPoint, imageBounds, originalSize);

        pixel.X.ShouldBe(999);
        pixel.Y.ShouldBe(999);
    }

    [Fact]
    public void MapUiPointToImagePixel_ZeroImageBounds_ShouldReturnEmptyPoint()
    {
        var uiPoint = new Avalonia.Point(50, 50);
        var imageBounds = new Avalonia.Rect(0, 0, 0, 0);
        var originalSize = new System.Drawing.Size(1000, 1000);

        var pixel = CoordinateMapper.MapUiPointToImagePixel(uiPoint, imageBounds, originalSize);

        pixel.ShouldBe(System.Drawing.Point.Empty);
    }
}
