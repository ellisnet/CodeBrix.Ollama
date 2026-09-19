using System;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The variable-length quantity - the format's own way of writing a number in as few bytes as it needs -
/// written and read, at every edge it has.
/// </summary>
/// <remarks>
/// It is the one thing in the format that every reader and every writer has to agree about byte for byte,
/// and the one place an off-by-one silently shifts a whole track in time.
/// </remarks>
public sealed class MidiFileWriterTests
{
    /// <summary>A number is written as the format defines it, at every width and edge.</summary>
    /// <param name="value">The number.</param>
    /// <param name="expected">The bytes the format asks for, as hexadecimal.</param>
    [Theory]
    [InlineData(0L, "00")]
    [InlineData(1L, "01")]
    [InlineData(64L, "40")]
    [InlineData(127L, "7F")]
    [InlineData(128L, "8100")]
    [InlineData(240L, "8170")]
    [InlineData(255L, "817F")]
    [InlineData(256L, "8200")]
    [InlineData(8192L, "C000")]
    [InlineData(16383L, "FF7F")]
    [InlineData(16384L, "818000")]
    [InlineData(2097151L, "FFFF7F")]
    [InlineData(2097152L, "81808000")]
    [InlineData(134217727L, "BFFFFF7F")]
    [InlineData(268435455L, "FFFFFF7F")]
    public void WriteVariableLengthQuantity_writes_what_the_format_defines(long value, string expected)
    {
        //Arrange
        using MemoryStream stream = new MemoryStream();

        //Act
        MidiFileWriter.WriteVariableLengthQuantity(stream, value);

        //Assert
        Convert.ToHexString(stream.ToArray()).Should().Be(expected);
    }

    /// <summary>What was written comes back.</summary>
    /// <param name="value">The number.</param>
    [Theory]
    [InlineData(0L)]
    [InlineData(127L)]
    [InlineData(128L)]
    [InlineData(16383L)]
    [InlineData(16384L)]
    [InlineData(134217727L)]
    [InlineData(268435455L)]
    public void ReadVariableLengthQuantity_reads_back_what_was_written(long value)
    {
        //Arrange
        using MemoryStream stream = new MemoryStream();
        MidiFileWriter.WriteVariableLengthQuantity(stream, value);
        byte[] bytes = stream.ToArray();
        int at = 0;

        //Act
        long read = MidiFileReader.ReadVariableLengthQuantity(bytes, ref at, bytes.Length);

        //Assert
        read.Should().Be(value);
        at.Should().Be(bytes.Length);
    }

    /// <summary>A number too large for four seven-bit bytes is refused rather than truncated.</summary>
    [Fact]
    public void WriteVariableLengthQuantity_of_too_large_a_number_is_refused()
    {
        //Arrange
        using MemoryStream stream = new MemoryStream();
        Action act = () => MidiFileWriter.WriteVariableLengthQuantity(stream, 268435456);

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("four");
    }

    /// <summary>A negative number is refused; a delta time cannot go backwards.</summary>
    [Fact]
    public void WriteVariableLengthQuantity_of_a_negative_number_is_refused()
    {
        //Arrange
        using MemoryStream stream = new MemoryStream();
        Action act = () => MidiFileWriter.WriteVariableLengthQuantity(stream, -1);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>A quantity that never ends is refused rather than read for ever.</summary>
    [Fact]
    public void ReadVariableLengthQuantity_of_five_continued_bytes_is_refused()
    {
        //Arrange
        byte[] bytes = { 0x81, 0x81, 0x81, 0x81, 0x81 };
        int at = 0;
        Action act = () =>
        {
            int position = at;
            MidiFileReader.ReadVariableLengthQuantity(bytes, ref position, bytes.Length);
        };

        //Act and assert
        act.Should().Throw<ArgumentException>().Which.Message.Should().Contain("four bytes");
    }

    /// <summary>A gap longer than a delta time can express is refused when the piece is written.</summary>
    [Fact]
    public void Write_of_a_gap_longer_than_a_delta_time_is_refused()
    {
        //Arrange
        MidiScore score = new MidiScore(480, new[]
        {
            MidiEvent.Note(0, 0, 0, 60, 90, 10),
            MidiEvent.Note(400000000, 0, 0, 62, 90, 10),
        });
        Action act = () => MidiFile.Write(score);

        //Act and assert
        act.Should().Throw<ArgumentException>();
    }
}
