using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Builds pickle streams by hand, opcode by opcode, so that the tests can present the restricted interpreter
/// with things no library would produce: a call to an operating-system function, an opcode from a protocol it
/// does not implement, a persistent id of the wrong shape.
/// </summary>
/// <remarks>
/// Nothing here is ever executed as a pickle by Python; the bytes exist only to be refused.
/// </remarks>
internal sealed class PickleBuilder
{
    private readonly MemoryStream _stream = new MemoryStream();

    /// <summary>Starts a protocol 2 stream.</summary>
    /// <param name="protocol">The protocol to declare.</param>
    /// <returns>This builder.</returns>
    internal PickleBuilder Protocol(byte protocol = 2)
    {
        _stream.WriteByte(0x80);
        _stream.WriteByte(protocol);
        return this;
    }

    /// <summary>Writes one raw opcode byte.</summary>
    /// <param name="opcode">The opcode.</param>
    /// <returns>This builder.</returns>
    internal PickleBuilder Opcode(byte opcode)
    {
        _stream.WriteByte(opcode);
        return this;
    }

    /// <summary>Writes an empty dictionary.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder EmptyDict()
    {
        return Opcode((byte)'}');
    }

    /// <summary>Writes an empty tuple.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder EmptyTuple()
    {
        return Opcode((byte)')');
    }

    /// <summary>Opens a group.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder Mark()
    {
        return Opcode((byte)'(');
    }

    /// <summary>Closes a group as a tuple.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder Tuple()
    {
        return Opcode((byte)'t');
    }

    /// <summary>Writes a unicode string.</summary>
    /// <param name="value">The string.</param>
    /// <returns>This builder.</returns>
    internal PickleBuilder Unicode(string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        _stream.WriteByte((byte)'X');
        _stream.Write(BitConverter.GetBytes(bytes.Length), 0, 4);
        _stream.Write(bytes, 0, bytes.Length);
        return this;
    }

    /// <summary>Writes a small whole number.</summary>
    /// <param name="value">The value, 0 to 255.</param>
    /// <returns>This builder.</returns>
    internal PickleBuilder Int(byte value)
    {
        _stream.WriteByte((byte)'K');
        _stream.WriteByte(value);
        return this;
    }

    /// <summary>Writes a reference to a global.</summary>
    /// <param name="module">The module name.</param>
    /// <param name="name">The name inside it.</param>
    /// <returns>This builder.</returns>
    internal PickleBuilder Global(string module, string name)
    {
        _stream.WriteByte((byte)'c');
        byte[] bytes = Encoding.UTF8.GetBytes(module + "\n" + name + "\n");
        _stream.Write(bytes, 0, bytes.Length);
        return this;
    }

    /// <summary>Calls whatever is on the stack with the argument tuple above it.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder Reduce()
    {
        return Opcode((byte)'R');
    }

    /// <summary>Resolves the tuple on the stack as a persistent id.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder PersistentId()
    {
        return Opcode((byte)'Q');
    }

    /// <summary>Fills the dictionary below the mark from the pairs above it.</summary>
    /// <returns>This builder.</returns>
    internal PickleBuilder SetItems()
    {
        return Opcode((byte)'u');
    }

    /// <summary>Ends the stream.</summary>
    /// <returns>The bytes.</returns>
    internal byte[] Stop()
    {
        _stream.WriteByte((byte)'.');
        return _stream.ToArray();
    }

    /// <summary>The bytes written so far, without an end marker.</summary>
    /// <returns>The bytes.</returns>
    internal byte[] ToArray()
    {
        return _stream.ToArray();
    }
}
