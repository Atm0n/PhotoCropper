using System.Buffers;
using System.Text;

namespace PhotoCropper.Trainer.Export;

/// <summary>
/// Writes protocol buffer messages, the encoding of .onnx files: each field is a tag (its number and wire type)
/// followed by a variable-length integer, a 32-bit float, or a length and bytes (strings, bytes, nested messages).
/// </summary>
internal sealed class ProtoWriter
{
    private readonly ArrayBufferWriter<byte> _buffer = new();

    public byte[] ToArray() => _buffer.WrittenSpan.ToArray();

    private void Varint(ulong value)
    {
        while (value >= 0x80)
        {
            _buffer.Write([(byte)(value | 0x80)]);
            value >>= 7;
        }
        _buffer.Write([(byte)value]);
    }

    private void Tag(int field, int wireType) => Varint(((ulong)field << 3) | (uint)wireType);

    public ProtoWriter Int(int field, long value)
    {
        Tag(field, 0);
        Varint(unchecked((ulong)value));
        return this;
    }

    public ProtoWriter Float(int field, float value)
    {
        Tag(field, 5);
        Span<byte> bytes = stackalloc byte[4];
        BitConverter.TryWriteBytes(bytes, value);
        _buffer.Write(bytes);
        return this;
    }

    public ProtoWriter Bytes(int field, ReadOnlySpan<byte> value)
    {
        Tag(field, 2);
        Varint((ulong)value.Length);
        _buffer.Write(value);
        return this;
    }

    public ProtoWriter String(int field, string value) => Bytes(field, Encoding.UTF8.GetBytes(value));

    public ProtoWriter Message(int field, Action<ProtoWriter> write)
    {
        ArgumentNullException.ThrowIfNull(write);
        var nested = new ProtoWriter();
        write(nested);
        return Bytes(field, nested.ToArray());
    }
}

/// <summary>
/// Builds an ONNX model: weights (initializers), operations (nodes) and the graph's input and output, written with
/// the field numbers of onnx.proto. Only what the photo network needs: float tensors and int / ints attributes.
/// </summary>
internal sealed class OnnxGraph
{
    private const int FloatType = 1;
    private const int AttributeInt = 2, AttributeInts = 7;
    private readonly List<Action<ProtoWriter>> _nodes = [];
    private readonly List<Action<ProtoWriter>> _weights = [];
    private int _names;

    /// <summary>Adds a constant weight tensor and returns its name.</summary>
    public string Weight(string name, float[] values, params long[] dims)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(dims);
        if (dims.Aggregate(1L, (a, b) => a * b) != values.Length) throw new ArgumentException($"{name}: {values.Length} values don't fill {string.Join('×', dims)}.", nameof(values));
        var raw = new byte[values.Length * sizeof(float)];
        Buffer.BlockCopy(values, 0, raw, 0, raw.Length);
        _weights.Add(t =>
        {
            foreach (long d in dims) t.Int(1, d);           // dims
            t.Int(2, FloatType);                              // data_type
            t.String(8, name);                                // name
            t.Bytes(9, raw);                                  // raw_data (little-endian floats)
        });
        return name;
    }

    /// <summary>Adds an operation and returns the name of its output.</summary>
    /// <param name="ints">Attributes that are lists of integers (kernel_shape, pads, strides).</param>
    /// <param name="intAttributes">Attributes that are one integer (axis).</param>
    public string Node(string op, string[] inputs, IReadOnlyDictionary<string, long[]>? ints = null, IReadOnlyDictionary<string, long>? intAttributes = null, string? output = null)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        string result = output ?? $"{op.ToUpperInvariant()}_{++_names}";
        string nodeName = $"{op}_{_names}";
        _nodes.Add(n =>
        {
            foreach (string input in inputs) n.String(1, input);
            n.String(2, result);
            n.String(3, nodeName);
            n.String(4, op);
            foreach (var (name, values) in ints ?? new Dictionary<string, long[]>())
            {
                n.Message(5, a =>
                {
                    a.String(1, name);
                    foreach (long v in values) a.Int(8, v);
                    a.Int(20, AttributeInts);
                });
            }
            foreach (var (name, value) in intAttributes ?? new Dictionary<string, long>())
            {
                n.Message(5, a =>
                {
                    a.String(1, name);
                    a.Int(3, value);
                    a.Int(20, AttributeInt);
                });
            }
        });
        return result;
    }

    /// <summary>
    /// The whole model, with an input of N × 3 × H × W and an output of N × 1 × H × W (N, H and W left free).
    /// Opset 17, read by ONNX Runtime and OpenCV alike.
    /// </summary>
    public byte[] Model(string input, string output, string producer, IReadOnlyDictionary<string, string>? metadata = null)
    {
        var model = new ProtoWriter();
        model.Int(1, 8);                                      // ir_version
        model.String(2, producer);                            // producer_name
        model.String(3, "1");                                 // producer_version
        model.Message(7, graph =>                             // graph
        {
            foreach (var node in _nodes) graph.Message(1, node);
            graph.String(2, "photo_mask");
            foreach (var weight in _weights) graph.Message(5, weight);
            graph.Message(11, v => ValueInfo(v, input, ["N", "3", "H", "W"]));
            graph.Message(12, v => ValueInfo(v, output, ["N", "1", "H", "W"]));
        });
        model.Message(8, opset => opset.String(1, string.Empty).Int(2, 17));   // opset_import: the default domain
        foreach (var (key, value) in metadata ?? new Dictionary<string, string>())
        {
            model.Message(14, entry => entry.String(1, key).String(2, value));  // metadata_props
        }
        return model.ToArray();
    }

    private static void ValueInfo(ProtoWriter v, string name, string[] dims)
    {
        v.String(1, name);
        v.Message(2, type => type.Message(1, tensor =>        // TypeProto.tensor_type
        {
            tensor.Int(1, FloatType);                         // elem_type
            tensor.Message(2, shape =>                        // shape
            {
                foreach (string dim in dims)
                {
                    shape.Message(1, d =>
                    {
                        if (long.TryParse(dim, out long size)) d.Int(1, size); // dim_value
                        else d.String(2, dim);                                 // dim_param
                    });
                }
            });
        }));
    }
}
