using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The names and widths of every <see cref="CheckpointDataType"/>, and the lookups the two container readers
/// use to turn a safetensors dtype string or a PyTorch storage class into one.
/// </summary>
internal static class CheckpointDataTypes
{
    /// <summary>Returns the number of bytes one value of a type occupies.</summary>
    /// <param name="dataType">The element type.</param>
    /// <returns>The width in bytes.</returns>
    internal static int GetByteWidth(CheckpointDataType dataType)
    {
        switch (dataType)
        {
            case CheckpointDataType.F64:
            case CheckpointDataType.I64:
                return 8;
            case CheckpointDataType.F32:
            case CheckpointDataType.I32:
                return 4;
            case CheckpointDataType.F16:
            case CheckpointDataType.BF16:
            case CheckpointDataType.I16:
                return 2;
            default:
                return 1;
        }
    }

    /// <summary>Returns the safetensors spelling of a type.</summary>
    /// <param name="dataType">The element type.</param>
    /// <returns>The name safetensors writes in its header.</returns>
    internal static string GetName(CheckpointDataType dataType)
    {
        switch (dataType)
        {
            case CheckpointDataType.F64: return "F64";
            case CheckpointDataType.F32: return "F32";
            case CheckpointDataType.F16: return "F16";
            case CheckpointDataType.BF16: return "BF16";
            case CheckpointDataType.I64: return "I64";
            case CheckpointDataType.I32: return "I32";
            case CheckpointDataType.I16: return "I16";
            case CheckpointDataType.I8: return "I8";
            case CheckpointDataType.U8: return "U8";
            default: return "BOOL";
        }
    }

    /// <summary>Maps a safetensors header dtype string on to an element type.</summary>
    /// <param name="name">The dtype string, for example <c>BF16</c>.</param>
    /// <param name="dataType">Receives the element type when the name is known.</param>
    /// <returns><see langword="true"/> when the name is one this reader supports.</returns>
    internal static bool TryParseSafetensors(string name, out CheckpointDataType dataType)
    {
        switch (name)
        {
            case "F64": dataType = CheckpointDataType.F64; return true;
            case "F32": dataType = CheckpointDataType.F32; return true;
            case "F16": dataType = CheckpointDataType.F16; return true;
            case "BF16": dataType = CheckpointDataType.BF16; return true;
            case "I64": dataType = CheckpointDataType.I64; return true;
            case "I32": dataType = CheckpointDataType.I32; return true;
            case "I16": dataType = CheckpointDataType.I16; return true;
            case "I8": dataType = CheckpointDataType.I8; return true;
            case "U8": dataType = CheckpointDataType.U8; return true;
            case "BOOL": dataType = CheckpointDataType.Bool; return true;
            default: dataType = CheckpointDataType.F32; return false;
        }
    }

    /// <summary>Maps a PyTorch storage class name on to an element type.</summary>
    /// <param name="typeName">The unqualified class name, for example <c>BFloat16Storage</c>.</param>
    /// <param name="dataType">Receives the element type when the class is one of the allowed storages.</param>
    /// <returns><see langword="true"/> when the class is on the allow-list.</returns>
    internal static bool TryParseTorchStorage(string typeName, out CheckpointDataType dataType)
    {
        switch (typeName)
        {
            case "DoubleStorage": dataType = CheckpointDataType.F64; return true;
            case "FloatStorage": dataType = CheckpointDataType.F32; return true;
            case "HalfStorage": dataType = CheckpointDataType.F16; return true;
            case "BFloat16Storage": dataType = CheckpointDataType.BF16; return true;
            case "LongStorage": dataType = CheckpointDataType.I64; return true;
            case "IntStorage": dataType = CheckpointDataType.I32; return true;
            case "ShortStorage": dataType = CheckpointDataType.I16; return true;
            case "CharStorage": dataType = CheckpointDataType.I8; return true;
            case "ByteStorage": dataType = CheckpointDataType.U8; return true;
            case "BoolStorage": dataType = CheckpointDataType.Bool; return true;
            default: dataType = CheckpointDataType.F32; return false;
        }
    }

    /// <summary>Multiplies a shape out, refusing an overflow rather than wrapping.</summary>
    /// <param name="shape">The dimensions.</param>
    /// <param name="what">What is being counted, for the exception message.</param>
    /// <returns>The number of values.</returns>
    internal static long ElementCount(IReadOnlyList<long> shape, string what)
    {
        long count = 1;
        for (int i = 0; i < shape.Count; i++)
        {
            long dimension = shape[i];
            if (dimension < 0)
            {
                throw new CheckpointFormatException(
                    "The shape of " + what + " has a negative dimension " + dimension + ".");
            }

            if (dimension != 0 && count > long.MaxValue / dimension)
            {
                throw new CheckpointFormatException("The element count of " + what + " overflows.");
            }

            count *= dimension;
        }

        return count;
    }
}
