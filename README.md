# CodeBrix.Ollama

Two cross-platform, zero-dependency .NET libraries for **downloading, managing and running
large language models in-process**, with no Ollama installation and no server:

* **CodeBrix.Ollama.ModelManager** - a local model store laid out exactly as Ollama's own
  (`blobs/sha256-<hex>` and `manifests/<host>/<namespace>/<model>/<tag>`), fed from any registry
  that speaks Ollama's manifest-and-blob protocol (`registry.ollama.ai` by default, Hugging Face
  through `hf.co/<user>/<repo>:<quant>` names). It pulls with resumable parallel downloads and
  sha256 verification, lists, shows, copies, deletes and creates models from Modelfiles, reads
  GGUF metadata, and resolves a model name to the GGUF files on disk. Pure managed code.
* **CodeBrix.Ollama.ModelRunner** - runs a GGUF model in-process over a self-built llama.cpp
  engine, bound through hand-written P/Invoke and exposed to the application through an
  interface contract: completion, streaming chat through the model's own chat template, tool
  calling, separated reasoning, embeddings, grammar- and JSON-schema-constrained output,
  tokenization and model metadata. One native library per supported platform ships inside the
  package; nothing is downloaded and nothing is compiled on the machine.

The two packages do not depend on each other. An application asks ModelManager for the path of
a model and hands that path to ModelRunner, or to any other in-process GGUF runner.

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

The `.MitLicenseForever` suffix belongs to the package ID only; it never appears in a namespace, a
using directive or a type name. Each library declares exactly one namespace, and every public type
lives in it - the folders you see in the repository are file organization, not namespaces.

XML documentation (IntelliSense) ships alongside each assembly.

Neither package has any NuGet dependencies: each dependency group is empty and they pull in
nothing but the .NET runtime. Downloads go through the in-box `HttpClient`, JSON through
`System.Text.Json` and hashing through `System.Security.Cryptography`.

The ModelRunner package additionally carries a native inference library for each supported
runtime identifier, in the standard NuGet `runtimes/<rid>/native/` layout - `win-x64`,
`win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64` and `linux-riscv64`. NuGet gives
your application the one it needs. There is no build-time compilation and no run-time download.

## CodeBrix.Ollama.ModelManager supports:

* Pulling a model from the Ollama registry, from Hugging Face (`hf.co/...` names), or from any
  registry that serves Ollama's `/v2/<namespace>/<model>/manifests/<tag>` and `blobs/<digest>`
  protocol - with parallel byte-range downloads, resumption after interruption, sha256 verification
  of every layer, and a progress stream
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
safetensors models, or any HTTP server.

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
* Public utilities you can use on their own, with no model loaded: `JinjaTemplate`,
  `OllamaTemplate`, `ThinkingParser`, `ToolCallParser`, `ToolCallFormat` and `JsonSchemaGrammar`
* The same async-only API rule: `Task`, `Task<T>` or `IAsyncEnumerable<T>`, with a
  `CancellationToken` last

Not in this package: an HTTP server or client, multimodal (vision or audio) models, speculative
decoding with a draft model, quantizing or writing model files, session save and restore, or
serving many conversations from one loaded model at once.

## Requirements

* .NET 10 or later, on Windows, macOS or Linux (x64 everywhere, ARM64 on all three, plus
  RISC-V 64 on Linux).
* For ModelManager: outbound HTTPS to the registry a model name refers to, for pulls only.
  Everything else works offline against the local store. Disk space for the store; a pull writes
  to the store directory and nowhere else.
* For ModelRunner: a GGUF file on disk, and enough memory for it. Memory mapping is the default
  load mode, so the weights are paged in on demand rather than read in, and a 20 GB model runs on
  a 32 GiB machine. No GPU is required - CPU-only inference is the tested path.

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
