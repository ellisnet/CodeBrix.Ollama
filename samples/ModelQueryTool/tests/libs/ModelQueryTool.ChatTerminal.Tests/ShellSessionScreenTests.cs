using System;
using System.Text;
using System.Threading.Tasks;
using ModelQueryTool.ChatTerminal.Tests.Infrastructure;
using SilverAssertions;
using Xunit;

namespace ModelQueryTool.ChatTerminal.Tests;

/// <summary>
/// What an out-of-band message does to the SCREEN, driven against the real
/// terminal engine the application's control renders with. A message that
/// arrives while the user is idle at the prompt has to take the input line's
/// place - leaving no prompt row, and no half-typed text, behind it - and the
/// input line has to come back underneath it exactly once, with its cursor
/// where it was.
/// </summary>
public class ShellSessionScreenTests
{
    /// <summary>The widths every scripted case is run at, narrow ones included.</summary>
    public static TheoryData<int> Widths => new(20, 24, 40, 80);

    private const string Message = "Model loaded.";
    private const string Prompt = "chat> ";

    /// <summary>With nothing typed, the message replaces the prompt row rather than pushing it down.</summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void an_out_of_band_message_leaves_no_empty_prompt_row_above_it(int columns)
    {
        //Arrange
        var harness = new SessionHarness(columns);

        //Act
        harness.Session.WriteOutOfBand(Message);

        //Assert - the message is the first row on the screen; the prompt is below it, once
        harness.AssertScreenShows([Message], string.Empty, 0);
        harness.Terminal.Screen().Should().HaveCount(2);
    }

    /// <summary>A half-typed line comes back under the message with its cursor untouched.</summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void an_out_of_band_message_puts_a_half_typed_line_back_with_its_cursor(int columns)
    {
        //Arrange - the cursor is left in the MIDDLE of the text, not at its end
        var harness = new SessionHarness(columns);
        harness.Type("hal");
        harness.MoveLeft(2);

        //Act
        harness.Session.WriteOutOfBand(Message);

        //Assert
        harness.AssertScreenShows([Message], "hal", 1);
    }

    /// <summary>A line that wraps over rows is wound back whole, and redrawn whole.</summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void an_out_of_band_message_puts_a_wrapped_line_back_exactly_once(int columns)
    {
        //Arrange - the prompt and the text together need more than one row
        var harness = new SessionHarness(columns);
        var text = TextOf(columns + 8);
        harness.Type(text);
        harness.MoveLeft(text.Length / 2);

        //Act
        harness.Session.WriteOutOfBand(Message);

        //Assert - every row of the line is back, and none of the old rows is left over
        harness.AssertScreenShows([Message], text, text.Length - (text.Length / 2));
    }

    /// <summary>A line holding a break - what a multi-line paste leaves - is put back the same way.</summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void an_out_of_band_message_puts_a_line_with_a_break_in_it_back(int columns)
    {
        //Arrange - a pasted block is one line with a break inside it, not two submissions
        var harness = new SessionHarness(columns);
        harness.Send("first\rsecond");
        harness.MoveLeft(3);

        //Act
        harness.Session.WriteOutOfBand(Message);

        //Assert
        harness.AssertScreenShows([Message], "first\nsecond", 9);
    }

    /// <summary>Two messages one after another leave two message rows and one prompt.</summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void two_out_of_band_messages_leave_one_prompt_under_both(int columns)
    {
        //Arrange
        var harness = new SessionHarness(columns);
        harness.Type("hal");

        //Act
        harness.Session.WriteOutOfBand("Downloading.");
        harness.Session.WriteOutOfBand(Message);

        //Assert
        harness.AssertScreenShows(["Downloading.", Message], "hal", 3);
    }

    /// <summary>
    /// The start-up sequence Jeremy sees: a banner, then two messages while nothing is typed.
    /// One prompt is on the screen at the end of it, not three.
    /// </summary>
    [Theory]
    [MemberData(nameof(Widths))]
    public void a_start_up_of_two_messages_ends_with_exactly_one_prompt(int columns)
    {
        //Arrange
        var harness = new SessionHarness(columns);

        //Act - both messages are short enough for the narrowest width, because the terminal
        //  wraps mid-word and it is the chat, not the session, that lays its own text out
        harness.Session.WriteOutOfBand("On disk; loading.");
        harness.Session.WriteOutOfBand("Loaded and ready.");

        //Assert
        harness.AssertScreenShows(["On disk; loading.", "Loaded and ready."], string.Empty, 0);
        CountOf(harness.Terminal.ScreenText(), Prompt).Should().Be(1);
    }

    /// <summary>
    /// A message that arrives while the input line is on the LAST row of the screen scrolls the
    /// screen, and the relative wind-back still reaches the prompt it has to erase.
    /// </summary>
    [Fact]
    public async Task an_out_of_band_message_on_the_last_row_scrolls_the_screen()
    {
        //Arrange - three empty submissions put the prompt on the bottom row of a four-row screen
        var harness = new SessionHarness(40, rows: 4);
        for (var index = 0; index < 3; index++)
        {
            harness.Enter();
            await harness.Session.ExecutionChain;
        }

        harness.Type("abc");

        //Act
        harness.Session.WriteOutOfBand(Message);

        //Assert - the top row has scrolled off, the message is where the prompt was, and the
        //  typed line is back below it
        harness.AssertScreenShows([Prompt, Prompt, Message], "abc", 3);
    }

    /// <summary>
    /// WHILE A LINE IS RUNNING NOTHING CHANGES. The message goes into the running line's output
    /// and no prompt is written for it - the line's own completion prints that.
    /// </summary>
    [Fact]
    public async Task an_out_of_band_message_while_a_line_runs_writes_the_message_only()
    {
        //Arrange
        var harness = new SessionHarness(40);
        harness.Interpreter.Gate = new TaskCompletionSource();
        harness.Type("ask something");
        harness.Enter();

        //Act
        harness.Session.WriteOutOfBand(Message);
        harness.Interpreter.Gate.SetResult();
        await harness.Session.ExecutionChain;

        //Assert
        harness.AssertScreenShows(["chat> ask something", Message], string.Empty, 0);
    }

    /// <summary>Text of a given length, so that a case can be run at several widths.</summary>
    private static string TextOf(int length)
    {
        const string words = "the quick brown fox jumps over the lazy dog and keeps going for a while longer ";
        var builder = new StringBuilder();

        while (builder.Length < length) { builder.Append(words); }

        return builder.ToString(0, length);
    }

    private static int CountOf(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);

        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }
}
