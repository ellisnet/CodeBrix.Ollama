using System;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the assembler that turns a token stream's bytes into text, which is the only thing standing
/// between a byte-fallback vocabulary and a stream full of replacement characters.
/// </summary>
public sealed class Utf8AssemblerTests
{
    /// <summary>Plain ASCII comes straight back.</summary>
    [Fact]
    public void Append_returns_ascii_unchanged()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();

        //Act
        string text = assembler.Append(Encoding.UTF8.GetBytes("Hello"));

        //Assert
        text.Should().Be("Hello");
        assembler.Flush().Should().BeEmpty();
    }

    /// <summary>A two-byte character split over two tokens arrives whole, once.</summary>
    [Fact]
    public void Append_holds_a_two_byte_character_until_it_is_complete()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();
        byte[] bytes = Encoding.UTF8.GetBytes("é");
        bytes.Should().HaveCount(2);

        //Act
        string first = assembler.Append(new[] { bytes[0] });
        string second = assembler.Append(new[] { bytes[1] });

        //Assert
        first.Should().BeEmpty();
        second.Should().Be("é");
    }

    /// <summary>A four-byte character delivered one byte at a time arrives whole on the fourth.</summary>
    [Fact]
    public void Append_holds_a_four_byte_character_until_its_last_byte()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();
        byte[] bytes = Encoding.UTF8.GetBytes("\U0001F600");
        bytes.Should().HaveCount(4);

        //Act
        string a = assembler.Append(new[] { bytes[0] });
        string b = assembler.Append(new[] { bytes[1] });
        string c = assembler.Append(new[] { bytes[2] });
        string d = assembler.Append(new[] { bytes[3] });

        //Assert
        a.Should().BeEmpty();
        b.Should().BeEmpty();
        c.Should().BeEmpty();
        d.Should().Be("\U0001F600");
    }

    /// <summary>Complete text before an incomplete tail is emitted at once; only the tail waits.</summary>
    [Fact]
    public void Append_emits_the_complete_prefix_and_keeps_only_the_partial_tail()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();
        byte[] partial = Encoding.UTF8.GetBytes("ab€");

        //Act
        string first = assembler.Append(new[] { partial[0], partial[1], partial[2] });
        string second = assembler.Append(new[] { partial[3], partial[4] });

        //Assert
        first.Should().Be("ab");
        second.Should().Be("€");
    }

    /// <summary>An empty or null run of bytes is not an error and produces nothing.</summary>
    [Fact]
    public void Append_returns_nothing_for_no_bytes()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();

        //Act
        string empty = assembler.Append(Array.Empty<byte>());
        string nothing = assembler.Append(null);

        //Assert
        empty.Should().BeEmpty();
        nothing.Should().BeEmpty();
    }

    /// <summary>Bytes that never complete a character are given up on by the flush that ends the stream.</summary>
    [Fact]
    public void Flush_gives_up_on_a_sequence_that_never_completed()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();

        //Act
        string held = assembler.Append(new byte[] { 0xE2, 0x82 });
        string flushed = assembler.Flush();

        //Assert
        held.Should().BeEmpty();
        flushed.Should().Be("�");
    }

    /// <summary>A reset throws away what was held back, so the next stream starts clean.</summary>
    [Fact]
    public void Reset_discards_what_was_held_back()
    {
        //Arrange
        Utf8Assembler assembler = new Utf8Assembler();
        assembler.Append(new byte[] { 0xE2, 0x82 }).Should().BeEmpty();

        //Act
        assembler.Reset();
        string text = assembler.Append(Encoding.UTF8.GetBytes("ok"));

        //Assert
        text.Should().Be("ok");
        assembler.Flush().Should().BeEmpty();
    }
}
