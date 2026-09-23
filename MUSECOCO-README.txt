================================================================================
MUSECOCO: TEXT OR ATTRIBUTES TO MIDI
CodeBrix.Ollama.ModelManager and CodeBrix.Ollama.ModelRunner
================================================================================

WHAT IS SUPPORTED
-----------------
MuseCoco's attribute-to-music checkpoint generates REMIGEN2 music tokens, which
ModelRunner decodes into MidiScore and Standard MIDI Files. The optional custom
BERT checkpoint translates a text prompt into categorical music attributes.
An application can inspect, override and reuse those attributes before music
generation. Direct attributes work without loading BERT.

ModelManager stages the original FP32 checkpoints and can separately reduce
either exported graph to INT8 or INT4. Every precision runs in managed .NET 10.
There is no reference between the two libraries, and ModelRunner neither loads
Python nor uses an ONNX native runtime. Its existing llama.cpp libraries are
not involved in MuseCoco inference. Training and ABC output are not implemented.

The tested checkpoints are XinXuNLPer/MuseCoco_attribute2music and
XinXuNLPer/MuseCoco_text2attribute on Hugging Face. These are the music checkpoint
and MuseCoco's fine-tuned custom BERT, not a generic bert-large-uncased model.
The music checkpoint has 24 layers, hidden size 2048 and 24 attention heads
of width 85. BERT has 24 layers, hidden size 1024 and 60 attribute classifiers.
Model weights are acquired separately and are not shipped in either package.

STAGE THE ORIGINAL MODELS (ModelManager)
---------------------------------------
Staging uses the existing CodeBrix.Python integration and a CPython environment
with torch, numpy and onnx. It does not import publisher checkpoint code,
fairseq, transformers or fast-transformers, and does not compile native code.
Use PythonSupport.Check as described in ModelManager's AGENT-README.
The validated environment used torch 2.14.0+cpu, numpy 2.5.3 and onnx 1.22.0.

Supply one root-level .pt checkpoint for music. For BERT supply config.json,
pytorch_model.bin, vocab.txt and tokenizer_config.json from its published model.
Both checkpoints must be FP32. The exporter checks the architecture, tensor
inventory and classifier shapes rather than silently dropping unknown layers.
Torch loads with weights_only=True; the music checkpoint additionally permits
argparse.Namespace for its saved architecture settings.

    using CodeBrix.Ollama.ModelManager;

    using var store = new ModelStore(new ModelStoreOptions
    {
        StoreDirectory = "/models/store",
        Python = new PythonOptions { VirtualEnvironment = "/venvs/staging" }
    });
    try
    {
        await store.ImportBundleAsync("local/muse-music:source", "/checkpoints/music");
        await store.ImportBundleAsync("local/muse-text:source", "/checkpoints/text");
        await store.ExportToOnnxAsync("local/muse-music:source", new ExportOptions
        {
            Route = ExportRoute.MuseCocoMusic, OutputName = "local/muse-music:fp32"
        });
        await store.ExportToOnnxAsync("local/muse-text:source", new ExportOptions
        {
            Route = ExportRoute.MuseCocoText, OutputName = "local/muse-text:fp32"
        });
        await store.MaterializeAsync("local/muse-music:fp32", "/bundles/music-fp32");
        await store.MaterializeAsync("local/muse-text:fp32", "/bundles/text-fp32");
    }
    finally
    {
        // At application shutdown, after ALL Python work has finished.
        PythonSupport.Shutdown();
    }

Music requires an explicit route. Auto recognizes BERT's BertForAttributModel
architecture; an existing ONNX bundle still takes the publisher pass-through
route. Precision must be fp32 for these exports. ReduceOnnxAsync is the separate
quantization step. Set Overwrite=true when intentionally replacing a derived
bundle. Import and materialization have their own overwrite/link options.

QUANTIZE EACH STAGE INDEPENDENTLY (ModelManager)
-----------------------------------------------
The existing managed quantizer needs no Python. It preserves the companion
metadata, vocabulary and schema. For an initial compact combined pipeline:

    ReduceResult music = await store.ReduceOnnxAsync("local/muse-music:fp32",
        new ReduceOptions
        {
            Engine = ReduceEngine.Managed, Mode = ReduceMode.WeightOnlyInt4,
            BlockSize = 128, OutputName = "local/muse-music:int4"
        });
    ReduceResult text = await store.ReduceOnnxAsync("local/muse-text:fp32",
        new ReduceOptions
        {
            Engine = ReduceEngine.Managed, Mode = ReduceMode.WeightOnlyInt8,
            BlockSize = 128, OutputName = "local/muse-text:int8"
        });
    await store.MaterializeAsync(music.Name, "/bundles/music-int4");
    await store.MaterializeAsync(text.Name, "/bundles/text-int8");

Either mode works for either model. NodesToExclude accepts exact ONNX node
names, for example new[] { "decoder.output_projection.MatMul" } for music or
new[] { "pooler.dense.MatMul" } for BERT. The exporter gives constant matrix
multiplications stable checkpoint-module names ending in .MatMul. Excluded
nodes retain their original precision. Exclusions are supported by managed
weight-only/dynamic reduction and the Python quantizers, recorded in provenance,
and refused with PreprocessOnly. Names refer to the graph being reduced; a
Python preprocessing pass can rename or replace nodes.

External weight locations come from ONNX tensor metadata, including nested
graphs. They are resolved relative to each .onnx file. Relative references to
shared weights inside the bundle are supported. Missing files and paths that
escape the bundle are rejected. Shared weights needed by an unselected graph
are preserved; newly produced external files are renamed if needed to avoid
overwriting retained companions. SourceBytes counts shared weight files once.

These are weight-only reductions: activations remain FP32. They use the valid
ONNX com.microsoft:MatMulNBits extension. Another engine must implement that
operator and the requested bit width; generic ONNX support alone is insufficient.
Quantization changes musical choices. Matching a seed does not promise identical
output between FP32, INT8 and INT4, or between engines with different arithmetic.

GENERATE FROM ATTRIBUTES (ModelRunner ONLY)
------------------------------------------
    using CodeBrix.Ollama.ModelRunner;

    using MuseCocoMusicModel music = await MuseCocoMusicModel.LoadFromDirectoryAsync(
        "/bundles/music-int4", new OnnxRunnerOptions { Threads = 4 });
    MusicAttributes attributes = music.Schema.CreateAttributes();
    foreach (MusicAttributeDefinition definition in music.Schema.Definitions)
    {
        if (definition.Name.StartsWith("instrument.", StringComparison.Ordinal))
            attributes = attributes.With(definition.Name,
                definition.Name == "instrument.piano" ? "present" : "absent");
    }
    attributes = attributes.With("tempo", "moderate").With("key", "major")
        .With("time_signature", "4/4").With("bars", "5-8");
    MuseCocoGenerationResult result = await music.GenerateAsync(attributes,
        new MuseCocoGenerationOptions
        {
            MaximumTokens = 1024, MinimumTokens = 128,
            TopK = 15, TopP = 1, Temperature = 1, Seed = 20260921
        }, cancellationToken: cancellationToken);
    await MidiFile.WriteAsync("piece.mid", result.Score, cancellationToken);

Use Schema.Definitions to discover names and categorical values; do not guess
strings. MusicAttributes is immutable: With/WithIndex return a new selection.
Null attributes select every schema default (usually "unspecified"). The model
learned these attributes as guidance; they are not hard note/instrument filters.
MidiScore uses the existing ModelRunner MIDI contracts suitable for application
conversion to CodeBrix.Audio. No CodeBrix.Audio reference is introduced.

STREAM MIDI WHILE LATER BARS ARE GENERATED
-----------------------------------------
GenerateStreamingAsync uses the same model and sampling options as GenerateAsync:

    var events = new List<MidiEvent>();
    await foreach (MidiEvent item in music.GenerateStreamingAsync(attributes,
        new MuseCocoGenerationOptions
        {
            MaximumTokens = 1024, MinimumTokens = 128, Seed = 20260921
        }, cancellationToken: cancellationToken))
    {
        events.Add(item);              // optional: retain a score for saving
        // Enqueue item on your player, using 480 ticks per quarter note.
        // Advance playback only through ticks STRICTLY BELOW item.HorizonTicks.
    }
    // On normal completion, let your player drain its remaining queued events.
    await MidiFile.WriteAsync("piece.mid", new MidiScore(480, events), cancellationToken);

The stream yields notes with their durations, program changes, tempo changes and
time signatures. It buffers the unfinished bar and releases completed bars while
the model continues composing. The final bar is cleaned up and released when EOS
or the token limit ends generation. This avoids playing a partial chord that the
publisher's ending cleanup would discard. Initial playback therefore waits for a
bar boundary; the consumer can buffer more music if generation trails playback.

Events arrive in non-decreasing Tick order. HorizonTicks equals Tick: a later
event can have the SAME tick, so play strictly before the horizon while generating.
Drain the remaining events when enumeration completes. A note's duration is known
when it arrives, even when the note extends into later bars.

Channels and tracks are assigned as instruments first become playable, and each
assignment stays fixed. Channel 9 is percussion. A program change is yielded at
the instrument's first note tick, before that note. Initial 120 BPM and 4/4 events
are supplied when no explicit values exist at tick zero. Musical notes and changes
match completed decoding; channel/track numbers and program-change positions can
differ from GenerateAsync, which sorts all instruments and sets programs at zero.
GenerateAsync and DecodeTokens retain their existing completed-score layout.

An enumeration owns the model until it completes or is disposed, including while
the caller processes an event. Breaking an await foreach disposes the enumeration
and stops generation. Cancellation discards buffered and unconsumed events; events
already yielded remain usable. Finish or dispose the enumeration before disposing
the model. GenerateAsync and GenerateStreamingAsync share the same busy check.
The stream itself does not retain the whole song or return token IDs/statistics;
keep events as above to save them, and use an explicit seed when repeatability is
required. Optional progress still reports generated token counts.

EXPERIMENTAL: CONTINUE WITH RECENT MUSICAL CONTEXT
------------------------------------------------
This path is EXPERIMENTAL. It enables MusicGeneration integration and listening
tests; coherent transitions and several-minute musical quality are not established.
It uses the same staged model and requires no retraining or re-export.

    MuseCocoContinuation context = music.CreateContinuation(contextBars: 4);
    for (int section = 0; section < 6; section++)
    {
        int available = music.MaximumGenerationTokens - context.ContextTokenCount;
        if (available < 1) break;
        long before = context.NextTick;
        await foreach (MidiEvent item in music.GenerateContinuationStreamingAsync(
            context, attributes, new MuseCocoGenerationOptions
            {
                MaximumTokens = Math.Min(512, available), MinimumTokens = 0,
                Seed = 20260922 + section, TopK = 15
            }, cancellationToken: cancellationToken))
        {
            // Enqueue NEW events directly; ticks/channels already span all sections.
            // Advance playback strictly before item.HorizonTicks.
        }
        // The completed section also settles all ticks strictly before context.NextTick.
        if (context.LastGeneratedTokenCount == 0 || context.NextTick == before) break;
    }
    // Drain the remaining playback queue when ending the whole continuation.

The first enumeration generates normally. The context retains at most the last
ContextBars completed bars (default four, configurable from one to sixteen).
Later enumerations replay those tokens after the attribute prefix with fresh
recurrent state, then sample new music. Replayed bars are never emitted again.
Inherited signature, tempo and instrument values are restored in the retained
prompt when needed. The unfinished final position receives normal cleanup; a
partial final bar is closed before starting the next section.

The same context keeps absolute ticks, tempo/signature state and stable channels
across requests. Do not add a per-section time offset. Notes may sustain across
boundaries. Collect all yielded events if saving the entire piece to one MIDI file.
Contexts belong to the model instance that created them. Other generation calls
can use that model between sections, but do not become part of the continuation.

MaximumTokens/MinimumTokens and progress refer only to NEW tokens; the retained
ContextTokenCount also uses the finite position table. An oversized request is
rejected, not silently shortened. Prefilling context costs inference time, so a
consumer still needs buffering if generation cannot keep up with playback.
MinimumTokens=0 allows natural EOS; a context does not guarantee more music on
every request. LastGeneratedTokenCount and NextTick let the caller detect no
progress. Attributes can guide the next section; they do not guarantee continuity.

Enumerate each section fully before starting another. Cancellation, errors or an
early break invalidate that context because some of its events may already have
reached playback. Create a fresh context afterward; the model itself stays reusable.
The existing GenerateAsync and GenerateStreamingAsync remain independent requests.

ADD OPTIONAL TEXT PROMPTING (ModelRunner ONLY)
----------------------------------------------
    using MuseCocoTextModel text = await MuseCocoTextModel.LoadFromDirectoryAsync(
        "/bundles/text-int8", new OnnxRunnerOptions { Threads = 4 });
    MusicAttributePrediction prediction = await text.PredictAsync(
        "A gentle piano piece in a major key.", cancellationToken);
    MusicAttributes edited = prediction.Attributes.With("tempo", "slow");
    MuseCocoGenerationResult result = await music.GenerateAsync(edited,
        new MuseCocoGenerationOptions { Seed = 123 }, cancellationToken: cancellationToken);

Prediction exposes Attributes, per-class Probabilities, TokenIds and WasTruncated.
Probabilities are classifier softmax values, not calibrated quality guarantees.
BERT predicts 60 of the 63 attributes. melody_instrument, chord_brightness and
structure remain at their defaults unless the caller sets them. Prompts use
uncased Unicode normalization and WordPiece. They are truncated to 512 prompt
tokens INCLUDING CLS and SEP; the 60 classifier positions are additional internal
inputs. Empty text is allowed. A null prompt is rejected.

Both model classes also offer LoadFromFilesAsync with an
IReadOnlyDictionary<string,string> of logical bundle names to physical files.
An application may build that map from ResolvedModel.Files; neither library
requires a reference to the other. Pass MusicAttributes between text and music
only when their schemas are compatible; IsCompatibleWith allows an explicit check.

GENERATION AND LIFETIME RULES
----------------------------
- Default MaximumTokens=2560, MinimumTokens=512, TopK=15, TopP=1, Temperature=1.
  Counts are REMIGEN2 TOKENS, not notes or seconds. For a shorter request lower
  MinimumTokens too. EOS can finish a request after the minimum; the maximum
  always bounds work. TopK=1 chooses greedily. TopP is applied after top-k.
- Both generation APIs filter candidates to valid REMIGEN2 continuations BEFORE
  top-k/top-p sampling. Pitch, duration and velocity stay in order; signatures
  occur at bar starts; prompt/special tokens cannot enter the generated music.
  This also applies when MinimumTokens suppresses an otherwise preferred EOS.
  DecodeTokens still rejects malformed saved sequences; no emitted tokens are
  repaired or silently dropped beyond the existing incomplete-ending cleanup.
  The filtering can change fixed-seed output compared with earlier builds.
- MinimumTokens=0 lets the model stop naturally. A high minimum can force weak
  musical continuation even though the MIDI is structurally valid. Token counts
  do not specify playback duration. Use bars/duration_seconds attributes to guide
  length; they are learned preferences, not guarantees of an exact duration.
- Seed=null requests a fresh seed, which the result reports. Fixed seeds must
  be nonnegative and cannot be 0xFFFFFFFF. -1 is rejected. Repeatability also
  requires the same model, settings, build and arithmetic. BERT is deterministic
  and has no sampling seed. MuseCoco shares the MIDI driver's stable RNG.
- Result contains Score, raw generated TokenIds, Attributes, Seed, EndedWithEos,
  PromptTime and GenerationTime. Prefix and EOS are excluded from TokenIds.
  DecodeTokens converts such raw IDs to a score without generating again.
- Progress reports generated token counts. GenerateAsync returns a complete
  score; GenerateStreamingAsync yields MIDI events as bars are completed.
  Both apply the publisher's REMIGEN2 cleanup to an unfinished final position.
- Every request starts with fresh recurrent state. There is no cross-request
  prompt cache. Internal state buffers are reused within a request. Disabling
  OnnxRunnerOptions.ReuseBuffers retains the allocating execution path.
- One request may run on an instance at a time. Concurrent calls are refused.
  Cancellation discards the partial GenerateAsync result; a stream keeps events
  already yielded. Finish or dispose an event enumeration, or cancel and await
  a completed-score request, before disposing the model. Disposal during a
  request is refused. Dispose/DisposeAsync release weights.
- MIDI uses 480 ticks per quarter note and 12 model positions per quarter note.
  Percussion uses channel 9; at most 15 distinct melodic programs fit the output
  channel contract. More programs are explicitly refused, not silently omitted.

BUNDLE CONTRACT: codebrix.musecoco.v1
------------------------------------
Each bundle has musecoco.json and music-attributes.json. Manifest paths are
relative filenames within the bundle; unknown formats or incompatible tensor
contracts are rejected. FP32 weights live in weights.bin; reduced graphs may
inline weights or name a separate external file. Preserve every referenced file.

Music manifest: kind="music", graph="decoder.onnx", vocabulary="vocabulary.json",
layers, hiddenSize, heads, headSize, positionCount, prefixPositionStart,
ticksPerQuarterNote=480, positionsPerQuarterNote=12. Vocabulary is an ordered
array of 1253 strings. The graph has token/position int64[1], one FP32 state_s
[heads,headSize,headSize] and state_z [heads,headSize] per layer; outputs are
logits float[1,vocabularyCount] and corresponding next_s/next_z arrays.

The prefix is leading EOS, 63 attributes in schema order, then <sep>. Preserve
the publisher's positional convention: positions are zero before prefix index
63, then 2 and 3 on its last attribute and separator; generation starts at 4.
The published position table has 8194 entries. MaximumGenerationTokens exposes
the permitted bound (8191 for that table).

Text manifest: kind="text", graph="attributes.onnx", tokenizer="wordpiece.json",
maxSequenceLength=512, classifierCount=60. Inputs are int64[1,sequence]
input_ids, attention_mask, token_type_ids and position_ids. Output is
logits float[1,219], concatenated by ascending schema bertHead. Custom classifier
tokens have word/type embeddings but no position embeddings, as in MuseCoco.

Schema format codebrix.music-attributes.v1 contains id and ordered definitions:
name, publisher key, ordered values/tokens, default index and bertHead (-1 means
direct input only). WordPiece data contains vocabulary, specialTokens, lowercase,
stripAccents, tokenizeChineseCharacters and maxWordCharacters=100. No executable
tokenizer or publisher Python accompanies a staged runtime bundle.

VALIDATION, PERFORMANCE AND LIMITATIONS
--------------------------------------
See MAINTAINER-README.txt, section MUSECOCO VALIDATION, for measured sizes,
fidelity gates, CPU timings, memory and the test commands. EXTRAS-README.txt
describes the local-checkpoint integration test and fixture regeneration.

FP32 export is numerically close to the original publisher rather than bitwise
identical: ELU's negative branch rounds differently in PyTorch and ONNX Runtime.
The controlled fidelity study and accepted probability bounds are documented.
INT4 has larger probability changes than INT8. Valid notes, conditioning and
distribution checks do not establish subjective musical equivalence; audition
generated MIDI in the application's intended instruments.

Intel AVX2/FMA optimizations are guarded at runtime. Scalar and portable vector
paths remain available, including ARM64. ARM performance has not been tuned or
measured. Staging still requires Python on the staging machine; deployment of
the resulting bundles with ModelRunner requires only .NET 10.
