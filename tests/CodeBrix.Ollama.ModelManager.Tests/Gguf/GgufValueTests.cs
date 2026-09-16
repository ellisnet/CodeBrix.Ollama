// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: fs/gguf/keyvalue.go at commit a43fad18.
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the conversion rules of one key-value's value. They are deliberately narrow: a value only reads
/// back through the accessor of its own family, exactly as Ollama's reflection-based accessors behave.
/// </summary>
public sealed class GgufValueTests
{
    private static readonly GgufValue[] SignedScalars =
    {
        GgufValue.CreateScalar(GgufValueType.Int8, (sbyte)42),
        GgufValue.CreateScalar(GgufValueType.Int16, (short)42),
        GgufValue.CreateScalar(GgufValueType.Int32, 42),
        GgufValue.CreateScalar(GgufValueType.Int64, 42L)
    };

    private static readonly GgufValue[] UnsignedScalars =
    {
        GgufValue.CreateScalar(GgufValueType.UInt8, (byte)42),
        GgufValue.CreateScalar(GgufValueType.UInt16, (ushort)42),
        GgufValue.CreateScalar(GgufValueType.UInt32, 42u),
        GgufValue.CreateScalar(GgufValueType.UInt64, 42UL)
    };

    private static readonly GgufValue[] FloatScalars =
    {
        GgufValue.CreateScalar(GgufValueType.Float32, 42f),
        GgufValue.CreateScalar(GgufValueType.Float64, 42d)
    };

    private static readonly GgufValue[] OtherScalars =
    {
        GgufValue.CreateScalar(GgufValueType.String, "42"),
        GgufValue.CreateScalar(GgufValueType.Bool, true)
    };

    [Fact]
    public void AsInt64_ReturnsTheValue_OnlyForSignedIntegers()
    {
        //Arrange
        //Act
        //Assert
        foreach (GgufValue value in SignedScalars)
        {
            value.AsInt64().Should().Be(42L);
            value.TryGetInt64(out long parsed).Should().BeTrue();
            parsed.Should().Be(42L);
        }

        foreach (GgufValue value in UnsignedScalars)
        {
            value.AsInt64().Should().Be(0L);
            value.TryGetInt64(out long _).Should().BeFalse();
        }

        foreach (GgufValue value in FloatScalars)
        {
            value.AsInt64().Should().Be(0L);
        }

        foreach (GgufValue value in OtherScalars)
        {
            value.AsInt64().Should().Be(0L);
        }
    }

    [Fact]
    public void AsUInt64_ReturnsTheValue_OnlyForUnsignedIntegers()
    {
        //Arrange
        //Act
        //Assert
        foreach (GgufValue value in UnsignedScalars)
        {
            value.AsUInt64().Should().Be(42UL);
            value.TryGetUInt64(out ulong parsed).Should().BeTrue();
            parsed.Should().Be(42UL);
        }

        foreach (GgufValue value in SignedScalars)
        {
            value.AsUInt64().Should().Be(0UL);
            value.TryGetUInt64(out ulong _).Should().BeFalse();
        }

        foreach (GgufValue value in FloatScalars)
        {
            value.AsUInt64().Should().Be(0UL);
        }

        foreach (GgufValue value in OtherScalars)
        {
            value.AsUInt64().Should().Be(0UL);
        }
    }

    [Fact]
    public void AsDouble_ReturnsTheValue_OnlyForFloats()
    {
        //Arrange
        //Act
        //Assert
        foreach (GgufValue value in FloatScalars)
        {
            value.AsDouble().Should().Be(42d);
            value.TryGetDouble(out double parsed).Should().BeTrue();
            parsed.Should().Be(42d);
        }

        foreach (GgufValue value in SignedScalars)
        {
            value.AsDouble().Should().Be(0d);
            value.TryGetDouble(out double _).Should().BeFalse();
        }

        foreach (GgufValue value in UnsignedScalars)
        {
            value.AsDouble().Should().Be(0d);
        }
    }

    [Fact]
    public void AsString_ReturnsTheValue_OnlyForStrings()
    {
        //Arrange
        var text = GgufValue.CreateScalar(GgufValueType.String, "hello");

        //Act
        //Assert
        text.AsString().Should().Be("hello");
        GgufValue.CreateScalar(GgufValueType.UInt32, 42u).AsString().Should().BeEmpty();
        GgufValue.CreateScalar(GgufValueType.Bool, true).AsString().Should().BeEmpty();
    }

    [Fact]
    public void AsBoolean_ReturnsTheValue_OnlyForBooleans()
    {
        //Arrange
        //Act
        //Assert
        GgufValue.CreateScalar(GgufValueType.Bool, true).AsBoolean().Should().BeTrue();
        GgufValue.CreateScalar(GgufValueType.Bool, false).AsBoolean().Should().BeFalse();
        GgufValue.CreateScalar(GgufValueType.UInt8, (byte)1).AsBoolean().Should().BeFalse();
        GgufValue.CreateScalar(GgufValueType.String, "true").AsBoolean().Should().BeFalse();
    }

    [Fact]
    public void AsInt64Array_WidensEverySignedArray_AndRejectsOthers()
    {
        //Arrange
        var signed = new GgufValue[]
        {
            GgufValue.CreateArray(GgufValueType.Int8, 1, new sbyte[] { 42 }),
            GgufValue.CreateArray(GgufValueType.Int16, 1, new short[] { 42 }),
            GgufValue.CreateArray(GgufValueType.Int32, 1, new[] { 42 }),
            GgufValue.CreateArray(GgufValueType.Int64, 1, new[] { 42L })
        };

        //Act
        //Assert
        foreach (GgufValue value in signed)
        {
            value.AsInt64Array().Should().Equal(new[] { 42L });
        }

        GgufValue.CreateArray(GgufValueType.UInt32, 1, new[] { 42u }).AsInt64Array().Should().BeNull();
        GgufValue.CreateArray(GgufValueType.String, 1, new[] { "42" }).AsInt64Array().Should().BeNull();
    }

    [Fact]
    public void AsUInt64Array_WidensEveryUnsignedArray_AndRejectsOthers()
    {
        //Arrange
        var unsigned = new GgufValue[]
        {
            GgufValue.CreateArray(GgufValueType.UInt8, 1, new byte[] { 42 }),
            GgufValue.CreateArray(GgufValueType.UInt16, 1, new ushort[] { 42 }),
            GgufValue.CreateArray(GgufValueType.UInt32, 1, new[] { 42u }),
            GgufValue.CreateArray(GgufValueType.UInt64, 1, new[] { 42UL })
        };

        //Act
        //Assert
        foreach (GgufValue value in unsigned)
        {
            value.AsUInt64Array().Should().Equal(new[] { 42UL });
        }

        GgufValue.CreateArray(GgufValueType.Int32, 1, new[] { 42 }).AsUInt64Array().Should().BeNull();
        GgufValue.CreateArray(GgufValueType.Bool, 1, new[] { true }).AsUInt64Array().Should().BeNull();
    }

    [Fact]
    public void AsDoubleArray_WidensEveryFloatArray_AndRejectsOthers()
    {
        //Arrange
        //Act
        //Assert
        GgufValue.CreateArray(GgufValueType.Float32, 1, new[] { 42f }).AsDoubleArray().Should().Equal(new[] { 42d });
        GgufValue.CreateArray(GgufValueType.Float64, 1, new[] { 42d }).AsDoubleArray().Should().Equal(new[] { 42d });
        GgufValue.CreateArray(GgufValueType.Int32, 1, new[] { 42 }).AsDoubleArray().Should().BeNull();
    }

    [Fact]
    public void AsStringArrayAndAsBooleanArray_ReturnOnlyTheirOwnKind()
    {
        //Arrange
        var strings = GgufValue.CreateArray(GgufValueType.String, 2, new[] { "hello", "world" });
        var booleans = GgufValue.CreateArray(GgufValueType.Bool, 2, new[] { true, false });

        //Act
        //Assert
        strings.AsStringArray().Should().Equal(new[] { "hello", "world" });
        strings.AsBooleanArray().Should().BeNull();
        booleans.AsBooleanArray().Should().Equal(new[] { true, false });
        booleans.AsStringArray().Should().BeNull();
    }

    [Fact]
    public void ArrayAccessors_ReturnNull_ForScalars()
    {
        //Arrange
        var scalar = GgufValue.CreateScalar(GgufValueType.UInt32, 42u);

        //Act
        //Assert
        scalar.AsInt64Array().Should().BeNull();
        scalar.AsUInt64Array().Should().BeNull();
        scalar.AsDoubleArray().Should().BeNull();
        scalar.AsStringArray().Should().BeNull();
        scalar.AsBooleanArray().Should().BeNull();
        scalar.IsArray.Should().BeFalse();
    }

    [Fact]
    public void CreateOmittedArray_KeepsTheLength_ButNoValues()
    {
        //Arrange
        var omitted = GgufValue.CreateOmittedArray(GgufValueType.String, 50_000);

        //Act
        //Assert
        omitted.IsArray.Should().BeTrue();
        omitted.IsOmittedArray.Should().BeTrue();
        omitted.ArrayLength.Should().Be(50_000L);
        omitted.ArrayElementType.Should().Be(GgufValueType.String);
        omitted.Type.Should().Be(GgufValueType.Array);
        omitted.RawValue.Should().BeNull();
        omitted.AsStringArray().Should().BeNull();
        omitted.AsString().Should().BeEmpty();
        omitted.ToString().Should().Be("[50000 items]");
    }

    [Theory]
    [InlineData(42u, "42")]
    [InlineData(true, "True")]
    [InlineData("hello", "hello")]
    [InlineData(1.5d, "1.5")]
    public void ToString_PrintsTheScalar(object raw, string expected)
    {
        //Arrange
        var value = GgufValue.CreateScalar(GgufValueType.UInt32, raw);

        //Act
        //Assert
        value.ToString().Should().Be(expected);
    }

    [Fact]
    public void ToString_PrintsTheItemCount_ForAnArray()
    {
        //Arrange
        var value = GgufValue.CreateArray(GgufValueType.Int32, 3, new[] { 1, 2, 3 });

        //Act
        //Assert
        value.ToString().Should().Be("[3 items]");
        value.ArrayElementType.Should().Be(GgufValueType.Int32);
        value.Type.Should().Be(GgufValueType.Array);
    }
}
