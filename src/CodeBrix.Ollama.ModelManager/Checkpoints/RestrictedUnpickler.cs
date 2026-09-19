using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The restricted interpreter for the pickle stream inside a PyTorch checkpoint.
/// </summary>
/// <remarks>
/// <para>
/// A pickle is a program. The ordinary interpreter for one may import any module and call any callable the
/// stream names, so reading an untrusted checkpoint with it hands the file's author the process. This
/// interpreter is not that: it implements only the opcodes a tensor state dictionary is built from, it never
/// imports anything, it never evaluates anything, and the only constructors it recognises are the four in
/// <see cref="PickleGlobal"/>. Every other opcode, every other global and every persistent id of an unexpected
/// shape raises <see cref="PickleRefusedException"/> with the construct named.
/// </para>
/// <para>
/// The result is a value tree of <see langword="null"/>, <see cref="bool"/>, <see cref="long"/>,
/// <see cref="double"/>, <see cref="string"/>, <c>object[]</c> (a tuple), <see cref="List{T}"/> of
/// <see cref="object"/> (a list), <see cref="OrderedDictionary{TKey,TValue}"/> keyed by string (a dictionary),
/// <see cref="PickleStorage"/> and <see cref="PickleTensor"/>. Nothing in it can execute.
/// </para>
/// </remarks>
internal sealed class RestrictedUnpickler
{
    /// <summary>The longest string the interpreter accepts.</summary>
    internal const int MaxStringLength = 16 << 20;

    /// <summary>The most items the interpreter accepts in one tuple, list or dictionary.</summary>
    internal const int MaxItemCount = 1 << 22;

    /// <summary>The most opcodes the interpreter will run before it gives up.</summary>
    internal const int MaxOpcodes = 64 << 20;

    private const byte OpMark = (byte)'(';
    private const byte OpStop = (byte)'.';
    private const byte OpPop = (byte)'0';
    private const byte OpPopMark = (byte)'1';
    private const byte OpDup = (byte)'2';
    private const byte OpBinInt = (byte)'J';
    private const byte OpBinInt1 = (byte)'K';
    private const byte OpBinInt2 = (byte)'M';
    private const byte OpNone = (byte)'N';
    private const byte OpBinPersId = (byte)'Q';
    private const byte OpReduce = (byte)'R';
    private const byte OpBinUnicode = (byte)'X';
    private const byte OpEmptyList = (byte)']';
    private const byte OpAppend = (byte)'a';
    private const byte OpBuild = (byte)'b';
    private const byte OpGlobal = (byte)'c';
    private const byte OpAppends = (byte)'e';
    private const byte OpBinGet = (byte)'h';
    private const byte OpLongBinGet = (byte)'j';
    private const byte OpEmptyTuple = (byte)')';
    private const byte OpBinFloat = (byte)'G';
    private const byte OpBinPut = (byte)'q';
    private const byte OpLongBinPut = (byte)'r';
    private const byte OpSetItem = (byte)'s';
    private const byte OpTuple = (byte)'t';
    private const byte OpSetItems = (byte)'u';
    private const byte OpEmptyDict = (byte)'}';
    private const byte OpProto = 0x80;
    private const byte OpNewObj = 0x81;
    private const byte OpTuple1 = 0x85;
    private const byte OpTuple2 = 0x86;
    private const byte OpTuple3 = 0x87;
    private const byte OpNewTrue = 0x88;
    private const byte OpNewFalse = 0x89;
    private const byte OpLong1 = 0x8a;
    private const byte OpLong4 = 0x8b;
    private const byte OpShortBinUnicode = 0x8c;
    private const byte OpBinUnicode8 = 0x8d;
    private const byte OpEmptySet = 0x8f;
    private const byte OpFrame = 0x95;
    private const byte OpMemoize = 0x94;
    private const byte OpStackGlobal = 0x93;
    private const byte OpNextBuffer = 0x97;

    private readonly byte[] _data;
    private readonly List<object> _stack = new List<object>();
    private readonly List<int> _marks = new List<int>();
    private readonly Dictionary<int, object> _memo = new Dictionary<int, object>();
    private int _position;
    private int _opcodes;

    private RestrictedUnpickler(byte[] data)
    {
        _data = data;
    }

    /// <summary>Interprets one pickle stream and returns the value it builds.</summary>
    /// <param name="data">The whole <c>data.pkl</c> entry.</param>
    /// <returns>The value tree.</returns>
    internal static object Load(byte[] data)
    {
        if (data == null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return new RestrictedUnpickler(data).Run();
    }

    private object Run()
    {
        while (true)
        {
            if (++_opcodes > MaxOpcodes)
            {
                throw new PickleRefusedException("opcode count",
                    "The pickle runs more than " + MaxOpcodes + " opcodes; it is refused as unreasonable.");
            }

            byte opcode = ReadByte();
            switch (opcode)
            {
                case OpProto:
                    ReadProtocol();
                    break;
                case OpMark:
                    _marks.Add(_stack.Count);
                    break;
                case OpStop:
                    return PopValue();
                case OpNone:
                    _stack.Add(null);
                    break;
                case OpNewTrue:
                    _stack.Add(true);
                    break;
                case OpNewFalse:
                    _stack.Add(false);
                    break;
                case OpBinInt:
                    _stack.Add((long)BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(4)));
                    break;
                case OpBinInt1:
                    _stack.Add((long)ReadByte());
                    break;
                case OpBinInt2:
                    _stack.Add((long)BinaryPrimitives.ReadUInt16LittleEndian(ReadBytes(2)));
                    break;
                case OpLong1:
                    _stack.Add(ReadLong(ReadByte()));
                    break;
                case OpLong4:
                    _stack.Add(ReadLong(ReadLengthPrefix(4, "long")));
                    break;
                case OpBinFloat:
                    _stack.Add(BinaryPrimitives.ReadDoubleBigEndian(ReadBytes(8)));
                    break;
                case OpBinUnicode:
                    _stack.Add(ReadUnicode(ReadLengthPrefix(4, "string")));
                    break;
                case OpEmptyTuple:
                    _stack.Add(Array.Empty<object>());
                    break;
                case OpTuple:
                    BuildTuple(PopMark());
                    break;
                case OpTuple1:
                    BuildTuple(_stack.Count - 1);
                    break;
                case OpTuple2:
                    BuildTuple(_stack.Count - 2);
                    break;
                case OpTuple3:
                    BuildTuple(_stack.Count - 3);
                    break;
                case OpEmptyList:
                    _stack.Add(new List<object>());
                    break;
                case OpEmptyDict:
                    _stack.Add(new OrderedDictionary<string, object>(StringComparer.Ordinal));
                    break;
                case OpAppend:
                    Append(_stack.Count - 1);
                    break;
                case OpAppends:
                    Append(PopMark());
                    break;
                case OpSetItem:
                    SetItems(_stack.Count - 2);
                    break;
                case OpSetItems:
                    SetItems(PopMark());
                    break;
                case OpBinPut:
                    _memo[ReadByte()] = Peek();
                    break;
                case OpLongBinPut:
                    _memo[ReadLengthPrefix(4, "memo key")] = Peek();
                    break;
                case OpBinGet:
                    _stack.Add(GetMemo(ReadByte()));
                    break;
                case OpLongBinGet:
                    _stack.Add(GetMemo(ReadLengthPrefix(4, "memo key")));
                    break;
                case OpGlobal:
                    _stack.Add(ResolveGlobal(ReadLine(), ReadLine()));
                    break;
                case OpBinPersId:
                    _stack.Add(ResolvePersistentId(PopValue()));
                    break;
                case OpReduce:
                    Reduce();
                    break;
                default:
                    throw Refuse(opcode);
            }
        }
    }

    private void ReadProtocol()
    {
        byte protocol = ReadByte();
        if (protocol > 2)
        {
            throw new PickleRefusedException("PROTO " + protocol,
                "The checkpoint's pickle declares protocol " + protocol + ". This reader implements the " +
                "protocol 2 opcodes a tensor state dictionary needs and nothing beyond them.");
        }
    }

    private static PickleRefusedException Refuse(byte opcode)
    {
        string name = OpcodeName(opcode);
        return new PickleRefusedException(name,
            "The checkpoint's pickle uses the opcode " + name + ", which this reader does not implement. " +
            "Only the opcodes a tensor state dictionary is built from are accepted.");
    }

    private static string OpcodeName(byte opcode)
    {
        switch (opcode)
        {
            case OpPop: return "POP";
            case OpPopMark: return "POP_MARK";
            case OpDup: return "DUP";
            case OpBuild: return "BUILD";
            case OpNewObj: return "NEWOBJ";
            case OpStackGlobal: return "STACK_GLOBAL";
            case OpMemoize: return "MEMOIZE";
            case OpFrame: return "FRAME";
            case OpShortBinUnicode: return "SHORT_BINUNICODE";
            case OpBinUnicode8: return "BINUNICODE8";
            case OpEmptySet: return "EMPTY_SET";
            case OpNextBuffer: return "NEXT_BUFFER";
            case (byte)'I': return "INT";
            case (byte)'L': return "LONG";
            case (byte)'F': return "FLOAT";
            case (byte)'S': return "STRING";
            case (byte)'T': return "BINSTRING";
            case (byte)'U': return "SHORT_BINSTRING";
            case (byte)'V': return "UNICODE";
            case (byte)'P': return "PERSID";
            case (byte)'i': return "INST";
            case (byte)'o': return "OBJ";
            case (byte)'g': return "GET";
            case (byte)'p': return "PUT";
            case (byte)'l': return "LIST";
            case (byte)'d': return "DICT";
            default:
                return "0x" + opcode.ToString("x2", CultureInfo.InvariantCulture);
        }
    }

    private static PickleGlobalReference ResolveGlobal(string module, string name)
    {
        string qualified = module + "." + name;
        if (string.Equals(module, "torch._utils", StringComparison.Ordinal))
        {
            if (string.Equals(name, "_rebuild_tensor_v2", StringComparison.Ordinal))
            {
                return new PickleGlobalReference(PickleGlobal.RebuildTensorV2, CheckpointDataType.F32, qualified);
            }

            if (string.Equals(name, "_rebuild_parameter", StringComparison.Ordinal))
            {
                return new PickleGlobalReference(PickleGlobal.RebuildParameter, CheckpointDataType.F32, qualified);
            }
        }
        else if (string.Equals(module, "collections", StringComparison.Ordinal)
            && string.Equals(name, "OrderedDict", StringComparison.Ordinal))
        {
            return new PickleGlobalReference(PickleGlobal.OrderedDict, CheckpointDataType.F32, qualified);
        }
        else if (string.Equals(module, "torch", StringComparison.Ordinal)
            && CheckpointDataTypes.TryParseTorchStorage(name, out CheckpointDataType dataType))
        {
            return new PickleGlobalReference(PickleGlobal.Storage, dataType, qualified);
        }

        throw new PickleRefusedException(qualified,
            "The checkpoint's pickle asks for the global \"" + qualified + "\". This reader imports nothing " +
            "and recognises only torch._utils._rebuild_tensor_v2, torch._utils._rebuild_parameter, " +
            "collections.OrderedDict and the torch storage classes.");
    }

    private static PickleStorage ResolvePersistentId(object id)
    {
        var parts = id as object[];
        if (parts == null || parts.Length != 5
            || !(parts[0] is string kind) || !string.Equals(kind, "storage", StringComparison.Ordinal))
        {
            throw new PickleRefusedException("persistent id",
                "The checkpoint's pickle carries a persistent id that is not a five-part storage tuple. " +
                "Only ('storage', <storage class>, <key>, <device>, <element count>) is accepted.");
        }

        var storageType = parts[1] as PickleGlobalReference;
        if (storageType == null || storageType.Global != PickleGlobal.Storage)
        {
            throw new PickleRefusedException("persistent id",
                "The second part of a storage persistent id is not one of the torch storage classes.");
        }

        if (!(parts[2] is string key) || !(parts[3] is string location) || !(parts[4] is long elementCount))
        {
            throw new PickleRefusedException("persistent id",
                "A storage persistent id must name a string key, a string device and a whole element count.");
        }

        if (elementCount < 0)
        {
            throw new PickleRefusedException("persistent id",
                "A storage persistent id declares " + elementCount + " elements.");
        }

        return new PickleStorage(key, storageType.DataType, location, elementCount);
    }

    private void Reduce()
    {
        object[] arguments = PopValue() as object[];
        var callable = PopValue() as PickleGlobalReference;
        if (callable == null)
        {
            throw new PickleRefusedException("REDUCE",
                "The checkpoint's pickle calls something that is not one of the allowed globals.");
        }

        if (arguments == null)
        {
            throw new PickleRefusedException("REDUCE",
                "The checkpoint's pickle calls \"" + callable.QualifiedName + "\" with something that is not " +
                "an argument tuple.");
        }

        switch (callable.Global)
        {
            case PickleGlobal.OrderedDict:
                if (arguments.Length != 0)
                {
                    throw new PickleRefusedException(callable.QualifiedName,
                        "collections.OrderedDict is accepted only with an empty argument tuple.");
                }

                _stack.Add(new OrderedDictionary<string, object>(StringComparer.Ordinal));
                break;
            case PickleGlobal.RebuildTensorV2:
                _stack.Add(RebuildTensor(arguments));
                break;
            case PickleGlobal.RebuildParameter:
                if (arguments.Length < 1)
                {
                    throw new PickleRefusedException(callable.QualifiedName,
                        "torch._utils._rebuild_parameter takes the tensor it wraps as its first argument.");
                }

                _stack.Add(arguments[0]);
                break;
            default:
                throw new PickleRefusedException(callable.QualifiedName,
                    "A torch storage class may appear only inside a persistent id, never as a call.");
        }
    }

    private static PickleTensor RebuildTensor(object[] arguments)
    {
        if (arguments.Length < 4)
        {
            throw new PickleRefusedException("torch._utils._rebuild_tensor_v2",
                "torch._utils._rebuild_tensor_v2 takes at least a storage, an offset, a shape and strides; " +
                "this call has " + arguments.Length + " arguments.");
        }

        var storage = arguments[0] as PickleStorage;
        if (storage == null)
        {
            throw new PickleRefusedException("torch._utils._rebuild_tensor_v2",
                "The first argument of torch._utils._rebuild_tensor_v2 is not a storage.");
        }

        if (!(arguments[1] is long storageOffset) || storageOffset < 0)
        {
            throw new PickleRefusedException("torch._utils._rebuild_tensor_v2",
                "The storage offset of torch._utils._rebuild_tensor_v2 is not a whole number that is zero or more.");
        }

        return new PickleTensor(storage, storageOffset, ReadDimensions(arguments[2], "shape"),
            ReadDimensions(arguments[3], "strides"));
    }

    private static IReadOnlyList<long> ReadDimensions(object value, string what)
    {
        var result = new List<long>();
        if (value is object[] tuple)
        {
            for (int i = 0; i < tuple.Length; i++)
            {
                result.Add(RequireDimension(tuple[i], what));
            }

            return result;
        }

        if (value is List<object> list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                result.Add(RequireDimension(list[i], what));
            }

            return result;
        }

        throw new PickleRefusedException("torch._utils._rebuild_tensor_v2",
            "The " + what + " of torch._utils._rebuild_tensor_v2 is not a sequence of whole numbers.");
    }

    private static long RequireDimension(object value, string what)
    {
        if (!(value is long number) || number < 0)
        {
            throw new PickleRefusedException("torch._utils._rebuild_tensor_v2",
                "The " + what + " of torch._utils._rebuild_tensor_v2 holds a value that is not a whole number " +
                "that is zero or more.");
        }

        return number;
    }

    private void BuildTuple(int start)
    {
        if (start < 0 || start > _stack.Count)
        {
            throw new PickleRefusedException("TUPLE", "The checkpoint's pickle builds a tuple from an empty stack.");
        }

        int count = _stack.Count - start;
        var tuple = new object[count];
        _stack.CopyTo(start, tuple, 0, count);
        _stack.RemoveRange(start, count);
        _stack.Add(tuple);
    }

    private void Append(int start)
    {
        if (start < 1 || start > _stack.Count)
        {
            throw new PickleRefusedException("APPENDS",
                "The checkpoint's pickle appends to something that is not on the stack.");
        }

        var list = _stack[start - 1] as List<object>;
        if (list == null)
        {
            throw new PickleRefusedException("APPENDS",
                "The checkpoint's pickle appends to something that is not a list.");
        }

        int count = _stack.Count - start;
        if (list.Count + count > MaxItemCount)
        {
            throw new PickleRefusedException("APPENDS",
                "The checkpoint's pickle builds a list of more than " + MaxItemCount + " items.");
        }

        for (int i = 0; i < count; i++)
        {
            list.Add(_stack[start + i]);
        }

        _stack.RemoveRange(start, count);
    }

    private void SetItems(int start)
    {
        if (start < 1 || start > _stack.Count)
        {
            throw new PickleRefusedException("SETITEMS",
                "The checkpoint's pickle sets items on something that is not on the stack.");
        }

        var dictionary = _stack[start - 1] as OrderedDictionary<string, object>;
        if (dictionary == null)
        {
            throw new PickleRefusedException("SETITEMS",
                "The checkpoint's pickle sets items on something that is not a dictionary.");
        }

        int count = _stack.Count - start;
        if ((count & 1) != 0)
        {
            throw new PickleRefusedException("SETITEMS",
                "The checkpoint's pickle sets dictionary items from an odd number of stack entries.");
        }

        if (dictionary.Count + (count / 2) > MaxItemCount)
        {
            throw new PickleRefusedException("SETITEMS",
                "The checkpoint's pickle builds a dictionary of more than " + MaxItemCount + " entries.");
        }

        for (int i = 0; i < count; i += 2)
        {
            if (!(_stack[start + i] is string key))
            {
                throw new PickleRefusedException("SETITEMS",
                    "The checkpoint's pickle uses a dictionary key that is not a string.");
            }

            dictionary[key] = _stack[start + i + 1];
        }

        _stack.RemoveRange(start, count);
    }

    private int PopMark()
    {
        if (_marks.Count == 0)
        {
            throw new PickleRefusedException("MARK",
                "The checkpoint's pickle closes a group it never opened.");
        }

        int mark = _marks[_marks.Count - 1];
        _marks.RemoveAt(_marks.Count - 1);
        return mark;
    }

    private object PopValue()
    {
        if (_stack.Count == 0)
        {
            throw new PickleRefusedException("stack",
                "The checkpoint's pickle takes a value off an empty stack.");
        }

        object value = _stack[_stack.Count - 1];
        _stack.RemoveAt(_stack.Count - 1);
        return value;
    }

    private object Peek()
    {
        if (_stack.Count == 0)
        {
            throw new PickleRefusedException("stack",
                "The checkpoint's pickle memoizes a value that is not on the stack.");
        }

        return _stack[_stack.Count - 1];
    }

    private object GetMemo(int key)
    {
        if (!_memo.TryGetValue(key, out object value))
        {
            throw new PickleRefusedException("BINGET",
                "The checkpoint's pickle reads memo entry " + key + ", which was never written.");
        }

        return value;
    }

    private byte ReadByte()
    {
        if (_position >= _data.Length)
        {
            throw new CheckpointFormatException("The checkpoint's pickle ends part way through an opcode.");
        }

        return _data[_position++];
    }

    private ReadOnlySpan<byte> ReadBytes(int count)
    {
        if (count < 0 || _data.Length - _position < count)
        {
            throw new CheckpointFormatException("The checkpoint's pickle ends part way through a value.");
        }

        var span = new ReadOnlySpan<byte>(_data, _position, count);
        _position += count;
        return span;
    }

    private int ReadLengthPrefix(int width, string what)
    {
        int value = BinaryPrimitives.ReadInt32LittleEndian(ReadBytes(width));
        if (value < 0 || value > MaxStringLength)
        {
            throw new PickleRefusedException("length",
                "The checkpoint's pickle declares a " + what + " of " + value + " bytes, which is outside the " +
                "range this reader accepts.");
        }

        return value;
    }

    private string ReadUnicode(int length)
    {
        return Encoding.UTF8.GetString(ReadBytes(length));
    }

    private long ReadLong(int length)
    {
        if (length == 0)
        {
            return 0L;
        }

        if (length > 8)
        {
            throw new PickleRefusedException("LONG1",
                "The checkpoint's pickle carries an integer of " + length + " bytes, wider than this reader " +
                "accepts.");
        }

        ReadOnlySpan<byte> bytes = ReadBytes(length);
        var value = new BigInteger(bytes, isUnsigned: false, isBigEndian: false);
        if (value > long.MaxValue || value < long.MinValue)
        {
            throw new PickleRefusedException("LONG1",
                "The checkpoint's pickle carries an integer outside the signed 64-bit range.");
        }

        return (long)value;
    }

    private string ReadLine()
    {
        int start = _position;
        while (_position < _data.Length && _data[_position] != (byte)'\n')
        {
            _position++;
        }

        if (_position >= _data.Length)
        {
            throw new CheckpointFormatException("The checkpoint's pickle ends part way through a global name.");
        }

        int length = _position - start;
        if (length > 1024)
        {
            throw new PickleRefusedException("GLOBAL",
                "The checkpoint's pickle names a global whose name is " + length + " bytes long.");
        }

        _position++;
        return Encoding.UTF8.GetString(_data, start, length);
    }
}
