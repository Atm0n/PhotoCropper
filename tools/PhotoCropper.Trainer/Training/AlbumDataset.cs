using System.Runtime.InteropServices;
using System.Threading.Channels;
using OpenCvSharp;

namespace PhotoCropper.Trainer.Training;

/// <summary>A batch ready for the network: pages and masks as flat float arrays (N × C × S × S, channels planar).</summary>
internal sealed record Batch(float[] Images, float[] Masks, int Count, int Size);

/// <summary>
/// The generated pages on disk (images/ and masks/ from <c>generate</c>), split into pages to train on and pages held
/// back to check the model on (every 20th). Pages are read and prepared on background threads while the network trains.
/// </summary>
internal sealed class AlbumDataset
{
    private readonly string _folder;

    private AlbumDataset(string folder, List<string> training, List<string> validation)
    {
        _folder = folder;
        Training = training;
        Validation = validation;
    }

    public IReadOnlyList<string> Training { get; }

    public IReadOnlyList<string> Validation { get; }

    /// <summary>The pages in <paramref name="folder"/> that have both an image and a mask.</summary>
    public static AlbumDataset Open(string folder)
    {
        string images = Path.Combine(folder, "images"), masks = Path.Combine(folder, "masks");
        if (!Directory.Exists(images) || !Directory.Exists(masks)) throw new DirectoryNotFoundException($"No images/ and masks/ in {folder}: run generate first.");
        var names = Directory.EnumerateFiles(images, "*.jpg")
            .Select(Path.GetFileNameWithoutExtension)
            .OfType<string>()
            .Where(n => File.Exists(Path.Combine(masks, n + ".png")))
            .Order(StringComparer.Ordinal)
            .ToList();
        // Every 20th page is held back: the network never trains on it, so it shows how well the network generalises.
        var validation = names.Where((_, i) => i % 20 == 0).ToList();
        var training = names.Where((_, i) => i % 20 != 0).ToList();
        return new AlbumDataset(folder, training, validation);
    }

    /// <summary>
    /// Produces the batches of one pass over <paramref name="names"/> on background threads, a few ahead of the
    /// network. Training batches are shuffled and varied (flips, a little brightness and contrast).
    /// </summary>
    public ChannelReader<Batch> Batches(IReadOnlyList<string> names, int batchSize, int size, bool augment, Rng rng, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(rng);
        var order = augment ? names.OrderBy(_ => rng.NextULong()).ToList() : [.. names];
        var seeds = order.Select(_ => rng.NextULong()).ToList();
        var channel = Channel.CreateBounded<Batch>(new BoundedChannelOptions(4) { SingleWriter = true });

        _ = Task.Run(async () =>
        {
            try
            {
                for (int first = 0; first < order.Count; first += batchSize)
                {
                    int count = Math.Min(batchSize, order.Count - first);
                    var images = new float[count * 3 * size * size];
                    var masks = new float[count * size * size];
                    int start = first;
                    Parallel.For(0, count, i => Load(order[start + i], size, augment ? new Rng(seeds[start + i]) : null, images, masks, i));
                    await channel.Writer.WriteAsync(new Batch(images, masks, count, size), ct);
                }
                channel.Writer.Complete();
            }
            catch (Exception ex)
            {
                channel.Writer.Complete(ex);
            }
        }, ct);
        return channel.Reader;
    }

    /// <summary>Reads one page into slot <paramref name="slot"/> of a batch: BGR, 0..1, channels planar; mask 0 or 1.</summary>
    private void Load(string name, int size, Rng? augment, float[] images, float[] masks, int slot)
    {
        using Mat image = Cv2.ImRead(Path.Combine(_folder, "images", name + ".jpg"), ImreadModes.Color);
        using Mat mask = Cv2.ImRead(Path.Combine(_folder, "masks", name + ".png"), ImreadModes.Grayscale);
        if (image.Empty() || mask.Empty()) throw new IOException($"Could not read page {name}.");
        if (image.Width != size || image.Height != size)
        {
            Cv2.Resize(image, image, new Size(size, size), 0, 0, InterpolationFlags.Area);
            Cv2.Resize(mask, mask, new Size(size, size), 0, 0, InterpolationFlags.Area);
            Cv2.Threshold(mask, mask, 127, 255, ThresholdTypes.Binary);
        }

        double contrast = 1, brightness = 0;
        if (augment != null)
        {
            if (augment.Chance(0.5))
            {
                Cv2.Flip(image, image, FlipMode.Y);
                Cv2.Flip(mask, mask, FlipMode.Y);
            }
            if (augment.Chance(0.25))
            {
                Cv2.Flip(image, image, FlipMode.XY);
                Cv2.Flip(mask, mask, FlipMode.XY);
            }
            contrast = augment.Range(0.85, 1.15);
            brightness = augment.Range(-0.06, 0.06);
        }

        int pixels = size * size;
        var bgr = new byte[pixels * 3];
        var maskBytes = new byte[pixels];
        Marshal.Copy(image.Data, bgr, 0, bgr.Length);
        Marshal.Copy(mask.Data, maskBytes, 0, maskBytes.Length);

        int imageOffset = slot * 3 * pixels, maskOffset = slot * pixels;
        for (int p = 0; p < pixels; p++)
        {
            for (int c = 0; c < 3; c++)
            {
                float value = (float)((((bgr[(p * 3) + c] / 255.0) - 0.5) * contrast) + 0.5 + brightness);
                images[imageOffset + (c * pixels) + p] = Math.Clamp(value, 0f, 1f);
            }
            masks[maskOffset + p] = maskBytes[p] > 127 ? 1f : 0f;
        }
    }
}
