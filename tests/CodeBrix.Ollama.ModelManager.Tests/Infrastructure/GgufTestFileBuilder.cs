using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Builds small synthetic GGUF files in memory so the reader can be exercised without any real model on disk.
/// It writes the header, the key-values and the tensor descriptors, and pads the tensor data section with
/// zero bytes of exactly the right length.
/// </summary>
public sealed class GgufTestFileBuilder
{
    private readonly List<KeyValueEntry> _keyValues = new List<KeyValueEntry>();
    private readonly List<TensorEntry> _tensors = new List<TensorEntry>();
    private readonly uint _version;
    private readonly bool _bigEndian;

    /// <summary>Creates a builder for a file of a given version and byte order.</summary>
    /// <param name="version">The GGUF version to write. Version 1 uses 32-bit counts and null-terminated strings.</param>
    /// <param name="bigEndian">Whether to write the big-endian form, whose magic reads <c>FUGG</c>.</param>
    public GgufTestFileBuilder(uint version = 3, bool bigEndian = false)
    {
        _version = version;
        _bigEndian = bigEndian;
    }

    /// <summary>Adds an unsigned 8-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt8(string key, byte value)
    {
        return AddScalar(key, 0, writer => writer.WriteByte(value));
    }

    /// <summary>Adds a signed 8-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt8(string key, sbyte value)
    {
        return AddScalar(key, 1, writer => writer.WriteByte(unchecked((byte)value)));
    }

    /// <summary>Adds an unsigned 16-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt16(string key, ushort value)
    {
        return AddScalar(key, 2, writer => writer.WriteUInt16(value));
    }

    /// <summary>Adds a signed 16-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt16(string key, short value)
    {
        return AddScalar(key, 3, writer => writer.WriteUInt16(unchecked((ushort)value)));
    }

    /// <summary>Adds an unsigned 32-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt32(string key, uint value)
    {
        return AddScalar(key, 4, writer => writer.WriteUInt32(value));
    }

    /// <summary>Adds a signed 32-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt32(string key, int value)
    {
        return AddScalar(key, 5, writer => writer.WriteUInt32(unchecked((uint)value)));
    }

    /// <summary>Adds a 32-bit floating point key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddFloat32(string key, float value)
    {
        return AddScalar(key, 6, writer => writer.WriteSingle(value));
    }

    /// <summary>Adds a boolean key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddBool(string key, bool value)
    {
        return AddScalar(key, 7, writer => writer.WriteByte(value ? (byte)1 : (byte)0));
    }

    /// <summary>Adds a string key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddString(string key, string value)
    {
        return AddScalar(key, 8, writer => writer.WriteString(value));
    }

    /// <summary>Adds an unsigned 64-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt64(string key, ulong value)
    {
        return AddScalar(key, 10, writer => writer.WriteUInt64(value));
    }

    /// <summary>Adds a signed 64-bit key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt64(string key, long value)
    {
        return AddScalar(key, 11, writer => writer.WriteUInt64(unchecked((ulong)value)));
    }

    /// <summary>Adds a 64-bit floating point key-value.</summary>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddFloat64(string key, double value)
    {
        return AddScalar(key, 12, writer => writer.WriteDouble(value));
    }

    /// <summary>Adds an array of unsigned 8-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt8Array(string key, params byte[] values)
    {
        return AddArray(key, 0, values.Length, writer =>
        {
            foreach (byte value in values)
            {
                writer.WriteByte(value);
            }
        });
    }

    /// <summary>Adds an array of signed 8-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt8Array(string key, params sbyte[] values)
    {
        return AddArray(key, 1, values.Length, writer =>
        {
            foreach (sbyte value in values)
            {
                writer.WriteByte(unchecked((byte)value));
            }
        });
    }

    /// <summary>Adds an array of unsigned 16-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt16Array(string key, params ushort[] values)
    {
        return AddArray(key, 2, values.Length, writer =>
        {
            foreach (ushort value in values)
            {
                writer.WriteUInt16(value);
            }
        });
    }

    /// <summary>Adds an array of signed 16-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt16Array(string key, params short[] values)
    {
        return AddArray(key, 3, values.Length, writer =>
        {
            foreach (short value in values)
            {
                writer.WriteUInt16(unchecked((ushort)value));
            }
        });
    }

    /// <summary>Adds an array of unsigned 32-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt32Array(string key, params uint[] values)
    {
        return AddArray(key, 4, values.Length, writer =>
        {
            foreach (uint value in values)
            {
                writer.WriteUInt32(value);
            }
        });
    }

    /// <summary>Adds an array of signed 32-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt32Array(string key, params int[] values)
    {
        return AddArray(key, 5, values.Length, writer =>
        {
            foreach (int value in values)
            {
                writer.WriteUInt32(unchecked((uint)value));
            }
        });
    }

    /// <summary>Adds an array of 32-bit floating point values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddFloat32Array(string key, params float[] values)
    {
        return AddArray(key, 6, values.Length, writer =>
        {
            foreach (float value in values)
            {
                writer.WriteSingle(value);
            }
        });
    }

    /// <summary>Adds an array of boolean values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddBoolArray(string key, params bool[] values)
    {
        return AddArray(key, 7, values.Length, writer =>
        {
            foreach (bool value in values)
            {
                writer.WriteByte(value ? (byte)1 : (byte)0);
            }
        });
    }

    /// <summary>Adds an array of strings.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddStringArray(string key, params string[] values)
    {
        return AddArray(key, 8, values.Length, writer =>
        {
            foreach (string value in values)
            {
                writer.WriteString(value);
            }
        });
    }

    /// <summary>Adds an array of unsigned 64-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddUInt64Array(string key, params ulong[] values)
    {
        return AddArray(key, 10, values.Length, writer =>
        {
            foreach (ulong value in values)
            {
                writer.WriteUInt64(value);
            }
        });
    }

    /// <summary>Adds an array of signed 64-bit values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddInt64Array(string key, params long[] values)
    {
        return AddArray(key, 11, values.Length, writer =>
        {
            foreach (long value in values)
            {
                writer.WriteUInt64(unchecked((ulong)value));
            }
        });
    }

    /// <summary>Adds an array of 64-bit floating point values.</summary>
    /// <param name="key">The key.</param>
    /// <param name="values">The values.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddFloat64Array(string key, params double[] values)
    {
        return AddArray(key, 12, values.Length, writer =>
        {
            foreach (double value in values)
            {
                writer.WriteDouble(value);
            }
        });
    }

    /// <summary>Adds a key-value whose type tag is written verbatim, so malformed files can be built.</summary>
    /// <param name="key">The key.</param>
    /// <param name="rawValueType">The type tag to write.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddRawTypedValue(string key, uint rawValueType)
    {
        _keyValues.Add(new KeyValueEntry(key, rawValueType, null));
        return this;
    }

    /// <summary>Adds a tensor descriptor and reserves zero-filled space for its data.</summary>
    /// <param name="name">The tensor name.</param>
    /// <param name="type">The ggml type of the tensor.</param>
    /// <param name="shape">The dimensions, fastest moving first.</param>
    /// <returns>This builder.</returns>
    public GgufTestFileBuilder AddTensor(string name, GgufTensorType type, params ulong[] shape)
    {
        _tensors.Add(new TensorEntry(name, type, shape ?? Array.Empty<ulong>()));
        return this;
    }

    /// <summary>Builds the file and returns its bytes.</summary>
    /// <returns>The complete file.</returns>
    public byte[] Build()
    {
        var writer = new GgufWriter(_bigEndian, _version);

        writer.WriteMagic();
        writer.WriteUInt32(_version);
        writer.WriteCount((ulong)_tensors.Count);
        writer.WriteCount((ulong)_keyValues.Count);

        foreach (KeyValueEntry entry in _keyValues)
        {
            writer.WriteString(entry.Key);
            writer.WriteUInt32(entry.ValueType);
            entry.WritePayload?.Invoke(writer);
        }

        long alignment = ResolveAlignment();
        var sizes = new long[_tensors.Count];
        var offsets = new ulong[_tensors.Count];
        long dataSize = 0;
        for (int i = 0; i < _tensors.Count; i++)
        {
            sizes[i] = TensorByteCount(_tensors[i]);
            offsets[i] = (ulong)dataSize;
            dataSize += sizes[i];
            dataSize += Padding(dataSize, alignment);
        }

        for (int i = 0; i < _tensors.Count; i++)
        {
            TensorEntry tensor = _tensors[i];
            writer.WriteString(tensor.Name);
            writer.WriteUInt32((uint)tensor.Shape.Length);
            foreach (ulong dimension in tensor.Shape)
            {
                writer.WriteUInt64(dimension);
            }

            writer.WriteUInt32((uint)tensor.Type);
            writer.WriteUInt64(offsets[i]);
        }

        if (_tensors.Count == 0)
        {
            return writer.ToArray();
        }

        writer.WriteZeros(Padding(writer.Length, alignment));
        for (int i = 0; i < _tensors.Count; i++)
        {
            writer.WriteZeros(sizes[i]);
            if (i + 1 < _tensors.Count)
            {
                writer.WriteZeros(Padding(sizes[i], alignment));
            }
        }

        return writer.ToArray();
    }

    /// <summary>Builds the file and returns it as a seekable stream positioned at its first byte.</summary>
    /// <returns>The stream.</returns>
    public MemoryStream BuildStream()
    {
        return new MemoryStream(Build(), false);
    }

    /// <summary>Builds the file and writes it to a path.</summary>
    /// <param name="path">Where to write the file.</param>
    /// <returns>The path that was written.</returns>
    public string BuildFile(string path)
    {
        File.WriteAllBytes(path, Build());
        return path;
    }

    private GgufTestFileBuilder AddScalar(string key, uint valueType, Action<GgufWriter> writePayload)
    {
        _keyValues.Add(new KeyValueEntry(key, valueType, writePayload));
        return this;
    }

    private GgufTestFileBuilder AddArray(string key, uint elementType, int count, Action<GgufWriter> writeElements)
    {
        _keyValues.Add(new KeyValueEntry(key, 9, writer =>
        {
            writer.WriteUInt32(elementType);
            writer.WriteUInt64((ulong)count);
            writeElements(writer);
        }));
        return this;
    }

    private long ResolveAlignment()
    {
        foreach (KeyValueEntry entry in _keyValues)
        {
            if (string.Equals(entry.Key, "general.alignment", StringComparison.Ordinal) &&
                entry.WritePayload != null)
            {
                var probe = new GgufWriter(_bigEndian, _version);
                entry.WritePayload(probe);
                byte[] bytes = probe.ToArray();
                if (bytes.Length == 4)
                {
                    return _bigEndian
                        ? BinaryPrimitives.ReadUInt32BigEndian(bytes)
                        : BinaryPrimitives.ReadUInt32LittleEndian(bytes);
                }

                if (bytes.Length == 8)
                {
                    return (long)(_bigEndian
                        ? BinaryPrimitives.ReadUInt64BigEndian(bytes)
                        : BinaryPrimitives.ReadUInt64LittleEndian(bytes));
                }
            }
        }

        return 32;
    }

    private static long TensorByteCount(TensorEntry tensor)
    {
        long elementCount = 1;
        foreach (ulong dimension in tensor.Shape)
        {
            elementCount *= (long)dimension;
        }

        long blockSize = GgufTensorTypes.GetBlockSize(tensor.Type);
        long typeSize = GgufTensorTypes.GetTypeSize(tensor.Type);
        if (blockSize == 0 || typeSize == 0)
        {
            return 0;
        }

        return elementCount / blockSize * typeSize;
    }

    private static long Padding(long value, long alignment)
    {
        return (alignment - value % alignment) % alignment;
    }

    private sealed class KeyValueEntry
    {
        public KeyValueEntry(string key, uint valueType, Action<GgufWriter> writePayload)
        {
            Key = key;
            ValueType = valueType;
            WritePayload = writePayload;
        }

        public string Key { get; }

        public uint ValueType { get; }

        public Action<GgufWriter> WritePayload { get; }
    }

    private sealed class TensorEntry
    {
        public TensorEntry(string name, GgufTensorType type, ulong[] shape)
        {
            Name = name;
            Type = type;
            Shape = shape;
        }

        public string Name { get; }

        public GgufTensorType Type { get; }

        public ulong[] Shape { get; }
    }

    /// <summary>
    /// The little byte-order aware writer the builder assembles a file through.
    /// </summary>
    public sealed class GgufWriter
    {
        private readonly MemoryStream _stream = new MemoryStream();
        private readonly byte[] _scratch = new byte[8];
        private readonly bool _bigEndian;
        private readonly uint _version;

        internal GgufWriter(bool bigEndian, uint version)
        {
            _bigEndian = bigEndian;
            _version = version;
        }

        /// <summary>How many bytes have been written so far.</summary>
        public long Length
        {
            get { return _stream.Length; }
        }

        /// <summary>Writes the four magic bytes for the configured byte order.</summary>
        public void WriteMagic()
        {
            byte[] magic = _bigEndian
                ? new[] { (byte)'F', (byte)'U', (byte)'G', (byte)'G' }
                : new[] { (byte)'G', (byte)'G', (byte)'U', (byte)'F' };
            _stream.Write(magic, 0, magic.Length);
        }

        /// <summary>Writes one raw byte.</summary>
        /// <param name="value">The byte.</param>
        public void WriteByte(byte value)
        {
            _stream.WriteByte(value);
        }

        /// <summary>Writes an unsigned 16-bit value in the configured byte order.</summary>
        /// <param name="value">The value.</param>
        public void WriteUInt16(ushort value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt16BigEndian(_scratch, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt16LittleEndian(_scratch, value);
            }

            _stream.Write(_scratch, 0, 2);
        }

        /// <summary>Writes an unsigned 32-bit value in the configured byte order.</summary>
        /// <param name="value">The value.</param>
        public void WriteUInt32(uint value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt32BigEndian(_scratch, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(_scratch, value);
            }

            _stream.Write(_scratch, 0, 4);
        }

        /// <summary>Writes an unsigned 64-bit value in the configured byte order.</summary>
        /// <param name="value">The value.</param>
        public void WriteUInt64(ulong value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteUInt64BigEndian(_scratch, value);
            }
            else
            {
                BinaryPrimitives.WriteUInt64LittleEndian(_scratch, value);
            }

            _stream.Write(_scratch, 0, 8);
        }

        /// <summary>Writes a 32-bit floating point value in the configured byte order.</summary>
        /// <param name="value">The value.</param>
        public void WriteSingle(float value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteSingleBigEndian(_scratch, value);
            }
            else
            {
                BinaryPrimitives.WriteSingleLittleEndian(_scratch, value);
            }

            _stream.Write(_scratch, 0, 4);
        }

        /// <summary>Writes a 64-bit floating point value in the configured byte order.</summary>
        /// <param name="value">The value.</param>
        public void WriteDouble(double value)
        {
            if (_bigEndian)
            {
                BinaryPrimitives.WriteDoubleBigEndian(_scratch, value);
            }
            else
            {
                BinaryPrimitives.WriteDoubleLittleEndian(_scratch, value);
            }

            _stream.Write(_scratch, 0, 8);
        }

        /// <summary>Writes a length-prefixed UTF-8 string, null-terminated when the version is 1.</summary>
        /// <param name="value">The string.</param>
        public void WriteString(string value)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(value ?? string.Empty);
            int length = _version == 1 ? bytes.Length + 1 : bytes.Length;
            WriteUInt64((ulong)length);
            _stream.Write(bytes, 0, bytes.Length);
            if (_version == 1)
            {
                _stream.WriteByte(0);
            }
        }

        /// <summary>Writes an item count, 32-bit for version 1 files and 64-bit otherwise.</summary>
        /// <param name="value">The count.</param>
        public void WriteCount(ulong value)
        {
            if (_version == 1)
            {
                WriteUInt32((uint)value);
            }
            else
            {
                WriteUInt64(value);
            }
        }

        /// <summary>Writes a run of zero bytes.</summary>
        /// <param name="count">How many bytes to write.</param>
        public void WriteZeros(long count)
        {
            for (long i = 0; i < count; i++)
            {
                _stream.WriteByte(0);
            }
        }

        /// <summary>Returns everything written so far.</summary>
        /// <returns>The bytes.</returns>
        public byte[] ToArray()
        {
            return _stream.ToArray();
        }
    }
}
