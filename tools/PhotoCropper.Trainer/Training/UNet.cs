using TorchSharp;
using TorchSharp.Modules;
using static TorchSharp.torch;

namespace PhotoCropper.Trainer.Training;

/// <summary>
/// The photo-detection network: a small U-Net. A scan goes in (3 × H × W, values 0..1); one map comes out (1 × H × W)
/// whose values, through a sigmoid, are how likely each pixel is part of a photo. H and W must be multiples of 16.
/// The encoder halves the image four times to see the whole page; the decoder grows it back, with skip links carrying
/// the fine detail across so photo edges stay sharp.
/// </summary>
internal sealed class UNet : nn.Module<Tensor, Tensor>
{
    private readonly Sequential _enc1, _enc2, _enc3, _enc4, _bottleneck, _dec4, _dec3, _dec2, _dec1;
    private readonly MaxPool2d _pool;
    private readonly ConvTranspose2d _up4, _up3, _up2, _up1;
    private readonly Conv2d _head;

    /// <param name="width">Channels at full size; each level down doubles it. 16 gives about 1.9 million weights.</param>
    public UNet(int width = 16) : base(nameof(UNet))
    {
        int c1 = width, c2 = width * 2, c3 = width * 4, c4 = width * 8, c5 = width * 16;
        _enc1 = Block(3, c1);
        _enc2 = Block(c1, c2);
        _enc3 = Block(c2, c3);
        _enc4 = Block(c3, c4);
        _bottleneck = Block(c4, c5);
        _pool = nn.MaxPool2d(2);
        _up4 = nn.ConvTranspose2d(c5, c4, 2, stride: 2);
        _dec4 = Block(c4 * 2, c4);
        _up3 = nn.ConvTranspose2d(c4, c3, 2, stride: 2);
        _dec3 = Block(c3 * 2, c3);
        _up2 = nn.ConvTranspose2d(c3, c2, 2, stride: 2);
        _dec2 = Block(c2 * 2, c2);
        _up1 = nn.ConvTranspose2d(c2, c1, 2, stride: 2);
        _dec1 = Block(c1 * 2, c1);
        _head = nn.Conv2d(c1, 1, 1);
        RegisterComponents();
    }

    /// <summary>Two 3×3 convolutions, each followed by batch normalisation and ReLU.</summary>
    private static Sequential Block(int input, int output) => nn.Sequential(
        nn.Conv2d(input, output, 3, padding: 1, bias: false),
        nn.BatchNorm2d(output),
        nn.ReLU(),
        nn.Conv2d(output, output, 3, padding: 1, bias: false),
        nn.BatchNorm2d(output),
        nn.ReLU());

    public override Tensor forward(Tensor input)
    {
        // Intermediate tensors are freed by the caller's dispose scope.
        using var scope = NewDisposeScope();
        var e1 = _enc1.forward(input);
        var e2 = _enc2.forward(_pool.forward(e1));
        var e3 = _enc3.forward(_pool.forward(e2));
        var e4 = _enc4.forward(_pool.forward(e3));
        var b = _bottleneck.forward(_pool.forward(e4));
        var d4 = _dec4.forward(cat([_up4.forward(b), e4], 1));
        var d3 = _dec3.forward(cat([_up3.forward(d4), e3], 1));
        var d2 = _dec2.forward(cat([_up2.forward(d3), e2], 1));
        var d1 = _dec1.forward(cat([_up1.forward(d2), e1], 1));
        return _head.forward(d1).MoveToOuterDisposeScope();
    }

    /// <summary>The network's layers, for exporting it.</summary>
    internal UNetParts Parts() => new(_enc1, _enc2, _enc3, _enc4, _bottleneck, _up4, _dec4, _up3, _dec3, _up2, _dec2, _up1, _dec1, _head);

    /// <summary>The number of trained weights.</summary>
    public long WeightCount() => parameters().Sum(p => p.numel());

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _enc1.Dispose();
            _enc2.Dispose();
            _enc3.Dispose();
            _enc4.Dispose();
            _bottleneck.Dispose();
            _pool.Dispose();
            _up4.Dispose();
            _dec4.Dispose();
            _up3.Dispose();
            _dec3.Dispose();
            _up2.Dispose();
            _dec2.Dispose();
            _up1.Dispose();
            _dec1.Dispose();
            _head.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>The layers of a <see cref="UNet"/>, in the order data flows through them.</summary>
internal sealed record UNetParts(
    Sequential Enc1, Sequential Enc2, Sequential Enc3, Sequential Enc4, Sequential Bottleneck,
    ConvTranspose2d Up4, Sequential Dec4, ConvTranspose2d Up3, Sequential Dec3,
    ConvTranspose2d Up2, Sequential Dec2, ConvTranspose2d Up1, Sequential Dec1, Conv2d Head);
