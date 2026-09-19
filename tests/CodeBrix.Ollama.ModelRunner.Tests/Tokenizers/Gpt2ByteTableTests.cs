using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The byte-to-symbol table a byte-level byte-pair encoding is written in.
/// </summary>
public sealed class Gpt2ByteTableTests
{
    /// <summary>Every byte has a symbol and no two bytes share one.</summary>
    [Fact]
    public void Symbol_gives_every_byte_a_symbol_of_its_own()
    {
        //Arrange
        HashSet<char> seen = new HashSet<char>();

        //Act
        for (int value = 0; value < 256; value++) seen.Add(Gpt2ByteTable.Symbol((byte)value));

        //Assert
        seen.Should().HaveCount(256);
    }

    /// <summary>A symbol takes its byte back, for all 256 of them.</summary>
    [Fact]
    public void TryByte_takes_every_symbol_back_to_its_byte()
    {
        //Act and assert
        for (int value = 0; value < 256; value++)
        {
            Gpt2ByteTable.TryByte(Gpt2ByteTable.Symbol((byte)value), out byte back).Should().BeTrue();
            back.Should().Be((byte)value);
        }
    }

    /// <summary>A printable ASCII byte stands for itself, which is what makes a merge file readable.</summary>
    [Theory]
    [InlineData('!')]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('~')]
    public void Symbol_leaves_a_printable_byte_alone(char character) =>
        Gpt2ByteTable.Symbol((byte)character).Should().Be(character);

    /// <summary>
    /// The three bytes a merge file could not otherwise hold get the symbols every tokenizer of this family
    /// gives them: the space, the newline and the tab.
    /// </summary>
    [Theory]
    [InlineData((byte)' ', 'Ġ')]
    [InlineData((byte)'\n', 'Ċ')]
    [InlineData((byte)'\t', 'ĉ')]
    [InlineData((byte)0x00, 'Ā')]
    [InlineData((byte)0x7f, 'ġ')]
    [InlineData((byte)0xad, 'Ń')]
    public void Symbol_gives_the_unprintable_bytes_the_published_symbols(byte value, char symbol) =>
        Gpt2ByteTable.Symbol(value).Should().Be(symbol);

    /// <summary>A character that is not one of the 256 is not a byte.</summary>
    [Fact]
    public void TryByte_with_a_symbol_of_its_own_returns_false() =>
        Gpt2ByteTable.TryByte('一', out byte _).Should().BeFalse();

    /// <summary>Text becomes one symbol per UTF-8 byte, not one per character.</summary>
    [Fact]
    public void Encode_writes_one_symbol_per_byte()
    {
        //Arrange
        byte[] utf8 = System.Text.Encoding.UTF8.GetBytes("aé");

        //Act
        string symbols = Gpt2ByteTable.Encode(utf8, utf8.Length);

        //Assert
        symbols.Should().HaveLength(3);
    }
}
