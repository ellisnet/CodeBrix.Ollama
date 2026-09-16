// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: parser/parser_test.go at commit a43fad18.
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Tests for <see cref="Modelfile"/>, ported from Ollama's parser/parser_test.go at commit
/// a43fad18. The cases that exercise CreateRequest's file hashing, globbing and "~" expansion are
/// not ported: this library parses the Modelfile and leaves path resolution to the model store.
/// </summary>
public sealed class ModelfileTests
{
    private const string Template =
        "{{ if .System }}<|start_header_id|>system<|end_header_id|>\n\n{{ .System }}<|eot_id|>" +
        "{{ end }}{{ if .Prompt }}<|start_header_id|>user<|end_header_id|>\n\n{{ .Prompt }}" +
        "<|eot_id|>{{ end }}<|start_header_id|>assistant<|end_header_id|>\n\n{{ .Response }}" +
        "<|eot_id|>";

    private const string InvalidCommandMessage =
        "command must be one of \"from\", \"license\", \"template\", \"system\", \"adapter\", " +
        "\"draft\", \"renderer\", \"parser\", \"parameter\", \"message\", or \"requires\"";

    private const string InvalidMessageRoleMessage =
        "message role must be one of \"system\", \"user\", or \"assistant\"";

    /// <summary>A complete Modelfile produces one command per line, in order.</summary>
    [Fact]
    public void Parse_WithFullFile_ReturnsExpectedCommands()
    {
        //Arrange
        var input = "\nFROM model1\nADAPTER adapter1\nLICENSE MIT\nPARAMETER param1 value1\n"
            + "PARAMETER param2 value2\nTEMPLATE \"\"\"" + Template + "\"\"\"    \n";

        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        modelfile.Commands.Should().HaveCount(6);
        modelfile.Commands[0].Name.Should().Be("model");
        modelfile.Commands[0].Args.Should().Be("model1");
        modelfile.Commands[1].Name.Should().Be("adapter");
        modelfile.Commands[1].Args.Should().Be("adapter1");
        modelfile.Commands[2].Name.Should().Be("license");
        modelfile.Commands[2].Args.Should().Be("MIT");
        modelfile.Commands[3].Name.Should().Be("param1");
        modelfile.Commands[3].Args.Should().Be("value1");
        modelfile.Commands[4].Name.Should().Be("param2");
        modelfile.Commands[4].Args.Should().Be("value2");
        modelfile.Commands[5].Name.Should().Be("template");
        modelfile.Commands[5].Args.Should().Be(Template);
    }

    /// <summary>A DRAFT command is kept and rendered back.</summary>
    [Fact]
    public void Parse_WithDraft_KeepsTheDraftCommand()
    {
        //Act
        var modelfile = Modelfile.Parse("\nFROM base\nDRAFT ./assistant\n");

        //Assert
        Render(modelfile).Should().Be("[model=base][draft=./assistant]");
        modelfile.Drafts.Should().Equal("./assistant");
        modelfile.ToString().Should().Contain("DRAFT ./assistant");
    }

    /// <summary>Spacing around arguments is trimmed unless the argument is quoted.</summary>
    [Fact]
    public void Parse_WithExtraSpacing_TrimsUnquotedArguments()
    {
        //Arrange
        var input = "\nFROM \"     model 1\"\nADAPTER      adapter3\nLICENSE \"MIT       \"\n"
            + "PARAMETER param1        value1\nPARAMETER param2    value2\n"
            + "TEMPLATE \"\"\"   " + Template + "   \"\"\"    \n";

        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        Render(modelfile).Should().Be("[model=     model 1][adapter=adapter3][license=MIT       ]"
            + "[param1=value1][param2=value2][template=   " + Template + "   ]");
    }

    /// <summary>The FROM cases from Ollama's TestParseFileFrom.</summary>
    /// <param name="input">The Modelfile text.</param>
    /// <param name="expected">The rendered commands.</param>
    [Theory]
    [InlineData("FROM \"FOO  BAR  \"", "[model=FOO  BAR  ]")]
    [InlineData("FROM \"FOO BAR\"\nPARAMETER param1 value1", "[model=FOO BAR][param1=value1]")]
    [InlineData("FROM     FOOO BAR    ", "[model=FOOO BAR]")]
    [InlineData("FROM /what/is/the path ", "[model=/what/is/the path]")]
    [InlineData("FROM foo", "[model=foo]")]
    [InlineData("FROM /path/to/model", "[model=/path/to/model]")]
    [InlineData("FROM /path/to/model/fp16.bin", "[model=/path/to/model/fp16.bin]")]
    [InlineData("FROM llama3:latest", "[model=llama3:latest]")]
    [InlineData("FROM llama3:7b-instruct-q4_K_M", "[model=llama3:7b-instruct-q4_K_M]")]
    [InlineData("PARAMETER param1 value1\nFROM foo", "[param1=value1][model=foo]")]
    [InlineData("PARAMETER what the \nFROM lemons make lemonade ",
        "[what=the][model=lemons make lemonade]")]
    public void Parse_WithFromLine_ReturnsExpectedCommands(string input, string expected)
    {
        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        Render(modelfile).Should().Be(expected);
    }

    /// <summary>A file without a FROM line is rejected.</summary>
    /// <param name="input">The Modelfile text.</param>
    [Theory]
    [InlineData("")]
    [InlineData("PARAMETER param1 value1")]
    [InlineData("# just a comment\n")]
    public void Parse_WithoutFromLine_Throws(string input)
    {
        //Act
        var exception = Catch(input);

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(0);
        exception.Message.Should().Be("no FROM line");
    }

    /// <summary>A PARAMETER without a value ends the file early.</summary>
    [Fact]
    public void Parse_WithParameterMissingValue_ThrowsUnexpectedEof()
    {
        //Act
        var exception = Catch("\nFROM foo\nPARAMETER param1\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(0);
        exception.Message.Should().Be("unexpected EOF: param1");
    }

    /// <summary>An unknown keyword is reported with the line it was found on.</summary>
    [Fact]
    public void Parse_WithBadCommand_ThrowsWithLineNumber()
    {
        //Act
        var exception = Catch("\nFROM foo\nBADCOMMAND param1 value1\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(3);
        exception.Message.Should().Be("(line 3): " + InvalidCommandMessage);
    }

    /// <summary>A keyword holding a character that is not a letter is an invalid command.</summary>
    [Fact]
    public void Parse_WithNonLetterInKeyword_Throws()
    {
        //Act
        var exception = Catch("FROM foo\nSYS.TEM hello\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(2);
        exception.Message.Should().Be("(line 2): " + InvalidCommandMessage);
    }

    /// <summary>RENDERER is kept as its own command.</summary>
    [Fact]
    public void Parse_WithRenderer_KeepsTheRendererCommand()
    {
        //Act
        var modelfile = Modelfile.Parse("\nFROM foo\nRENDERER renderer1\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][renderer=renderer1]");
        modelfile.Renderer.Should().Be("renderer1");
    }

    /// <summary>PARSER is kept as its own command.</summary>
    [Fact]
    public void Parse_WithParser_KeepsTheParserCommand()
    {
        //Act
        var modelfile = Modelfile.Parse("\nFROM foo\nPARSER parser1\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][parser=parser1]");
        modelfile.Parser.Should().Be("parser1");
    }

    /// <summary>REQUIRES is kept as its own command, exactly as written.</summary>
    [Fact]
    public void Parse_WithRequires_KeepsTheRequiresCommand()
    {
        //Act
        var modelfile = Modelfile.Parse("\nFROM foo\nREQUIRES 0.14.0\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][requires=0.14.0]");
        modelfile.Requires.Should().Be("0.14.0");
    }

    /// <summary>The MESSAGE cases from Ollama's TestParseFileMessages.</summary>
    /// <param name="input">The Modelfile text.</param>
    /// <param name="expected">The rendered commands.</param>
    [Theory]
    [InlineData("\nFROM foo\nMESSAGE system You are a file parser. Always parse things.\n",
        "[model=foo][message=system: You are a file parser. Always parse things.]")]
    [InlineData("\nFROM foo\nMESSAGE system You are a file parser. Always parse things.",
        "[model=foo][message=system: You are a file parser. Always parse things.]")]
    [InlineData("\nFROM foo\nMESSAGE system You are a file parser. Always parse things.\n"
        + "MESSAGE user Hey there!\nMESSAGE assistant Hello, I want to parse all the things!\n",
        "[model=foo][message=system: You are a file parser. Always parse things.]"
        + "[message=user: Hey there!][message=assistant: Hello, I want to parse all the things!]")]
    [InlineData("\nFROM foo\nMESSAGE system \"\"\"\n"
        + "You are a multiline file parser. Always parse things.\n\"\"\"\n\t\t\t",
        "[model=foo][message=system: \nYou are a multiline file parser. Always parse things.\n]")]
    public void Parse_WithMessages_ReturnsExpectedCommands(string input, string expected)
    {
        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        Render(modelfile).Should().Be(expected);
    }

    /// <summary>Only the three known roles are accepted.</summary>
    [Fact]
    public void Parse_WithInvalidMessageRole_ThrowsWithLineNumber()
    {
        //Act
        var exception = Catch("\nFROM foo\nMESSAGE badguy I'm a bad guy!\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(3);
        exception.Message.Should().Be("(line 3): " + InvalidMessageRoleMessage);
    }

    /// <summary>A MESSAGE without content ends the file early.</summary>
    /// <param name="input">The Modelfile text.</param>
    /// <param name="expectedMessage">The expected error text.</param>
    [Theory]
    [InlineData("\nFROM foo\nMESSAGE system\n", "unexpected EOF: system")]
    [InlineData("\nFROM foo\nMESSAGE system", "unexpected EOF")]
    public void Parse_WithIncompleteMessage_ThrowsUnexpectedEof(string input, string expectedMessage)
    {
        //Act
        var exception = Catch(input);

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(0);
        exception.Message.Should().Be(expectedMessage);
    }

    /// <summary>The quoting cases from Ollama's TestParseFileQuoted.</summary>
    /// <param name="input">The Modelfile text.</param>
    /// <param name="expected">The rendered commands.</param>
    [Theory]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"\nThis is a\nmultiline system.\n\"\"\"\n\t\t\t",
        "[model=foo][system=\nThis is a\nmultiline system.\n]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"\nThis is a\nmultiline system.\"\"\"\n\t\t\t",
        "[model=foo][system=\nThis is a\nmultiline system.]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"This is a\nmultiline system.\"\"\"\n\t\t\t",
        "[model=foo][system=This is a\nmultiline system.]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"This is a multiline system.\"\"\"\n\t\t\t",
        "[model=foo][system=This is a multiline system.]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"\nThis is a multiline system with \"quotes\".\n\"\"\"\n",
        "[model=foo][system=\nThis is a multiline system with \"quotes\".\n]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"\"\"\"\n", "[model=foo][system=]")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\n", "[model=foo][system=]")]
    [InlineData("\nFROM foo\nSYSTEM \"'\"\n", "[model=foo][system=']")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"''\"'\"\"'\"\"'\"'''''\"\"'\"\"'\"\"\"\n",
        "[model=foo][system=''\"'\"\"'\"\"'\"'''''\"\"'\"\"']")]
    [InlineData("\nFROM foo\nTEMPLATE \"\"\"\n{{ .Prompt }}\n\"\"\"",
        "[model=foo][template=\n{{ .Prompt }}\n]")]
    public void Parse_WithQuotedValues_ReturnsExpectedCommands(string input, string expected)
    {
        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        Render(modelfile).Should().Be(expected);
    }

    /// <summary>Quoting that is never closed ends the file early.</summary>
    /// <param name="input">The Modelfile text.</param>
    [Theory]
    [InlineData("\nFROM foo\nSYSTEM \"\"\"This is a multiline system.\"\"\n\t\t\t")]
    [InlineData("\nFROM foo\nSYSTEM \"\n\t\t\t")]
    public void Parse_WithUnclosedQuote_ThrowsUnexpectedEof(string input)
    {
        //Act
        var exception = Catch(input);

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(0);
        exception.Message.Should().Be("unexpected EOF");
    }

    /// <summary>The PARAMETER cases from Ollama's TestParseFileParameters.</summary>
    /// <param name="line">The text after the PARAMETER keyword.</param>
    /// <param name="expectedName">The expected parameter name.</param>
    /// <param name="expectedValue">The expected parameter value.</param>
    [Theory]
    [InlineData("numa true", "numa", "true")]
    [InlineData("num_ctx 1", "num_ctx", "1")]
    [InlineData("num_batch 1", "num_batch", "1")]
    [InlineData("num_gqa 1", "num_gqa", "1")]
    [InlineData("num_gpu 1", "num_gpu", "1")]
    [InlineData("main_gpu 1", "main_gpu", "1")]
    [InlineData("use_mmap true", "use_mmap", "true")]
    [InlineData("num_thread 1", "num_thread", "1")]
    [InlineData("num_keep 1", "num_keep", "1")]
    [InlineData("seed 1", "seed", "1")]
    [InlineData("num_predict 1", "num_predict", "1")]
    [InlineData("top_k 1", "top_k", "1")]
    [InlineData("top_p 1.0", "top_p", "1.0")]
    [InlineData("min_p 0.05", "min_p", "0.05")]
    [InlineData("typical_p 1.0", "typical_p", "1.0")]
    [InlineData("repeat_last_n 1", "repeat_last_n", "1")]
    [InlineData("temperature 1.0", "temperature", "1.0")]
    [InlineData("repeat_penalty 1.0", "repeat_penalty", "1.0")]
    [InlineData("presence_penalty 1.0", "presence_penalty", "1.0")]
    [InlineData("frequency_penalty 1.0", "frequency_penalty", "1.0")]
    [InlineData("penalize_newline true", "penalize_newline", "true")]
    [InlineData("stop ### User:", "stop", "### User:")]
    [InlineData("stop ### User: ", "stop", "### User:")]
    [InlineData("stop \"### User:\"", "stop", "### User:")]
    [InlineData("stop \"### User: \"", "stop", "### User: ")]
    [InlineData("stop \"\"\"### User:\"\"\"", "stop", "### User:")]
    [InlineData("stop \"\"\"### User:\n\"\"\"", "stop", "### User:\n")]
    [InlineData("stop <|endoftext|>", "stop", "<|endoftext|>")]
    [InlineData("stop <|eot_id|>", "stop", "<|eot_id|>")]
    [InlineData("stop </s>", "stop", "</s>")]
    public void Parse_WithParameterLine_ReturnsExpectedCommand(string line, string expectedName,
        string expectedValue)
    {
        //Act
        var modelfile = Modelfile.Parse("FROM foo\nPARAMETER " + line + "\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][" + expectedName + "=" + expectedValue + "]");
    }

    /// <summary>Comment lines are skipped.</summary>
    [Fact]
    public void Parse_WithComment_SkipsTheCommentLine()
    {
        //Act
        var modelfile = Modelfile.Parse("\n# comment\nFROM foo\n\t");

        //Assert
        Render(modelfile).Should().Be("[model=foo]");
    }

    /// <summary>A comment after a command is skipped too.</summary>
    [Fact]
    public void Parse_WithTrailingComment_SkipsTheCommentLine()
    {
        //Act
        var modelfile = Modelfile.Parse("FROM foo\n# a comment about the system prompt\nSYSTEM hi\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][system=hi]");
    }

    /// <summary>Rendering a file and parsing it again produces the same commands.</summary>
    /// <param name="input">The Modelfile text.</param>
    [Theory]
    [InlineData("\nFROM foo\nADAPTER adapter1\nLICENSE MIT\nPARAMETER param1 value1\n"
        + "PARAMETER param2 value2\nTEMPLATE template1\n"
        + "MESSAGE system You are a file parser. Always parse things.\nMESSAGE user Hey there!\n"
        + "MESSAGE assistant Hello, I want to parse all the things!\n")]
    [InlineData("\nFROM foo\nADAPTER adapter1\nLICENSE MIT\nPARAMETER param1 value1\n"
        + "PARAMETER param2 value2\nTEMPLATE template1\n"
        + "MESSAGE system \"\"\"\nYou are a store greeter. Always respond with \"Hello!\".\n\"\"\"\n"
        + "MESSAGE user Hey there!\nMESSAGE assistant Hello, I want to parse all the things!\n")]
    [InlineData("\nFROM foo\nADAPTER adapter1\nLICENSE \"\"\"\n"
        + "Very long and boring legal text.\nBlah blah blah.\n\"Oh look, a quote!\"\n\"\"\"\n\n"
        + "PARAMETER param1 value1\nPARAMETER param2 value2\nTEMPLATE template1\n"
        + "MESSAGE system \"\"\"\nYou are a store greeter. Always respond with \"Hello!\".\n\"\"\"\n"
        + "MESSAGE user Hey there!\nMESSAGE assistant Hello, I want to parse all the things!\n")]
    [InlineData("\nFROM foo\nSYSTEM \"\"\n")]
    public void ToString_RoundTripsThroughParse(string input)
    {
        //Arrange
        var modelfile = Modelfile.Parse(input);

        //Act
        var reparsed = Modelfile.Parse(modelfile.ToString());

        //Assert
        Render(reparsed).Should().Be(Render(modelfile));
        reparsed.ToString().Should().Be(modelfile.ToString());
    }

    /// <summary>Every rendered command line ends with a newline.</summary>
    [Fact]
    public void ToString_EndsEveryLineWithANewline()
    {
        //Act
        var text = Modelfile.Parse("FROM foo\nPARAMETER temperature 0.5\n").ToString();

        //Assert
        text.Should().Be("FROM foo\nPARAMETER temperature 0.5\n");
    }

    /// <summary>A leading byte order mark is ignored.</summary>
    [Fact]
    public void Parse_WithByteOrderMark_IgnoresIt()
    {
        //Act
        var modelfile = Modelfile.Parse("﻿FROM bob\nSYSTEM You are a bom file.\n");

        //Assert
        Render(modelfile).Should().Be("[model=bob][system=You are a bom file.]");
    }

    /// <summary>Multi-byte characters, including ones outside the basic plane, survive parsing.</summary>
    [Fact]
    public void Parse_WithMultiByteCharacters_KeepsThem()
    {
        //Act
        var modelfile = Modelfile.Parse("FROM test\n\tSYSTEM 你好👋");

        //Assert
        Render(modelfile).Should().Be("[model=test][system=你好👋]");
    }

    /// <summary>Carriage return and line feed pairs end a line the same way a line feed does.</summary>
    [Fact]
    public void Parse_WithCarriageReturnLineFeed_ParsesEveryCommand()
    {
        //Act
        var modelfile = Modelfile.Parse("FROM foo\r\nSYSTEM You are a bot.\r\n"
            + "PARAMETER temperature 0.5\r\n");

        //Assert
        Render(modelfile).Should().Be("[model=foo][system=You are a bot.][temperature=0.5]");
    }

    /// <summary>
    /// A carriage return and a line feed each advance the line counter, so an error in a file with
    /// Windows line endings reports twice the line number. This matches Ollama.
    /// </summary>
    [Fact]
    public void Parse_WithCarriageReturnLineFeed_CountsBothCharactersAsLines()
    {
        //Act
        var exception = Catch("FROM foo\r\nBADCOMMAND x\r\n");

        //Assert
        exception.Should().NotBeNull();
        exception.LineNumber.Should().Be(3);
    }

    /// <summary>A reader is read to the end and parsed.</summary>
    [Fact]
    public void Parse_WithTextReader_ParsesTheWholeText()
    {
        //Arrange
        using var reader = new StringReader("FROM foo\nSYSTEM hi\n");

        //Act
        var modelfile = Modelfile.Parse(reader);

        //Assert
        Render(modelfile).Should().Be("[model=foo][system=hi]");
    }

    /// <summary>Null text is rejected.</summary>
    [Fact]
    public void Parse_WithNullText_Throws()
    {
        //Arrange
        Action act = () => Modelfile.Parse((string)null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A null reader is rejected.</summary>
    [Fact]
    public void Parse_WithNullReader_Throws()
    {
        //Arrange
        Action act = () => Modelfile.Parse((TextReader)null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A UTF-8 file on disk is read and parsed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReadFileAsync_WithUtf8File_ParsesIt()
    {
        await RoundTripFileAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>A UTF-8 file that starts with a byte order mark is read and parsed.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReadFileAsync_WithUtf8ByteOrderMark_ParsesIt()
    {
        await RoundTripFileAsync(new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    /// <summary>A little-endian UTF-16 file is read and parsed, as Ollama's BOM override does.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReadFileAsync_WithUtf16LittleEndianFile_ParsesIt()
    {
        await RoundTripFileAsync(new UnicodeEncoding(bigEndian: false, byteOrderMark: true));
    }

    /// <summary>A big-endian UTF-16 file is read and parsed, as Ollama's BOM override does.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReadFileAsync_WithUtf16BigEndianFile_ParsesIt()
    {
        await RoundTripFileAsync(new UnicodeEncoding(bigEndian: true, byteOrderMark: true));
    }

    /// <summary>A null path is rejected.</summary>
    /// <returns>A task.</returns>
    [Fact]
    public async Task ReadFileAsync_WithNullPath_Throws()
    {
        //Arrange
        Func<Task> act = () => Modelfile.ReadFileAsync(null, TestContext.Current.CancellationToken);

        //Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    /// <summary>The typed views report what each command carries.</summary>
    [Fact]
    public void Parse_WithEveryCommand_FillsTheTypedViews()
    {
        //Arrange
        var input = "FROM base.gguf\nFROM projector.gguf\nADAPTER adapter1\nADAPTER adapter2\n"
            + "DRAFT draft.gguf\nLICENSE MIT\nLICENSE Apache-2.0\nTEMPLATE template1\n"
            + "SYSTEM You are a bot.\nRENDERER renderer1\nPARSER parser1\nREQUIRES 0.14.0\n"
            + "MESSAGE user Hello there!\nMESSAGE assistant Hi! How are you?\n"
            + "PARAMETER temperature 0.5\nPARAMETER stop </s>\nPARAMETER mirostat 1\n";

        //Act
        var modelfile = Modelfile.Parse(input);

        //Assert
        modelfile.From.Should().Be("base.gguf");
        modelfile.ModelArgs.Should().Equal("base.gguf", "projector.gguf");
        modelfile.Adapters.Should().Equal("adapter1", "adapter2");
        modelfile.Drafts.Should().Equal("draft.gguf");
        modelfile.Licenses.Should().Equal("MIT", "Apache-2.0");
        modelfile.Template.Should().Be("template1");
        modelfile.System.Should().Be("You are a bot.");
        modelfile.Renderer.Should().Be("renderer1");
        modelfile.Parser.Should().Be("parser1");
        modelfile.Requires.Should().Be("0.14.0");
        modelfile.Messages.Should().HaveCount(2);
        modelfile.Messages[0].Role.Should().Be("user");
        modelfile.Messages[0].Content.Should().Be("Hello there!");
        modelfile.Messages[1].Role.Should().Be("assistant");
        modelfile.Messages[1].Content.Should().Be("Hi! How are you?");
        modelfile.ParameterLines.Should().HaveCount(3);
        modelfile.DeprecatedParameters.Should().Equal("mirostat");
    }

    /// <summary>A message with no ": " separator keeps the whole argument as the role.</summary>
    [Fact]
    public void Messages_WithoutSeparator_KeepsTheWholeArgumentAsTheRole()
    {
        //Arrange
        var commands = new[]
        {
            new ModelfileCommand("model", "foo"),
            new ModelfileCommand("message", "system"),
        };

        //Act
        var modelfile = new Modelfile(commands);

        //Assert
        modelfile.Messages.Should().HaveCount(1);
        modelfile.Messages[0].Role.Should().Be("system");
        modelfile.Messages[0].Content.Should().BeEmpty();
    }

    /// <summary>The constructor rejects a null command list.</summary>
    [Fact]
    public void Constructor_WithNullCommands_Throws()
    {
        //Arrange
        Action act = () => new Modelfile(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    /// <summary>The quoting rules from Ollama's quote function.</summary>
    /// <param name="value">The argument text.</param>
    /// <param name="expected">The expected quoted text.</param>
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("has \"quotes\" but one line", "has \"quotes\" but one line")]
    [InlineData(" leading", "\" leading\"")]
    [InlineData("trailing ", "\"trailing \"")]
    [InlineData("two\nlines", "\"two\nlines\"")]
    [InlineData("two\nlines with \"quotes\"", "\"\"\"two\nlines with \"quotes\"\"\"\"")]
    [InlineData(" \"both\" ", "\"\"\" \"both\" \"\"\"")]
    [InlineData("", "")]
    public void Quote_QuotesOnlyWhenNeeded(string value, string expected)
    {
        //Act
        var quoted = Modelfile.Quote(value);

        //Assert
        quoted.Should().Be(expected);
    }

    /// <summary>The unquoting rules from Ollama's unquote function.</summary>
    /// <param name="value">The quoted text.</param>
    /// <param name="expected">The expected unquoted text.</param>
    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("\"quoted\"", "quoted")]
    [InlineData("\"\"", "")]
    [InlineData("\"\"\"\"\"\"", "")]
    [InlineData("\"\"\"triple\"\"\"", "triple")]
    [InlineData("\"\"\"with \"one\" inside\"\"\"", "with \"one\" inside")]
    [InlineData("'single'", "'single'")]
    [InlineData("\"'\"", "'")]
    public void TryUnquote_RemovesMatchingQuotes(string value, string expected)
    {
        //Act
        var unquoted = Modelfile.TryUnquote(value, out var result);

        //Assert
        unquoted.Should().BeTrue();
        result.Should().Be(expected);
    }

    /// <summary>Quoting that is opened and never closed is rejected.</summary>
    /// <param name="value">The quoted text.</param>
    [Theory]
    [InlineData("\"")]
    [InlineData("\"unterminated")]
    [InlineData("\"\"\"")]
    [InlineData("\"\"\"\"")]
    [InlineData("\"\"\"\"\"")]
    [InlineData("\"\"\"unterminated")]
    [InlineData("\"\"\"unterminated\"\"")]
    public void TryUnquote_WithUnclosedQuote_ReturnsFalse(string value)
    {
        //Act
        var unquoted = Modelfile.TryUnquote(value, out var result);

        //Assert
        unquoted.Should().BeFalse();
        result.Should().BeEmpty();
    }

    private static async Task RoundTripFileAsync(Encoding encoding)
    {
        //Arrange
        const string text = "FROM bob\nPARAMETER param1 1\nPARAMETER param2 4096\n"
            + "SYSTEM You are a 你好 file.\n";
        var directory = Path.Combine(Path.GetTempPath(), "codebrix-ollama-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "Modelfile");

        try
        {
            var preamble = encoding.GetPreamble();
            var body = encoding.GetBytes(text);
            var bytes = new byte[preamble.Length + body.Length];
            preamble.CopyTo(bytes, 0);
            body.CopyTo(bytes, preamble.Length);
            await File.WriteAllBytesAsync(path, bytes, TestContext.Current.CancellationToken);

            //Act
            var modelfile = await Modelfile.ReadFileAsync(path,
                TestContext.Current.CancellationToken);

            //Assert
            Render(modelfile).Should().Be(
                "[model=bob][param1=1][param2=4096][system=You are a 你好 file.]");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static ModelfileParseException Catch(string input)
    {
        try
        {
            Modelfile.Parse(input);
        }
        catch (ModelfileParseException exception)
        {
            return exception;
        }

        return null;
    }

    private static string Render(Modelfile modelfile)
    {
        var builder = new StringBuilder();
        foreach (var command in modelfile.Commands)
        {
            builder.Append('[');
            builder.Append(command.Name);
            builder.Append('=');
            builder.Append(command.Args);
            builder.Append(']');
        }

        return builder.ToString();
    }
}
