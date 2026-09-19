// --------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation.  All rights reserved.
// Licensed under the MIT License.
// --------------------------------------------------------------------------

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager; //was previously: onnxruntime/python/tools/quantization/quant_utils.py@v1.30.0

/// <summary>
/// The pieces of ONNX Runtime's <c>quant_utils</c> the managed engine needs: the quantized ranges of each integer
/// type, the scale and zero-point formula, the element-wise quantization, the four-bit packing, and the tensor
/// reading and building that ONNX's own numpy helpers do on the Python side. The weight-quantization defaults - which
/// types are symmetric unless told otherwise - come from <c>base_quantizer.py</c> at the same tag.
/// </summary>
internal static class OnnxQuantizationUtilities
{
    /// <summary>The suffix ONNX Runtime appends to a tensor name when it quantizes it.</summary>
    internal const string TensorNameQuantSuffix = "_quantized";

    /// <summary>The producer name ONNX Runtime's quantizer stamps on the models it writes.</summary>
    internal const string ProducerName = "onnx.quantize";

    /// <summary>The producer version ONNX Runtime's quantizer stamps on the models it writes.</summary>
    internal const string ProducerVersion = "0.1.0";

    /// <summary>The domain of ONNX Runtime's own contributed operators.</summary>
    internal const string MicrosoftDomain = "com.microsoft";

    /// <summary>The metadata key that marks a model as having been through shape inference.</summary>
    internal const string InferMetadataKey = "onnx.infer";

    /// <summary>The metadata value that marks a model as having been through shape inference.</summary>
    internal const string InferMetadataValue = "onnxruntime.quant";

    /// <summary>
    /// The smallest tensor, in raw bytes, that the ONNX package moves into a side file when it is asked to save a model
    /// with external data and nothing names another threshold.
    /// </summary>
    /// <remarks>
    /// The package's own threshold is 1024, but it measures the size of the whole Python bytes object rather than the
    /// data in it, and such an object carries a 33-byte header - so the tensors it moves are the ones whose raw data is
    /// 991 bytes or more. The number matters wherever the managed engine has to write what a round trip through the
    /// ONNX tools would have written.
    /// </remarks>
    internal const int ExternalDataRawSizeThreshold = 991;

    /// <summary>Whether a quantized type is one ONNX Runtime treats as symmetric by default.</summary>
    /// <param name="quantizedType">The quantized element type.</param>
    /// <returns><see langword="true"/> for the signed integer types the tools list.</returns>
    internal static bool IsWeightSymmetric(OnnxTensorDataType quantizedType) =>
        quantizedType == OnnxTensorDataType.Int4
        || quantizedType == OnnxTensorDataType.Int8
        || quantizedType == OnnxTensorDataType.Int16
        || quantizedType == OnnxTensorDataType.Float8E4M3Fn;

    /// <summary>The smallest and largest value a quantized type can hold, in the range the caller asks for.</summary>
    /// <param name="quantizedType">The quantized element type.</param>
    /// <param name="reduceRange">Whether to use the seven-bit range.</param>
    /// <param name="symmetric">Whether to use the symmetric range.</param>
    /// <param name="minimum">Receives the smallest value.</param>
    /// <param name="maximum">Receives the largest value.</param>
    internal static void GetQuantizedRange(
        OnnxTensorDataType quantizedType,
        bool reduceRange,
        bool symmetric,
        out int minimum,
        out int maximum)
    {
        if (reduceRange)
        {
            switch (quantizedType)
            {
                case OnnxTensorDataType.UInt8:
                    minimum = 0;
                    maximum = 127;
                    return;
                case OnnxTensorDataType.Int8:
                    minimum = -64;
                    maximum = 64;
                    return;
                default:
                    throw new NotSupportedException(
                        $"The managed quantizer supports only 8-bit dynamic quantization; {quantizedType} was asked for.");
            }
        }

        if (symmetric && quantizedType == OnnxTensorDataType.Int8)
        {
            minimum = -127;
            maximum = 127;
            return;
        }

        switch (quantizedType)
        {
            case OnnxTensorDataType.UInt8:
                minimum = 0;
                maximum = 255;
                return;
            case OnnxTensorDataType.Int8:
                minimum = -128;
                maximum = 127;
                return;
            default:
                throw new NotSupportedException(
                    $"The managed quantizer supports only 8-bit dynamic quantization; {quantizedType} was asked for.");
        }
    }

    /// <summary>
    /// The scale and zero point for the relation <c>r = s(q - z)</c>, widening the real range to include zero and
    /// symmetrizing it when asked. The subtraction happens in the weight's own precision and the division in double
    /// precision, exactly as the Python does.
    /// </summary>
    /// <param name="realMinimum">The smallest real value.</param>
    /// <param name="realMaximum">The largest real value.</param>
    /// <param name="quantizedMinimum">The smallest quantized value.</param>
    /// <param name="quantizedMaximum">The largest quantized value.</param>
    /// <param name="symmetric">Whether to symmetrize the real range.</param>
    /// <param name="halfPrecision">Whether the weight's own precision is half rather than single.</param>
    /// <param name="zeroPoint">Receives the zero point.</param>
    /// <returns>The scale, narrowed to the weight's own precision.</returns>
    internal static float ComputeScaleAndZeroPoint(
        float realMinimum,
        float realMaximum,
        int quantizedMinimum,
        int quantizedMaximum,
        bool symmetric,
        bool halfPrecision,
        out int zeroPoint)
    {
        if (quantizedMinimum > 0 || quantizedMaximum < 0)
        {
            throw new InvalidOperationException(
                $"A quantized range must include zero; it was [{quantizedMinimum}, {quantizedMaximum}].");
        }

        float minimum = MathF.Min(realMinimum, 0f);
        float maximum = MathF.Max(realMaximum, 0f);
        if (symmetric)
        {
            float absoluteMaximum = MathF.Max(MathF.Abs(minimum), MathF.Abs(maximum));
            minimum = -absoluteMaximum;
            maximum = absoluteMaximum;
        }

        float difference = Narrow(maximum - minimum, halfPrecision);
        double range = difference;
        double quantizedRange = (double)quantizedMaximum - quantizedMinimum;
        double scale = range / quantizedRange;
        double tiny = halfPrecision ? 6.103515625e-05d : 1.1754943508222875e-38d;
        if (scale < tiny)
        {
            zeroPoint = 0;
            return Narrow(1f, halfPrecision);
        }

        if (symmetric)
        {
            zeroPoint = (int)Math.Round(((double)quantizedMinimum + quantizedMaximum) / 2.0d, MidpointRounding.ToEven);
        }
        else
        {
            zeroPoint = (int)Math.Round(quantizedMinimum - (minimum / scale), MidpointRounding.ToEven);
        }

        return Narrow((float)scale, halfPrecision);
    }

    /// <summary>
    /// Quantizes an array element by element with one scale and zero point, clamping to the type's full range - the
    /// reduced range never reaches this step.
    /// </summary>
    /// <param name="quantizedType">The quantized element type.</param>
    /// <param name="values">The values to quantize.</param>
    /// <param name="scale">The scale to divide by.</param>
    /// <param name="zeroPoint">The zero point to add.</param>
    /// <returns>The quantized values, one per element.</returns>
    internal static int[] QuantizeArray(
        OnnxTensorDataType quantizedType,
        ReadOnlySpan<float> values,
        float scale,
        int zeroPoint)
    {
        GetQuantizedRange(quantizedType, false, false, out int low, out int high);
        int[] quantized = new int[values.Length];
        for (int i = 0; i < values.Length; i++)
        {
            float scaled = MathF.Round(values[i] / scale) + zeroPoint;
            if (scaled < low)
            {
                scaled = low;
            }
            else if (scaled > high)
            {
                scaled = high;
            }

            quantized[i] = (int)scaled;
        }

        return quantized;
    }

    /// <summary>Packs already-ranged 8-bit values into 4-bit pairs, low nibble first.</summary>
    /// <param name="values">The 8-bit values to pack.</param>
    /// <returns>The packed bytes.</returns>
    internal static byte[] PackBytesTo4Bit(ReadOnlySpan<byte> values)
    {
        if (values.Length == 0)
        {
            return Array.Empty<byte>();
        }

        byte[] packed = new byte[(values.Length + 1) / 2];
        for (int i = 0; i < packed.Length; i++)
        {
            int first = values[i * 2] & 0xF;
            int second = (i * 2) + 1 < values.Length ? values[(i * 2) + 1] & 0xF : 0;
            packed[i] = (byte)(first | (second << 4));
        }

        return packed;
    }

    /// <summary>Reads a float or half-precision tensor's elements, following neither shape nor side files.</summary>
    /// <param name="tensor">The tensor to read.</param>
    /// <param name="rawData">The tensor's bytes, already resolved from any side file.</param>
    /// <returns>The elements as single-precision values.</returns>
    internal static float[] ReadFloatElements(OnnxTensorProto tensor, ReadOnlySpan<byte> rawData)
    {
        OnnxTensorDataType type = (OnnxTensorDataType)(tensor.DataType ?? 0);
        if (type != OnnxTensorDataType.Float && type != OnnxTensorDataType.Float16)
        {
            throw new NotSupportedException(
                $"The managed quantizer reads float and half-precision weights only; '{tensor.Name}' is {type}.");
        }

        if (rawData.Length > 0)
        {
            if (type == OnnxTensorDataType.Float)
            {
                float[] values = new float[rawData.Length / 4];
                for (int i = 0; i < values.Length; i++)
                {
                    values[i] = BitConverter.Int32BitsToSingle(
                        BinaryPrimitives.ReadInt32LittleEndian(rawData.Slice(i * 4, 4)));
                }

                return values;
            }

            float[] halves = new float[rawData.Length / 2];
            for (int i = 0; i < halves.Length; i++)
            {
                halves[i] = (float)BitConverter.UInt16BitsToHalf(
                    BinaryPrimitives.ReadUInt16LittleEndian(rawData.Slice(i * 2, 2)));
            }

            return halves;
        }

        if (type == OnnxTensorDataType.Float)
        {
            return tensor.FloatData.ToArray();
        }

        float[] fromInt32 = new float[tensor.Int32Data.Count];
        for (int i = 0; i < fromInt32.Length; i++)
        {
            fromInt32[i] = (float)BitConverter.UInt16BitsToHalf((ushort)tensor.Int32Data[i]);
        }

        return fromInt32;
    }

    /// <summary>The number of elements a tensor's shape describes.</summary>
    /// <param name="tensor">The tensor to measure.</param>
    /// <returns>The element count.</returns>
    internal static long ElementCount(OnnxTensorProto tensor)
    {
        long count = 1;
        foreach (long dimension in tensor.Dimensions)
        {
            count *= dimension;
        }

        return count;
    }

    /// <summary>Builds a tensor that carries its bytes in the raw-data field, the way numpy's helper does.</summary>
    /// <param name="name">The tensor's name.</param>
    /// <param name="dataType">The element type.</param>
    /// <param name="dimensions">The shape.</param>
    /// <param name="rawData">The bytes in their natural layout.</param>
    /// <returns>The new tensor.</returns>
    internal static OnnxTensorProto MakeRawTensor(
        string name,
        OnnxTensorDataType dataType,
        IReadOnlyList<long> dimensions,
        byte[] rawData)
    {
        OnnxTensorProto tensor = new OnnxTensorProto
        {
            Name = name,
            DataType = (int)dataType,
            RawData = rawData,
        };
        foreach (long dimension in dimensions)
        {
            tensor.Dimensions.Add(dimension);
        }

        return tensor;
    }

    /// <summary>
    /// Builds a tensor that carries float values in the typed field, the way ONNX's <c>make_tensor</c> does when it is
    /// handed a list rather than raw bytes.
    /// </summary>
    /// <param name="name">The tensor's name.</param>
    /// <param name="dataType">The element type, single or half precision.</param>
    /// <param name="dimensions">The shape.</param>
    /// <param name="values">The values to store.</param>
    /// <returns>The new tensor.</returns>
    internal static OnnxTensorProto MakeFloatTensor(
        string name,
        OnnxTensorDataType dataType,
        IReadOnlyList<long> dimensions,
        IReadOnlyList<float> values)
    {
        OnnxTensorProto tensor = new OnnxTensorProto
        {
            Name = name,
            DataType = (int)dataType,
        };
        foreach (long dimension in dimensions)
        {
            tensor.Dimensions.Add(dimension);
        }

        if (dataType == OnnxTensorDataType.Float)
        {
            foreach (float value in values)
            {
                tensor.FloatData.Add(value);
            }
        }
        else if (dataType == OnnxTensorDataType.Float16)
        {
            foreach (float value in values)
            {
                tensor.Int32Data.Add(BitConverter.HalfToUInt16Bits((Half)value));
            }
        }
        else
        {
            throw new NotSupportedException($"A scale tensor cannot have element type {dataType}.");
        }

        return tensor;
    }

    /// <summary>
    /// Builds a tensor that carries integer values in the typed field, the way ONNX's <c>make_tensor</c> does for the
    /// 8-bit types.
    /// </summary>
    /// <param name="name">The tensor's name.</param>
    /// <param name="dataType">The element type.</param>
    /// <param name="dimensions">The shape.</param>
    /// <param name="values">The values to store.</param>
    /// <returns>The new tensor.</returns>
    internal static OnnxTensorProto MakeInt32Tensor(
        string name,
        OnnxTensorDataType dataType,
        IReadOnlyList<long> dimensions,
        IReadOnlyList<int> values)
    {
        OnnxTensorProto tensor = new OnnxTensorProto
        {
            Name = name,
            DataType = (int)dataType,
        };
        foreach (long dimension in dimensions)
        {
            tensor.Dimensions.Add(dimension);
        }

        foreach (int value in values)
        {
            tensor.Int32Data.Add(value);
        }

        return tensor;
    }

    /// <summary>Turns single-precision values into the bytes a raw-data tensor of the given type carries.</summary>
    /// <param name="values">The values to write.</param>
    /// <param name="dataType">The element type, single or half precision.</param>
    /// <returns>The bytes in their natural layout.</returns>
    internal static byte[] FloatsToRawBytes(ReadOnlySpan<float> values, OnnxTensorDataType dataType)
    {
        if (dataType == OnnxTensorDataType.Float)
        {
            byte[] bytes = new byte[values.Length * 4];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteInt32LittleEndian(
                    bytes.AsSpan(i * 4, 4),
                    BitConverter.SingleToInt32Bits(values[i]));
            }

            return bytes;
        }

        if (dataType == OnnxTensorDataType.Float16)
        {
            byte[] bytes = new byte[values.Length * 2];
            for (int i = 0; i < values.Length; i++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(
                    bytes.AsSpan(i * 2, 2),
                    BitConverter.HalfToUInt16Bits((Half)values[i]));
            }

            return bytes;
        }

        throw new NotSupportedException($"A scale tensor cannot have element type {dataType}.");
    }

    /// <summary>Formats an integer the way ONNX's side-file entries do.</summary>
    /// <param name="value">The value to format.</param>
    /// <returns>The value in invariant decimal form.</returns>
    internal static string FormatNumber(long value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Reads the integer attribute of a node, falling back when the node does not carry it.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="fallback">The value to use when the attribute is absent.</param>
    /// <returns>The attribute's value.</returns>
    internal static long GetIntAttribute(OnnxNodeProto node, string name, long fallback)
    {
        OnnxAttributeProto attribute = node.FindAttribute(name);
        return attribute != null && attribute.Int.HasValue ? attribute.Int.Value : fallback;
    }

    /// <summary>Reads the float attribute of a node, falling back when the node does not carry it.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="name">The attribute name.</param>
    /// <param name="fallback">The value to use when the attribute is absent.</param>
    /// <returns>The attribute's value.</returns>
    internal static float GetFloatAttribute(OnnxNodeProto node, string name, float fallback)
    {
        OnnxAttributeProto attribute = node.FindAttribute(name);
        return attribute != null && attribute.Float.HasValue ? attribute.Float.Value : fallback;
    }

    /// <summary>Rejects a model the managed engine cannot read as the Python tools would.</summary>
    /// <param name="condition">The condition that must hold.</param>
    /// <param name="message">What to say when it does not.</param>
    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }

    private static float Narrow(float value, bool halfPrecision) => halfPrecision ? (float)(Half)value : value;
}
