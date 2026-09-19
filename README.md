# CodeBrix.Ollama

Two cross-platform .NET libraries for **downloading, managing and running large language
models in-process**, with no Ollama installation and no server:

* **CodeBrix.Ollama.ModelManager** - a local model store laid out exactly as Ollama's own
  (`blobs/sha256-<hex>` and `manifests/<host>/<namespace>/<model>/<tag>`), fed from any registry
  that speaks Ollama's manifest-and-blob protocol (`registry.ollama.ai` by default, Hugging Face
  through `hf.co/<user>/<repo>:<quant>` names). It pulls with resumable parallel downloads and
  sha256 verification, lists, shows, copies, deletes and creates models from Modelfiles, reads
  GGUF metadata, and resolves a model name to the GGUF files on disk. It also obtains models
  that no such registry serves - a Hugging Face file repository, any list of HTTPS addresses, a
  folder on disk - into the same store, and lays their files back out as the publisher wrote
  them. It converts a checkpoint into a GGUF model a runner can load, stores a smaller,
  quantized copy of a model that a quantizer the application supplies writes, exports a model to
  ONNX and makes the graphs it holds several times smaller, keeping each result in the same store
  with a record of what made it. Pure managed code.
* **CodeBrix.Ollama.ModelRunner** - runs a GGUF model in-process over a self-built native
  inference engine, bound through hand-written P/Invoke and exposed to the application through
  an interface contract: completion, streaming chat through the model's own chat template, tool
  calling, separated reasoning, embeddings, grammar- and JSON-schema-constrained output,
  tokenization and model metadata. The same engine rewrites a model file at a smaller
  quantization, so making a model smaller needs nothing installed either. One native library per
  supported platform ships inside the package; nothing is downloaded and nothing is compiled on
  the machine. It **also runs ONNX models**, on a pure-managed interpreter inside the same
  package: tensors in and tensors out for a graph of your own, MIDI music streamed event by
  event as it is written, or text through the same interface contract - with nothing installed
  at all and nothing native, so it runs wherever .NET runs.

The two packages do not depend on each other. An application asks ModelManager for the path of
a model and hands that path to ModelRunner, or to any other in-process runner. The two are
released together and should be installed at the same version.

Both are provided as .NET 10 libraries and associated `*.MitLicenseForever` NuGet packages, and
support applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Ollama.ModelManager.MitLicenseForever
dotnet add package CodeBrix.Ollama.ModelRunner.MitLicenseForever
```

Add either one on its own, or both. Note that the NuGet package IDs and the namespaces are
different - there are no packages named plain `CodeBrix.Ollama.ModelManager` or
`CodeBrix.Ollama.ModelRunner`:

* NuGet package IDs: `CodeBrix.Ollama.ModelManager.MitLicenseForever` and
  `CodeBrix.Ollama.ModelRunner.MitLicenseForever`
* Assemblies and primary namespaces: `CodeBrix.Ollama.ModelManager` and
  `CodeBrix.Ollama.ModelRunner` - i.e. `using CodeBrix.Ollama.ModelManager;` and
  `using CodeBrix.Ollama.ModelRunner;`

The two packages are versioned and released together: when an application uses both, install
them at the SAME version.

The `.MitLicenseForever` suffix belongs to the package ID only; it never appears in a namespace, a
using directive or a type name. Each library declares exactly one namespace, and every public type
lives in it - the folders you see in the repository are file organization, not namespaces.

XML documentation (IntelliSense) ships alongside each assembly.

ModelRunner has no NuGet dependencies at all: its dependency group is empty and it pulls in
nothing but the .NET runtime - and that stays true of running ONNX models, which is managed code
inside the package and needs no runtime, no interpreter and no native library. ModelManager has
exactly one, `CodeBrix.Python`, and it is inert - nothing of it is loaded until you use a Python
feature, and obtaining, listing, resolving and materializing models are not Python features, nor
is converting a checkpoint to GGUF, nor is making an existing ONNX graph smaller. Everything else
in both libraries is in-box: downloads go through `HttpClient`, JSON through `System.Text.Json`
and hashing through `System.Security.Cryptography`.

The ModelRunner package additionally carries a native inference library for each supported
runtime identifier, in the standard NuGet `runtimes/<rid>/native/` layout - `win-x64`,
`win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-riscv64`. NuGet gives
your application the one it needs. There is no build-time compilation and no run-time download.
That library is for GGUF models only; nothing of it is loaded to run an ONNX model, which is why
that road works on a platform with no native library in the package at all.

## CodeBrix.Ollama.ModelManager supports:

* Pulling a model from the Ollama registry, from Hugging Face (`hf.co/...` names), or from any
  registry that serves Ollama's `/v2/<namespace>/<model>/manifests/<tag>` and `blobs/<digest>`
  protocol - with parallel byte-range downloads, resumption after interruption, sha256 verification
  of every layer, and a progress stream
* Pulling a *bundle* - a model that no such registry serves - from a Hugging Face file
  repository, or from any list of HTTPS addresses including the objects under a public storage
  bucket prefix, into the same store: the same resumable, hash-verified downloads, plus
  include/exclude filters over the publisher's own file paths
* Importing a folder already on disk as a bundle, which is the route for anything a publisher
  keeps behind a sign-in
* Materializing a bundle as the file tree its publisher wrote, hard-linked by default so laying
  it out costs no disk space
* Exporting a bundle to ONNX and keeping the result in the same store as a *derived bundle* that
  records what made it and from what: the graphs a publisher already ships are passed through with
  no conversion and no Python at all, and a checkpoint is converted by the ONNX Runtime GenAI model
  builder or by Hugging Face Optimum, running in a CPython the machine already has
* Converting a checkpoint into a GGUF model with **nothing installed at all**: the weights a
  publisher ships - `safetensors` or PyTorch containers, one file or many - are read by this
  library's own managed code, mapped on to the tensor names the native inference engine expects
  and written as an ordinary GGUF model in the same store, which `ResolveAsync` then hands to a
  runner like any other. No Python and no publisher tooling; tensors are read one at a time and
  written as they are read, so a checkpoint of several gigabytes converts in a process that
  holds a tensor's worth of buffers rather than a file's
* Reducing the ONNX graphs a bundle holds - dynamic INT8, or block-wise four- or eight-bit
  weights - into another derived bundle a few times smaller than the one it came from, with the
  files that are not graphs carried through unchanged and the settings recorded; a reduced model
  is an approximation of the one it came from, so measure it before you ship it
* Making an existing `.onnx` graph smaller with **nothing installed at all**: block-wise four- and
  eight-bit weights, and dynamic INT8 on a graph that has already been prepared, are done by this
  library's own managed ONNX codec and quantizer - no Python, no native library - and write the
  same bytes ONNX Runtime's own tools write for the same model, which the test suite checks
  against real published models file by file
* Storing a **quantized copy of a GGUF model** in the same store, under `<name>:gguf-<type>`, with
  the source's template, parameters, messages and licence carried over and the provenance
  recorded. The quantizer is a delegate the application supplies - this package depends on no
  inference engine, so the one line that joins it to CodeBrix.Ollama.ModelRunner is written in the
  application's own code
* Reporting the licence a publisher states - the identifier, the address it was read from, and
  the `LICENSE` text a bundle ships - and applying no rule of its own
* A store directory interchangeable with a real Ollama install's `~/.ollama/models` (the default
  location, honoring `OLLAMA_MODELS`), so models pulled by either are visible to both
* List, show, exists, copy and delete, with unreferenced blobs removed when the last manifest that
  used them goes
* Creating a model from a Modelfile: `FROM` a GGUF file on disk or a model already in the store,
  plus `TEMPLATE`, `SYSTEM`, `PARAMETER`, `LICENSE`, `MESSAGE` and `ADAPTER`
* Resolving a name to everything an in-process runner needs: the GGUF weights (and shards),
  projector and adapter files, the template and system text, default parameters, licenses and
  preset messages
* A Modelfile parser that accepts exactly what Ollama accepts, with typed parameters
* A GGUF metadata reader (versions 1 to 3, both byte orders) that reads key-values and tensor
  descriptors and never tensor data
* Ollama's model-name grammar (`[scheme://][host/][namespace/]model[:tag]`) with the same defaults
* An async-only API: every public operation returns `Task`, `Task<T>` or `IAsyncEnumerable<T>` and
  takes a `CancellationToken` as its last parameter

Not in this package: running a model, rendering chat templates, pushing to a registry,
safetensors layers in an Ollama manifest, exporting a checkpoint to ONNX without the publisher's
own tooling, deciding anything about a licence, or any HTTP server.

## CodeBrix.Ollama.ModelRunner supports:

* Loading a GGUF file into your own process - `ModelRunner.LoadAsync(options)` returns an
  `IRunningModel` - or reading everything the engine knows about a file without loading its
  weights, with `ModelRunner.ProbeAsync(path)`
* Completion from a raw prompt, streamed token by token (`GenerateAsync`) or awaited whole
  (`GenerateToEndAsync`), with stop sequences, a token limit and Ollama's own sampling defaults
* Chat through the model's own chat template (`ChatAsync` / `ChatToEndAsync`), in three dialects:
  the **Jinja** template embedded in the GGUF file, an **Ollama** Go template (the `TEMPLATE` text
  ModelManager resolves for you), or the engine's own built-in **native** templates - `Auto`
  picks the right one. `RenderChatPromptAsync` returns the rendered prompt without generating
* Reasoning separated from the answer, so a thinking model's `<think>` block arrives as
  `ThinkingDelta` and never contaminates the content, with a per-request `Think` switch
* Tool calling: hand over `ToolDefinition`s and read back `ToolCall`s with their arguments as
  JSON, in either the JSON or the `<function=...><parameter=...>` convention, chosen from the
  template
* Structured output that is valid the first time: a GBNF grammar, a JSON schema this library
  converts into one, or plain JSON mode - constrained during sampling rather than validated and
  retried
* Embeddings (`EmbedAsync`), tokenization and detokenization, and a prefix cache that makes the
  next turn of a conversation re-evaluate only what changed
* LoRA adapters applied at load or swapped at run time, a progress callback for long loads,
  cancellation that interrupts even a large model's prompt evaluation, and a log handler
  (`ModelRunner.SetLogHandler`) that hands you the engine's own output
* Rewriting a GGUF file at a smaller quantization (`ModelRunner.QuantizeAsync`) with the same
  engine that runs it and **nothing installed** - `Q4_K_M`, `Q8_0`, `Q6_K` and the rest, byte for
  byte what the engine's own command-line quantizer writes, with no partial file ever left behind
* Public utilities you can use on their own, with no model loaded: `JinjaTemplate`,
  `OllamaTemplate`, `ThinkingParser`, `ToolCallParser`, `ToolCallFormat` and `JsonSchemaGrammar`
* **Running an ONNX graph with nothing installed at all** - no ONNX runtime, no Python, no native
  library and no NuGet dependency: the interpreter, its operators, the tokenizer and the model
  drivers are managed code inside the package, so a graph runs wherever .NET runs, on the CPU.
  `OnnxModel.LoadAsync` is the raw surface - tensors in by name, tensors out by name, with the
  graph's own metadata, and a decoder's key/value cache fed from one step to the next without a
  byte being copied. Full-precision and quantized graphs alike; a four-bit graph keeps four-bit
  weights in memory
* **Generating MIDI music from an ONNX music model** (`MidiGenerationModel`), streamed as it is
  written: every event carries its absolute position in ticks, a note carries its own length, and
  each one says how far the piece is settled - so an application can start playing the beginning
  of a piece whose end does not exist yet, and save the whole of it as a Standard MIDI File
  afterwards. It can also continue a piece read from a file
* **Generating text from an ONNX bundle** (`OnnxCausalLmModel`), through the same `IRunningModel`
  contract, the same `GenerationOptions` and the same `SamplingOptions` as the rest of the
  package, with a managed byte-level byte-pair tokenizer read from the bundle's own files. What a
  bundle cannot honour - chat templates, embeddings, adapters and grammar-constrained output - is
  refused by name rather than answered wrongly
* The same async-only API rule: `Task`, `Task<T>` or `IAsyncEnumerable<T>`, with a
  `CancellationToken` last

Not in this package: an HTTP server or client, multimodal (vision or audio) models, speculative
decoding with a draft model, converting a training framework's checkpoint into a model file or
making an ONNX graph smaller (that is the other package's work), keeping a store of models,
session save and restore, serving many conversations from one loaded model at once, training, or
running an ONNX graph on an accelerator - that road is CPU-only by design, because that is what
runs everywhere with nothing installed.

## Requirements

* .NET 10 or later, on Windows, macOS or Linux (x64 everywhere, ARM64 on all three, plus
  RISC-V 64 on Linux).
* For ModelManager: outbound HTTPS to the registry a model name refers to, or to the host a
  bundle's files come from, for pulls only.
  Everything else works offline against the local store. Disk space for the store; a pull writes
  to the store directory and nowhere else.
* For ModelManager's ONNX features: exporting a checkpoint to ONNX needs a CPython shared library
  on the machine with the export tooling installed in it, and the library says which module is
  missing and how to install it when one is not. Passing a publisher's own ONNX graphs through, and
  making an existing graph smaller with block-wise four- or eight-bit weights, need nothing
  installed.
* For ModelManager's GGUF conversion: nothing at all. It is pure managed code - no CPython, no pip
  module, no native library - even though what it reads is a checkpoint written by a training
  framework. Point `TMPDIR` at a real file system if yours is in memory; the file it writes is as
  large as the model.
* For ModelRunner's quantization: nothing at all - the quantizer is the engine already inside the
  package. Room beside the output for a second copy of the model, and patience: it reads the whole
  model and writes a whole new one.
* For ModelRunner: a GGUF file on disk, and enough memory for it. Memory mapping is the default
  load mode, so the weights are paged in on demand rather than read in, and a 20 GB model runs on
  a 32 GiB machine. No GPU is required - CPU-only inference is the tested path.
* For ModelRunner's ONNX models: nothing at all - no runtime, no Python, no native library and no
  package. The files of the model on disk, and memory for the graph's weights plus a little
  working room; the peak while a graph is read has been measured at up to about twice what the
  loaded graph then settles at, so size for the load and not for the steady state. It runs on
  every platform .NET runs on, including ones this package ships no native library for, because
  that road never loads one.

## Sample Code

### Pull a Model and Resolve It to a File

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();   // ~/.ollama/models, or OLLAMA_MODELS

await foreach (var progress in store.PullAsync("smollm:135m"))
{
    if (progress.Digest != null)
        Console.WriteLine($"{progress.Status} {progress.Percent:F0}%");
    else
        Console.WriteLine(progress.Status);
}

var resolved = await store.ResolveAsync("smollm:135m");
Console.WriteLine($"GGUF: {resolved.ModelPath}");
Console.WriteLine($"Template: {resolved.Template}");
```

### Pull a Model's Files from a Hugging Face Repository and Lay Them Out

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();

const string name = "hf.co/example-org/example-model";

// A bundle pull is asked for. PullAsync(name) on its own still means the Ollama registry
// protocol, which cannot serve a repository of a publisher's own files.
var options = PullOptions.ForHuggingFace(null, null, FileFilter.ExcludeTrainingArtifacts);

await foreach (var progress in store.PullAsync(name, options))
    Console.WriteLine(progress.Status);   // listing ... / pulling config.json / ... / success

var info = await store.ShowAsync(name);
Console.WriteLine($"{info.Format}, licence {info.License.LicenseId ?? "not stated"}");

// The publisher's own file tree, hard-linked out of the store.
foreach (var path in await store.MaterializeAsync(name, "/work/example-model"))
    Console.WriteLine(path);
```

### Export a Bundle to ONNX and Make It Smaller

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();

// Export. The automatic route passes a publisher's own .onnx files through - no conversion,
// no Python, no bytes copied - and converts a checkpoint with the tooling that suits it.
ExportResult exported = await store.ExportToOnnxAsync("hf.co/example-org/example-model");
Console.WriteLine($"{exported.Name} by {exported.Tool} ({exported.RouteUsed})");

// Reduce. Block-wise four-bit weights are this library's own managed code: nothing installed,
// and the same bytes ONNX Runtime's own tools write.
ReduceResult reduced = await store.ReduceOnnxAsync(
    exported.Name, new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4 });

Console.WriteLine($"{reduced.Name}: {reduced.SourceBytes} -> {reduced.ReducedBytes} bytes " +
                  $"({reduced.EngineUsed})");

// Both results are ordinary models in the same store, recording what made them.
var info = await store.ShowAsync(reduced.Name);
Console.WriteLine($"{info.Tool} {info.ToolVersion} made this from {info.DerivedFrom}");

foreach (var path in await store.MaterializeAsync(reduced.Name, "/work/example-model-int4"))
    Console.WriteLine(path);
```

### Convert a Checkpoint to a GGUF Model

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();

// The publisher's own files, then the conversion. Nothing is installed for either
// step, and no interpreter is started: the readers, the tokenizer and the GGUF
// writer are this library's own managed code.
const string name = "hf.co/example-org/example-model";
await foreach (var progress in store.PullAsync(
    name, PullOptions.ForHuggingFace(null, null, FileFilter.ExcludeTrainingArtifacts)))
{
    Console.WriteLine(progress.Status);
}

ConvertResult result = await store.ConvertToGgufAsync(name);

Console.WriteLine($"{result.Name}: {result.TensorCount} tensors, " +
                  $"{result.OutputBytes} bytes, {result.TypeWritten}");

// An ordinary GGUF model in the same store - resolve it and hand the path on.
var resolved = await store.ResolveAsync(result.Name);
Console.WriteLine(resolved.ModelPath);
```

### Make a Stored Model Smaller

```csharp
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelRunner;

using var store = new ModelStore();

// The store finds the file, names the result and records where it came from; the
// runner does the quantizing. Neither package references the other, so the line
// that joins them is this one, here, in your code.
QuantizeGgufResult result = await store.QuantizeGgufAsync(
    "hf.co/example-org/example-model:gguf",
    new QuantizeGgufOptions
    {
        Type = "q4_k_m",
        Tool = "CodeBrix.Ollama.ModelRunner",
        Quantizer = (input, output, ct) => ModelRunner.QuantizeAsync(
            input, output, GgufQuantizationType.Q4_K_M, null, ct),
    });

Console.WriteLine($"{result.Name}: {result.SourceBytes} -> {result.OutputBytes} bytes");

// An ordinary model in the same store - about a third of the size, and it loads.
var resolved = await store.ResolveAsync(result.Name);
await using IRunningModel model = await ModelRunner.LoadAsync(
    new ModelRunnerOptions { ModelPath = resolved.ModelPath });
```

### Describe What Is in the Store

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();

foreach (var model in await store.ListAsync())
    Console.WriteLine($"{model.DisplayName}  {model.Config.ModelFamily}  {model.Config.ModelType}  {model.Config.FileType}  {model.Size / (1024 * 1024)} MB");

var info = await store.ShowAsync("smollm:135m");
Console.WriteLine($"{info.Metadata.Architecture}, context {info.Metadata.ContextLength}, {string.Join(", ", info.Capabilities)}");
```

### Create a Model from a Modelfile

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore();

var modelfile = Modelfile.Parse("""
    FROM smollm:135m
    SYSTEM You are a terse assistant.
    PARAMETER temperature 0.2
    PARAMETER stop <|im_end|>
    """);

await store.CreateAsync("my-smollm", modelfile);
```

### Use a Store Somewhere Else, or a Different Registry

```csharp
using CodeBrix.Ollama.ModelManager;

using var store = new ModelStore(new ModelStoreOptions
{
    StoreDirectory = "/data/models",
    MaxConcurrentParts = 8,
});

await foreach (var _ in store.PullAsync("hf.co/HuggingFaceTB/smollm-360M-instruct-v0.2-Q8_0-GGUF")) { }
```

### Load a Model and Hold a Conversation

```csharp
using CodeBrix.Ollama.ModelRunner;

await using IRunningModel model = await ModelRunner.LoadAsync(new ModelRunnerOptions
{
    ModelPath = "/models/smollm-360m-instruct-q8_0.gguf",
    ContextSize = 4096,
});

var request = new ChatRequest();
request.Messages.Add(new ChatMessage(ChatRole.System, "Answer in one short sentence."));
request.Messages.Add(new ChatMessage(ChatRole.User, "What colour is the sky?"));

ChatResponse reply = await model.ChatToEndAsync(request);

Console.WriteLine(reply.Message.Content);
Console.WriteLine($"{reply.Statistics.TokensPerSecond:F1} tokens/s");
```

### Stream the Reply, with the Reasoning Kept Apart

```csharp
using CodeBrix.Ollama.ModelRunner;

var request = new ChatRequest { Think = true };
request.Messages.Add(new ChatMessage(ChatRole.User, "Why is the sky blue?"));

await foreach (ChatUpdate update in model.ChatAsync(request))
{
    if (update.ThinkingDelta != null) Console.Write(update.ThinkingDelta);
    if (update.ContentDelta != null) Console.Write(update.ContentDelta);

    if (update.IsFinal)
        Console.WriteLine($"[{update.FinishReason}] {update.Statistics.GeneratedTokens} tokens");
}
```

### Ask for a Tool

```csharp
using CodeBrix.Ollama.ModelRunner;

var request = new ChatRequest();
request.Tools.Add(new ToolDefinition
{
    Name = "get_weather",
    Description = "Look up the current weather in a city.",
    ParametersJsonSchema =
        @"{""type"":""object"",""properties"":{""city"":{""type"":""string""}},""required"":[""city""]}",
});
request.Messages.Add(new ChatMessage(ChatRole.User, "What is the weather in Paris?"));

ChatResponse reply = await model.ChatToEndAsync(request);

foreach (ToolCall call in reply.Message.ToolCalls)
    Console.WriteLine($"{call.Name}({call.ArgumentsJson})");   // get_weather({"city":"Paris"})
```

### Constrain the Output to a JSON Schema

```csharp
using CodeBrix.Ollama.ModelRunner;

var options = new GenerationOptions
{
    MaxTokens = 200,
    JsonSchema =
        @"{""type"":""object"",""properties"":{""city"":{""type"":""string""},
           ""country"":{""type"":""string""}},""required"":[""city"",""country""]}",
};

GenerationResult result = await model.GenerateToEndAsync(
    "Give me the capital of France as JSON.", options);

Console.WriteLine(result.Text);   // parses the first time; the grammar saw to that
```

### Run an ONNX Model, with Nothing Installed

```csharp
using CodeBrix.Ollama.ModelRunner;

// A graph of your own: tensors in by name, tensors out by name.
await using IOnnxModel graph = await OnnxModel.LoadAsync("/models/example/model.onnx");

foreach (OnnxValueMetadata input in graph.Metadata.Inputs)
    Console.WriteLine($"{input.Name} {input.ElementType}");

var outputs = graph.Run(new Dictionary<string, OnnxTensor>
{
    ["input_ids"] = OnnxTensor.FromInt64(new long[] { 1, 2, 3 }, 1, 3),
});

Console.WriteLine(outputs["logits"].Floats.Length);

// Or text, through the same contract the GGUF road hands back.
await using IOnnxCausalLmModel model =
    await OnnxCausalLmModel.LoadFromDirectoryAsync("/models/example-onnx");

GenerationResult result = await model.GenerateToEndAsync(
    "the prompt", new GenerationOptions { MaxTokens = 64 });

Console.WriteLine(result.Text);
```

### Generate MIDI Music and Start Playing It Before It Is Finished

```csharp
using CodeBrix.Ollama.ModelRunner;

await using IMidiGenerationModel model =
    await MidiGenerationModel.LoadFromDirectoryAsync("/models/example-midi-onnx");

var options = new MidiGenerationOptions { MaximumEvents = 512, Seed = 42 };
var events = new List<MidiEvent>();

// `player` is YOUR OWN streaming MIDI player - nothing in this package knows about
// one. Events arrive AS THEY ARE MADE, and a model may write music more slowly than
// it is played, so buffer enough of the piece before you start it; HorizonTicks is
// how far the piece is settled, and channels here are 0-15, so a player numbering
// them 1-16 adds one.
await foreach (MidiEvent e in model.GenerateAsync(options))
{
    events.Add(e);

    if (e.Kind == MidiEventKind.Note)
        player.AppendNote(e.Tick, e.Channel + 1, e.NoteNumber, e.Velocity, e.DurationTicks);

    player.SettledThrough(e.HorizonTicks);
}

await model.SaveAsync("/tmp/piece.mid", events);
```

### Pair the Two Packages

```csharp
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelRunner;

using var store = new ModelStore();
var resolved = await store.ResolveAsync("smollm:135m");

await using IRunningModel model = await ModelRunner.LoadAsync(new ModelRunnerOptions
{
    ModelPath = resolved.ModelPath,       // the GGUF file on disk
    OllamaTemplate = resolved.Template,   // the Modelfile TEMPLATE text
});
```

An ONNX model is many files rather than one, so the seam is a name-to-path pair per file -
still nothing but strings, and still written here in your own code:

```csharp
using CodeBrix.Ollama.ModelManager;
using CodeBrix.Ollama.ModelRunner;

using var store = new ModelStore();
var resolved = await store.ResolveAsync("example/midi-model:onnx");

var files = new Dictionary<string, string>();
foreach (ResolvedFile file in resolved.Files)
    files[file.Name] = file.BlobPath;     // nothing is copied or laid out first

await using IMidiGenerationModel model =
    await MidiGenerationModel.LoadFromFilesAsync(files);
```

Neither package references the other, in either direction and at either level. They are
released together, so install both at the same version.

## Documentation

Each NuGet package includes its own `AGENT-README.txt`, a complete API reference and usage guide
written for AI coding agents - point your agent at that file when it is writing code against the
library.

Additional sample code and usage examples are available in the test projects:
https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelManager.Tests
https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelRunner.Tests

## License

CodeBrix.Ollama is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Ollama/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
these packages, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Ollama/blob/main/THIRD-PARTY-NOTICES.txt).
