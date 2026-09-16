using System;
using System.Text;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the text helpers over the engine's own built-in chat templates, which need no model, and the
/// UTF-8 conversion the whole binding rests on.
/// </summary>
/// <remarks>
/// Tokenizing cannot be covered by the conformance model: it has no tokenizer at all - its vocabulary type
/// is "no_vocab" and the gate feeds it token ids - so the round trip is exercised by the live tests that
/// load a real model.
/// </remarks>
public sealed class NativeTextTests
{
    /// <summary>A built-in template renders both messages and opens the assistant turn.</summary>
    [Fact]
    public void ApplyChatTemplate_renders_a_built_in_template()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        string[] roles = { "system", "user" };
        string[] contents = { "Be brief.", "Hello." };

        //Act
        string rendered = NativeText.ApplyChatTemplate("chatml", roles, contents, true);

        //Assert
        rendered.Should().Contain("Be brief.");
        rendered.Should().Contain("Hello.");
        rendered.Should().Contain("assistant");
    }

    /// <summary>A template the engine does not know is reported rather than rendered as nonsense.</summary>
    [Fact]
    public void ApplyChatTemplate_reports_an_unknown_template()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        Action act = () => NativeText.ApplyChatTemplate("not-a-template-this-engine-knows", new[] { "user" }, new[] { "Hi." }, false);

        //Assert
        act.Should().Throw<ChatTemplateException>();
    }

    /// <summary>The two arrays have to describe the same messages.</summary>
    [Fact]
    public void ApplyChatTemplate_rejects_mismatched_arrays()
    {
        //Arrange
        Action act = () => NativeText.ApplyChatTemplate("chatml", new[] { "user", "assistant" }, new[] { "Hi." }, false);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    /// <summary>The engine lists the templates it has built in.</summary>
    [Fact]
    public unsafe void llama_chat_builtin_templates_lists_the_built_in_templates()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        int count = NativeMethods.llama_chat_builtin_templates(null, 0);

        //Assert
        (count > 0).Should().BeTrue();
    }

    /// <summary>The render estimate counts every character of the roles as well as of the message bodies.</summary>
    /// <param name="roles">Each message's role.</param>
    /// <param name="contents">Each message's text.</param>
    /// <param name="expected">Two bytes per character of both, plus a kilobyte of template markup.</param>
    [Theory]
    [InlineData(new string[0], new string[0], 1024)]
    [InlineData(new[] { "user" }, new[] { "Hi." }, 1024 + 8 + 6)]
    [InlineData(new[] { "system", "user" }, new[] { "Be brief.", "Hello." }, 1024 + 12 + 8 + 18 + 12)]
    public void EstimateRenderCapacity_counts_the_roles_as_well_as_the_contents(
        string[] roles,
        string[] contents,
        int expected)
        => NativeText.EstimateRenderCapacity(roles, contents).Should().Be(expected);

    /// <summary>Text becomes NUL-terminated UTF-8, never the platform's code page.</summary>
    [Fact]
    public void ToUtf8_terminates_and_encodes_as_utf8()
    {
        //Act
        byte[] bytes = NativeText.ToUtf8("aé中");

        //Assert
        bytes.Should().HaveCount(Encoding.UTF8.GetByteCount("aé中") + 1);
        bytes[bytes.Length - 1].Should().Be((byte)0);
        Encoding.UTF8.GetString(bytes, 0, bytes.Length - 1).Should().Be("aé中");
    }

    /// <summary>A null string still produces a valid empty C string.</summary>
    [Fact]
    public void ToUtf8_with_null_produces_an_empty_c_string()
    {
        //Act
        byte[] bytes = NativeText.ToUtf8(null);

        //Assert
        bytes.Should().HaveCount(1);
        bytes[0].Should().Be((byte)0);
    }
}
