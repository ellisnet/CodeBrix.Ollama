using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the stop-sequence detector, whose whole job is to be right about text that arrives a few
/// characters at a time.
/// </summary>
public sealed class StopSequenceTests
{
    /// <summary>With nothing to watch for, text passes straight through.</summary>
    [Fact]
    public void Append_passes_text_through_when_there_are_no_stop_sequences()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(null);

        //Act
        string emitted = detector.Append("anything at all");

        //Assert
        detector.IsWatching.Should().BeFalse();
        emitted.Should().Be("anything at all");
        detector.IsStopped.Should().BeFalse();
    }

    /// <summary>A stop sequence inside one chunk ends the watch and is not emitted.</summary>
    [Fact]
    public void Append_stops_on_a_stop_sequence_in_one_piece()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "STOP" });

        //Act
        string emitted = detector.Append("before STOP after");

        //Assert
        emitted.Should().Be("before ");
        detector.IsStopped.Should().BeTrue();
        detector.MatchedStopSequence.Should().Be("STOP");
    }

    /// <summary>A stop sequence split across three chunks is still caught, and nothing leaks before it.</summary>
    [Fact]
    public void Append_catches_a_stop_sequence_split_across_chunks()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "<|end|>" });

        //Act
        string first = detector.Append("text <|");
        string second = detector.Append("end");
        string third = detector.Append("|> more");

        //Assert
        first.Should().Be("text ");
        second.Should().BeEmpty();
        third.Should().BeEmpty();
        detector.IsStopped.Should().BeTrue();
        detector.MatchedStopSequence.Should().Be("<|end|>");
    }

    /// <summary>Text held back because it might have been a stop sequence is emitted once it turns out not to be.</summary>
    [Fact]
    public void Append_emits_held_text_once_it_cannot_be_a_stop_sequence()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "STOP" });

        //Act
        string first = detector.Append("go ST");
        string second = detector.Append("ay");

        //Assert
        first.Should().Be("go ");
        second.Should().Be("STay");
        detector.IsStopped.Should().BeFalse();
    }

    /// <summary>When two stop sequences match, the one that starts earliest wins.</summary>
    [Fact]
    public void Append_prefers_the_stop_sequence_that_matches_earliest()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "END", "ND" });

        //Act
        string emitted = detector.Append("the END here");

        //Assert
        emitted.Should().Be("the ");
        detector.MatchedStopSequence.Should().Be("END");
    }

    /// <summary>Between two stop sequences starting at the same place, the longer wins.</summary>
    [Fact]
    public void Append_prefers_the_longer_of_two_stop_sequences_at_the_same_place()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "EN", "END" });

        //Act
        string emitted = detector.Append("an END");

        //Assert
        emitted.Should().Be("an ");
        detector.MatchedStopSequence.Should().Be("END");
    }

    /// <summary>Nothing more is emitted once a stop sequence has matched.</summary>
    [Fact]
    public void Append_emits_nothing_after_it_has_stopped()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "X" });
        detector.Append("aX").Should().Be("a");

        //Act
        string more = detector.Append("bbb");

        //Assert
        more.Should().BeEmpty();
    }

    /// <summary>Held-back text is handed over when generation ends for some other reason.</summary>
    [Fact]
    public void Flush_returns_the_text_that_was_being_held_back()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "STOP" });
        detector.Append("done ST").Should().Be("done ");

        //Act
        string remainder = detector.Flush();

        //Assert
        remainder.Should().Be("ST");
        detector.Flush().Should().BeEmpty();
    }

    /// <summary>A stop sequence that has matched leaves nothing behind to flush.</summary>
    [Fact]
    public void Flush_returns_nothing_once_a_stop_sequence_has_matched()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { "STOP" });
        detector.Append("x STOP y");

        //Act
        string remainder = detector.Flush();

        //Assert
        remainder.Should().BeEmpty();
    }

    /// <summary>Null and empty stop sequences are ignored rather than matching everything.</summary>
    [Fact]
    public void Append_ignores_null_and_empty_stop_sequences()
    {
        //Arrange
        StopSequenceDetector detector = new StopSequenceDetector(new[] { null, "", "STOP" });

        //Act
        string emitted = detector.Append("plain text");

        //Assert
        emitted.Should().Be("plain text");
        detector.IsStopped.Should().BeFalse();
    }
}
