using Avalonia.Threading;

namespace PhotoCropper.Gui.Services;

/// <summary>
/// Disposes bitmaps a little later, on the UI thread, once the UI has stopped drawing them: disposing one while an
/// animation (e.g. the carousel's slide) is still rendering it crashes the render thread.
/// </summary>
internal static class DeferredDisposal
{
    public static void Later(IDisposable? item, int delayMs = 300)
    {
        if (item == null) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            Dispatcher.UIThread.Post(item.Dispose);
        });
    }

    public static void Later(IEnumerable<IDisposable> items, int delayMs = 300)
    {
        var list = items.ToList();
        if (list.Count == 0) return;
        _ = Task.Run(async () =>
        {
            await Task.Delay(delayMs);
            Dispatcher.UIThread.Post(() =>
            {
                foreach (var item in list) item.Dispose();
            });
        });
    }
}
