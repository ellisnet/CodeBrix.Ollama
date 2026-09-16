using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The rule that keeps a chat prompt from carrying two beginning-of-sequence tokens, or none.
/// </summary>
public sealed class ChatBosRuleTests
{
    /// <summary>A vocabulary that asks for no beginning-of-sequence token leaves the tokenizer to it.</summary>
    [Fact]
    public void AddSpecialTokens_with_a_vocabulary_that_adds_none_is_true()
    {
        ChatBosRule.AddSpecialTokens("<|im_start|>user", false, "<s>").Should().BeTrue();
    }

    /// <summary>A template that already wrote the token means the tokenizer must not add another.</summary>
    [Fact]
    public void AddSpecialTokens_with_a_prompt_that_starts_with_the_token_is_false()
    {
        ChatBosRule.AddSpecialTokens("<s>[INST] hello [/INST]", true, "<s>").Should().BeFalse();
    }

    /// <summary>A template that wrote no such token leaves the tokenizer to add one.</summary>
    [Fact]
    public void AddSpecialTokens_with_a_prompt_that_does_not_start_with_the_token_is_true()
    {
        ChatBosRule.AddSpecialTokens("<|im_start|>user\nhello", true, "<s>").Should().BeTrue();
    }

    /// <summary>The token appearing later in the prompt is not the same as starting with it.</summary>
    [Fact]
    public void AddSpecialTokens_with_the_token_in_the_middle_is_true()
    {
        ChatBosRule.AddSpecialTokens("hello <s> world", true, "<s>").Should().BeTrue();
    }

    /// <summary>A vocabulary with no token text to compare against leaves the tokenizer to it.</summary>
    [Fact]
    public void AddSpecialTokens_with_no_token_text_is_true()
    {
        ChatBosRule.AddSpecialTokens("anything", true, null).Should().BeTrue();
        ChatBosRule.AddSpecialTokens("anything", true, "").Should().BeTrue();
    }

    /// <summary>A null prompt is treated as the empty one, which starts with nothing.</summary>
    [Fact]
    public void AddSpecialTokens_with_a_null_prompt_is_true()
    {
        ChatBosRule.AddSpecialTokens(null, true, "<s>").Should().BeTrue();
    }
}
