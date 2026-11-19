using OpenCvSharp;

namespace PhotoCropper.Core.Common;

/// <summary>
/// The channels of an image, split with <see cref="Cv2.Split(Mat)"/>, disposed together:
/// <c>using var channels = MatChannels.Split(image); ... channels[1] ...</c>.
/// </summary>
public sealed class MatChannels : IDisposable
{
    private readonly Mat[] _channels;

    private MatChannels(Mat[] channels) => _channels = channels;

    public static MatChannels Split(Mat image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new MatChannels(Cv2.Split(image));
    }

    public int Count => _channels.Length;

    public Mat this[int index] => _channels[index];

    public void Dispose()
    {
        foreach (var channel in _channels) channel.Dispose();
    }
}
