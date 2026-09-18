using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The fields of a message the codec does not model, kept as the exact bytes they arrived in - tag included - and in
/// the order they arrived. A message writes them back interleaved by field number, so a file that is read and written
/// unchanged comes out byte for byte the same even where it carries functions, sparse tensors or training information.
/// </summary>
internal sealed class ProtobufUnknownFields
{
    private readonly List<int> _fieldNumbers = new List<int>();
    private readonly List<byte[]> _payloads = new List<byte[]>();
    private int _writeCursor;

    /// <summary>Whether any unmodelled field was captured.</summary>
    internal bool IsEmpty => _payloads.Count == 0;

    /// <summary>The total number of bytes the captured fields occupy.</summary>
    internal int TotalSize
    {
        get
        {
            int size = 0;
            foreach (byte[] payload in _payloads)
            {
                size += payload.Length;
            }

            return size;
        }
    }

    /// <summary>Captures one field, tag and value together.</summary>
    /// <param name="fieldNumber">The field number read from the tag.</param>
    /// <param name="raw">The field's bytes, starting at its tag.</param>
    internal void Add(int fieldNumber, ReadOnlySpan<byte> raw)
    {
        _fieldNumbers.Add(fieldNumber);
        _payloads.Add(raw.ToArray());
    }

    /// <summary>Starts a new write pass over the captured fields.</summary>
    internal void BeginWrite() => _writeCursor = 0;

    /// <summary>
    /// Writes every captured field whose number is below the field the message is about to write, keeping the file's
    /// original field-number ordering.
    /// </summary>
    /// <param name="writer">The writer to append to.</param>
    /// <param name="nextKnownFieldNumber">The field number the message writes next.</param>
    internal void WriteBefore(ProtobufWriter writer, int nextKnownFieldNumber)
    {
        while (_writeCursor < _payloads.Count && _fieldNumbers[_writeCursor] < nextKnownFieldNumber)
        {
            writer.WriteRaw(_payloads[_writeCursor]);
            _writeCursor++;
        }
    }

    /// <summary>Writes whatever the interleaving pass has not written yet.</summary>
    /// <param name="writer">The writer to append to.</param>
    internal void WriteRemaining(ProtobufWriter writer)
    {
        while (_writeCursor < _payloads.Count)
        {
            writer.WriteRaw(_payloads[_writeCursor]);
            _writeCursor++;
        }
    }

    /// <summary>Copies the captured fields into another carrier.</summary>
    /// <returns>A carrier holding the same fields in the same order.</returns>
    internal ProtobufUnknownFields Clone()
    {
        ProtobufUnknownFields clone = new ProtobufUnknownFields();
        for (int i = 0; i < _payloads.Count; i++)
        {
            clone._fieldNumbers.Add(_fieldNumbers[i]);
            clone._payloads.Add((byte[])_payloads[i].Clone());
        }

        return clone;
    }
}
