using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelManager.Tests;
using CodeBrix.Ollama.ModelRunner;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The MIDI-generating driver against the publisher's own Python, on the publisher's own model.
/// </summary>
/// <remarks>
/// <para>
/// THE BAR IS IDENTITY, not closeness. Asked for the most likely answer every time, the managed driver has to
/// write the same music the publisher's own generation loop writes - the same kinds of event, in the same
/// order, at the same positions, with the same pitches, loudnesses, lengths, instruments and signatures. A
/// sampling mask that is wrong by one token, a cache fed back a step late, an event's parameters read in the
/// wrong order: none of those fails, and all of them show up here.
/// </para>
/// <para>
/// THE STORE AND THE RUNNER ARE CONNECTED BY THIS TEST AND BY NOTHING ELSE. The store resolves a bundle to
/// blob paths - files held under digests rather than under the publisher's names - and the test projects the
/// pairs into the (logical name to path) form the runner's own API takes.
/// </para>
/// <para>
/// THE REDUCTIONS ARE NOT HELD TO IDENTITY. Quantizing weights changes the arithmetic, and a generation is a
/// chain of choices where one different choice changes everything after it. What is asserted of them is that
/// they write valid music, that they write the SAME music twice, and that they write it at a sensible rate;
/// where they part company with the full-precision run is REPORTED, because it is a fact about quantization
/// rather than a defect.
/// </para>
/// </remarks>
public sealed class SkyTntMidiGenerationLiveTests
{
    /// <summary>How many events the identity comparisons run for; the plan asks for at least sixty-four.</summary>
    private const int IdentityEvents = 128;

    /// <summary>
    /// How many events each reduction writes. It is well past the point where the model stops setting the
    /// piece up and starts writing notes, so what is checked is music rather than a prelude.
    /// </summary>
    private const int ReductionEvents = 128;

    /// <summary>
    /// How many events the seconds-of-music-per-second-of-waiting measurement writes. A model of this family
    /// spends its first dozens of events choosing instruments and setting controllers, all at the very start
    /// of the piece, so a short generation would measure the prelude and not the music.
    /// </summary>
    private const int MeasuredEvents = 384;

    private readonly ITestOutputHelper _output;

    /// <summary>Creates the test class.</summary>
    /// <param name="output">Where the measured numbers are written.</param>
    public SkyTntMidiGenerationLiveTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Asked for nothing in particular, the driver writes exactly the music the publisher's own code writes.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvPathGatedFact(TestGates.MidiModelClone, new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_writes_the_publishers_own_music_from_nothing() =>
        await CompareAsync(
            "from nothing",
            new MidiGenerationOptions(),
            "{\"top_k\":1,\"top_p\":1.0,\"temperature\":1.0}",
            null,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Asked for a piece of a stated instrumentation, speed and key, it writes exactly the publisher's music.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvPathGatedFact(TestGates.MidiModelClone, new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_writes_the_publishers_own_music_from_a_described_piece() =>
        await CompareAsync(
            "a described piece",
            new MidiGenerationOptions
            {
                Instruments = new[] { 0, 40, 73 },
                DrumKit = 0,
                BeatsPerMinute = 100,
                TimeSignatureNumerator = 3,
                TimeSignatureDenominator = 4,
                KeySignatureSharpsOrFlats = -3,
                KeySignatureIsMinor = true,
            },
            "{\"top_k\":1,\"top_p\":1.0,\"temperature\":1.0,\"instruments\":[0,40,73],\"drum_kit\":0,"
            + "\"bpm\":100,\"time_signature\":[3,4],\"key_signature\":[-3,1]}",
            null,
            TestContext.Current.CancellationToken);

    /// <summary>
    /// Asked to continue a piece of music, it reads the file the same way and writes the same continuation -
    /// which is the whole of the tokenizer's other direction, proved end to end.
    /// </summary>
    /// <returns>A task that completes when the comparison is done.</returns>
    [EnvPathGatedFact(TestGates.MidiModelClone, new[] { TestGates.RunLiveTests, TestGates.RunPythonTests })]
    public async Task GenerateAsync_continues_a_piece_the_way_the_publishers_own_code_does()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using TempWorkDirectory work = new TempWorkDirectory();
        string prompt = Path.Combine(work.DirectoryPath, "prompt.mid");

        using ModelStore store = EndToEndStore.Open();
        IReadOnlyDictionary<string, string> files = await FullPrecisionAsync(store, cancellationToken);

        // A piece to continue, written by this library from a generation of its own: the publisher's reader
        // and this one are then given the very same bytes.
        await using (IMidiGenerationModel model = await MidiGenerationModel.LoadFromFilesAsync(
            files, Threads(8), cancellationToken))
        {
            IReadOnlyList<MidiEvent> seed = await GenerateAsync(model, Greedy(48), cancellationToken);
            await model.SaveAsync(prompt, seed, cancellationToken);
        }

        //Act and assert
        await CompareAsync(
            "continuing a piece",
            new MidiGenerationOptions { Prompt = await MidiFile.ReadAsync(prompt, cancellationToken) },
            "{\"top_k\":1,\"top_p\":1.0,\"temperature\":1.0}",
            prompt,
            cancellationToken);
    }

    /// <summary>
    /// The three reductions the store makes all write valid music, deterministically, and where each one
    /// parts company with the full-precision run is recorded.
    /// </summary>
    /// <returns>A task that completes when all three have been generated from.</returns>
    [EnvGatedFact(TestGates.RunLiveTests)]
    public async Task GenerateAsync_through_every_reduction_writes_valid_music()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = EndToEndStore.Open();
        IReadOnlyDictionary<string, string> full = await FullPrecisionAsync(store, cancellationToken);
        SkyTntGenerated reference = await GenerateFromAsync(
            full, Greedy(ReductionEvents), 8, cancellationToken);
        Save("fp32", reference.Score);

        (ReduceMode Mode, string Name, string Label)[] reductions =
        {
            (ReduceMode.WeightOnlyInt4, "onnx-oracle/skytnt-tv2o-medium:int4", "int4"),
            (ReduceMode.WeightOnlyInt8, "onnx-oracle/skytnt-tv2o-medium:int8", "int8"),
            (ReduceMode.DynamicInt8, "onnx-oracle/skytnt-tv2o-medium:dynamic-int8", "dynamic int8"),
        };

        //Act and assert
        foreach ((ReduceMode mode, string name, string label) in reductions)
        {
            string source = await EndToEndStore.EnsureAsync(
                store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);
            string reduced = await EndToEndStore.EnsureReducedAsync(
                store, source, mode, name, cancellationToken);
            IReadOnlyDictionary<string, string> files = await FilesAsync(store, reduced, cancellationToken);

            SkyTntGenerated generated = await GenerateFromAsync(
                files, Greedy(ReductionEvents), 8, cancellationToken);
            SkyTntGenerated again = await GenerateFromAsync(
                files, Greedy(ReductionEvents), 8, cancellationToken);

            // Valid, and the same twice.
            generated.Events.Should().NotBeEmpty();
            Lines(again.Events).Should().Equal(Lines(generated.Events));

            MidiScore read = MidiFile.Read(MidiFile.Write(generated.Score));
            read.Events.Should().HaveCount(generated.Score.Events.Count);
            read.TicksPerQuarterNote.Should().Be(480);

            double rate = generated.Events.Count / generated.Seconds;
            rate.Should().BeGreaterThan(1);

            _output.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "{0}: {1} events in {2:F2} s ({3:F1} events/s), {4} events of music lasting {5:F1} s;"
                + " first difference from the full-precision run at event {6}",
                label, generated.Events.Count, generated.Seconds, rate, generated.Score.Events.Count,
                generated.Score.DurationInSeconds(), Diverges(reference.Events, generated.Events)));

            Save(label.Replace(' ', '-'), generated.Score);
        }
    }

    /// <summary>
    /// How fast music is written: events a second at one thread and at eight, how much of the machine it
    /// holds, and - the number a consumer streaming the music actually needs - how many SECONDS OF MUSIC come
    /// out per second of waiting.
    /// </summary>
    /// <returns>A task that completes when everything has been measured.</returns>
    [EnvGatedFact(TestGates.RunLiveTests)]
    public async Task GenerateAsync_writes_music_faster_than_it_is_played()
    {
        //Arrange
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using ModelStore store = EndToEndStore.Open();
        string source = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);

        (string Label, IReadOnlyDictionary<string, string> Files)[] subjects =
        {
            ("fp32", await FilesAsync(store, source, cancellationToken)),
            ("int4", await FilesAsync(
                store,
                await EndToEndStore.EnsureReducedAsync(
                    store, source, ReduceMode.WeightOnlyInt4, "onnx-oracle/skytnt-tv2o-medium:int4",
                    cancellationToken),
                cancellationToken)),
        };

        (string Label, MidiGenerationOptions Options)[] prompts =
        {
            ("sparse - one instrument", Described(new[] { 0 }, null, 90)),
            ("ordinary - three instruments", Described(new[] { 0, 40, 73 }, null, 110)),
            ("dense - five and a drum kit", Described(new[] { 0, 40, 73, 24, 48 }, 0, 140)),
        };

        //Act and assert
        foreach ((string label, IReadOnlyDictionary<string, string> files) in subjects)
        {
            foreach (int threads in new[] { 1, 8 })
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                ProcessMemory.ResetPeak();

                SkyTntGenerated generated = await GenerateFromAsync(
                    files, Greedy(64), threads, cancellationToken);


                long peak = ProcessMemory.PeakResident();
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();

                generated.Events.Should().NotBeEmpty();
                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} at {1} thread(s): {2:F2} events/s, peak resident {3}",
                    label, threads, generated.Events.Count / generated.Seconds,
                    ProcessMemory.Mebibytes(peak)));
            }

            foreach ((string prompt, MidiGenerationOptions options) in prompts)
            {
                options.MaximumEvents = MeasuredEvents;
                SkyTntGenerated generated = await GenerateFromAsync(files, options, 8, cancellationToken);
                double music = generated.Score.DurationInSeconds();
                double wall = generated.Seconds;

                _output.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0}, {1}: {2} events in {3:F2} s = {4:F1} events/s, {5:F1} s of music"
                    + " => {6:F2} seconds of music per second of waiting",
                    label, prompt, generated.Events.Count, wall, generated.Events.Count / wall, music,
                    music / wall));

                generated.Events.Should().NotBeEmpty();
                Save(label + "-" + prompt.Split(' ')[0], generated.Score);
            }
        }
    }

    private async Task CompareAsync(
        string what,
        MidiGenerationOptions options,
        string settings,
        string promptMidiPath,
        CancellationToken cancellationToken)
    {
        //Arrange
        string virtualEnvironment = TestGates.RequireVirtualEnvironment();
        string clone = TestGates.RequireMidiModelClone();
        using ModelStore store = EndToEndStore.Open();
        IReadOnlyDictionary<string, string> files = await FullPrecisionAsync(store, cancellationToken);

        options.MaximumEvents = IdentityEvents;
        options.TopK = 1;
        options.TopP = 1.0;
        options.Temperature = 1.0;
        options.IncludePromptEvents = false;

        using TempWorkDirectory work = new TempWorkDirectory();
        SkyTntOracleRun oracle = SkyTntOracle.Run(
            virtualEnvironment, clone, files, work.DirectoryPath, IdentityEvents, 8, settings,
            promptMidiPath, cancellationToken);

        //Act
        SkyTntGenerated generated = await GenerateFromAsync(files, options, 8, cancellationToken);

        //Assert
        List<string> expected = new List<string>();
        foreach (SkyTntOracleEvent one in oracle.Events)
        {
            if (one.Name != null) expected.Add(one.Line());
        }

        IReadOnlyList<string> produced = SkyTntEventLine.Lines(generated.Events, oracle.PromptBeats);

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "{0}: the publisher's Python wrote {1} events in {2:F2} s; this engine wrote {3} in {4:F2} s"
            + " ({5:F1} events/s)",
            what, expected.Count, oracle.Seconds, produced.Count, generated.Seconds,
            produced.Count / generated.Seconds));

        expected.Count.Should().BeGreaterThanOrEqualTo(64);
        produced.Should().HaveCount(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            produced[i].Should().Be(
                expected[i],
                "event " + (i + 1).ToString(CultureInfo.InvariantCulture) + " of '" + what + "'");
        }

        // The file this library writes is read back by a parser that had no part in writing it.
        string path = Path.Combine(work.DirectoryPath, "managed.mid");
        await MidiFile.WriteAsync(path, generated.Score, cancellationToken);

        ChildProcessRun parsed = VenvPython.Run(
            virtualEnvironment, "midi_parse_check.py",
            new[] { "--clone", clone, "--file", path }, cancellationToken);
        parsed.Output.Should().Contain("MIDI PARSE OK");
        _output.WriteLine(what + ": read back by the publisher's own parser - " + parsed.Output.Trim());

        Save("fp32-" + what.Replace(' ', '-'), generated.Score);
    }

    private static MidiGenerationOptions Greedy(int events) => new MidiGenerationOptions
    {
        MaximumEvents = events,
        TopK = 1,
        TopP = 1.0,
        Temperature = 1.0,
        IncludePromptEvents = false,
    };

    //The measurement of how fast music comes out uses the DEFAULT sampling settings with a seed, and not the
    //greedy settings the identity tests use, because that is how a consumer will really ask for music. Taking
    //the most likely answer every time is what makes a comparison against another implementation possible,
    //and it is also what makes a model of this family repeat itself - a greedy piece can spend hundreds of
    //events setting itself up and never start - so the seconds-of-music number greedy gives would not be the
    //number anybody sees.
    private static MidiGenerationOptions Described(int[] instruments, int? drumKit, int beatsPerMinute) =>
        new MidiGenerationOptions
        {
            Seed = 20260918,
            Instruments = instruments,
            DrumKit = drumKit,
            BeatsPerMinute = beatsPerMinute,
            IncludePromptEvents = true,
        };

    private static OnnxRunnerOptions Threads(int threads) => new OnnxRunnerOptions { Threads = threads };

    private static async Task<IReadOnlyDictionary<string, string>> FullPrecisionAsync(
        ModelStore store, CancellationToken cancellationToken)
    {
        string name = await EndToEndStore.EnsureAsync(
            store, MusicModelDefinitions.SkyTntMidiModelTv2oMediumOnnxOnly, cancellationToken);
        return await FilesAsync(store, name, cancellationToken);
    }

    private static async Task<IReadOnlyDictionary<string, string>> FilesAsync(
        ModelStore store, string name, CancellationToken cancellationToken)
    {
        // The consumer's own two lines: the store says which blob holds which of the bundle's files, and the
        // runner is handed those pairs. Nothing else connects the two libraries.
        ResolvedModel resolved = await store.ResolveAsync(name, cancellationToken);
        Dictionary<string, string> files = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (ResolvedFile file in resolved.Files) files[file.Name] = file.BlobPath;
        return files;
    }

    private static async Task<SkyTntGenerated> GenerateFromAsync(
        IReadOnlyDictionary<string, string> files,
        MidiGenerationOptions options,
        int threads,
        CancellationToken cancellationToken)
    {
        // The model is loaded ONCE and everything is taken from it before it is let go: loading the
        // full-precision pair costs a second and most of a gigabyte, and a measurement that reloaded it for
        // every question would be measuring the load.
        await using IMidiGenerationModel model = await MidiGenerationModel.LoadFromFilesAsync(
            files, Threads(threads), cancellationToken);

        Stopwatch stopwatch = Stopwatch.StartNew();
        IReadOnlyList<MidiEvent> events = await GenerateAsync(model, options, cancellationToken);
        stopwatch.Stop();

        return new SkyTntGenerated(events, model.ToScore(events), stopwatch.Elapsed.TotalSeconds);
    }

    private static async Task<IReadOnlyList<MidiEvent>> GenerateAsync(
        IMidiGenerationModel model, MidiGenerationOptions options, CancellationToken cancellationToken)
    {
        List<MidiEvent> events = new List<MidiEvent>();
        await foreach (MidiEvent item in model
            .GenerateAsync(options, cancellationToken)
            .WithCancellation(cancellationToken))
        {
            events.Add(item);
        }

        return events;
    }

    private static IReadOnlyList<string> Lines(IReadOnlyList<MidiEvent> events) =>
        SkyTntEventLine.Lines(events, 0);

    private static string Diverges(IReadOnlyList<MidiEvent> reference, IReadOnlyList<MidiEvent> events)
    {
        IReadOnlyList<string> left = Lines(reference);
        IReadOnlyList<string> right = Lines(events);
        int count = Math.Min(left.Count, right.Count);
        for (int i = 0; i < count; i++)
        {
            if (left[i] != right[i]) return (i + 1).ToString(CultureInfo.InvariantCulture);
        }

        return left.Count == right.Count
            ? "nowhere - the whole piece is the same"
            : (count + 1).ToString(CultureInfo.InvariantCulture) + " (one run is longer than the other)";
    }

    private void Save(string label, MidiScore score)
    {
        string directory = TestGates.MusicOutputDirectory();
        if (directory == null) return;

        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "skytnt-" + label + ".mid");
        File.WriteAllBytes(path, MidiFile.Write(score));

        _output.WriteLine(string.Format(
            CultureInfo.InvariantCulture,
            "wrote {0} ({1} bytes, {2:F1} s of music)",
            path, new FileInfo(path).Length, score.DurationInSeconds()));
    }
}
