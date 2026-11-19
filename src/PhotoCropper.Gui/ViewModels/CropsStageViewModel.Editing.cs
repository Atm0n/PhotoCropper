using PhotoCropper.Core.Common;
using Cv2 = OpenCvSharp.Cv2;
using Mat = OpenCvSharp.Mat;
using PhotoCropper.Core;
using PhotoCropper.Core.Models;
using PhotoCropper.Gui.Controls;
using PhotoCropper.Gui.Services;
using System.Drawing;

namespace PhotoCropper.Gui.ViewModels;

/// <summary>What a press on the scan started.</summary>
internal enum BoxGesture
{
    /// <summary>Nothing (no scan open).</summary>
    None,

    /// <summary>Dragging a box's rotation handle.</summary>
    Rotate,

    /// <summary>Dragging a box's corner.</summary>
    Resize,

    /// <summary>Not on a handle: the view draws a selection rectangle, which ends as a click or a manual crop.</summary>
    Draw,
}

/// <summary>
/// Editing boxes on the open scan. The view maps the pointer to scan pixels and draws the selection rectangle; this
/// decides what a press hits, moves boxes while dragging, and on release re-extracts the photo and records undo.
/// </summary>
internal sealed partial class CropsStageViewModel
{
    // Hit distances in scan pixels at the reference resolution; scaled like the overlay so they match the handles.
    private const double RotateHandleHitRadius = 55;
    private const double CornerHitRadius = 60;
    private const double DeleteBadgeHitRadius = 60;

    private BoxGesture _gesture;
    private int _dragIndex = -1;
    private int _dragCorner = -1;
    private double _dragStartMouseAngle;
    private double _dragStartBoxAngle;

    // The photo and boxes as they were before the drag, for undo or to put the box back if the drag fails.
    private Mat? _beforePhoto;
    private Mat? _beforeRaw;
    private IReadOnlyList<CropCandidate>? _beforeBoxes;

    private PhotoCropperEngine? OpenEngine => Current?.IsActive == true ? Current.Engine : null;

    /// <summary>Starts a gesture at <paramref name="point"/> (scan pixels): on a rotation handle, a corner, or elsewhere.</summary>
    public BoxGesture PressAt(PointF point)
    {
        CancelGesture();
        var session = Current;
        if (session == null) return BoxGesture.None;

        var engine = session.Activate();
        var scanSize = new Size(engine.Original.Width, engine.Original.Height);
        var boxes = engine.AcceptedCandidates;

        // Topmost (last drawn) box first.
        for (int i = boxes.Count - 1; i >= 0; i--)
        {
            var box = boxes[i];
            var geometry = CropBoxGeometry.For(box, scanSize, engine.OverlayScale);

            if (Distance(geometry.RotationHandle, point) <= engine.ScaleOverlay(RotateHandleHitRadius))
            {
                BeginDrag(engine, i, BoxGesture.Rotate);
                _dragStartMouseAngle = AngleFromCenter(box, point);
                _dragStartBoxAngle = box.Rotated.Angle;
                return _gesture;
            }

            for (int corner = 0; corner < 4; corner++)
            {
                if (Distance(geometry.Vertices[corner], point) <= engine.ScaleOverlay(CornerHitRadius))
                {
                    BeginDrag(engine, i, BoxGesture.Resize);
                    _dragCorner = corner;
                    return _gesture;
                }
            }
        }

        _gesture = BoxGesture.Draw;
        return _gesture;
    }

    /// <summary>Moves the dragged handle to <paramref name="point"/>; the box updates live, the photo on release.</summary>
    public void DragTo(PointF point)
    {
        var engine = OpenEngine;
        if (engine == null || _dragIndex < 0 || _dragIndex >= engine.AcceptedCandidates.Count) return;

        var box = engine.AcceptedCandidates[_dragIndex];
        CropCandidate moved = _gesture switch
        {
            BoxGesture.Rotate => BoxEditMath.Rotate(box, _dragStartMouseAngle, _dragStartBoxAngle, AngleFromCenter(box, point)),
            BoxGesture.Resize => BoxEditMath.ResizeFromCorner(box, _dragCorner, point),
            _ => box,
        };
        var updated = engine.AcceptedCandidates.ToList();
        updated[_dragIndex] = moved;
        engine.UpdateCandidates(updated);
        _host.ShowBoxes(engine);
    }

    /// <summary>
    /// Ends a rotate or resize. A tap on the top-right corner (no drag) deletes the box, since the delete badge
    /// sits on that corner; otherwise the photo is re-extracted from the moved box and the change can be undone.
    /// </summary>
    public void Release(bool wasTap)
    {
        var engine = OpenEngine;
        var gesture = _gesture;
        int index = _dragIndex;
        int corner = _dragCorner;
        _gesture = BoxGesture.None;
        _dragIndex = -1;
        _dragCorner = -1;

        if (engine == null || index < 0 || gesture is not (BoxGesture.Rotate or BoxGesture.Resize))
        {
            ClearDragSnapshot();
            return;
        }

        if (gesture == BoxGesture.Resize && wasTap && index < engine.AcceptedCandidates.Count)
        {
            var geometry = CropBoxGeometry.For(engine.AcceptedCandidates[index], new Size(engine.Original.Width, engine.Original.Height), engine.OverlayScale);
            if (Array.IndexOf(geometry.Vertices, geometry.DeleteBadge) == corner)
            {
                ClearDragSnapshot();
                DeleteBox(engine, index);
                return;
            }
        }

        CommitDrag(engine, index, gesture == BoxGesture.Rotate ? "Rotate Crop Box" : "Resize Crop Box");
        Present(engine, select: index);
    }

    /// <summary>A click that wasn't on a handle: on a delete badge it deletes that box, inside a box it selects its photo.</summary>
    public void ClickAt(PointF point)
    {
        _gesture = BoxGesture.None;
        var engine = OpenEngine ?? Current?.Activate();
        if (engine == null) return;

        var scanSize = new Size(engine.Original.Width, engine.Original.Height);
        for (int i = engine.AcceptedCandidates.Count - 1; i >= 0; i--)
        {
            var box = engine.AcceptedCandidates[i];
            var badge = CropBoxGeometry.For(box, scanSize, engine.OverlayScale).DeleteBadge;
            if (Distance(badge, point) <= engine.ScaleOverlay(DeleteBadgeHitRadius))
            {
                DeleteBox(engine, i);
                return;
            }

            if (Cv2.PointPolygonTest(CvInterop.ToCv(box.Rotated.GetVertices()), CvInterop.ToCv(point), false) >= 0)
            {
                SelectPhoto(i);
                return;
            }
        }
    }

    /// <summary>Adds a photo from a rectangle drawn on the scan (scan pixels) and selects it.</summary>
    public async Task AddManualCropAsync(Rectangle area)
    {
        _gesture = BoxGesture.None;
        var session = Current;
        if (session == null || area.Width <= 10 || area.Height <= 10) return;

        var engine = session.Activate();
        int scanIndex = _sessions.CurrentIndex;
        await _host.RunBusyAsync(LocalizationService.GetString(ResourceKeys.MsgExtractingCrop, "Extracting manual crop..."), async ct =>
        {
            int before = engine.DetectedPhotos.Count;
            session.IsModified = true;
            await Task.Run(() => engine.AddManualCrop(area), ct);
            if (engine.DetectedPhotos.Count > before)
            {
                int added = engine.DetectedPhotos.Count - 1;
                CropCandidate? box = added < engine.AcceptedCandidates.Count ? engine.AcceptedCandidates[added] : null;
                _undo.PushAdd(scanIndex, added, engine.DetectedPhotos[added], box);
            }
            Present(engine, select: engine.DetectedPhotos.Count - 1);
            _host.ShowStatus(LocalizationService.GetString(ResourceKeys.MsgManualCropAdded, "Manual crop added."));
        });
    }

    /// <summary>Drops a gesture that won't be finished (e.g. the scan changed under it).</summary>
    public void CancelGesture()
    {
        _gesture = BoxGesture.None;
        _dragIndex = -1;
        _dragCorner = -1;
        ClearDragSnapshot();
    }

    private void DeleteBox(PhotoCropperEngine engine, int index)
    {
        if (index < engine.DetectedPhotos.Count)
        {
            _undo.PushDelete(_sessions.CurrentIndex, index, engine);
        }
        engine.DeletePhoto(index);
        Current!.IsModified = true;
        Present(engine, select: index);
    }

    private void BeginDrag(PhotoCropperEngine engine, int index, BoxGesture gesture)
    {
        _gesture = gesture;
        _dragIndex = index;
        _beforePhoto = index < engine.DetectedPhotos.Count ? engine.DetectedPhotos[index].Clone() : null;
        _beforeRaw = index < engine.RawDetectedPhotos.Count ? engine.RawDetectedPhotos[index].Clone() : null;
        _beforeBoxes = [.. engine.AcceptedCandidates];
    }

    /// <summary>
    /// Re-extracts the photo for a moved box and records undo. If nothing can be extracted (dragged too small), the
    /// box goes back to where it was, so it keeps matching the unchanged photo.
    /// </summary>
    private void CommitDrag(PhotoCropperEngine engine, int index, string description)
    {
        try
        {
            if (!engine.ApplyGrabHandleResize(index))
            {
                if (_beforeBoxes != null) engine.UpdateCandidates(_beforeBoxes);
                return;
            }

            Current!.IsModified = true;
            if (_beforePhoto != null && _beforeBoxes != null && index < engine.DetectedPhotos.Count)
            {
                _undo.PushResizeCandidate(
                    _sessions.CurrentIndex, index,
                    _beforePhoto, engine.DetectedPhotos[index],
                    _beforeBoxes, engine.AcceptedCandidates,
                    description,
                    _beforeRaw,
                    index < engine.RawDetectedPhotos.Count ? engine.RawDetectedPhotos[index] : null);
            }
        }
        finally
        {
            ClearDragSnapshot();
        }
    }

    private void ClearDragSnapshot()
    {
        _beforePhoto?.Dispose();
        _beforePhoto = null;
        _beforeRaw?.Dispose();
        _beforeRaw = null;
        _beforeBoxes = null;
    }

    private static double Distance(PointF a, PointF b) => Math.Sqrt(Math.Pow(a.X - b.X, 2) + Math.Pow(a.Y - b.Y, 2));

    private static double AngleFromCenter(CropCandidate box, PointF point) =>
        Math.Atan2(point.Y - box.Rotated.Center.Y, point.X - box.Rotated.Center.X) * 180.0 / Math.PI;
}

/// <summary>How dragging a handle changes a box, in scan pixels.</summary>
internal static class BoxEditMath
{
    /// <summary>Turns the box by how far the pointer has turned around its centre since the drag started.</summary>
    public static CropCandidate Rotate(CropCandidate box, double startMouseAngle, double startBoxAngle, double mouseAngle) =>
        box with { Rotated = new RotatedBox(box.Rotated.Center, box.Rotated.Size, (float)(startBoxAngle + mouseAngle - startMouseAngle)) };

    /// <summary>Moves one corner to <paramref name="point"/>, keeping the opposite corner and the box's angle.</summary>
    public static CropCandidate ResizeFromCorner(CropCandidate box, int corner, PointF point)
    {
        PointF[] vertices = box.Rotated.GetVertices();
        PointF opposite = vertices[(corner + 2) % 4];
        var center = new PointF((point.X + opposite.X) / 2f, (point.Y + opposite.Y) / 2f);

        double theta = box.Rotated.Angle * Math.PI / 180.0;
        double dx = point.X - opposite.X;
        double dy = point.Y - opposite.Y;
        float width = (float)Math.Abs(dx * Math.Cos(theta) + dy * Math.Sin(theta));
        float height = (float)Math.Abs(-dx * Math.Sin(theta) + dy * Math.Cos(theta));

        return box with { Rotated = new RotatedBox(center, new SizeF(width, height), box.Rotated.Angle) };
    }
}
