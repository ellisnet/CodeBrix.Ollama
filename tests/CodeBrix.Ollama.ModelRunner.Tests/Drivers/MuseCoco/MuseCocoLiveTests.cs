using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>Opt-in regression for U1 using an existing INT4 bundle; never stages or downloads models.</summary>
public sealed class MuseCocoLiveTests
{
    private readonly ITestOutputHelper _output;

    public MuseCocoLiveTests(ITestOutputHelper output) => _output = output;

    [EnvGatedFact(new[] { TestGates.LiveTests, "CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS" })]
    public async Task GenerateAsync_and_GenerateStreamingAsync_complete_the_512_token_int4_regression()
    {
        //Arrange
        string bundle = Environment.GetEnvironmentVariable("CODEBRIX_OLLAMA_MUSECOCO_MUSIC_BUNDLE");
        if (string.IsNullOrWhiteSpace(bundle))
            throw new InvalidOperationException("Set CODEBRIX_OLLAMA_MUSECOCO_MUSIC_BUNDLE to the staged INT4 music bundle.");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(bundle, new OnnxRunnerOptions { Threads = 4 }, cancel);
        MusicAttributes attributes = model.Schema.CreateAttributes().With("instrument.piano", "present").With("tempo", "moderate");
        var options = new MuseCocoGenerationOptions
        {
            MaximumTokens = 512, MinimumTokens = 512, Seed = 20260921, TopK = 15, TopP = 1, Temperature = 1
        };
        int tokens = 0, firstNoteAtToken = 0;
        var events = new List<MidiEvent>();
        var timer = Stopwatch.StartNew();

        //Act
        MuseCocoGenerationResult completed = await model.GenerateAsync(attributes, options, cancellationToken: cancel);
        _output.WriteLine($"Completed score: tokens={completed.TokenIds.Count}, events={completed.Score.Events.Count}, seconds={timer.Elapsed.TotalSeconds:F3}");
        timer.Restart();
        await foreach (MidiEvent item in model.GenerateStreamingAsync(attributes, options,
            new MuseCocoTestProgress(count => tokens = count), cancel))
        {
            if (firstNoteAtToken == 0 && item.Kind == MidiEventKind.Note)
            {
                firstNoteAtToken = tokens;
                _output.WriteLine($"First streamed note: token={tokens}, seconds={timer.Elapsed.TotalSeconds:F3}");
            }
            events.Add(item);
        }
        _output.WriteLine($"Stream completed: tokens={tokens}, events={events.Count}, notes={events.Count(e => e.Kind == MidiEventKind.Note)}, seconds={timer.Elapsed.TotalSeconds:F3}");

        //Assert
        completed.TokenIds.Should().HaveCount(512);
        completed.EndedWithEos.Should().BeFalse();
        tokens.Should().Be(512);
        firstNoteAtToken.Should().BeInRange(1, 511);
        events.Select(e => e.Tick).Should().BeInAscendingOrder();
        Notes(events).Should().NotBeEmpty().And.Equal(Notes(completed.Score.Events), "both APIs must generate the same music");
        Metadata(events).Should().Equal(Metadata(completed.Score.Events));
        MidiScore roundtrip = MidiFile.Read(MidiFile.Write(new MidiScore(480, events)));
        // SMF note-offs cannot identify individual overlapping voices of the same pitch.
        // Its reader may pair those durations differently; the note onsets must survive.
        Notes(roundtrip.Events).Select(n => (n.Tick, n.Program, n.Pitch, n.Velocity)).OrderBy(n => n)
            .Should().Equal(Notes(events).Select(n => (n.Tick, n.Program, n.Pitch, n.Velocity)).OrderBy(n => n));
    }

    [EnvGatedFact(new[] { TestGates.LiveTests, "CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS" })]
    public async Task GenerateContinuationStreamingAsync_conditions_a_second_section_on_four_recent_bars()
    {
        //Arrange
        string bundle = Environment.GetEnvironmentVariable("CODEBRIX_OLLAMA_MUSECOCO_MUSIC_BUNDLE");
        if (string.IsNullOrWhiteSpace(bundle))
            throw new InvalidOperationException("Set CODEBRIX_OLLAMA_MUSECOCO_MUSIC_BUNDLE to the staged INT4 music bundle.");
        CancellationToken cancel = TestContext.Current.CancellationToken;
        using var model = await MuseCocoMusicModel.LoadFromDirectoryAsync(bundle, new OnnxRunnerOptions { Threads = 4 }, cancel);
        var attributes = model.Schema.CreateAttributes().With("instrument.piano", "present")
            .With("tempo", "moderate").With("bars", "13-16").With("time_signature", "4/4");
        MuseCocoContinuation continuation = model.CreateContinuation(4);
        var first = new List<MidiEvent>();
        var second = new List<MidiEvent>();
        var timer = Stopwatch.StartNew();

        //Act: EOS is allowed naturally in both sections; these are ceilings, not forced lengths.
        await foreach (MidiEvent item in model.GenerateContinuationStreamingAsync(continuation, attributes,
            new MuseCocoGenerationOptions { MaximumTokens = 192, MinimumTokens = 0, Seed = 20260921 }, cancellationToken: cancel))
            first.Add(item);
        long boundary = continuation.NextTick;
        int retained = continuation.ContextTokenCount;
        _output.WriteLine($"First experimental section: generated={continuation.LastGeneratedTokenCount}, retained={retained}, nextTick={boundary}, seconds={timer.Elapsed.TotalSeconds:F3}");
        timer.Restart();
        await foreach (MidiEvent item in model.GenerateContinuationStreamingAsync(continuation, attributes,
            new MuseCocoGenerationOptions { MaximumTokens = 128, MinimumTokens = 0, Seed = 20260922 }, cancellationToken: cancel))
            second.Add(item);
        _output.WriteLine($"Second experimental section: generated={continuation.LastGeneratedTokenCount}, notes={second.Count(e => e.Kind == MidiEventKind.Note)}, nextTick={continuation.NextTick}, seconds={timer.Elapsed.TotalSeconds:F3}");

        //Assert: this checks inference and event continuity, not perceived musical quality.
        retained.Should().BeGreaterThan(0);
        first.Should().Contain(e => e.Kind == MidiEventKind.Note);
        second.Should().Contain(e => e.Kind == MidiEventKind.Note);
        second.Should().OnlyContain(e => e.Tick >= boundary);
        first.Concat(second).Select(e => e.Tick).Should().BeInAscendingOrder();
        continuation.CompletedSections.Should().Be(2);
        continuation.NextTick.Should().BeGreaterThan(boundary);
    }

    private static IEnumerable<(long Tick, int Program, int Pitch, int Velocity, long Duration)> Notes(IEnumerable<MidiEvent> source)
    {
        MidiEvent[] events = source.ToArray();
        var programs = events.Where(e => e.Kind == MidiEventKind.ProgramChange).ToDictionary(e => e.Track, e => e.Program);
        return events.Where(e => e.Kind == MidiEventKind.Note)
            .Select(e => (e.Tick, Program: e.Channel == 9 ? 128 : programs[e.Track], Pitch: e.NoteNumber, e.Velocity, Duration: e.DurationTicks))
            .OrderBy(e => e.Tick).ThenBy(e => e.Program).ThenBy(e => e.Pitch).ThenBy(e => e.Duration).ThenBy(e => e.Velocity);
    }

    private static IEnumerable<(MidiEventKind Kind, long Tick, long Tempo, int Numerator, int Denominator)> Metadata(IEnumerable<MidiEvent> source) =>
        source.Where(e => e.Kind is MidiEventKind.Tempo or MidiEventKind.TimeSignature).OrderBy(e => e.Tick).ThenBy(e => e.Kind)
            .Select(e => (e.Kind, e.Tick, e.MicrosecondsPerQuarterNote, e.Numerator, e.Denominator));
}
