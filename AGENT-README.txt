================================================================================
AGENT-README: CodeBrix.Ollama.ModelRunner
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Ollama.ModelRunner.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Ollama.ModelRunner is a cross-platform, zero-dependency .NET 10
library that loads a model file and runs it IN YOUR OWN PROCESS. There is no
server to start, no daemon to talk to, no Ollama installation to find, no HTTP
anywhere, and no NuGet dependency of any kind.

IT HAS TWO ROADS THROUGH IT, and which one you take is decided by the file you
have. A GGUF file runs over a llama.cpp engine this repository builds itself
and ships inside the package; an ONNX graph runs on an interpreter written in
managed code inside the same assembly, which needs nothing installed at all and
runs wherever .NET runs. The GGUF road is everything from here to QUANTIZING A
GGUF MODEL below; the ONNX road is the one section RUNNING ONNX MODELS. They
share the package and nothing else.

What it does with a GGUF file, through one interface:

  1. COMPLETION -- stream tokens from a raw prompt, or wait for the whole
     text. Stop sequences, a token limit, and Ollama's sampling defaults.
  2. CHAT -- render a conversation through the model's own chat template,
     stream the reply, separate the model's reasoning from its answer, and
     read back the tool calls it asked for.
  3. EMBEDDINGS, TOKENIZATION AND METADATA -- one vector per input, text to
     token ids and back, and everything the engine knows about a model file,
     with or without loading its weights.
  4. CONSTRAINED OUTPUT -- a GBNF grammar, or a JSON schema this library
     converts into one, so structured output comes back valid the first time
     rather than being validated and retried.
  5. QUANTIZATION -- rewrite a GGUF file at a smaller type (Q4_K_M, Q8_0 and
     the rest), with the same engine that runs it and nothing installed. The
     file is byte for byte what the engine's own command-line quantizer
     writes.

And what it does with an ONNX graph, on managed code with NOTHING installed:

  6. RUNNING AN ONNX GRAPH -- tensors in by name, tensors out by name, with
     the graph's own metadata; a driver that generates MIDI music and streams
     the events as it writes them; and a driver that generates TEXT through
     the same IRunningModel contract. See RUNNING ONNX MODELS below.

Everything that touches the engine is async and takes a CancellationToken
last, with a default. The template engines, the parsers and the schema-to-
grammar converter are ordinary synchronous, pure-managed types, and there are
no synchronous wrappers over the async work.

Target framework: .NET 10 or later; no netstandard and no .NET Framework
target. Source: https://github.com/ellisnet/CodeBrix.Ollama

HOW THIS RELATES TO CodeBrix.Ollama.ModelManager
------------------------------------------------
The same repository produces a second, SEPARATE package,
CodeBrix.Ollama.ModelManager.MitLicenseForever, which keeps a local model
store in exactly Ollama's on-disk layout, pulls models into it and resolves a
model name to the files on disk. It has its own AGENT-README, and nothing in
this document describes its API. The two packages are independent -- neither
references the other -- and the seam between them is a FILE PATH (or, for a
bundle of several files, a name-to-path pair per file; see RUNNING ONNX MODELS):

    ResolvedModel resolved = await store.ResolveAsync("smollm:135m");

    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = resolved.ModelPath,     // the GGUF file
            OllamaTemplate = resolved.Template, // the Modelfile TEMPLATE
        });

ModelManager is not required. Any GGUF file on disk works, from anywhere.


INSTALLATION
============
PackageId: CodeBrix.Ollama.ModelRunner.MitLicenseForever

    dotnet add package CodeBrix.Ollama.ModelRunner.MitLicenseForever

IMPORTANT: the ".MitLicenseForever" suffix belongs to the PACKAGE ID only. It
never appears in a namespace, a using directive, an assembly name or a type
name; the assembly is CodeBrix.Ollama.ModelRunner and the single namespace is
CodeBrix.Ollama.ModelRunner. NuGet dependencies: NONE -- the dependency group
is empty, JSON goes through the in-box System.Text.Json, and the native
engine is inside the package. License: MIT, with license acceptance required.

THE TWO CodeBrix.Ollama PACKAGES ARE VERSIONED AND RELEASED TOGETHER. If your
application also uses CodeBrix.Ollama.ModelManager.MitLicenseForever, install
both at the SAME version. A mixture builds without complaint and then fails at
run time with a message telling you to do exactly this.

THE PACKAGE CARRIES NATIVE CODE, one library per runtime identifier, in the
standard NuGet runtimes/<rid>/native/ layout: win-x64, win-arm64, osx-x64,
osx-arm64, linux-x64, linux-arm64 and linux-riscv64. Nothing is downloaded at
run time and nothing is compiled on the machine. See THE NATIVE LIBRARY below
for how it is found and what goes wrong when it is not.

NULLABLE REFERENCE TYPES ARE OFF in the library, so the compiler will not warn
you about anything it hands back. Where a member can be null its XML
documentation says so, and this file repeats it: treat ModelDetails.Name,
ModelDetails.ChatTemplate, ChatMessage.Content, ChatMessage.Thinking,
ChatMessage.ToolCallId, ChatMessage.ToolName, ToolCall.Id, ResponseFormat
.JsonSchema, ToolDefinition.ParametersJsonSchema and the Statistics property
of GenerationUpdate and ChatUpdate (null until the final update) as nullable
however your own project is configured.


KEY NAMESPACES / USINGS
=======================

    using CodeBrix.Ollama.ModelRunner;   // EVERY public type in the package

That is the whole story. The library declares ONE namespace and every public
type lives in it, on BOTH roads -- the ONNX types are in the same namespace as
the GGUF ones and need no second using. The repository folders (Contracts/,
Options/, Common/, Native/, Engine/, Templates/Jinja/, Templates/OllamaGo/,
Parsing/, Grammar/, Onnx/, Drivers/, Midi/, Tokenizers/) are FILE ORGANIZATION
ONLY, not namespaces: `using CodeBrix.Ollama.ModelRunner.Engine;` is a CS0246
error. Everything under Native/, Engine/, Onnx/, Drivers/, Midi/ and
Tokenizers/ is internal -- the P/Invoke surface, the worker thread, the decode
loop, the ONNX interpreter, its operators and the model drivers are reached
only through the public entry points and their interfaces. You will also want
the ordinary framework usings: System, System.Collections.Generic,
System.Threading and System.Threading.Tasks.

NAMING SHARP EDGE. The static entry point is called ModelRunner and so is the
last segment of the namespace. That is not a conflict in ordinary code:
`ModelRunner.LoadAsync(...)` in a file with `using
CodeBrix.Ollama.ModelRunner;` binds to the TYPE, because no namespace called
ModelRunner exists at global scope. It only becomes ambiguous inside a
namespace of your own that is itself under CodeBrix.Ollama -- rare, and the
fix is the fully qualified name.


THE 60-SECOND EXAMPLE
=====================
Load a model, ask it something, print the answer. This is the whole API for
most callers.

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelRunner;

    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/smollm-360m-instruct-q8_0.gguf",
            ContextSize = 4096,
        });

    ChatRequest request = new ChatRequest();
    request.Messages.Add(
        new ChatMessage(ChatRole.System, "Answer in one short sentence."));
    request.Messages.Add(
        new ChatMessage(ChatRole.User, "What colour is the sky?"));

    ChatResponse reply = await model.ChatToEndAsync(request);

    Console.WriteLine(reply.Message.Content);
    Console.WriteLine($"{reply.Statistics.TokensPerSecond:F1} tokens/s");

Three things happened that are worth knowing about. The chat template embedded
in the model file rendered the conversation, because that is what Auto
resolves to for a file that carries one. The reply was generated on a
dedicated thread owned by this model, and awaiting it never blocked yours. And
`await using` unloaded the model at the end of the scope: an IRunningModel
holds the weights, so let it go when you are done with it.


CORE API REFERENCE
==================
The reference below is organised by what you hand the library and what it
hands back:

    THE ModelRunner STATIC CLASS    load, probe, describe the engine, log
    LOADING A MODEL                 every ModelRunnerOptions property
    THE IRunningModel CONTRACT      every member, with an example
    GENERATION AND SAMPLING         GenerationOptions, SamplingOptions
    THE CONTRACT TYPES              messages, updates, results, statistics
    CHAT TEMPLATES                  the three dialects and how one is chosen
    THINKING AND TOOL CALLS         how the reply is taken apart
    STRUCTURED OUTPUT               JSON, schemas and GBNF grammars
    EMBEDDINGS                      one vector per input, and the limits
    LoRA ADAPTERS                   applying and replacing them
    QUANTIZING A GGUF MODEL         a smaller file, with nothing installed
    RUNNING ONNX MODELS             the other road: a managed interpreter,
                                    a MIDI driver and a text driver
    THREADS: HOW MANY ARE USED, AND HOW TO CONTROL IT
                                    both roads: the defaults, Threads,
                                    BatchThreads and MaxThreads
    THE CACHE, CANCELLATION AND THREADS   what is coordinated, what is not
    LOGGING                         SetLogHandler and what the engine says
    MEMORY AND SPEED                what to set for a model that barely fits
    THE QWEN 3.5 35B-A3B RECIPE     a 20 GiB model on a CPU, measured
    UTILITIES                       the public template and parser types
    THE ERROR MODEL                 every exception and when it is thrown
    THE NATIVE LIBRARY              names, paths and the failure message


THE ModelRunner STATIC CLASS
============================
Five members, and they are the only way into the engine.

    static Task<IRunningModel> LoadAsync(ModelRunnerOptions options,
                                         CancellationToken)
    static Task<ModelDetails>  ProbeAsync(string modelPath,
                                          CancellationToken)
    static Task<QuantizeResult> QuantizeAsync(string inputPath,
                                              string outputPath,
                                              GgufQuantizationType type,
                                              QuantizeOptions options,
                                              CancellationToken)
    static NativeRuntimeInfo   GetNativeRuntimeInfo()
    static void SetLogHandler(Action<ModelRunnerLogLevel, string> handler)

LoadAsync
---------
Loads a model and hands back the contract to query it through. The native
library is loaded and checked on the first call of the process, the weights
are read according to ModelRunnerOptions.LoadMode, an inference context is
created, the chat-template dialect is resolved, and any LoRA adapters named in
the options are applied. Disposing the result unloads everything.

ArgumentNullException for a null options object, ArgumentException for an
option out of range, ModelLoadException when there is no file at ModelPath or
the engine will not read it or cannot make a context for it,
NativeLibraryException when the native engine itself could not be loaded, and
OperationCanceledException when the token is cancelled -- the load really is
interruptible, through the engine's own progress callback, so a twenty-
gigabyte read can be abandoned part way.

ProbeAsync
----------
Reads what the engine knows about a model file WITHOUT loading its weights:
architecture, parameter count, the memory the weights would need, the training
context, the embedded chat template and every string-valued metadata key. It
is fast and cheap at any file size -- seconds on a twenty-gigabyte file -- and
it is the right thing to call before deciding what to pass to LoadAsync.

    ModelDetails details = await ModelRunner.ProbeAsync(path);
    Console.WriteLine($"{details.Architecture} {details.Description}");
    Console.WriteLine($"weights would need {details.WeightsSize:N0} bytes");
    Console.WriteLine($"trained context {details.TrainingContextLength}");

ArgumentException for a null or blank path, ModelLoadException when there is
no such file or the engine cannot read it as a model. The probe loads the
file's metadata with the engine's "simulate the allocations" flag, and falls
back to a vocabulary-only load for a file that will not open that way, so a
file with unusual tensors is still described.

QuantizeAsync
-------------
Reads a GGUF file and writes a smaller one at another quantization. It needs
nothing installed, changes nothing about the input file, and is described in
full under QUANTIZING A GGUF MODEL below.

GetNativeRuntimeInfo
--------------------
Loads the native engine if it is not loaded yet and reports on it. Useful in a
diagnostic command, and the fastest way to find out why a machine is slow.

    NativeRuntimeInfo info = ModelRunner.GetNativeRuntimeInfo();
    Console.WriteLine(info.LoadedPath);          // the .dylib/.so/.dll
    Console.WriteLine(info.RuntimeIdentifier);   // e.g. "osx-x64"
    Console.WriteLine(info.BuildInfo);           // upstream commit and date
    Console.WriteLine(info.SystemInfo);          // CPU features in use

    foreach (NativeDeviceInfo device in info.Devices)
    {
        Console.WriteLine($"{device.Type} {device.Name}: {device.Description}");
    }

SupportsMemoryMapping, SupportsMemoryLocking and SupportsGpuOffload are the
engine's own answers for this build on this platform. NativeLibraryException
when nothing could be loaded.

SetLogHandler
-------------
Routes the native engine's log to a handler of yours. See LOGGING below.


LOADING A MODEL (ModelRunnerOptions)
====================================
Only ModelPath is required. Every other property has a default that lets a
small model load and generate. A null on a nullable numeric property means
"leave it to the engine", which usually means "read it from the model file"
and is NOT the same as zero.

    string   ModelPath          = null    REQUIRED; the GGUF file
    IList<LoraAdapterOptions> LoraAdapters  empty; applied at load
    ModelLoadMode LoadMode      = MemoryMap
    int?     GpuLayers          = null    null -> the engine decides: every
                                          layer on an accelerator where there
                                          is one, none on a CPU-only build.
                                          0 keeps everything on the CPU
    uint?    ContextSize        = null    null -> the model's TRAINED context,
                                          which for a large model can be
                                          enormous; set it for such models
    uint     BatchSize          = 2048    logical batch, tokens per call
    uint     PhysicalBatchSize  = 512     physical batch; must be <= BatchSize
    int?     Threads            = null    generation; null -> the physical
                                          core count. See THREADS below
    int?     BatchThreads       = null    prompt processing; null -> whatever
                                          Threads resolved to
    int?     MaxThreads         = null    a ceiling on what the library picks
                                          BY ITSELF; null -> no ceiling
    FlashAttentionMode FlashAttention = Auto | Disabled | Enabled
    KvCacheType KeyCacheType    = Default   Default F32 F16 BF16 Q8_0 Q4_0
    KvCacheType ValueCacheType  = Default
    float?   RopeFrequencyBase  = null    null -> from the model
    float?   RopeFrequencyScale = null    null -> from the model
    bool     EmbeddingsMode     = false   true for an embedding-only model
    EmbeddingPooling EmbeddingPooling = Unspecified
    uint     MaxSequences       = 1
    uint     RecurrentStateSnapshots = 0
    bool     LoadMtpLayers      = false   reserved; costs memory, unused here
    bool     CheckTensors       = false   validate every tensor while loading
    bool     UseExtraBufferTypes = true   let the CPU backend repack weights
    bool     CollectTimings     = true
    ChatTemplateDialect ChatTemplateDialect = Auto
    string   OllamaTemplate     = null    a Modelfile TEMPLATE, as text
    string   JinjaTemplate      = null    replaces the file's own template
    IProgress<float> LoadProgress = null  0.0 to 1.0 while loading

VALIDATED BEFORE ANYTHING NATIVE IS TOUCHED: a blank ModelPath, a zero
BatchSize, PhysicalBatchSize or MaxSequences, a PhysicalBatchSize larger than
BatchSize, a ContextSize of 0, a Threads, BatchThreads or MaxThreads below 1,
and a LoraAdapterOptions with no path, are all ArgumentException. A ModelPath or an
adapter path that names no file is ModelLoadException. Nothing has been
allocated by the time either is thrown.

LoadMode, one value at a time
-----------------------------
    MemoryMap        the default. The operating system pages the weights in on
                     demand and can share them with another process. THE ONLY
                     MODE that lets a model close to the size of physical
                     memory load at all.
    Read             read the whole file into allocated memory.
    LockInMemory     read it and lock it in physical memory, so it can never
                     be swapped or compressed.
    MemoryMapAndLock map it and lock the mapping.
    DirectIo         read it with direct I/O where the platform offers it,
                     bypassing the page cache.

CONTEXT SIZE IS THE MEMORY DIAL. The key/value cache is proportional to it,
and for a large model it is the difference between loading and not. Leaving it
null asks for the model's trained context, which on a modern model can be
hundreds of thousands of tokens; for anything above a few billion parameters,
set it. KeyCacheType and ValueCacheType are the second dial: Q8_0 halves the
cache against the F16 default for a small loss of accuracy.

THREADS. Leave Threads, BatchThreads and MaxThreads null and this road uses one
thread per PHYSICAL core, which is right for most applications. Set Threads
when you know the machine; set MaxThreads when you do not and want a ceiling
anyway. THREADS: HOW MANY ARE USED, AND HOW TO CONTROL IT, below, is the whole
of it for both roads -- the rule, the precedence, the examples and how to
measure your own machine.

UseExtraBufferTypes lets the CPU backend repack the weights into its faster
layouts at load time. It is true by default and it costs a second copy of much
of the model in ordinary memory: on the 20.5 GiB Qwen recipe below that is an
extra 11.25 GiB. On a machine with the headroom it is free speed; on one
without, set it to false -- the measured head-to-head found the same tokens
per second either way and half the load time without it.

ChatTemplateDialect, OllamaTemplate and JinjaTemplate are covered under CHAT
TEMPLATES below.

LoadProgress is called from the engine's loading thread, repeatedly, with a
fraction from 0.0 to 1.0. Keep the handler short.


THE IRunningModel CONTRACT
==========================
One loaded model. Obtained from LoadAsync; disposing it unloads the model.
Application code and tests can implement the interface themselves to stand in
for a real model, and the library's own test suite does exactly that.

    ModelDetails        Details { get; }
    ModelRunnerOptions  Options { get; }
    ChatTemplateDialect ChatTemplateDialect { get; }

    Task<IReadOnlyList<int>> TokenizeAsync(string text,
            bool addSpecialTokens = true, bool parseSpecialTokens = true,
            CancellationToken)
    Task<string> DetokenizeAsync(IReadOnlyList<int> tokens,
            bool renderSpecialTokens = false, CancellationToken)

    IAsyncEnumerable<GenerationUpdate> GenerateAsync(string prompt,
            GenerationOptions options = null, CancellationToken)
    Task<GenerationResult> GenerateToEndAsync(string prompt,
            GenerationOptions options = null, CancellationToken)

    IAsyncEnumerable<ChatUpdate> ChatAsync(ChatRequest request,
            CancellationToken)
    Task<ChatResponse> ChatToEndAsync(ChatRequest request, CancellationToken)
    Task<string> RenderChatPromptAsync(ChatRequest request, CancellationToken)

    Task<EmbeddingResult> EmbedAsync(IReadOnlyList<string> inputs,
            CancellationToken)
    Task SetLoraAdaptersAsync(IReadOnlyList<LoraAdapterOptions> adapters,
            CancellationToken)
    Task ClearCacheAsync(CancellationToken)

    void Dispose()  /  ValueTask DisposeAsync()

Every CancellationToken parameter is last and defaulted. Every member throws
ObjectDisposedException after disposal, and ArgumentNullException for a null
required argument, before anything else happens.

Details, Options and ChatTemplateDialect
----------------------------------------
Details is the same ModelDetails a probe returns, for the file that was
loaded. Options is the object you passed to LoadAsync, unchanged -- the
library never writes to it. ChatTemplateDialect is the dialect that WILL
render chat requests, resolved at load, so it can be asked before the first
chat rather than after it.

    Console.WriteLine(model.Details.Architecture);       // "llama"
    Console.WriteLine(model.Details.TrainingContextLength);
    Console.WriteLine(model.ChatTemplateDialect);        // Jinja

TokenizeAsync and DetokenizeAsync
---------------------------------
Text to token ids and back. Neither touches the inference context, so both
stay answerable while a generation is running on the same model.

    IReadOnlyList<int> tokens =
        await model.TokenizeAsync("Hello, world!", false, true);
    string text = await model.DetokenizeAsync(tokens);
    // text == "Hello, world!"

addSpecialTokens (default true) lets the tokenizer add the vocabulary's
beginning-of-sequence token where it expects one -- pass false when you are
counting the tokens of a fragment. parseSpecialTokens (default true) makes
special-token TEXT in the input, such as "<|im_start|>", become that token
rather than ordinary characters. renderSpecialTokens (default false) decides
whether detokenizing writes such tokens back out as their text or drops them.

GenerateAsync and GenerateToEndAsync
------------------------------------
A completion of a RAW prompt. No chat template is applied and nothing is
prepended except the beginning-of-sequence token the vocabulary asks for.

    GenerationOptions options = new GenerationOptions
    {
        MaxTokens = 64,
        Sampling = new SamplingOptions { Temperature = 0f },
    };

    GenerationResult result =
        await model.GenerateToEndAsync("The capital of France is", options);

    Console.WriteLine(result.Text);                    // " Paris..."
    Console.WriteLine(result.FinishReason);            // Stop | Length | ...
    Console.WriteLine(result.Statistics.GeneratedTokens);

The streaming form yields a GenerationUpdate for every piece of text that
becomes unambiguous, and always one FINAL update carrying the finish reason
and the statistics. The final update's Text may be empty; its IsFinal is true.

    await foreach (GenerationUpdate update in
        model.GenerateAsync("Count from one to ten:", options))
    {
        if (!update.IsFinal) Console.Write(update.Text);
        else Console.WriteLine($"\n{update.FinishReason}");
    }

TEXT ARRIVES WHEN IT IS VALID, not when it is sampled. A token that is half a
UTF-8 sequence, or that might be the beginning of a stop sequence, is held
back until the next token settles it -- so an update can carry several tokens,
or none, and Tokens holds the ids decoded since the previous update.

ChatAsync, ChatToEndAsync and RenderChatPromptAsync
---------------------------------------------------
A conversation, rendered through the model's chat template. The request
carries the WHOLE conversation every time; see THE CACHE below for why that is
not as expensive as it sounds.

    ChatRequest request = new ChatRequest
    {
        Think = false,
        Options = new GenerationOptions { MaxTokens = 200 },
    };
    request.Messages.Add(new ChatMessage(ChatRole.System, "Be brief."));
    request.Messages.Add(new ChatMessage(ChatRole.User, "Name one colour."));

    await foreach (ChatUpdate update in model.ChatAsync(request))
    {
        Console.Write(update.ContentDelta);

        if (update.IsFinal)
        {
            foreach (ToolCall call in update.ToolCalls)
            {
                Console.WriteLine($"{call.Id} {call.Name}{call.ArgumentsJson}");
            }
        }
    }

ChatToEndAsync is the same stream collected into a ChatResponse whose Message
is an assistant ChatMessage with Content, Thinking and ToolCalls filled in.
Thinking is null when the model produced no reasoning.

RenderChatPromptAsync returns the prompt text the model WOULD see and
generates nothing. It is the fastest way to find out why a template is not
doing what you expected, and it costs nothing:

    string prompt = await model.RenderChatPromptAsync(request);
    Console.WriteLine(prompt);
    // <|im_start|>system\nBe brief.<|im_end|>\n<|im_start|>user\n...

EmbedAsync
----------
See EMBEDDINGS below.

SetLoraAdaptersAsync
--------------------
See LoRA ADAPTERS below.

ClearCacheAsync
---------------
Forgets the conversation held in the context's key/value memory, so the next
request evaluates its whole prompt again. For native GGUF reproducibility
comparisons, call it before EACH run being compared. See THE CACHE below.

Dispose and DisposeAsync
------------------------
Unloads the model, the inference context, the sampler, any embedding context
and every LoRA adapter, and ends the model's worker thread. Prefer
DisposeAsync (`await using`), because unloading twenty gigabytes is work and
the synchronous form blocks the calling thread while it happens.

DISPOSING WHILE A STREAM IS OPEN IS SAFE but it ends the stream: the disposal
raises the engine's abort flag, waits (up to thirty seconds) for the request
in flight to let go of its turn, and only then frees anything, so nothing is
released under a decode that is still running. The enumeration in progress
then fails with ObjectDisposedException rather than continuing to read a
context that has been freed. Disposing from INSIDE your own enumeration is
allowed too, and skips that wait, because a suspended enumeration has no
native call in flight. Disposal is idempotent, and it never throws because
unloading failed.


GENERATION AND SAMPLING
=======================
GenerationOptions controls ONE request and is passed to GenerateAsync,
GenerateToEndAsync, or through ChatRequest.Options.

    int?    MaxTokens       = null   null -> until the model stops or the
                                     context fills
    IList<string> StopSequences      empty; the stop string itself is NOT
                                     returned
    SamplingOptions Sampling = null  null -> SamplingOptions' own defaults
    string  Grammar         = null   GBNF text; wins over JsonSchema
    string  JsonSchema      = null   converted to a grammar; wins over JsonMode
    bool    JsonMode        = false  any JSON value at all

SamplingOptions -- THE DEFAULTS ARE OLLAMA'S, so a model behaves here the way
it behaves under Ollama:

    float Temperature     = 0.8f   0 or less is greedy: the most likely token
                                   every time (see reproducibility below)
    int   TopK            = 40     0 disables
    float TopP            = 0.9f   1.0 disables
    float MinP            = 0.0f   0 disables
    float TypicalP        = 1.0f   1.0 disables
    float RepeatPenalty   = 1.1f   1.0 disables
    int   RepeatLastN     = 64     0 disables; -1 means the whole context
    float PresencePenalty = 0.0f
    float FrequencyPenalty = 0.0f
    uint? Seed            = null   null -> a fresh seed per request

THE SAMPLER ORDER IS llama.cpp's, and it is not a detail: penalties first,
then the grammar, then top-k, typical-p, top-p and min-p, then temperature,
then the draw. A chain in any other order produces different text for the same
seed.

RANDOM OR FIXED IS AN EXPLICIT CHOICE:

    new SamplingOptions { Seed = null }   // fresh random seed per request
    new SamplingOptions { Seed = 1234 }   // fixed random stream

null remains the default. Numeric seeds are fixed seeds: 0 through 4294967294
are accepted. Assigning 0xFFFFFFFF (uint.MaxValue, also the result of an
unchecked conversion of -1 to uint) throws ArgumentOutOfRangeException. Use
null to ask for randomness; do not pass the native engine's sentinel.

REPRODUCIBILITY also requires the same model, settings, build and hardware.
For native GGUF, await ClearCacheAsync() BEFORE EACH RUN being compared and
give the model exclusive use throughout the comparison. Cached prompt
evaluation changes batch shapes and can change floating-point rounding.
Temperature 0 selects greedily and ignores the seed, but it is still affected
by inference rounding. ONNX text and MIDI generation keep no cache between
requests. A fixed seed does not promise identical output across these runners:
their random generators and inference implementations differ.

PENALTIES SEE ONLY WHAT THIS REQUEST GENERATED, not the prompt: RepeatLastN is
a window over the generated tokens. That is a deliberate deviation from
llama.cpp's command-line front end, which feeds it the prompt as well.

FINISH REASONS

    Stop          the model produced an end-of-generation token
    StopSequence  the output reached one of StopSequences
    Length        MaxTokens was reached
    ContextFull   the context window filled up
    ToolCall      the reply carried at least one complete tool call
    None          only on a non-final update


THE CONTRACT TYPES
==================

ChatRequest     IList<ChatMessage> Messages (get-only, add to it);
    IList<ToolDefinition> Tools (get-only); bool? Think; ResponseFormat
    ResponseFormat; GenerationOptions Options.

ChatMessage     ChatRole Role; string Content; string Thinking (the model's
    reasoning, separated out by this library; null when there was none);
    IList<ToolCall> ToolCalls; string ToolCallId and string ToolName (for a
    tool RESULT message). Constructors: () and (role, content).

ChatRole        System, User, Assistant, Tool.

ToolDefinition  string Name; string Description; string
    ParametersJsonSchema (an object schema as JSON text; may be null for a
    tool that takes no arguments).

ToolCall        string Id (assigned by this library, "call_1" upwards within a
    turn; echo it back in ChatMessage.ToolCallId); string Name; string
    ArgumentsJson (a JSON object as text, as the model wrote it).

ResponseFormat  static ResponseFormat Json; static
    FromJsonSchema(string jsonSchema); bool IsJson; string JsonSchema.

ChatUpdate      string ContentDelta and string ThinkingDelta, each "" when the
    update carries none; IReadOnlyList<ToolCall> ToolCalls (the FINAL update
    only); bool IsFinal; FinishReason FinishReason; GenerationStatistics
    Statistics (null until the final update).

ChatResponse    ChatMessage Message; FinishReason FinishReason;
    GenerationStatistics Statistics.

GenerationUpdate  string Text; int[] Tokens (the ids decoded since the
    previous update); bool IsFinal; FinishReason FinishReason;
    GenerationStatistics Statistics (null until the final update).

GenerationResult  string Text; FinishReason FinishReason;
    GenerationStatistics Statistics.

GenerationStatistics  int PromptTokens; int CachedPromptTokens (how many of
    them were already in the cache and were not evaluated); int
    GeneratedTokens; TimeSpan PromptDuration, GenerationDuration and
    TotalDuration (which for a chat request INCLUDES rendering the template
    and tokenizing); double TokensPerSecond, computed over GenerationDuration.

EmbeddingResult  IReadOnlyList<float[]> Embeddings, one per input in input
    order; int Dimensions; int PromptTokens across all inputs.

ModelDetails    string Path; long FileSize; string Description (the engine's
    one-line summary); string Architecture; string Name (may be null); ulong
    ParameterCount; ulong WeightsSize (bytes in memory once loaded); int
    TrainingContextLength, EmbeddingLength, LayerCount, HeadCount,
    VocabularySize; bool IsHybrid, IsRecurrent, HasEncoder, HasDecoder;
    string ChatTemplate (the Jinja template the file embeds, or null);
    IReadOnlyDictionary<string, string> Metadata -- every STRING-valued
    metadata key; array values such as the token list are deliberately absent.

NativeRuntimeInfo  string LoadedPath, RuntimeIdentifier, BuildInfo,
    SystemInfo; IReadOnlyList<NativeDeviceInfo> Devices; bool
    SupportsMemoryMapping, SupportsMemoryLocking, SupportsGpuOffload.

NativeDeviceInfo  string Name, Description, Type ("CPU", "GPU", "IGPU",
    "ACCEL", "META" or "UNKNOWN"); ulong TotalMemory, FreeMemory.

ModelRunnerLogLevel  Debug, Info, Warning, Error.


CHAT TEMPLATES
==============
A chat template turns a list of messages into the one string of text a model
was trained to answer. Getting it wrong is the most common reason a local
model produces nonsense, so this library carries THREE engines and resolves
between them once, at load.

    ChatTemplateDialect.Jinja    the Jinja template the model file embeds in
                                 its tokenizer.chat_template metadata,
                                 rendered by this library's own Jinja engine
    ChatTemplateDialect.Ollama   an Ollama Go text/template -- the text of a
                                 Modelfile TEMPLATE -- rendered by this
                                 library's port of Ollama's template engine
    ChatTemplateDialect.Native   the engine's own template matching, which
                                 recognizes a fixed set of well-known families
                                 and nothing else. A last resort
    ChatTemplateDialect.Auto     choose. The default

AUTO RESOLVES IN THIS ORDER, and the decision is made at load so that
IRunningModel.ChatTemplateDialect can be asked before the first chat:

    1. ModelRunnerOptions.OllamaTemplate is set          -> Ollama
    2. ModelRunnerOptions.JinjaTemplate is set, OR the
       model file embeds a chat template                 -> Jinja
    3. neither                                           -> Native

NAMING A DIALECT WITH NO TEMPLATE BEHIND IT FAILS THE LOAD, with a
ModelLoadException saying which option to set. That is deliberate: a chat
template problem discovered at load is cheap, and the same problem discovered
on the first request of a production run is not.

JinjaTemplate REPLACES the template embedded in the file; that is what it is
for. It is also how you fix a model whose own template this library cannot
parse, since an unparsable embedded template does not stop the model loading
-- only chat is impossible for it, and a raw GenerateAsync still works.

PASSING AN OLLAMA TEMPLATE FROM ModelManager. ResolvedModel.Template is the
Modelfile TEMPLATE text of a model pulled from the Ollama registry, and it is
exactly what OllamaTemplate wants:

    ResolvedModel resolved = await store.ResolveAsync("smollm:135m");

    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = resolved.ModelPath,
            OllamaTemplate = resolved.Template,   // may be null; then Jinja
        });

When the template you pass is one of Ollama's own twenty built-ins, byte for
byte, the stop strings that accompany it in Ollama's registry are added to
every chat request automatically. Anything else gets none, and you add your
own through GenerationOptions.StopSequences.

THE THINK SWITCH. ChatRequest.Think is a bool? and it means three things:

    null    say nothing; the template's own default stands
    true    ask the model to think before answering
    false   ask it not to

A Jinja template sees it as the `enable_thinking` variable, and ONLY when it
has a value -- so null really does leave the template's default alone. An
Ollama template sees it as `.Think`, with `.IsThinkSet` false when it is null.
A template with no such switch ignores it entirely.

TOOLS reach a Jinja template as the OpenAI-shaped `tools` list
({"type": "function", "function": {"name", "description", "parameters"}}) and
an Ollama template as `.Tools`. THE NATIVE DIALECT CANNOT RENDER TOOLS AT ALL
-- the engine's built-in template support takes a role and a body per message
and has nowhere to put a tool definition -- so a request that offers tools on
that dialect is refused with ChatTemplateException rather than quietly
rendered without them.

WHAT A JINJA CHAT TEMPLATE IS GIVEN, exactly: `messages` (a list of mappings
with "role" and "content", plus "reasoning_content", "tool_calls",
"tool_call_id" and "name" where the message has them), `tools` when any were
offered, `add_generation_prompt` (always true -- this library renders a prompt
for a reply, never a transcript), `bos_token` and `eos_token` from the
vocabulary, and `enable_thinking` when the request has an opinion.

THE BEGINNING-OF-SEQUENCE RULE. Some templates open with `{{ bos_token }}` and
some do not, and the vocabulary separately declares whether a sequence starts
with that token. Tokenizing a rendered prompt with "add special tokens" always
on gives the first kind TWO of them, which measurably changes what the model
says. This library's rule is THE TEXT WINS: when the rendered prompt already
starts with the token's own text, the tokenizer is told not to add another.
You do not have to do anything about this; it is here so you know it is
handled, and so you do not try to handle it yourself.


THINKING AND TOOL CALLS
=======================
The raw text a model generates for a chat request is taken apart in a fixed
order, and the order matters.

REASONING IS SEPARATED FIRST. A thinking model writes its whole plan between a
pair of tags -- <think> and </think> for most of them -- and that plan often
mentions tool-call literals. A tool parser reading the plan would call a tool
the model was only thinking about, so the reasoning is lifted out before
anything looks for a call. Reasoning arrives as ChatUpdate.ThinkingDelta while
it is streaming and as ChatMessage.Thinking at the end; it never appears in
ContentDelta or Content.

THE TAGS ARE INFERRED FROM THE TEMPLATE, not guessed from the output. A Jinja
template is searched for known literal pairs -- <think>/</think> (Qwen 3 and
3.5, DeepSeek-R1, Hermes and most others), <|START_THINKING|>/<|END_THINKING|>
(Command R), <|channel|>analysis<|message|>/<|end|> (the gpt-oss harmony
format), <seed:think>/</seed:think>, the Kimi markers,
<reasoning>/</reasoning> and <thought>/</thought> -- and an Ollama Go template
is walked structurally for the literals around its `.Thinking` reference, the
way Ollama does it. A model whose template has no such tags gets no reasoning
stage at all and everything it writes is content.

THINKING OFF IS NOT THE SAME AS NO REASONING STAGE. With Think false, a
template that HAS a thinking switch renders an EMPTY, already-closed reasoning
block into the prompt, so everything the model writes really is the answer and
no reasoning stage is needed. There are two exceptions and the library handles
both: a template that ignores the switch and leaves the block open -- which
the rendered prompt shows -- and a template that writes reasoning tags but has
no switch at all, which never saw the request's answer. In either case the
reasoning stage stays on, and reasoning still comes back separated rather than
mixed into the answer.

TOOL CALLS COME NEXT, and only when the request offered tools: a tool call is
only a tool call when the tool was on offer. Two conventions are handled, and
the template says which the model learnt:

  - JSON AFTER A LITERAL, which is what most models write. The literal is read
    out of the template -- <tool_call>, [TOOL_CALLS] [, a DeepSeek begin
    marker, <|python_tag|> and so on -- and what follows it is read as JSON
    objects, one call at a time, as each object completes. A template that
    writes bare JSON with no literal of its own is handled too, and then a
    call is only recognized when the JSON is the first non-whitespace thing in
    the output.
  - NESTED TAGS, which Qwen 3.5 writes:
    <tool_call><function=NAME><parameter=P>VALUE</parameter></function>
    </tool_call>. A template that teaches <function= and <parameter= literals
    selects this reader instead.

A JSON OBJECT ONLY BECOMES A CALL WHEN IT NAMES A TOOL THAT WAS OFFERED. A
model writing ABOUT a tool it was not given produces content, not a call, and
a block naming an unknown tool comes back as content so nothing is silently
lost. Duplicate tool names in one request are resolved by keeping the first.

TOOL CALLS ARRIVE ON THE FINAL UPDATE ONLY, complete, never in pieces, and the
finish reason is then FinishReason.ToolCall. Identifiers are assigned here:
neither convention carries one, and the contract asks you to echo one back, so
the calls of a turn are numbered "call_1" upwards in the order the model wrote
them.

THE ROUND TRIP, in full:

    ChatRequest request = new ChatRequest { Think = false };
    request.Messages.Add(
        new ChatMessage(ChatRole.User, "What is the weather in Paris?"));
    request.Tools.Add(new ToolDefinition
    {
        Name = "get_weather",
        Description = "Get the current weather for a city.",
        ParametersJsonSchema =
            "{\"type\":\"object\",\"properties\":{\"city\":{\"type\":"
            + "\"string\"}},\"required\":[\"city\"]}",
    });

    ChatResponse asked = await model.ChatToEndAsync(request);
    ToolCall call = asked.Message.ToolCalls[0];
    // call.Id "call_1", call.Name "get_weather",
    // call.ArgumentsJson {"city":"Paris"}

    string answer = LookUpTheWeather(call.ArgumentsJson);   // your code

    ChatMessage assistant =
        new ChatMessage(ChatRole.Assistant, asked.Message.Content);
    assistant.ToolCalls.Add(call);

    request.Messages.Add(assistant);
    request.Messages.Add(new ChatMessage(ChatRole.Tool, answer)
    {
        ToolCallId = call.Id,
        ToolName = call.Name,
    });

    ChatResponse used = await model.ChatToEndAsync(request);
    Console.WriteLine(used.Message.Content);   // "It is sunny and 21 C ..."


STRUCTURED OUTPUT
=================
Four ways to constrain what the model may produce, in the order they win when
more than one is set:

    1. GenerationOptions.Grammar      GBNF text, used as written
    2. GenerationOptions.JsonSchema   converted into a grammar
    3. GenerationOptions.JsonMode     any JSON value at all
    4. ChatRequest.ResponseFormat     the coarsest, so it comes last

A constraint is enforced BY THE SAMPLER, token by token, so the model
physically cannot produce anything the grammar rejects. There is no retry loop
and no validation step, and the output parses the first time.

    request.ResponseFormat = ResponseFormat.FromJsonSchema(
        "{\"type\":\"object\",\"properties\":{"
        + "\"city\":{\"type\":\"string\"},"
        + "\"degrees\":{\"type\":\"integer\"}},"
        + "\"required\":[\"city\",\"degrees\"]}");

    ChatResponse reply = await model.ChatToEndAsync(request);
    using JsonDocument document = JsonDocument.Parse(reply.Message.Content);

ResponseFormat.Json, and GenerationOptions.JsonMode, use llama.cpp's own
json.gbnf, which accepts any JSON object.

THE SCHEMA CONVERTER is a port of llama.cpp's, rule for rule, and is public in
its own right as JsonSchemaGrammar -- see UTILITIES. It covers $ref within the
document and the $defs/definitions it points into, oneOf, anyOf, allOf, enum,
const, strings with minLength, maxLength, pattern and the date, time, date-time
and uuid formats, numbers and integers with minimum, maximum and their
exclusive forms, booleans, nulls, arrays with items, prefixItems, minItems and
maxItems, and objects with properties, required and additionalProperties. A
REMOTE $ref -- one pointing at another document over http -- is refused rather
than fetched, with GrammarException, because converting a schema does no I/O.

A grammar that does not parse, or that defines no rule named `root`, is a
GrammarException raised when the request starts.

A NOTE ON GRAMMARS AND TOOLS. A grammar constrains the WHOLE output, tool-call
literals included, so a schema and a tool-calling turn do not mix: constrain
the reply, or offer tools, not both in one request.


EMBEDDINGS
==========
One vector per input, in input order.

    EmbeddingResult result = await model.EmbedAsync(
        new[] { "the first input", "a quite different second input" });

    Console.WriteLine(result.Dimensions);           // e.g. 960
    Console.WriteLine(result.Embeddings.Count);     // 2
    Console.WriteLine(result.PromptTokens);         // across both inputs

AN EMBEDDING-ONLY MODEL (a BERT-style one) must be loaded with
EmbeddingsMode = true; its context is created for embeddings and it cannot
generate. A GENERATIVE model embeds either way: when EmbeddingsMode is false,
the first call to EmbedAsync creates a SECOND, small context on the same
weights, so embedding never disturbs the conversation sitting in the
generation context's cache. That second context is kept until the model is
disposed and costs a cache of a couple of thousand tokens.

EmbeddingPooling says how the per-token vectors are reduced to one:
Unspecified (the default, meaning whatever the model file specifies, falling
back to Mean), None (the last token's vector, unpooled), Mean, Cls, Last, or
Rank for a reranking model. It is a LOAD-TIME option, on ModelRunnerOptions,
not a per-call one.

THERE IS A PER-INPUT TOKEN LIMIT, because an embedding is computed in ONE
physical batch: an encoder, and any model whose attention is not causal, has
to see the whole input at once. The limit is the smaller of the embedding
context's size and its physical batch -- for a model loaded with
EmbeddingsMode true that is the smaller of ContextSize and PhysicalBatchSize,
and for the second context created on demand for a generative model it is that
context's own size, at most 2048 tokens. An input over the limit is an
InferenceException that states the limit and how to raise it; split or
truncate the text yourself. An input that tokenizes to nothing at all is also
an InferenceException -- an embedding needs at least one token.

Every vector has exactly Dimensions elements: the model's declared EMBEDDING
OUTPUT width, which for a reranking model is not the same as its hidden size,
falling back to the hidden size when the file declares no output width. A
model with no embedding output at all raises InferenceException saying so.


LoRA ADAPTERS
=============
Adapters are GGUF files that modify the loaded weights. Name them at load
time, or replace the whole set later:

    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions { ModelPath = path });

    await model.SetLoraAdaptersAsync(new[]
    {
        new LoraAdapterOptions { Path = "/models/style.gguf", Scale = 0.8f },
    });

    // ... and to remove them all again:
    await model.SetLoraAdaptersAsync(Array.Empty<LoraAdapterOptions>());

SetLoraAdaptersAsync REPLACES the set; it does not add to it. An adapter is
loaded from disk on first use, keyed by its full path, and kept until the
model is disposed, so switching back and forth between two adapters loads each
file once. An adapter with no path is ArgumentException; a file the engine
will not read, or a set it will not apply, is ModelLoadException.

Adapters named in ModelRunnerOptions.LoraAdapters are applied during the load,
before LoadAsync returns, and a missing adapter file is caught in validation
before anything is allocated.


QUANTIZING A GGUF MODEL
=======================
A quantized model is the same model with its weights written at fewer bits per
value. It is smaller on disk, loads faster, needs less memory and generates
faster; it also generates slightly differently, because rounding the weights
changes the arithmetic. QuantizeAsync does that rewrite with the engine that
is already inside this package.

    QuantizeResult result = await ModelRunner.QuantizeAsync(
        "/models/mymodel-f16.gguf",
        "/models/mymodel-q4_k_m.gguf",
        GgufQuantizationType.Q4_K_M);

    Console.WriteLine($"{result.InputBytes:N0} -> {result.OutputBytes:N0}"
                      + $" in {result.Elapsed.TotalSeconds:F1} s");

NOTHING IS INSTALLED AND NOTHING IS FETCHED. The quantizer is the native
engine this package ships, the same one that runs a model, so there is no tool
to download, no Python, and no second copy of anything. With no options the
call writes, byte for byte, the file the engine's own command-line quantizer
writes for the same input and the same type.

WHICH TYPE
----------
GgufQuantizationType carries the engine's own file types, with the engine's
own numbers. The ones worth knowing:

    Q8_0     about 53% of an F16 file; all but indistinguishable from it
    Q6_K     about 41%; very close
    Q5_K_M   about 37%
    Q4_K_M   about 32%; the usual choice when a model has to be made smaller
    Q4_0     about 31%; older and simpler than Q4_K_M, and measurably worse
    F16 / BF16 / F32    not quantizations at all, but written through the
                        same call, which is how a BF16 file becomes an F16 one

The rest are there too: Q4_1, Q5_0, Q5_1, Q2_K, Q2_K_S, Q3_K_S/M/L, Q4_K_S,
Q5_K_S, TQ1_0, TQ2_0, MXFP4_MOE, Q1_0, Q2_0 and the IQ family (IQ1_S, IQ1_M,
IQ2_XXS, IQ2_XS, IQ2_S, IQ2_M, IQ3_XXS, IQ3_XS, IQ3_S, IQ3_M, IQ4_NL,
IQ4_XS). A value that is not a member of the enumeration is
ArgumentOutOfRangeException; the engine writes no others.

A "K" type is a mixture on purpose: the engine writes the tensors that matter
most at the next type up, which is the whole difference between Q4_K_S and
Q4_K_M. The IQ types are smaller still and are meant to be given an importance
matrix, which this API does not take -- without one the engine quantizes some
of their tensors with more bits, so an IQ file made here is larger and better
than one made with a matrix.

QuantizeOptions
---------------
All three default to what the engine itself defaults to, so a null options
object and a `new QuantizeOptions()` produce the same file.

    int  Threads          = 0      0 (or less) -> one per hardware thread
    bool AllowRequantize  = false  quantize weights that are already quantized
    bool Pure             = false  write every tensor at the one type, turning
                                   the k-quant mixture off

Threads changes how long the work takes and never what is written: each thread
quantizes whole rows of its own. AllowRequantize is what a source that is
already Q8_0 needs before it can be made Q4_K_M, and quantizing twice loses
far more than quantizing once -- go back to the F16 or BF16 file if there is
one. Pure makes the file smaller and the model measurably worse.

The engine has more switches than these (an importance matrix, per-tensor and
per-layer type overrides, metadata overrides, layer pruning, a dry run,
leaving the output tensor alone, writing the result as several shards). They
are deliberately not here: each needs a file format of its own or changes what
the output of one call IS, and a caller who needs them wants the engine's own
tool. Everything not settable stays at the engine's default.

QuantizeResult
--------------
    string InputPath / OutputPath   the two files, as full paths
    GgufQuantizationType Type       what was written
    long   InputBytes / OutputBytes the two sizes
    TimeSpan Elapsed                the native call alone

WHAT IT COSTS
-------------
It reads the whole model and writes a whole second one, so the output's
directory needs room for both and the work is bounded by the disk and the
CPU -- on a modern laptop, roughly a second per 400 MB of input at Q4_K_M and
less at Q8_0. There is no progress report and nothing is streamed; what the
engine has to say goes to the handler SetLogHandler installed, which is where
to watch a long quantization.

CANCELLATION IS HONOURED BEFORE THE ENGINE STARTS AND NOT AFTER. The native
quantizer offers no way to interrupt it, so a token cancelled while it runs is
noticed only when it returns: the call runs to the end and the file is
written. This is stated plainly rather than hidden behind a token that looks
like it works. A caller that must be able to stop should quantize in a process
of its own.

NO PARTIAL FILE IS EVER LEFT AT THE OUTPUT PATH. The engine is given a file of
its own beside the output, and that file is moved into place only when the
call has returned success; a failure removes it and leaves a file that was
already at the output path exactly as it was. The output's directory must
exist -- DirectoryNotFoundException when it does not -- because the move is a
rename within it.

WHAT IT REFUSES
---------------
ArgumentException for a path that is not set and for an output path that names
the input file, ArgumentOutOfRangeException for a type the engine does not
write, FileNotFoundException when there is no input file,
DirectoryNotFoundException for an output directory that is not there, and
ModelLoadException when the engine will not do the work -- the file is not a
GGUF model, its weights are already quantized and AllowRequantize is not set,
or it could not be written. The engine's own last words are on that
exception's message, as they are for a failed load.

STORING THE RESULT
------------------
This package writes a file and says nothing about where models live. The
separate CodeBrix.Ollama.ModelManager package has QuantizeGgufAsync, which
takes the quantizer as a DELEGATE and puts the result away as a model of its
own with its provenance recorded. The two never reference each other, so the
consumer writes the one line that joins them:

    await store.QuantizeGgufAsync("mymodel:gguf", new QuantizeGgufOptions
    {
        Type = "q4_k_m",
        Tool = "CodeBrix.Ollama.ModelRunner",
        Quantizer = (input, output, ct) => ModelRunner.QuantizeAsync(
            input, output, GgufQuantizationType.Q4_K_M, null, ct),
    });


RUNNING ONNX MODELS
===================
This package also runs ONNX graphs, on an interpreter written in managed code
INSIDE the package. Everything above this section is about GGUF files and the
native engine; everything in this section is a separate road through the same
package, and the two never meet.

NOTHING IS INSTALLED AND NOTHING IS FETCHED. There is no ONNX runtime to
install, no Python, no pip module, no publisher tooling and no native library
of the engine's own: the graph reader, the operators, the model drivers,
the tokenizer and the Standard MIDI File writer are all managed code in this
assembly. The package still declares NO NuGet dependencies. So a graph runs
wherever .NET 10 runs -- including a runtime identifier this package ships no
native for, because this road never loads one.

WHAT IT COSTS. The arithmetic is done on the CPU with the widest vector
instructions the processor offers, and there is no accelerator path: a graph
runs at processor speed and nothing offloads. Expect it to be in the same
order as a specialized CPU runtime on a decoder of a few hundred million
parameters, and expect a GGUF file of the same model through the native engine
to be faster -- that road is what this package is built around, and this one is
what runs when nothing may be installed. Memory is the graph's own weights plus
a small pool of working buffers: a four-bit graph really does hold four-bit
weights, because a packed weight is unpacked inside the multiply and never
expanded in memory. LOADING COSTS MORE THAN RUNNING DOES, briefly: while a
large full-precision graph is being read the peak is roughly twice what the
loaded graph then settles at, and for a small or four-bit graph it is a larger
multiple of a much smaller figure, so a machine sized for the steady figure
alone can fail on the load of the largest graph. Load the largest one first if
you load several.

THREADS. OnnxRunnerOptions.Threads defaults to a count the engine works out
from the processor -- the PERFORMANCE cores where the operating system says
which cores are which, and the physical cores where they are all alike -- and
OnnxRunnerOptions.MaxThreads is how an application that ships to machines it
has never seen puts a ceiling on that without naming a count. THREADS: HOW MANY
ARE USED, AND HOW TO CONTROL IT, below, is the whole of it for both roads: the
rule, why this road's default differs from the GGUF road's, the precedence,
the examples and how to measure your own machine.

    THE THREE ENTRY POINTS        which to use, and when
    OnnxModel                     the raw surface: tensors in, tensors out
    MidiGenerationModel           MIDI music, streamed as it is written
    OnnxCausalLmModel             text, through this package's own
                                  IRunningModel contract
    LOADING BY PATH               and the two-package example
    STREAMING MIDI TO A PLAYER    the consumer-side loop, in full
    WHAT IS SUPPORTED AND WHAT IS REFUSED
    HOW CLOSE THE NUMBERS ARE
    WHAT BELONGS TO YOU AND NOT TO THIS PACKAGE

WHICH ENTRY POINT TO USE
------------------------
    OnnxModel            You have a graph and you know what to feed it. It
                         runs the graph and hands back what the graph
                         computes -- no tokens, no sampling, no cache. Use it
                         for a model whose family this package has no driver
                         for, for a step of your own around a driver, or to
                         look at what a graph declares.
    MidiGenerationModel  The bundle is a two-graph MIDI model. Use it to
                         generate music: it drives both graphs, samples,
                         keeps the caches and hands you musical EVENTS as it
                         makes them.
    OnnxCausalLmModel    The bundle is a text decoder with a generation
                         configuration beside it. Use it to generate text:
                         it tokenizes, prefills, decodes, samples and stops,
                         through the same IRunningModel contract the rest of
                         this package uses.

Each driver decides what it can do from what the BUNDLE says about itself, and
refuses by name what it cannot. Nothing about any particular publisher's model
is built into the engine.

OnnxModel AND IOnnxModel -- THE RAW SURFACE
-------------------------------------------
    static Task<IOnnxModel> LoadAsync(string modelPath,
            OnnxRunnerOptions options = null, CancellationToken)
    static Task<IOnnxModel> LoadFromDirectoryAsync(string bundleDirectory,
            string modelFileName, OnnxRunnerOptions options = null,
            CancellationToken)
    static Task<IOnnxModel> LoadFromFilesAsync(
            IReadOnlyDictionary<string, string> files, string modelFileName,
            OnnxRunnerOptions options = null, CancellationToken)

Three named methods rather than three overloads, because the call site then
says which one it means. The first takes one .onnx file and finds any side
file it names beside it; the second takes a directory and the graph's name
inside it, which may name a sub-folder ("onnx/model_base.onnx"); the third
takes (logical file name -> path) pairs, which is what a content-addressed
store needs, since it keeps every file under a digest rather than under the
publisher's name for it.

    IOnnxModel : IDisposable, IAsyncDisposable
        OnnxModelMetadata  Metadata { get; }
        OnnxRunnerOptions  Options  { get; }   // defaults resolved
        IReadOnlyDictionary<string, OnnxTensor> Run(
                IReadOnlyDictionary<string, OnnxTensor> inputs)
        Task<IReadOnlyDictionary<string, OnnxTensor>> RunAsync(
                IReadOnlyDictionary<string, OnnxTensor> inputs,
                CancellationToken)

Run is SYNCHRONOUS because it is pure computation and touches no I/O; RunAsync
is the same work on a thread pool thread, for a caller who must not be blocked.
Its token is honoured BETWEEN nodes -- a run already inside one large matrix
multiply finishes that node first.

    await using IOnnxModel model = await OnnxModel.LoadAsync(
        "/models/example/model.onnx",
        new OnnxRunnerOptions { Threads = 8 });

    foreach (OnnxValueMetadata input in model.Metadata.Inputs)
        Console.WriteLine($"{input.Name} {input.ElementType} " +
                          string.Join(",", input.Shape));

    Dictionary<string, OnnxTensor> inputs = new Dictionary<string, OnnxTensor>
    {
        ["input_ids"] = OnnxTensor.FromInt64(new long[] { 1, 2, 3 }, 1, 3),
    };

    IReadOnlyDictionary<string, OnnxTensor> outputs = model.Run(inputs);
    float[] logits = outputs["logits"].Floats;

OnnxTensor -- AN ELEMENT TYPE, A SHAPE AND ONE ARRAY. Build one with
FromFloats, FromInt32, FromInt64 or FromBooleans, each taking the array and
then the shape as `params long[]`; read one back through ElementType, Shape,
Count and whichever of Floats, Int32s, Int64s and Booleans matches its type
(the other three are null). The elements are in row-major order and a rank of
zero is a scalar. A dimension of ZERO is ordinary and not an error: it is how a
cached decode step says it is adding no new positions, and every operator
handles it.

THE ARRAY IS NEVER COPIED, IN EITHER DIRECTION, and that is the point. A
tensor you build keeps YOUR array and the engine reads it where it lies; a
tensor a run hands back owns an array of exactly Count elements that the engine
will not write to again. So a decoder's key/value cache costs nothing to carry
forward:

    // The graph's `present` outputs become the next step's `past` inputs.
    // The same instances go back in; nothing is copied, whatever the size.
    next["past_key_values.0.key"] = outputs["present.0.key"];

The consequence is the usual one: do not write to an array you handed in while
a run is in flight, and do not modify a tensor another run still reads.

    OnnxElementType   Float = 1, UInt8 = 2, Int8 = 3, Int32 = 6, Int64 = 7,
                      Bool = 9     -- the ONNX specification's own numbers

UInt8 and Int8 are the QUANTIZED types and they behave differently from the
rest: they live only INSIDE a quantized graph -- a weight the loader folded, or
an activation between DynamicQuantizeLinear and MatMulInteger -- and an
OnnxTensor does not carry them. A graph that declares one as its OWN input or
output is refused at load, saying so.

    OnnxModelMetadata  string GraphName, ProducerName, ProducerVersion;
                       long IrVersion;
                       IReadOnlyList<OnnxOpsetImport> Opsets;
                       IReadOnlyList<OnnxValueMetadata> Inputs, Outputs;
                       IReadOnlyList<string> Operators
    OnnxValueMetadata  string Name; OnnxElementType ElementType;
                       IReadOnlyList<OnnxDimension> Shape
    OnnxDimension      long Length (-1 when the dimension is symbolic);
                       string Symbol; bool IsFixed
    OnnxOpsetImport    string Domain; long Version

    OnnxRunnerOptions  int? Threads = null    see THREADS: HOW MANY ARE USED
                       int? MaxThreads = null a ceiling on the engine's own
                                              choice; ignored once Threads is
                                              set. See the same section
                       OnnxKernelPath KernelPath = Automatic
                       bool ReuseBuffers = true

KernelPath is Automatic (the widest arithmetic the processor offers), Vector
(the portable vector path) or Scalar (one element at a time). Automatic is the
right answer everywhere; the other two exist so that the three can be compared
on one machine, and all three compute the same thing -- not in the same ORDER,
so a long sum of products can differ in its last bits between them, exactly as
it does between any two matrix libraries. ReuseBuffers lets a run hand a buffer
out again once every node that could read it has run, which keeps a decode
step's allocation flat instead of proportional to the size of the graph; turn
it off only to compare the two.

MidiGenerationModel AND IMidiGenerationModel -- MUSIC
------------------------------------------------------
    static Task<IMidiGenerationModel> LoadFromDirectoryAsync(
            string bundleDirectory, OnnxRunnerOptions options = null,
            CancellationToken)
    static Task<IMidiGenerationModel> LoadFromFilesAsync(
            IReadOnlyDictionary<string, string> files,
            OnnxRunnerOptions options = null, CancellationToken)

WHAT IT EXPECTS TO FIND is a bundle holding a config.json describing the model
and its tokenizer, and TWO graphs -- one that turns the events so far into
hidden states, and one that turns a hidden state into an event's tokens. The
driver reads the tokenizer's whole layout out of that config.json and checks it
against the port before a model is run; a bundle describing something else is
refused by name.

    IMidiGenerationModel : IDisposable, IAsyncDisposable
        MidiGenerationMetadata Metadata { get; }
        OnnxRunnerOptions      Options  { get; }
        IAsyncEnumerable<MidiEvent> GenerateAsync(
                MidiGenerationOptions options = null, CancellationToken)
        MidiScore ToScore(IEnumerable<MidiEvent> events)
        Task SaveAsync(string path, IEnumerable<MidiEvent> events,
                CancellationToken)

    MidiGenerationMetadata   string Architecture, TokenizerVersion;
                             int TicksPerQuarterNote, MaximumContextEvents,
                             VocabularySize, MaximumTokensPerEvent;
                             string BaseGraphFileName, TokenGraphFileName

TicksPerQuarterNote is the resolution of EVERYTHING the model produces -- 480
for the published model of this family -- and it is stated on the model rather
than on each event, because every event of one piece shares it.

MidiEvent -- ONE MUSICAL EVENT, READY TO PLAY
- - - - - - - - - - - - - - - - - - - - - - -
    MidiEventKind Kind    Note, ProgramChange, ControlChange, Tempo,
                          TimeSignature, KeySignature
    long Tick             ABSOLUTE ticks from the start of the piece
    long HorizonTicks     how far the piece is settled; see THE ORDER below
    int  Track            from nought; one per instrumental part
    int  Channel          0 to 15, or MidiEvent.NoChannel (-1)
    int  NoteNumber       Note: the pitch, 0 to 127, 60 is middle C
    int  Velocity         Note: how hard it is struck, 1 to 127
    long DurationTicks    Note: how long it sounds; always above nought
    int  Program          ProgramChange: the instrument, 0 to 127
    int  Controller       ControlChange: which controller, 0 to 127
    int  Value            ControlChange: what it is set to, 0 to 127
    double BeatsPerMinute Tempo: quarter notes per minute
    long MicrosecondsPerQuarterNote   Tempo: the same, in a file's own unit
    int  Numerator        TimeSignature: the 3 of 3/4
    int  Denominator      TimeSignature: the 4 of 3/4; a power of two
    int  SharpsOrFlats    KeySignature: -7 to 7, sharps above nought
    bool IsMinor          KeySignature: a minor key rather than a major one

PROPERTIES THAT DO NOT APPLY TO THE KIND ARE NOUGHT, and Channel is NoChannel
for the three kinds that belong to no channel. Switch on Kind and read the ones
that belong to it. The six static factories -- Note, ProgramChange,
ControlChange, Tempo, TempoFromMicroseconds, TimeSignature and KeySignature --
build one by hand and validate every range, throwing
ArgumentOutOfRangeException for anything outside it.

A NOTE CARRIES ITS OWN LENGTH. There is no note-off event to pair up, so
nothing can be left hanging and a player never has to match one event with
another.

THE CHANNEL CONVENTION. Channels are numbered 0 TO 15, which is how the MIDI
wire format numbers them and how the model emits them, and channel 9 is
percussion. MANY PLAYERS AND MUSIC LIBRARIES NUMBER CHANNELS 1 TO 16: for one
of those, pass Channel PLUS ONE. It is one line and it is the commonest thing
to get wrong.

THE ORDER EVENTS ARRIVE IN, and the guarantee you can build on. The model
places each event at a whole BEAT plus an offset inside that beat. The beat
only ever moves forward; the offset inside a beat is absolute and CAN step
backwards. So the absolute ticks are non-decreasing BEAT BY BEAT and NOT event
by event: two events on the same beat can arrive with the later one first. What
every event does carry is HorizonTicks -- NO EVENT YIELDED AFTER THIS ONE WILL
HAVE A Tick BELOW THIS NUMBER. A player streaming a piece that is still being
written can sound everything up to the horizon and hold the rest. HorizonTicks
is never above Tick and is usually below it.

MidiGenerationOptions -- WHAT TO GENERATE
- - - - - - - - - - - - - - - - - - - - -
    int  MaximumEvents  = 512    events, not seconds; it may stop sooner
    double Temperature  = 1.0    below one is more predictable
    double TopP         = 0.98   1 keeps every token
    int  TopK           = 20     ONE MAKES IT GREEDY (see reproducibility below)
    long?  Seed         = null   null -> fresh seed; fixed -> repeatable
                                 random stream (see reproducibility below)
    IReadOnlyList<int> Instruments = null   General MIDI program numbers
    int?  DrumKit       = null   the percussion channel's program number
    int?  BeatsPerMinute = null  1 to 383
    int?  TimeSignatureNumerator / TimeSignatureDenominator
    int?  KeySignatureSharpsOrFlats  -7 to 7
    bool  KeySignatureIsMinor = false
    MidiScore Prompt    = null   a piece to CONTINUE
    int  PromptEventLimit = 4096
    bool ReduceRepeatedChanges = true
    bool AllowControlChange    = true
    bool IncludePromptEvents   = true

THERE ARE TWO WAYS TO START IT OFF AND THEY ARE ALTERNATIVES: either DESCRIBE
the piece (instruments, drum kit, tempo, time and key signature) or hand it a
piece to CONTINUE through Prompt. Setting both is refused with
ArgumentException rather than quietly resolved.

THE SEED SELECTS A REPEATABLE RANDOM STREAM. The stream of random numbers is
this library's own and does not change between framework releases; inference
arithmetic can differ between processors and builds. Reproducing a piece also
requires the same model, settings, build and hardware. TopK = 1 draws no random
number at all, but still depends on inference arithmetic.

Set Seed = null explicitly for a fresh seed (also the default), or use a fixed
value such as Seed = 1234. Negative MIDI seeds and the reserved value
0xFFFFFFFF (4294967295) throw ArgumentOutOfRangeException when assigned. Other
nonnegative 64-bit values remain supported. Text generation uses uint? seeds;
the shared fixed-seed range is 0 through 4294967294.

IncludePromptEvents defaults to true so that an `await foreach` is the WHOLE
piece from its first event -- the instruments, tempo and signatures you asked
for are real musical events, and a player fed only what the model added would
get a piece with no tempo and no instruments.

NAMING INSTRUMENTS ALSO CONSTRAINS THE MODEL: it then may not choose
instruments of its own or write on a channel that was not asked for. Leaving
Instruments empty lets it choose the instrumentation itself, which it does
well. AllowControlChange = false makes a plainer piece and generates it faster,
because those events are not spent.

A NOTE ON GREEDY GENERATION. TopK = 1 sounds like the safe setting and is not
the one to use for music: taking the most likely answer every time makes a
model of this family repeat itself, and a greedy piece can spend hundreds of
events setting itself up before it starts. Greedy is for reproducing a result
exactly; the defaults are for making music.

SAVING A STANDARD MIDI FILE
- - - - - - - - - - - - - -
    MidiScore score = model.ToScore(events);       // or SaveAsync directly
    await model.SaveAsync("/tmp/piece.mid", events);

    byte[] bytes = MidiFile.Write(score);
    MidiScore again = MidiFile.Read(bytes);
    await MidiFile.WriteAsync("/tmp/piece.mid", score);
    MidiScore fromDisk = await MidiFile.ReadAsync("/tmp/piece.mid");

MidiScore carries TicksPerQuarterNote, Events (sorted by position), TrackCount,
LengthInTicks and DurationInSeconds(), which follows the piece's own tempo
changes from beginning to end. MidiFile.Write has an overload taking a
runningStatus flag, which decides whether a repeated channel message may leave
its status byte out; every reader understands the shorthand, so leave it alone
unless you want every byte spelled out.

ToScore IS NOT THE SAME AS THE EVENT STREAM, and the difference is one
streaming cannot avoid: the model's own tokenizer shortens a note that the next
note of the same pitch cuts off, and that needs the future. ToScore does it; a
player sounding the stream holds such a note a little longer than the saved
file does. It is inaudible on most material and it is stated so that a
byte-for-byte comparison of the two does not surprise you.

CONTINUING A PIECE
- - - - - - - - - -
    MidiScore existing = await MidiFile.ReadAsync("/music/opening.mid");

    MidiGenerationOptions options = new MidiGenerationOptions
    {
        Prompt = existing,
        MaximumEvents = 512,
    };

    await foreach (MidiEvent e in model.GenerateAsync(options))
    {
        // the prompt's own events first, then what the model adds
    }

The prompt is read through the model's own tokenizer, so what the model sees is
what it was trained to see. A file this library did not write is read too --
anything it has no event for (pitch bends, aftertouch, lyrics, track names,
system-exclusive data) is stepped over and dropped.

OnnxCausalLmModel AND IOnnxCausalLmModel -- TEXT
-------------------------------------------------
    static Task<IOnnxCausalLmModel> LoadFromDirectoryAsync(
            string bundleDirectory, OnnxRunnerOptions options = null,
            CancellationToken)
    static Task<IOnnxCausalLmModel> LoadFromFilesAsync(
            IReadOnlyDictionary<string, string> files,
            OnnxRunnerOptions options = null, CancellationToken)

WHAT IT EXPECTS TO FIND is a bundle holding a generation configuration
(genai_config.json), the graph that configuration names, any side file holding
the weights beside it, and a GPT-2 style byte-level byte-pair tokenizer --
vocab.json and merges.txt, plus tokenizer_config.json and
special_tokens_map.json where the publisher wrote them. EVERYTHING the driver
does is read out of that configuration: the graph's file name, every tensor
name, the cache-name patterns, the layer, head and key-value-head counts, the
head size, the context length and the token numbers that begin and end a
sequence.

WHAT COMES BACK IS AN IRunningModel, the same contract the GGUF road hands
back, so an application can hold either in one variable and use the same
GenerationOptions, SamplingOptions, GenerationUpdate, GenerationResult,
FinishReason and GenerationStatistics types.

    IOnnxCausalLmModel : IRunningModel
        CausalLmMetadata  Metadata      { get; }
        OnnxRunnerOptions RunnerOptions { get; }

    CausalLmMetadata   string Architecture, ModelFileName, TokenizerKind;
                       int LayerCount, HeadCount, KeyValueHeadCount, HeadSize,
                       HiddenSize (-1 when the bundle states none),
                       ContextLength, VocabularySize,
                       BeginningOfSequenceTokenId (-1 when none);
                       IReadOnlyList<int> EndOfSequenceTokenIds;
                       int PaddingTokenId (-1 when none), MergeCount

WHAT OF THE CONTRACT IS IMPLEMENTED:

    TokenizeAsync / DetokenizeAsync   through the bundle's own tokenizer,
                                      honouring addSpecialTokens (what the
                                      bundle says about a beginning token) and
                                      parseSpecialTokens
    GenerateAsync / GenerateToEndAsync  with the existing GenerationOptions
                                      and SamplingOptions
    ClearCacheAsync                   completes at once -- see below
    Details / Options                 filled in from the bundle. Options
                                      carries the three settings the shared
                                      contract has a place for (the graph's
                                      path, the thread count in use, the
                                      context length); RunnerOptions is what
                                      the graph was really loaded with
    ChatTemplateDialect               Auto, because the enumeration has no
                                      "none" and chat is refused anyway

WHAT IS REFUSED, each with a NotSupportedException whose message NAMES THE
MEMBER and says why:

    ChatAsync, ChatToEndAsync, RenderChatPromptAsync
        a bundle carries no chat template and this driver applies none, so a
        conversation is YOURS to render into a prompt and pass to
        GenerateAsync
    EmbedAsync
        a decoder graph answers with one score per token of the vocabulary and
        hands back no hidden state to pool into a vector
    SetLoraAdaptersAsync
        an adapter is applied to a checkpoint's weights as they load, and a
        graph's weights are already baked into it
    GenerationOptions.Grammar, .JsonSchema and .JsonMode
        constraining output needs a grammar engine, which this driver does not
        carry. The three are refused when the request is validated, so an
        options object carrying one fails immediately rather than generating
        something unconstrained

EVERY SAMPLING SETTING IS IMPLEMENTED -- Temperature (nought or below is
greedy), TopK, TopP, MinP, TypicalP, RepeatPenalty with RepeatLastN,
PresencePenalty, FrequencyPenalty and Seed -- and the stages run in the same
order the native road runs them, so one SamplingOptions means one thing on both
roads. Only GREEDY output is claimed to match another engine's.

EVERY REQUEST EVALUATES ITS WHOLE PROMPT. There is no prefix cache between
requests on this road: the key/value cache belongs to one generation and is let
go when it ends. A growing conversation therefore re-reads its history every
turn, and ClearCacheAsync has nothing to clear and completes at once. That is
the one place where this road costs more than the GGUF road on the same
conversation.

    await using IOnnxCausalLmModel model =
        await OnnxCausalLmModel.LoadFromDirectoryAsync("/models/example-onnx");

    Console.WriteLine(model.Metadata.TokenizerKind);   // GPT-2 byte-level BPE
    Console.WriteLine(model.Metadata.ContextLength);

    GenerationOptions options = new GenerationOptions
    {
        MaxTokens = 128,
        Sampling = new SamplingOptions { Temperature = 0f },
    };

    await foreach (GenerationUpdate update in
        model.GenerateAsync("the prompt", options))
    {
        if (!update.IsFinal) Console.Write(update.Text);
    }

AN EMPTY PROMPT is not an error: a prompt that tokenizes to nothing starts from
the token the bundle says a sequence begins with, and is refused only when the
bundle names none.

FOUR THINGS END A GENERATION -- an end-of-sequence token, a stop sequence, the
token limit, and the context filling up -- and cancellation is honoured between
steps. The end-of-sequence token is not part of what the generation returns,
which is what the GGUF road does too.

TOKENIZERS. A GPT-2 style byte-level byte-pair tokenizer is what this driver
reads, and it is written out in managed code here: the byte table, the
pre-tokenizer's rule and the merges, with added tokens matched as text before
anything else. A bundle carrying a tokenizer of another kind -- a SentencePiece
model with no byte-level pair beside it, a single tokenizer.json, or none at
all -- is refused by the name of what was found. So is an added token asking
for a matching rule this library does not implement.

LOADING BY PATH, AND THE TWO PACKAGES TOGETHER
-----------------------------------------------
EVERY LOAD ON THIS ROAD TAKES PATHS, and working those paths out is YOUR
business. This package knows nothing about model stores and a model store knows
nothing about this package: that is deliberate, and it is the same seam the
GGUF road has.

The separate CodeBrix.Ollama.ModelManager package is what obtains a bundle and
resolves it to files on disk. ITS ResolveAsync HANDS BACK A NAME AND A PATH FOR
EACH FILE, and the pair form of the load methods here is what takes them -- no
copies are made and nothing is laid out first, even though the store keeps
every file under a digest rather than under the publisher's name for it:

    using CodeBrix.Ollama.ModelManager;
    using CodeBrix.Ollama.ModelRunner;

    using ModelStore store = new ModelStore();

    // ModelManager's side: a bundle it holds, resolved to files.
    ResolvedModel resolved =
        await store.ResolveAsync("example/midi-model:onnx");

    Dictionary<string, string> files = new Dictionary<string, string>();
    foreach (ResolvedFile file in resolved.Files)
        files[file.Name] = file.BlobPath;

    // ModelRunner's side: the paths, and nothing else.
    await using IMidiGenerationModel model =
        await MidiGenerationModel.LoadFromFilesAsync(files);

    await foreach (MidiEvent e in model.GenerateAsync()) { /* ... */ }

THE ONLY PLACE THE TWO PACKAGES MEET IS THE LINES ABOVE, in your own code.
Neither package references the other -- not as a NuGet dependency, not in a
signature -- and that is a rule rather than an accident. A dictionary of
strings is the whole of the seam.

If the bundle's files are already laid out as a directory, whether by the
store's own MaterializeAsync or because you downloaded them yourself, use
LoadFromDirectoryAsync instead and pass the directory.

THE TWO PACKAGES ARE VERSIONED AND RELEASED TOGETHER, ALWAYS. They are packed
in the same minute and published at the same version, and AN APPLICATION THAT
USES BOTH MUST INSTALL THEM AT THE SAME VERSION. They carry one shared assembly
inside each package rather than depending on a third package, so a mixture
BUILDS WITHOUT COMPLAINT and then fails at run time -- and when it does, the
failure is an InvalidOperationException at the first call that reaches the
shared code, naming both packages and telling you to install one version of
each. If you see that message, that is what it means.

STREAMING MIDI TO A PLAYER
--------------------------
EVENTS ARRIVE AS THEY ARE GENERATED. GenerateAsync hands over each event the
moment its last token is chosen, so a streaming player can be fed from an
`await foreach` and start sounding the beginning of a piece whose end does not
exist yet.

A MODEL MAY WELL WRITE MUSIC MORE SLOWLY THAN IT IS PLAYED, and that is
expected rather than a problem to design around: the CONSUMER buffers enough of
the song before it starts playing. It is never a reason not to stream the
events as they are made -- buffering a few seconds of music while the generator
works ahead is what makes a piece start in a second rather than a minute, and
the buffer you need depends on your own machine and settings, so measure it.

Below is the whole of the consumer-side loop, written against a GENERIC
streaming player -- one that takes note-with-duration, tempo and channel
events at absolute ticks, which is what most of them take. Nothing in this
package knows about any player; this is your code, and it is short on purpose:

    // `player` is your own streaming MIDI player. This example assumes the
    // common shape: it is told the resolution once, then fed events at
    // ABSOLUTE ticks, and it numbers channels 1 to 16 (hence the + 1).
    player.Start(model.Metadata.TicksPerQuarterNote);

    long buffered = 0;
    const long BufferTicks = 480 * 8;      // eight quarter notes of music

    await foreach (MidiEvent e in
        model.GenerateAsync(options, cancellationToken))
    {
        switch (e.Kind)
        {
            case MidiEventKind.Note:
                player.AppendNote(e.Tick, e.Channel + 1, e.NoteNumber,
                                  e.Velocity, e.DurationTicks);
                break;
            case MidiEventKind.ProgramChange:
                player.AppendProgram(e.Tick, e.Channel + 1, e.Program);
                break;
            case MidiEventKind.ControlChange:
                player.AppendControl(e.Tick, e.Channel + 1, e.Controller,
                                     e.Value);
                break;
            case MidiEventKind.Tempo:
                player.AppendTempo(e.Tick, e.BeatsPerMinute);
                break;
        }

        // Only what is below the horizon is settled, so that is what may be
        // played. Start once enough of it has accumulated.
        player.SettledThrough(e.HorizonTicks);

        if (buffered == 0 && e.HorizonTicks >= BufferTicks)
        {
            buffered = e.HorizonTicks;
            player.Play();
        }
    }

    player.Complete();

THREE THINGS IN THAT LOOP ARE THE WHOLE STORY: `e.Channel + 1` for a player
that numbers channels from one; `e.HorizonTicks` rather than `e.Tick` as the
point the player may sound up to, because ticks are non-decreasing beat by beat
and not event by event; and starting playback once enough has accumulated
rather than on the first event. Time signatures and key signatures are notation
rather than sound and most players ignore them; pass them on if yours does not.

CANCELLING STOPS THE GENERATION between token steps. Everything already handed
over stays handed over, the model is immediately usable again, and ToScore over
what you collected still gives a playable, saveable piece.

WHAT IS SUPPORTED AND WHAT IS REFUSED
-------------------------------------
EVERY REFUSAL IS AT LOAD, AND EVERY REFUSAL NAMES WHAT IT REFUSED. A graph that
loads will run; there is no class of graph that loads and then fails halfway
through a decode because of something the engine could have seen at the start.
The exception is ModelLoadException and the message names the node, the
operator and the domain where those apply, and lists what the engine does
implement where that helps.

    OPERATORS      41 of them: the standard set a decoder of this kind uses,
                   plus the contributed operators the model builders and
                   quantizers emit -- MatMulNBits, GroupQueryAttention,
                   SkipSimplifiedLayerNormalization,
                   SimplifiedLayerNormalization, DynamicQuantizeLinear and
                   MatMulInteger. An operator the engine does not implement is
                   refused by name, with its domain, at the node that uses it.
    OPERATOR SETS  ai.onnx 13 to 23. An older set is REFUSED rather than run:
                   several operators changed meaning at 13 without changing
                   shape, so running an opset-12 graph under the newer meaning
                   would give a wrong answer silently. A newer set is refused
                   until its semantics have been checked.
    DOMAINS        a graph may DECLARE a contributed domain it never uses; the
                   refusal is at the node, not at the declaration.
    ELEMENT TYPES  float, int64, int32 and bool at the graph's own edge. A
                   16-BIT FLOAT WEIGHT IS WIDENED as the model is read,
                   because that is a storage format rather than a compute
                   format; a 16-bit float input or output is refused, because
                   the graph would then be computing something else. The two
                   8-bit quantized types are refused at the edge for the
                   reason given above. Anything else is refused by name.
    ATTRIBUTES     an attribute a kernel does not implement is REFUSED, not
                   ignored: an attribute quietly dropped changes what the
                   graph computes, and the difference then shows up as a wrong
                   number rather than as an error.
    QUANTIZED
    WEIGHTS        stay PACKED in memory for the life of the model. A block is
                   turned back into floats inside the loop that multiplies it,
                   so a four-bit graph holds four-bit weights and a load does
                   not quietly expand them.
    BUNDLE SHAPES  the text driver refuses, by the name the configuration gives
                   it, an encoder, an encoder-decoder, a vision, speech,
                   audio or embedding block, and a decoder made of a pipeline
                   of several graphs. The MIDI driver refuses a bundle whose
                   config.json describes something other than a MIDI model of
                   the family it drives, and one missing either graph.

AT RUN TIME the two things that can go wrong are InferenceException -- shapes
that do not agree, an index outside its tensor, a graph output nothing produced,
or a second run started while one is in flight -- and the ordinary
ArgumentException for an input that is missing, is not one the graph declares,
or carries the wrong element type. ObjectDisposedException after disposal.

HOW CLOSE THE NUMBERS ARE
-------------------------
Three things are worth knowing before you compare this engine's output with
another runtime's, and none of them needs a number to be useful.

A FULL-PRECISION GRAPH AGREES ESSENTIALLY EXACTLY. The difference is the last
bits of a floating-point sum, which is what any two matrix libraries differ by,
and the token or event chosen is the same one.

A GRAPH WITH QUANTIZED WEIGHTS AGREES CLOSELY TOO -- the weights are the same
bytes in both runtimes and both turn them back into floats the same way. Weight
quantization is the kind that is faithful across runtimes.

A GRAPH THAT QUANTIZES ITS ACTIVATIONS LEGITIMATELY DIFFERS BETWEEN RUNTIMES,
by rather more, and that is a property of the graph and not a defect in either
engine. Such a graph takes a whole tensor's scale from that tensor's own
largest and smallest element, so a last-bit difference anywhere moves the scale,
which moves every value sitting halfway between two integers by a whole count,
and a dozen layers multiply it up. If you need output that matches another
runtime closely, quantize WEIGHTS and not activations.

A GRAPH THAT ASKS FOR 8-BIT ACTIVATIONS BY REQUEST is the one case where this
engine deliberately differs: some four-bit exports carry an attribute asking a
runtime to quantize the activations to 8 bits as a speed trade. This engine
READS THAT ATTRIBUTE AND KEEPS FLOATS, which is the MORE exact of the two
answers, so its output can differ from a runtime that takes the offer -- in the
direction of being right. There is nothing to configure; it is stated so that a
difference against another runtime on such a graph is not mistaken for a
defect.

WHAT COMES OUT OF A REDUCED MODEL IS NOT WHAT CAME OUT OF THE ONE IT WAS MADE
FROM. Making a model smaller is a speed and memory choice, not a transparent
one: the same prompt and the same seed can write different music or different
text from a few events or tokens in. Measure the reduction you intend to ship
on the inputs you care about.

WHAT BELONGS TO YOU AND NOT TO THIS PACKAGE
--------------------------------------------
A MODEL'S PROMPT CONVENTIONS ARE THE APPLICATION'S. Some text models are
trained on text reshaped in a particular way -- a marker written in place of
every newline is the usual example -- and the driver applies none of it and
undoes none of it: it tokenizes what you give it and detokenizes what the model
wrote. Apply the convention before you call GenerateAsync and undo it on the
way out. The driver deliberately knows nothing about any publisher's, because
the moment it knew one it would be wrong for the next.

TURNING GENERATED NOTATION TEXT INTO MUSIC IS ANOTHER LIBRARY'S JOB. A text
model that writes a notation format writes TEXT, and this package hands you
that text; converting it into MIDI, rendering it or playing it belongs
elsewhere, and this package neither does it nor names a library that does.

CHOOSING A MODEL, AND OBTAINING ONE, BELONGS TO YOUR CODE. This package takes
paths to files that already exist. Downloading a bundle, keeping it, and
resolving a name to files on disk are the separate
CodeBrix.Ollama.ModelManager package's work.

THREAD SAFETY ON THIS ROAD
--------------------------
  - ONE RUN AT A TIME PER LOADED MODEL, and a second call that arrives while
    one is in flight is REFUSED with InferenceException rather than left to
    wait. That is different from the GGUF road, where concurrent requests are
    serialized behind a gate. Load a second model to run two at once; the
    weights are not shared between instances.
  - THE SAME GOES FOR A DRIVER: a second GenerateAsync started while one
    enumeration is still live is refused, on both drivers.
  - SEVERAL LOADED MODELS IN PARALLEL IS FINE. Each owns its own weights,
    buffers and caches and they share nothing. Memory is the only limit.
  - A RUN IS STATELESS. Two runs with the same inputs give the same outputs;
    everything a decoder carries between steps is in the tensors YOU pass
    back in.
  - THE ENGINE'S OWN THREADS are inside one Run: the matrix kernels spread
    their work over OnnxRunnerOptions.Threads and gather it again before Run
    returns. Nothing of the engine outlives a call.
  - DISPOSE WHAT YOU LOAD. Every loaded model holds its weights until it is
    disposed. `await using` is one word.

ERRORS ON THIS ROAD
-------------------
    ModelLoadException      everything refused at load: no such file; not an
                            ONNX graph; an operator, an operator set, an
                            element type or an attribute the engine does not
                            implement; a graph whose nodes are out of
                            topological order or that reads something nothing
                            produces; a side file that is missing or that a
                            weight points outside of; a bundle that is not the
                            shape its driver drives; a missing graph or
                            tokenizer
    InferenceException      a run that could not proceed, and a second run
                            started on a model that is already running
    NotSupportedException   a member of IRunningModel, or a constraint setting,
                            that a bundle cannot honour -- see the list under
                            OnnxCausalLmModel above. It is a FRAMEWORK
                            exception and not one of this library's, so
                            catching ModelRunnerException does not catch it
    ArgumentException /     a path or a name that is not set; an input missing,
    ArgumentNullException / undeclared or of the wrong type; an option outside
    ArgumentOutOfRange      its stated range; a MIDI value outside 0 to 127; a
                            generation given both a prompt and a description
    ObjectDisposedException a model used after it was disposed
    InvalidOperationException  the mismatched-package guard described under THE
                            TWO PACKAGES TOGETHER

ModelLoadException and InferenceException are this library's own and derive
from ModelRunnerException, so the catch-all under THE ERROR MODEL below catches
them. NotSupportedException does not, and that is deliberate: a member a bundle
cannot honour is a programming error to fix, not a condition to handle.

RUNNING ONNX MODELS -- QUICK REFERENCE
--------------------------------------
INSTALLED   nothing. Managed code inside this package; no ONNX runtime, no
            Python, no native library, no NuGet dependency; runs wherever
            .NET 10 runs
RAW         await using IOnnxModel m = await OnnxModel.LoadAsync(path, opts);
            IReadOnlyDictionary<string, OnnxTensor> out = m.Run(inputs);
MIDI        await using IMidiGenerationModel m =
                await MidiGenerationModel.LoadFromDirectoryAsync(dir, opts);
            await foreach (MidiEvent e in m.GenerateAsync(options)) { }
            await m.SaveAsync(path, events);
TEXT        await using IOnnxCausalLmModel m =
                await OnnxCausalLmModel.LoadFromDirectoryAsync(dir, opts);
            await foreach (GenerationUpdate u in m.GenerateAsync(prompt, o)) { }
OPTIONS     OnnxRunnerOptions { Threads (null -> performance cores),
            MaxThreads (null -> no ceiling; bounds only the automatic count),
            KernelPath (Automatic), ReuseBuffers (true) }
TENSORS     OnnxTensor.FromFloats / FromInt32 / FromInt64 / FromBooleans,
            each (array, params long[] shape). Arrays are never copied, so a
            `present` output goes back in as the next `past` input as is
CHANNELS    MidiEvent.Channel is 0 to 15 (9 is percussion). ADD ONE for a
            player that numbers them 1 to 16
ORDER       ticks are non-decreasing BEAT BY BEAT, not event by event. Play up
            to HorizonTicks and hold the rest
TICKS       absolute, from the start of the piece;
            Metadata.TicksPerQuarterNote is the resolution
REFUSED     text: ChatAsync, ChatToEndAsync, RenderChatPromptAsync,
            EmbedAsync, SetLoraAdaptersAsync, and GenerationOptions.Grammar /
            .JsonSchema / .JsonMode -- all NotSupportedException by name
GRAPHS      opsets ai.onnx 13 to 23; 41 operators; float, int64, int32 and
            bool at the edge; everything else refused at LOAD, by name
PAIRING     ModelManager resolves a name to paths, you hand the paths here.
            INSTALL BOTH PACKAGES AT THE SAME VERSION

COMMON PITFALLS ON THIS ROAD
----------------------------
 1. DO NOT pass MidiEvent.Channel straight to a player that numbers channels
    1 to 16. Add one. Percussion is channel 9 here and channel 10 there, and
    the symptom is a drum kit playing melodies.

 2. DO NOT sound an event as soon as it arrives. Ticks are non-decreasing beat
    by beat and not event by event, so an event slightly EARLIER than the last
    one can still arrive. Play up to HorizonTicks.

 3. DO NOT refuse to stream because the model is slower than real time. That
    is expected; buffer enough of the piece before you start playing it. A
    generator that keeps ahead of a player after a few seconds' head start is
    the normal case.

 4. DO NOT set TopK = 1 for music because it sounds safe. Greedy makes a model
    of this family repeat itself. Use it to reproduce a result exactly, and
    the defaults to make music.

 5. DO NOT expect ToScore's notes to be exactly the ones you were handed. A
    note that a later note of the same pitch cuts short is trimmed when the
    piece is gathered, which needs the future and so cannot happen in the
    stream.

 6. DO NOT copy a `present` output before feeding it back as `past`. The
    tensor already owns its array and nothing will write to it again;
    copying a key/value cache is the most expensive thing you can do here for
    no benefit at all.

 7. DO NOT write to an array you handed in while a run is in flight. Tensors
    do not copy, in either direction.

 8. DO NOT call Run on a model that is already running. It is refused with
    InferenceException, not queued. Load a second model.

 9. DO NOT expect chat, embeddings, LoRA adapters or grammar-constrained
    output from an ONNX bundle. Each is refused with a NotSupportedException
    naming it; render a conversation into a prompt yourself and call
    GenerateAsync.

10. DO NOT expect a conversation on this road to reuse the last turn's work.
    Every request evaluates its whole prompt, and ClearCacheAsync has nothing
    to clear.

11. DO NOT install the two CodeBrix.Ollama packages at different versions. It
    builds cleanly and then fails at run time with a message saying exactly
    this.

12. DO NOT compare this engine's output with another runtime's on a graph that
    quantizes its ACTIVATIONS and read the difference as a defect. Compare on
    a full-precision or weight-quantized graph, where the two agree closely.

13. DO NOT apply a model's prompt conventions in the driver's name. If a model
    wants its newlines written as a marker, your code writes them and your
    code puts them back; the driver does neither.

14. DO NOT hand a GGUF file to OnnxModel or an .onnx file to ModelRunner
    .LoadAsync. They are two roads through one package and neither reads the
    other's format; each says so.


THREADS: HOW MANY ARE USED, AND HOW TO CONTROL IT
=================================================
This section is about HOW MANY THREADS THE ARITHMETIC RUNS ON, on both roads
through this package, and how to decide that number. It is not about which of
YOUR threads may call what -- that is THE THREADING MODEL, under THE CACHE,
CANCELLATION AND THREADS below. Everything here is a LOAD-TIME setting: it is
fixed when the model is loaded and does not change while it runs.

If you read nothing else: SAY NOTHING AND THE LIBRARY PICKS A GOOD NUMBER. Set
Threads when you know the machine. Set MaxThreads when you do not.

THE TWO ENGINES AND THEIR OPTIONS
---------------------------------
Each road has its own options object, and a name means the same thing on both.

    GGUF ROAD -- ModelRunner.LoadAsync(ModelRunnerOptions)

        int? Threads       threads for GENERATION: one token at a time, the
                           whole model read for each one
        int? BatchThreads  threads for PROMPT PROCESSING: the prompt's tokens
                           evaluated together, before the first token comes
                           out. null follows Threads
        int? MaxThreads    a ceiling on what the library picks BY ITSELF.
                           null (the default) means no ceiling

    ONNX ROAD -- OnnxModel.LoadAsync, .LoadFromDirectoryAsync and
                 .LoadFromFilesAsync; MidiGenerationModel
                 .LoadFromDirectoryAsync and .LoadFromFilesAsync;
                 OnnxCausalLmModel.LoadFromDirectoryAsync and
                 .LoadFromFilesAsync -- ALL OF THEM take the same
                 OnnxRunnerOptions

        int? Threads       threads the matrix kernels spread their work over.
                           This road has ONE count: a prompt and a generated
                           token are the same graph run with more or fewer
                           rows
        int? MaxThreads    the same ceiling, with the same meaning

EVERY ONNX ENTRY POINT TAKES THE SAME OPTIONS OBJECT, and the two drivers hand
it on to every graph they load -- a MIDI bundle is two graphs and both get it.
There is no thread setting anywhere else: GenerationOptions, SamplingOptions
and MidiGenerationOptions have nothing to say about threads, and
QuantizeOptions.Threads is a different thing altogether -- it belongs to
rewriting a file at a smaller type, not to running a model.

WHAT "UNSET" RESOLVES TO
------------------------
Leave Threads null and the library asks the operating system about the
processor. THE RULE is:

    the PERFORMANCE cores      where the processor has two kinds of core --
                               fast ones and efficient ones -- and the
                               operating system says which is which
    the PHYSICAL cores         where the cores are all alike (hyperthreads are
                               not cores and are not counted)
    the LOGICAL processors     where neither can be found out

THE TWO ROADS DO NOT APPLY THE SAME PART OF THAT RULE, and the difference is
deliberate:

    ModelRunnerOptions.Threads (GGUF)       the PHYSICAL core count
    ModelRunnerOptions.BatchThreads (GGUF)  whatever Threads resolved to
    OnnxRunnerOptions.Threads (ONNX)        the PERFORMANCE core count where
                                            the processor states one, and the
                                            physical count where it does not

On a processor whose cores are all alike the two roads agree: one thread per
physical core. On a processor that mixes fast and efficient cores they differ
-- the GGUF road uses every physical core, the ONNX road uses the fast ones
only. That is not an oversight. Each engine was measured on such a processor
and they behave differently, which is what the next part is about.

WHY THE NUMBERS ARE WHAT THEY ARE
---------------------------------
Two things decide what a thread is worth.

THE SLOWEST PIECE SETS THE PACE. Where a matrix multiply is cut into equal
pieces, one per thread, it is not finished until its last piece is. Put a
thread on a core that runs at two-thirds the speed of the others and every fast
thread waits for it, so the whole multiply takes longer than it would have with
fewer threads and none of them slow. That is what the managed ONNX engine does,
and it is exactly why its default counts only the fast cores. The native engine
behind the GGUF road divides its work differently, and measuring it on the same
kind of processor found the opposite: it is FASTER using every physical core
than using the fast ones alone. So its default counts them all.

MEMORY BANDWIDTH IS THE OTHER CEILING. Generating one token reads the whole
model once, so generation is limited by how fast memory can be read rather than
by arithmetic -- especially on a small model. Past a handful of threads the
memory is already saturated and another thread buys very little; the curve
flattens rather than climbing. This is why a big thread count is not a big
speed-up, and why the difference between one good count and another is a few
per cent.

AND HYPERTHREADS ARE NOT CORES. Two hardware threads on one core share its
arithmetic units and its cache, so one thread per LOGICAL processor is asking
half of them to fight the other half for the same memory. On BOTH roads that is
measurably worse than one thread per physical core -- not slightly worse but a
large fraction of the speed, and erratic from one run to the next. The library
never does this by itself. You can, by setting Threads to the logical count. Do
not.

THE THREE WAYS TO CONTROL IT
----------------------------
 1. SAY NOTHING -- leave Threads, BatchThreads and MaxThreads all null. This is
    right for most applications. The library reads the processor and picks the
    count above, on whatever machine your application lands on.

 2. SET Threads -- you know the machine and want an exact number. It is used
    EXACTLY as you wrote it: never clamped, never lowered, never checked
    against the machine. Threads = 16 on a four-core machine really does start
    sixteen threads, and they really do fight each other. Use this when the
    process shares the machine with something else, when you are pinning a
    benchmark, or when you have measured the target and know better than the
    default.

 3. SET MaxThreads -- you ship to machines you have never seen and want to
    bound what the library does on the biggest of them. It says "use what this
    machine has, but never more than this". A count cannot say that: Threads =
    8 oversubscribes a four-core machine and wastes a sixty-four-core one.

WHAT EACH ONE GIVES, on three machines. "Hybrid laptop" means a processor with
eight fast cores and eight efficient ones, sixteen physical in all.

    WHAT YOU SET               4-CORE      HYBRID LAPTOP    64-CORE
                               MACHINE     (8 fast, 8 slow) SERVER
    nothing, GGUF road            4              16            64
    nothing, ONNX road            4               8            64
    Threads = 8                   8               8             8
    MaxThreads = 8, GGUF road     4               8             8
    MaxThreads = 8, ONNX road     4               8             8
    Threads = 8 and MaxThreads=2  8               8             8

The last row is the precedence rule in one line: the cap did nothing, because
Threads was set.

THE PRECEDENCE RULE, EXACTLY
----------------------------
  - AN EXPLICIT Threads ALWAYS WINS. It is used as it stands and MaxThreads is
    ignored for it, even when it is larger than MaxThreads and larger than the
    machine.
  - ON THE GGUF ROAD AN EXPLICIT BatchThreads ALWAYS WINS the same way, for
    prompt processing. With BatchThreads null, prompt processing uses whatever
    Threads resolved to -- so a cap reaches prompt processing only through the
    generation count it follows, and never over an explicit BatchThreads.
  - MaxThreads BOUNDS ONLY THE AUTOMATIC CHOICE: the resolved count is the
    SMALLER of what the processor suggests and MaxThreads. A MaxThreads larger
    than what the machine has changes nothing at all.
  - MaxThreads = 1 IS LEGAL and means one thread.
  - AN INVALID VALUE IS REFUSED BEFORE ANYTHING IS LOADED, and the message
    names the property:

        GGUF road    a Threads, BatchThreads or MaxThreads below 1 is
                     ArgumentException. For the cap the message is
                     "ModelRunnerOptions.MaxThreads must be at least 1; leave
                     it null for no cap."
        ONNX road    a Threads or MaxThreads below 1 is
                     ArgumentOutOfRangeException. For the cap the message
                     names it: "OnnxRunnerOptions.MaxThreads must be at least
                     one; leave it null for no cap."

    Nothing has been allocated and no model file has been opened by the time
    either is thrown.

AFTER A LOAD, THE ONNX ROAD TELLS YOU WHAT IT CHOSE. IOnnxModel.Options
.Threads, IMidiGenerationModel.Options.Threads and IOnnxCausalLmModel
.RunnerOptions.Threads are the RESOLVED count: a real number, never null, with
the cap already applied. The GGUF road's IRunningModel.Options is the options
object you handed in, so Threads there is still null if you left it null.

WHAT THIS LIBRARY DELIBERATELY DOES NOT DO
------------------------------------------
  - IT NEVER LOWERS THE COUNT BECAUSE A MODEL IS SMALL. There is no adaptive
    rule anywhere: a 100M model and a 30B model on the same machine get the
    same number of threads unless you say otherwise. If you know your models
    are small and you want fewer threads, MaxThreads is the setting for it.
  - IT NEVER RAISES A COUNT YOU GAVE IT, and never clamps one down.
  - IT DOES NOT TELL YOU THE DETECTED CORE COUNTS. No public member answers
    "how many performance cores has this machine?", so "the smaller of the
    fast-core count and 8" is not something your code can work out. MaxThreads
    is how you say it: it is the way to use this library's own detection.
  - IT DOES NOT CHANGE THE COUNT AFTER A LOAD. To run the same model at a
    different count, load it again.
  - A THREAD COUNT NEVER CHANGES WHAT IS PRODUCED. The same prompt with the
    same sampling settings gives the same tokens at 4 threads and at 16; only
    the time differs. Sampling above temperature 0 varies between runs at any
    thread count, for reasons that have nothing to do with threads.

HOW TO CHOOSE A NUMBER
----------------------
MEASURE ON THE MACHINE THAT MATTERS. It takes a minute and it answers the
question for that machine better than any rule can. This program loads one
model once per thread count and prints tokens a second, for generation and for
prompt processing:

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelRunner;

    string path = "/models/my-model-q4_k_m.gguf";
    string prompt = "Write one paragraph about the sea.";

    GenerationOptions options = new GenerationOptions
    {
        MaxTokens = 128,
        Sampling = new SamplingOptions { Temperature = 0f },  // reproducible
    };

    foreach (int threads in new[] { 4, 8, 12, 16 })
    {
        await using IRunningModel model = await ModelRunner.LoadAsync(
            new ModelRunnerOptions
            {
                ModelPath = path,
                ContextSize = 2048,
                Threads = threads,
                BatchThreads = threads,
            });

        await model.GenerateToEndAsync(prompt, options);   // warm-up, ignored
        await model.ClearCacheAsync();

        GenerationResult result =
            await model.GenerateToEndAsync(prompt, options);

        GenerationStatistics statistics = result.Statistics;
        double generated = statistics.TokensPerSecond;
        double prompted = statistics.PromptTokens
            / statistics.PromptDuration.TotalSeconds;

        Console.WriteLine(
            $"{threads,3} threads: {generated,8:F1} tokens/s generated, " +
            $"{prompted,9:F1} tokens/s prompt");
    }

The same shape works on the ONNX road: load with OnnxCausalLmModel
.LoadFromDirectoryAsync(dir, new OnnxRunnerOptions { Threads = threads }) and
read the same GenerationStatistics off the final update of GenerateAsync.

Run each count a few times and take the MEDIAN rather than the best: one run on
a busy machine tells you about the machine's other work. Ignore differences
under about five per cent.

THE RULES OF THUMB, from the measurements the defaults above were chosen on:

  - NEVER MORE THREADS THAN THE MACHINE HAS PHYSICAL CORES. This is the one
    that actually costs: past that point both engines lose a large fraction of
    their speed and become erratic run to run. It is also the easy mistake to
    make, because Environment.ProcessorCount reports the LOGICAL count.
  - ON THE GGUF ROAD, MORE CORES KEPT PAYING almost to the physical count, on
    every model measured -- a few hundred million to two billion parameters,
    full precision and four-bit alike. The last quarter of the cores is worth
    only a few per cent either way, so anything from about three-quarters of
    the physical count up to it is a good answer, and the default is the
    physical count.
  - ON THE ONNX ROAD the curve flattens after a handful of threads, and on a
    processor that mixes fast and efficient cores, threads beyond the fast
    cores do not pay. The default is therefore the performance-core count.
  - PROMPT PROCESSING AND GENERATION DO NOT PEAK AT THE SAME COUNT. A prompt
    does real arithmetic on many tokens at once and usually keeps scaling a
    little longer than generation does -- but not on every model: one of the
    models measured processed prompts a third FASTER on half its cores. That
    is what BatchThreads is for, and it is worth one measurement if your
    prompts are long.
  - HALVING THE COUNT COSTS LESS THAN YOU WOULD EXPECT. On every model
    measured, half the cores gave roughly nine-tenths of the speed of all of
    them. Leaving cores for the rest of your application is cheap; taking more
    threads than there are cores is not.

EXAMPLES, ONE PER CASE, ON BOTH ROADS
-------------------------------------
SAY NOTHING -- the default, and the right answer for most applications.

    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelRunner;

    await using IRunningModel gguf = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/my-model-q4_k_m.gguf",
            ContextSize = 4096,
        });

    await using IOnnxCausalLmModel onnx =
        await OnnxCausalLmModel.LoadFromDirectoryAsync("/models/my-bundle");

AN EXACT COUNT -- you know the machine, and you are leaving it room for the
rest of your application.

    await using IRunningModel gguf = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/my-model-q4_k_m.gguf",
            ContextSize = 4096,
            Threads = 6,        // generation
            BatchThreads = 8,   // prompt processing, measured separately
        });

    await using IMidiGenerationModel midi =
        await MidiGenerationModel.LoadFromDirectoryAsync(
            "/models/midi-bundle", new OnnxRunnerOptions { Threads = 6 });

A CEILING -- you ship to machines you have never seen.

    await using IRunningModel gguf = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/my-model-q4_k_m.gguf",
            ContextSize = 4096,
            MaxThreads = 8,     // all four on a 4-core box, 8 on a big server
        });

    await using IOnnxModel graph = await OnnxModel.LoadAsync(
        "/models/my-graph.onnx", new OnnxRunnerOptions { MaxThreads = 8 });

    // What it settled on, on THIS machine. Never null after a load.
    System.Console.WriteLine(graph.Options.Threads);

ONE THREAD -- a background job that must not disturb anything else.

    await using IRunningModel quiet = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/my-model-q4_k_m.gguf",
            ContextSize = 2048,
            MaxThreads = 1,     // or Threads = 1; both give one thread here
        });

PITFALLS ABOUT THREADS
----------------------
 1. DO NOT SET Threads TO Environment.ProcessorCount. That is the LOGICAL
    count, which on a hyperthreaded processor is twice the number of cores.
    Both engines are measurably slower there, and erratic. If you want "as many
    as this machine can use", set nothing at all.

 2. DO NOT EXPECT MaxThreads TO DO ANYTHING ONCE Threads IS SET. It bounds the
    library's own choice and nothing else. Setting both and then wondering why
    the ceiling was ignored is the commonest mistake with it; if you want a
    ceiling, leave Threads null.

 3. DO NOT ASSUME 16 THREADS BEATS 8 -- OR THAT 8 BEATS 16. Which is faster
    depends on the road, the model and the processor, and between two sensible
    counts the difference is usually a few per cent. Measure, or leave it
    alone.

 4. DO NOT CHANGE THE OPTIONS OBJECT AFTER THE LOAD AND EXPECT ANYTHING TO
    HAPPEN. The count is fixed when the model is loaded. Load again to change
    it.

 5. DO NOT READ IRunningModel.Options.Threads AS "THE COUNT IN USE". On the
    GGUF road that property is the object you handed in, so it is null if you
    left it null. The ONNX road's Options.Threads IS the resolved count.

 6. DO NOT REACH FOR THREADS TO GET REPRODUCIBILITY, OR BLAME THEM FOR THE LACK
    OF IT. The thread count does not change which tokens come out. Temperature
    does; set it to 0 for anything you will test.

 7. DO NOT CONFUSE QuantizeOptions.Threads WITH THESE. That one belongs to
    rewriting a file at a smaller type and has a default of its own; see
    QUANTIZING A GGUF MODEL.


THE CACHE, CANCELLATION AND THREADS
===================================

THE PREFIX CACHE
----------------
Every request carries its whole prompt, so a conversation of twenty turns
sends the same history again each turn. The inference context still holds that
history in its key/value memory from the previous turn, so the library
compares the new prompt with what is there and evaluates only the part that
differs. GenerationStatistics.CachedPromptTokens says how much was skipped.

    first.Statistics.CachedPromptTokens    // 0 on a fresh context
    second.Statistics.CachedPromptTokens   // most of the prompt, on turn two

ONE TOKEN IS ALWAYS RE-EVALUATED, even for an identical prompt: the sampler
needs the logits of the position it is about to sample from, and those exist
only for a position that was part of the last decode.

THE CACHE IS ONE CONVERSATION DEEP. It is a prefix, not a store: changing an
early message invalidates everything after it, and interleaving two different
conversations on one model instance means each turn re-evaluates from the
point where they diverge. For several concurrent conversations, load several
models, or accept the re-evaluation.

ClearCacheAsync throws the whole thing away, and the next request evaluates
its entire prompt. Hybrid and recurrent models keep a rolled-up state rather
than a per-token cache and cannot drop a suffix; when one of those cannot be
truncated the library clears it and starts the sequence again, which is
correct but is a full re-evaluation.

FOR REPRODUCIBILITY COMPARISONS, await ClearCacheAsync() before EACH native
GGUF run. Reusing a prefix changes the batches used to evaluate a prompt, and
floating-point rounding can change the output despite identical prompts and
seeds. This can also affect greedy sampling. Keep the model, settings, build
and hardware fixed, and do not let another request use the model between the
clear and the compared run. Clearing the cache does not guarantee matching
output across hardware or runners. Managed ONNX text and MIDI generation
start fresh for each request, so no cache reset is needed there.

CANCELLATION
------------
Every member takes a CancellationToken and honours it promptly, INCLUDING
during a long decode: a cancelled token raises the engine's own abort flag, so
evaluating the prompt of a large model -- one native call that can run for
minutes -- stops rather than running to completion. The enumeration then ends
with OperationCanceledException.

    using CancellationTokenSource source = new CancellationTokenSource();

    await foreach (GenerationUpdate update in
        model.GenerateAsync(prompt, options, source.Token))
    {
        Console.Write(update.Text);
        if (Console.KeyAvailable) source.Cancel();
    }

CANCELLING ENDS A REQUEST; IT DOES NOT BREAK THE MODEL. The context's memory
is cleaned up, the next request works normally, and the only cost is that the
cache no longer describes the aborted prompt. Cancelling a LOAD is the same:
LoadAsync throws OperationCanceledException and nothing is left behind.

THE THREADING MODEL
-------------------
  - ONE MODEL OWNS ONE THREAD. Every native call for a model is made on that
    model's own worker thread, because an inference context is not re-entrant.
    Awaiting a member never blocks the calling thread, and nothing needs a
    synchronization context.
  - ONE ACTIVE GENERATION PER MODEL. A second generation is refused with
    InferenceException when its enumeration starts, before chat rendering or
    tokenization. There is no built-in generation queue. The active stream
    holds its turn until the enumeration completes or is disposed, including
    after it yields its final update. Merely creating an enumerable does not
    reserve the model. If an application needs a queue, it should await its
    own semaphore around each complete generation.
  - CACHE, EMBEDDING AND ADAPTER OPERATIONS STILL WAIT for the native model's
    context gate when called from another call chain. A generation started
    while one of those operations holds the gate is also refused.
  - THREE MEMBERS DO NOT WAIT: TokenizeAsync, DetokenizeAsync and
    RenderChatPromptAsync read the vocabulary and the template, not the
    context, so they stay answerable while a generation is running.
  - INSIDE THE SAME MODEL'S await foreach, another generation gets the same
    InferenceException as any concurrent generation. Cache, embedding and
    adapter calls instead throw InvalidOperationException to prevent waiting
    on their own enumeration. Finish or dispose the enumeration first.
  - SEVERAL MODELS IN PARALLEL IS FINE. Each has its own thread, context and
    cache, and they share only the native library and the log handler. Memory
    is the only limit.
  - THE LOG HANDLER IS PROCESS-WIDE, and may be called on any engine thread.
  - MANAGED ONNX AND NATIVE GGUF GENERATION SHARE THE SAME BUSY RULE: a second
    generation is refused with InferenceException. Raw ONNX RunAsync also
    refuses concurrent runs on one instance.
    THREAD SAFETY ON THIS ROAD, under RUNNING ONNX MODELS, has the whole of it.


LOGGING
=======
The engine has a great deal to say, especially while loading, and it says it
nowhere else: the buffer sizes it settled on, the CPU features it is using,
the reason a context could not be created. Route it somewhere:

    ModelRunner.SetLogHandler((level, line) =>
    {
        if (level >= ModelRunnerLogLevel.Warning) Console.Error.Write(line);
    });

    // ... and to stop:
    ModelRunner.SetLogHandler(null);

null discards everything, which is the DEFAULT: a library that writes to the
console uninvited is a nuisance, so nothing is printed unless you ask.

THE HANDLER IS CALLED ON THE ENGINE'S THREADS, one line at a time, WHILE a
load or a decode is in progress. It must return quickly and must be safe to
call from several threads at once; a handler that throws is ignored rather
than allowed to fail the load. Lines arrive with their own trailing newline.

There is ONE handler for the whole process, not one per model. The library
keeps its own tap on the log underneath yours, so the engine's last words can
be attached to the exception when a load or a decode fails -- which is why a
ModelLoadException message often ends with "The engine's log for this
operation:" and a dozen lines of engine output. The lines that tap keeps are
per OPERATION, so with several models loaded at once one model's decode cannot
put its output into another model's exception. Setting or clearing your
handler never disturbs any of this, and your handler still sees every line.


MEMORY AND SPEED
================
PROBE BEFORE YOU LOAD. ModelRunner.ProbeAsync reads a model file's shape in
seconds at any size, and WeightsSize is what the weights will occupy:

    ModelDetails details = await ModelRunner.ProbeAsync(path);
    if (details.WeightsSize > availableBytes) { /* choose a smaller file */ }

WHAT A MODEL COSTS, in three parts:

  1. THE WEIGHTS. A Q4_K_M quantization is roughly 0.6 bytes per parameter:
     about 4.5 GB for a 7B model, 20.5 GiB for the 35B-A3B model in the recipe
     below. With LoadMode.MemoryMap these are pages of a mapped file, so the
     operating system can reclaim them under pressure; with Read they are
     ordinary allocations and cannot be.
  2. THE KEY/VALUE CACHE, proportional to ContextSize. This is the dial that
     turns "will not load" into "loads": a model asked for its full trained
     context can need more cache than weights. Quantizing it with
     KeyCacheType and ValueCacheType Q8_0 halves it again.
  3. THE REPACKED WEIGHTS, when UseExtraBufferTypes is true (the default): the
     CPU backend's faster layouts, which can approach the size of the model
     again. Set it to false on a machine without that headroom.

THE SETTINGS THAT MATTER, in order of effect:

    LoadMode        keep it at MemoryMap. It is the only mode that lets a
                    model close to the size of physical memory load at all
    ContextSize     set it explicitly for anything above a few billion
                    parameters; 4096 is a sensible starting point
    KeyCacheType /
    ValueCacheType  Q8_0 when the cache is the problem
    UseExtraBufferTypes  false when memory is tight; measured: same tokens per
                    second, half the load time, and one large buffer saved
    Threads         defaults to the PHYSICAL core count, which is usually
                    right; lower it when the process shares the machine, or
                    set MaxThreads when you do not know the machine. THREADS:
                    HOW MANY ARE USED, AND HOW TO CONTROL IT has the whole of it
    GpuLayers       null lets the engine offload everything where there is an
                    accelerator. 0 pins everything to the CPU, which is what
                    a CPU-only build does anyway

SPEED, honestly: on a processor with no accelerator behind it, a 360M model
runs at about 70 tokens a second and a 35B mixture-of-experts model at about
9. Prompt evaluation is the other half of the story and is often the larger
one -- a 288-token prompt on that 35B model takes nine to fifteen seconds --
which is exactly what the prefix cache exists to avoid paying twice.


THE QWEN 3.5 35B-A3B RECIPE
===========================
The model this library was designed for, on a machine that has no business
running it: an Intel Mac mini, six cores, 32 GiB of memory, no accelerator.
Every number here was measured on that machine with the library's own live
test, and the options are the ones that test uses.

    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = "/models/Qwen3.5-35B-A3B-Q4_K_M.gguf",
            GpuLayers = 0,                        // CPU only
            ContextSize = 4096,                   // NOT the trained context
            LoadMode = ModelLoadMode.MemoryMap,   // the default; essential
            Threads = 6,                          // the physical core count
            // UseExtraBufferTypes = false,       // see below
        });

THE FILE is 22,016,023,168 bytes -- 20.5 GiB -- at Q4_K_M.

WHAT IT DOES. Loading takes 31 to 44 seconds warm and about 86 seconds cold
from disk. Generation runs at 8.8 to 9.3 tokens a second. A 23-token prompt
evaluates in 0.63 seconds; a 288-token prompt carrying tool definitions takes
9 to 15 seconds; the same conversation's next turn, hitting the prefix cache,
takes 0.88 seconds. Peak resident memory is about 23.5 GB.

THE ONE DECISION TO MAKE is UseExtraBufferTypes. Left at its default of true,
the CPU backend repacks the weights and the engine reports a repacked buffer
of 11.25 GiB ON TOP OF the 20.5 GiB of mapped weights -- on a 32 GiB machine
that is memory the mapped file would otherwise have used. Set to false, the
load takes 15 to 17 seconds instead of 31 to 44, the repack buffer is not
allocated at all, and the head-to-head measured THE SAME tokens per second.
The default stays true because the repacking does help elsewhere; on a machine
without the headroom, set it to false.

WHY ContextSize IS SET. This model's trained context is very large and its
cache at that size does not fit alongside the weights. 4096 is what the live
test uses and it is enough for a real conversation with tools.

TOOL CALLS. Qwen 3.5 does not write JSON tool calls. It writes
<tool_call><function=NAME><parameter=P>VALUE</parameter></function>
</tool_call>, and the library recognizes that from the template and reads it
into ToolCall.Name and ToolCall.ArgumentsJson like any other. Nothing has to
be configured.

THINKING. The model reasons by default. With Think = false its template
renders an empty, closed <think> block and it answers immediately, which is
what you want for a tool-calling turn: the deliberation of a model this size
runs to hundreds of tokens before it writes anything else.


UTILITIES
=========
The template engines, the output parsers and the schema converter are PUBLIC
in their own right, because they are useful without a model: for rendering a
prompt to inspect it, for parsing the output of a model served elsewhere, or
for building a grammar to hand to something else. None of them does any I/O
and none of them needs the native library.

JinjaTemplate
-------------
    static JinjaTemplate Parse(string source)
    string Render(IReadOnlyDictionary<string, object> variables)
    string Source { get; }

An original implementation of the Jinja template language, rendered the way
the Hugging Face transformers library renders a chat template -- because that
is what model authors write against: trim_blocks and lstrip_blocks on,
keep_trailing_newline off, auto-escaping off, break and continue available,
and a missing variable yielding the non-strict undefined value.

    JinjaTemplate template = JinjaTemplate.Parse(details.ChatTemplate);

    Dictionary<string, object> variables = new Dictionary<string, object>();
    variables["messages"] = new object[]
    {
        new Dictionary<string, object> { { "role", "user" },
                                         { "content", "Hi" } },
    };
    variables["add_generation_prompt"] = true;

    string prompt = template.Render(variables);

Variables are ordinary .NET values: a string is a Python string, any integral
type an int, any floating-point type a float, null is None, any IEnumerable is
a sequence, any string-keyed dictionary is a mapping, and a JsonElement is
accepted and converted. Values are copied when Render is called, so a template
that appends to a list cannot change your data, and mapping key order is
preserved because that is what `| tojson` writes. ChatTemplateException for a
source that does not parse (with the line and column) and for a failure while
rendering, including the message of a template's own raise_exception().

OllamaTemplate and OllamaTemplateValues
---------------------------------------
    static OllamaTemplate Parse(string text)
    static OllamaTemplate BuiltIn(string name)
    static IReadOnlyList<string> BuiltInNames { get; }
    static IReadOnlyList<string> BuiltInStopStrings(string name)
    static string NamedBuiltIn(string text)
    string Render(OllamaTemplateValues values)
    string Source { get; }   IReadOnlyList<string> Variables { get; }

A port of Ollama's template layer, template language and all, so a model
pulled from the Ollama registry is prompted exactly as Ollama prompts it. Both
shapes are handled: the modern one that ranges over .Messages and the older
.System/.Prompt/.Response form, which is rendered once per exchange.

    OllamaTemplate template = OllamaTemplate.BuiltIn("chatml");

    OllamaTemplateValues values = new OllamaTemplateValues();
    values.Messages.Add(new ChatMessage(ChatRole.System, "Be brief."));
    values.Messages.Add(new ChatMessage(ChatRole.User, "Hello!"));

    string prompt = template.Render(values);

BuiltInNames lists the twenty templates Ollama ships -- alfred, alpaca,
chatml, chatqa, codellama-70b-instruct, command-r, falcon-instruct,
gemma-instruct, gemma3-instruct, granite-instruct, llama2-chat,
llama3-instruct, magicoder, mistral-instruct, openchat, phi-3, solar-instruct,
starcoder2-instruct, vicuna and zephyr -- and they are embedded in the
assembly, byte for byte as Ollama ships them, with their stop strings.
NamedBuiltIn(text) is Ollama's fuzzy match: hand it an arbitrary chat
template, typically a model file's Jinja one, and it names the closest
built-in, or returns null when nothing is close enough.

OllamaTemplateValues carries IList<ChatMessage> Messages, IList<ToolDefinition>
Tools, string Prompt and string Suffix (the fill-in-the-middle pair), string
System (instructions prepended as a system message), bool? Think and string
ThinkLevel. ChatTemplateException for a template that will not parse or will
not render.

ThinkingParser, ThinkingParserState and ThinkingTags
---------------------------------------------------
    ThinkingParser(string openingTag = "<think>",
                   string closingTag = "</think>",
                   bool startsInsideThinking = false)
    (string thinking, string content) AddContent(string chunk)
    string Flush()
    string OpeningTag { get; }   string ClosingTag { get; }
    ThinkingParserState State { get; }   bool HasBufferedText { get; }

Splits a thinking model's output into reasoning and answer AS IT ARRIVES. Feed
it every chunk; it hands back the text that can be shown right away and
buffers anything still ambiguous -- a chunk ending in "<thi" might be the
start of a tag or might be literal text.

    ThinkingParser parser = new ThinkingParser();

    (string thinking, string content) =
        parser.AddContent("<think>hmm</think>Yes");
    // thinking "hmm", content "Yes"

    string leftover = parser.Flush();

Whitespace is handled the way Ollama handles it: eaten before the opening tag,
between the opening tag and the first reasoning character, and between the
closing tag and the first content character; a stream with no tags at all is
left exactly as the model wrote it. State walks LookingForOpening ->
ThinkingStartedEatingWhitespace -> Thinking -> ThinkingDoneEatingWhitespace ->
ThinkingDone. Flush returns whatever never resolved into a tag, which belongs
to the reasoning when State is Thinking and to the content otherwise; Ollama
drops that text, and returning it is a deliberate deviation, because dropped
text is a silently truncated answer. One instance handles one response and it
is not thread-safe.

ThinkingTags is the static half: TemplateSupportsThinking(chatTemplate),
Infer(chatTemplate, out opening, out closing), InferGoTemplateTags,
InferJinjaTemplateTags and PromptEndsWithOpeningTag(renderedPrompt, opening).

ToolCallParser, ToolCallParserState and ToolCallFormat
-----------------------------------------------------
    ToolCallFormat(string prefix, string suffix = null)
    static ToolCallFormat Auto { get; }
    static ToolCallFormat FromGoTemplateText(string goTemplateText)
    static ToolCallFormat FromJinjaTemplateText(string jinjaTemplateText)
    static ToolCallFormat FromRenderedToolCall(string renderedToolCall)
    string Prefix { get; }  string Suffix { get; }  bool IsBareJson { get; }

    ToolCallParser(ToolCallFormat format, IReadOnlyList<ToolDefinition> tools)
    (IReadOnlyList<ToolCall> calls, string content) AddContent(string chunk)
    string Flush()
    ToolCallFormat Format { get; }   ToolCallParserState State { get; }
    int CallCount { get; }

Pulls JSON tool calls out of an output stream, separating them from the text
the user should see.

    ToolCallFormat format =
        ToolCallFormat.FromJinjaTemplateText(details.ChatTemplate);
    ToolCallParser parser = new ToolCallParser(format, offeredTools);

    (IReadOnlyList<ToolCall> calls, string content) =
        parser.AddContent(chunk);

Prefix is never empty: "{" or "[" mean the model writes bare JSON with no
literal of its own, which IsBareJson reports and which restricts a call to the
very start of the output. State walks LookingForTag -> ToolCalling -> Done.
Only names in `tools` become calls; arguments are re-serialized as compact
JSON into ToolCall.ArgumentsJson with the model's property order preserved,
and ToolCall.Id is null -- the parser does not invent identifiers, which is
what the chat layer above it does. One instance handles one response and it is
not thread-safe.

JsonSchemaGrammar
-----------------
    static string JsonGrammar { get; }
    static string FromSchema(string jsonSchemaText)
    static string FromSchema(string jsonSchemaText,
                             out IReadOnlyList<string> warnings)
    static string FromSchema(JsonElement schema)
    static string FromSchema(JsonElement schema,
                             out IReadOnlyList<string> warnings)

The schema-to-grammar converter described under STRUCTURED OUTPUT, usable on
its own. JsonGrammar is llama.cpp's grammars/json.gbnf verbatim. The `warnings`
overloads report what the converter had to widen -- a pattern it cannot
express exactly, for instance -- while still producing a usable grammar; a
schema it converts exactly produces none.

    string grammar = JsonSchemaGrammar.FromSchema(schemaText, out var notes);
    foreach (string note in notes) Console.Error.WriteLine(note);

    GenerationOptions options = new GenerationOptions { Grammar = grammar };


THE ERROR MODEL
===============
Everything this library raises derives from ModelRunnerException, itself an
Exception. Catch the base type to catch all of it.

    ModelRunnerException         the base
      NativeLibraryException     the native engine could not be loaded -- the
                                 message lists every path that was tried -- or
                                 the library that was found is the wrong build
                                 or the wrong runtime identifier
      ModelLoadException         no file at ModelPath or at a LoRA adapter's
                                 path; the engine would not read the file as a
                                 model; no inference context could be created;
                                 a chat dialect was named with no template
                                 behind it; an adapter would not load or apply;
                                 a quantization the engine refused. ON THE ONNX
                                 ROAD it is also every graph or bundle the
                                 managed engine refuses to load, naming what
                                 it refused
      InferenceException         a decode failed, was aborted, or ran out of
                                 context memory; a prompt longer than the
                                 context; an empty prompt; an embedding input
                                 that is too long, tokenizes to nothing, or
                                 comes from a model with no embedding output.
                                 ON THE ONNX ROAD it is a run that could not
                                 proceed and a second run started while one is
                                 in flight
      ChatTemplateException      a chat template would not parse or would not
                                 render this request; the Native dialect was
                                 asked to render tools
      GrammarException           a GBNF grammar the engine will not parse, a
                                 JSON schema that is not JSON, or a schema
                                 using something the converter cannot express
                                 (a remote $ref among them)

Framework exceptions you will also see: ArgumentNullException (a null options
object, prompt, request, token list, input list or adapter list),
ArgumentException (an option out of range, a LoraAdapterOptions with no path,
a blank probe path, a quantization whose two paths name one file),
ArgumentOutOfRangeException (a quantization type the engine does not write),
FileNotFoundException and DirectoryNotFoundException (a quantization's input
file and its output's directory), ObjectDisposedException (any member after Dispose, and an
enumeration that was open when the model was disposed),
OperationCanceledException, InvalidOperationException (calling a member of a
model from inside an await foreach over that same model; and, on either road,
the mismatched-package guard described under RUNNING ONNX MODELS),
NotSupportedException (a member of IRunningModel, or a constraint setting, that
an ONNX bundle cannot honour -- it is a framework exception and this catch-all
does NOT catch it), and IOException and UnauthorizedAccessException from the
file system, unwrapped.

MOST MESSAGES CARRY THE ENGINE'S OWN LAST WORDS. The engine reports the real
reason for a failed load or decode only through its log, so the library keeps
the last few dozen lines and appends them to the exception message under "The
engine's log for this operation:". Read them; they usually name the problem
exactly.


THE NATIVE LIBRARY
==================
NAME: codebrix_llama -- libcodebrix_llama.dylib on macOS,
libcodebrix_llama.so on Linux, codebrix_llama.dll on Windows. It is the
unchanged llama.cpp C API plus two identity functions, built from vendored
source by this repository and committed; nothing downloads or compiles
anything at run time.

RUNTIME IDENTIFIERS SHIPPED: win-x64, win-arm64, osx-x64, osx-arm64,
linux-x64, linux-arm64, linux-riscv64. Backends: the CPU on every one, plus
Metal on osx-arm64. There is no CUDA, ROCm or Vulkan build in this version, so
GpuLayers has nothing to offload to except on Apple Silicon.

WHERE IT IS FOUND, in this order, under both AppContext.BaseDirectory and the
directory the managed assembly sits in:

    <dir>/runtimes/<rid>/native/<file>      the package's own layout
    <dir>/<file>                            a loose copy beside the assembly
    <file>                                  the operating system's search path

That works both for a publish WITH a runtime identifier, where the build
system copies the right native beside the application, and for one without,
where the natives stay in their runtimes folders and nothing else would look
there.

WHEN IT CANNOT BE LOADED you get a NativeLibraryException that lists every
path it tried, one per line, and then says:

    The package ships this library under runtimes/<rid>/native/. If it is
    missing, the application was probably published in a way that dropped it -
    a trimmed or single-file publish, a manual copy of the managed assembly
    alone, or a runtime identifier the package has no native for.

That list of paths is the answer to nearly every packaging question.

THE LIBRARY IS CHECKED BEFORE IT IS USED. It reports the upstream llama.cpp
commit it was built from and the runtime identifier it was built for, and both
must agree with what this binding expects; a mismatch is a
NativeLibraryException naming the file, because a library built from
different headers would have different structure layouts and would be read
wrongly. A platform the package ships no native for is refused by name before
anything is probed.


COMPLETE EXAMPLES
=================
Every name below exists in the package exactly as described above. Each
example is a complete method body: put it in an async Main (the MINIMUM
VIABLE PROJECT TEMPLATE shows one) with these usings:

    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelRunner;

EXAMPLE 1 - PROBE, LOAD, STREAM A CHAT, AND UNLOAD
--------------------------------------------------
    string path = "/models/smollm-360m-instruct-q8_0.gguf";

    // 1. PROBE. No weights are read; this is cheap at any file size.
    ModelDetails details = await ModelRunner.ProbeAsync(path);
    Console.WriteLine($"{details.Architecture}: {details.Description}");
    Console.WriteLine($"weights {details.WeightsSize:N0} bytes, "
        + $"trained context {details.TrainingContextLength}");

    // 2. LOAD. `await using` unloads it at the end of the scope.
    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions
        {
            ModelPath = path,
            ContextSize = 4096,
        });

    Console.WriteLine($"dialect: {model.ChatTemplateDialect}");   // Jinja

    // 3. CHAT, streaming.
    ChatRequest request = new ChatRequest
    {
        Options = new GenerationOptions
        {
            MaxTokens = 200,
            Sampling = new SamplingOptions { Temperature = 0.7f },
        },
    };

    request.Messages.Add(
        new ChatMessage(ChatRole.System, "Answer in one short sentence."));
    request.Messages.Add(
        new ChatMessage(ChatRole.User, "Why is the sky blue?"));

    await foreach (ChatUpdate update in model.ChatAsync(request))
    {
        Console.Write(update.ContentDelta);

        if (update.IsFinal)
        {
            Console.WriteLine();
            Console.WriteLine($"{update.FinishReason}, "
                + $"{update.Statistics.GeneratedTokens} tokens at "
                + $"{update.Statistics.TokensPerSecond:F1}/s");
        }
    }

EXAMPLE 2 - A MULTI-TURN CONVERSATION THAT REUSES THE CACHE
-----------------------------------------------------------
    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions { ModelPath = path, ContextSize = 4096 });

    // ONE request object, grown a turn at a time. Sending the whole
    // conversation is the contract; the prefix cache is what makes it cheap.
    ChatRequest conversation = new ChatRequest
    {
        Options = new GenerationOptions { MaxTokens = 120 },
    };

    conversation.Messages.Add(new ChatMessage(ChatRole.System, "Be brief."));

    foreach (string question in new[] { "Name one colour.", "And another?" })
    {
        conversation.Messages.Add(new ChatMessage(ChatRole.User, question));

        ChatResponse reply = await model.ChatToEndAsync(conversation);
        Console.WriteLine($"> {question}");
        Console.WriteLine(reply.Message.Content);
        Console.WriteLine($"  ({reply.Statistics.CachedPromptTokens} of "
            + $"{reply.Statistics.PromptTokens} prompt tokens were cached)");

        // The model's own reply becomes part of the next turn's prompt.
        conversation.Messages.Add(
            new ChatMessage(ChatRole.Assistant, reply.Message.Content));
    }

EXAMPLE 3 - STRUCTURED OUTPUT FROM A JSON SCHEMA
------------------------------------------------
    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions { ModelPath = path, ContextSize = 2048 });

    const string schema =
        "{\"type\":\"object\","
        + "\"properties\":{\"city\":{\"type\":\"string\"},"
        + "\"country\":{\"type\":\"string\"}},"
        + "\"required\":[\"city\",\"country\"]}";

    ChatRequest request = new ChatRequest
    {
        ResponseFormat = ResponseFormat.FromJsonSchema(schema),
        Options = new GenerationOptions
        {
            MaxTokens = 120,
            Sampling = new SamplingOptions { Temperature = 0f },
        },
    };

    request.Messages.Add(new ChatMessage(ChatRole.User,
        "Where is the Eiffel Tower? Answer as JSON."));

    ChatResponse reply = await model.ChatToEndAsync(request);

    // The sampler could not produce anything else, so this always parses.
    Console.WriteLine(reply.Message.Content);
    // {"city": "Paris", "country": "France"}

    // The same constraint on a raw completion, as a grammar:
    GenerationOptions constrained = new GenerationOptions
    {
        MaxTokens = 8,
        Grammar = "root ::= \"yes\" | \"no\"",
        Sampling = new SamplingOptions { Temperature = 0f },
    };

    GenerationResult yesNo = await model.GenerateToEndAsync(
        "Is the sky blue? Answer yes or no.", constrained);

    Console.WriteLine(yesNo.Text.Trim());   // "yes"

EXAMPLE 4 - EMBEDDINGS AND A COSINE SIMILARITY
----------------------------------------------
    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions { ModelPath = path, ContextSize = 2048 });

    EmbeddingResult result = await model.EmbedAsync(new[]
    {
        "a cat sat on the mat",
        "a feline rested on the rug",
        "quarterly revenue exceeded expectations",
    });

    Console.WriteLine($"{result.Embeddings.Count} vectors of "
        + $"{result.Dimensions} dimensions, "
        + $"{result.PromptTokens} tokens in total");

    Console.WriteLine(Cosine(result.Embeddings[0], result.Embeddings[1]));
    Console.WriteLine(Cosine(result.Embeddings[0], result.Embeddings[2]));

    static double Cosine(float[] left, float[] right)
    {
        double dot = 0, leftLength = 0, rightLength = 0;

        for (int i = 0; i < left.Length; i++)
        {
            dot += left[i] * right[i];
            leftLength += left[i] * left[i];
            rightLength += right[i] * right[i];
        }

        return dot / (Math.Sqrt(leftLength) * Math.Sqrt(rightLength));
    }

EXAMPLE 5 - HANDLING THE ERRORS A LOAD AND A REQUEST CAN RAISE
--------------------------------------------------------------
    try
    {
        await using IRunningModel model = await ModelRunner.LoadAsync(
            new ModelRunnerOptions
            {
                ModelPath = "/models/enormous.gguf",
                ContextSize = 131072,
            });

        GenerationResult result =
            await model.GenerateToEndAsync("Hello", null);

        Console.WriteLine(result.Text);
    }
    catch (NativeLibraryException ex)
    {
        // The message lists every path that was tried. A packaging question.
        Console.Error.WriteLine(ex.Message);
    }
    catch (ModelLoadException ex)
    {
        // No such file, or a context this machine cannot hold. The engine's
        // own log lines are appended to the message.
        Console.Error.WriteLine(ex.Message);
    }
    catch (ChatTemplateException ex)
    {
        Console.Error.WriteLine($"Template: {ex.Message}");
    }
    catch (GrammarException ex)
    {
        Console.Error.WriteLine($"Grammar: {ex.Message}");
    }
    catch (ModelRunnerException ex)
    {
        // Everything else this library raises, InferenceException included.
        Console.Error.WriteLine(ex.Message);
    }

EXAMPLE 6 - CANCELLING A LONG GENERATION
----------------------------------------
    await using IRunningModel model = await ModelRunner.LoadAsync(
        new ModelRunnerOptions { ModelPath = path, ContextSize = 4096 });

    using CancellationTokenSource source =
        new CancellationTokenSource(TimeSpan.FromSeconds(5));

    GenerationOptions options = new GenerationOptions { MaxTokens = 4096 };

    try
    {
        await foreach (GenerationUpdate update in model.GenerateAsync(
            "Write a very long story about a lighthouse.",
            options,
            source.Token))
        {
            Console.Write(update.Text);
        }
    }
    catch (OperationCanceledException)
    {
        Console.WriteLine();
        Console.WriteLine("stopped");
    }

    // The model is still perfectly usable: cancelling ends a request, it does
    // not break the context.
    GenerationResult after = await model.GenerateToEndAsync("Two plus two is",
        new GenerationOptions { MaxTokens = 4 });

    Console.WriteLine(after.Text);


MINIMUM VIABLE PROJECT TEMPLATE
===============================
A complete, working console application. Both files as shown compile and run;
point it at any GGUF file on disk.

MyRunnerTool.csproj

    <Project Sdk="Microsoft.NET.Sdk">

      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>disable</ImplicitUsings>
      </PropertyGroup>

      <ItemGroup>
        <PackageReference
            Include="CodeBrix.Ollama.ModelRunner.MitLicenseForever" />
      </ItemGroup>

    </Project>

Program.cs

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelRunner;

    namespace MyRunnerTool;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.Error.WriteLine(
                    "usage: MyRunnerTool <model.gguf> [question]");
                return 1;
            }

            string question =
                args.Length > 1 ? args[1] : "Why is the sky blue?";

            ModelDetails details = await ModelRunner.ProbeAsync(args[0]);
            Console.WriteLine($"{details.Architecture}  {details.Description}");

            await using IRunningModel model = await ModelRunner.LoadAsync(
                new ModelRunnerOptions
                {
                    ModelPath = args[0],
                    ContextSize = 4096,
                });

            ChatRequest request = new ChatRequest
            {
                Options = new GenerationOptions { MaxTokens = 300 },
            };

            request.Messages.Add(new ChatMessage(ChatRole.User, question));

            await foreach (ChatUpdate update in model.ChatAsync(request))
            {
                Console.Write(update.ContentDelta);
                if (update.IsFinal) Console.WriteLine();
            }

            return 0;
        }
    }


PERFORMANCE TIPS
================
  - LOAD ONE MODEL AND KEEP IT. Loading is the expensive part -- seconds for a
    small model, a minute for a large one -- and a loaded model answers
    request after request. Load at start-up, dispose at shutdown.
  - LET THE PREFIX CACHE WORK. Send the conversation in one growing
    ChatRequest against one model instance, and each turn evaluates only the
    new messages. Interleaving two conversations on one model throws that away
    every time; use two models, or accept the cost.
  - PROBE BEFORE YOU LOAD. ProbeAsync reads a twenty-gigabyte file's shape in
    seconds and tells you what the weights will need. It is also the cheap way
    to read the embedded chat template.
  - SET ContextSize. The default is the model's TRAINED context, which on a
    modern model can be enormous and is nearly always more than you need. The
    key/value cache is proportional to it.
  - KEEP LoadMode AT MemoryMap. It is what lets a model close to the size of
    physical memory load at all, and it lets the operating system reclaim
    weight pages under pressure.
  - TURN OFF UseExtraBufferTypes WHEN MEMORY IS TIGHT. Measured on a 20.5 GiB
    model: the same tokens per second, half the load time, and one buffer the
    size of half the model not allocated.
  - PREFER Temperature 0 FOR ANYTHING YOU WILL TEST. Greedy sampling is
    reproducible, which makes a failing case a failing case rather than a
    mood.
  - USE ChatToEndAsync WHEN YOU DO NOT DISPLAY THE STREAM. It is the same work
    with fewer allocations and no enumeration to keep alive.
  - RenderChatPromptAsync COSTS NOTHING and answers "what is the model
    actually being asked?" faster than any amount of reasoning about it.
  - QUANTIZE THE CACHE, NOT THE ATTENTION, FIRST. KeyCacheType and
    ValueCacheType Q8_0 halve the cache for very little accuracy; changing
    FlashAttention away from Auto rarely helps and sometimes fails the load.
  - LEAVE THE THREAD COUNT ALONE UNLESS YOU HAVE MEASURED, and if you ship to
    machines you have never seen, set MaxThreads rather than Threads: a count
    that suits a big server oversubscribes a small one. THREADS: HOW MANY ARE
    USED, AND HOW TO CONTROL IT has the rule, the precedence and a program
    that measures your own machine in a minute.


COMMON PITFALLS TO AVOID
========================
 1. DO NOT confuse the package id with the namespace. Package:
    CodeBrix.Ollama.ModelRunner.MitLicenseForever; namespace:
    CodeBrix.Ollama.ModelRunner, one namespace for every public type, so
    `using CodeBrix.Ollama.ModelRunner.Engine;` is a CS0246 error.

 2. DO NOT expect this to reach a running Ollama, or to start one. It loads a
    file and runs it in this process. "Ask the local Ollama server" is the
    wrong job for this package; "run the model without installing Ollama" is
    the right one.

 3. DO NOT leave ContextSize null for a large model. The default is the
    model's trained context, and the cache for it may be larger than the
    weights; the load then fails with a ModelLoadException about memory that
    reads like a bug and is a setting.

 4. DO NOT drop the result of GenerateAsync or ChatAsync: they return
    IAsyncEnumerable, nothing happens until you iterate, and the request holds
    its turn until the enumeration finishes or is disposed.

 5. DO NOT call another member of the same model from inside an await foreach
    over that model. It throws InvalidOperationException. Collect what you
    need, let the enumeration end, then call.

 6. DO NOT assume the strings coming back are non-null: the library is
    compiled with nullable reference types off, so your compiler will not warn
    you -- ModelDetails.ChatTemplate, ModelDetails.Name, ChatMessage.Thinking,
    ToolCall.Id and the Statistics of a non-final update can all be null.

 7. DO NOT apply a chat template by hand and then call ChatAsync. Chat renders
    the template itself. Hand-built prompt text belongs in GenerateAsync,
    which applies no template at all.

 8. DO NOT expect tool calls before the final update. They arrive complete, on
    the update whose IsFinal is true, and the finish reason is then ToolCall.

 9. DO NOT offer tools on the Native dialect: it cannot express them and the
    request is refused with ChatTemplateException. Use the Jinja dialect --
    the model file usually embeds a template that handles tools -- or supply
    an Ollama template that renders them.

10. DO NOT combine a grammar or a JSON response format with a tool-calling
    turn. A grammar constrains the whole output, tool-call literals included.

11. DO NOT treat ClearCacheAsync as a way to save memory. It clears the
    conversation held in a cache that was allocated at load and stays
    allocated; what it changes is correctness and speed, not footprint.

12. DO NOT feed an embedding input longer than one physical batch. An
    embedding is computed in a single batch, so the limit is the smaller of
    the embedding context's size and PhysicalBatchSize, not the context size
    alone; the InferenceException states it, and the fix is to raise
    PhysicalBatchSize or split the text.

13. DO NOT set a handler with SetLogHandler that does real work. It is called
    on engine threads, line by line, during a load; queue the line and return.

14. DO NOT forget to dispose. An IRunningModel holds the weights, a context, a
    thread and possibly a mapped file. `await using` is one word.

15. DO NOT expect a CancellationToken to stop a quantization that has started.
    The native quantizer cannot be interrupted, so the token is honoured
    before the engine is asked and not after; the call runs to the end. It is
    the one place in this package where a token does less than it looks like
    it does, and it is documented rather than papered over.

16. DO NOT quantize an already-quantized file to save a step. It needs
    AllowRequantize, and what comes out is measurably worse than quantizing
    the F16 or BF16 file once. Keep the unquantized file if you can.

17. DO NOT hand QuantizeAsync the same path twice. An output that names the
    input is ArgumentException, not an in-place rewrite; a quantization reads
    one file and writes another, and the input is never modified.

18. DO NOT mix the two roads. ModelRunner.LoadAsync reads a GGUF file and
    OnnxModel.LoadAsync reads an ONNX graph; neither reads the other's format,
    and each says so when it is handed the wrong one. RUNNING ONNX MODELS has
    fourteen more pitfalls that belong to that road alone.

19. DO NOT install this package and CodeBrix.Ollama.ModelManager at different
    versions. They are released together and must be installed together; a
    mixture builds without complaint and then fails at run time with a message
    telling you to do exactly this.

20. DO NOT SET Threads TO Environment.ProcessorCount, and do not expect
    MaxThreads to do anything once Threads is set. Those are the two mistakes
    that cost real speed; THREADS: HOW MANY ARE USED, AND HOW TO CONTROL IT
    has seven more pitfalls that belong to the thread settings on both roads.


WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this package to:

  - DOWNLOAD a model. There is no registry client, no HTTP and no model store
    here: this package takes a path to a file that already exists. That is the
    separate CodeBrix.Ollama.ModelManager package, whose ResolveAsync hands
    you exactly the path this one wants.
  - CONVERT a checkpoint into a GGUF file, or put a file it wrote away as a
    model. It quantizes a GGUF file that already exists, and hands back a
    path; converting a publisher's checkpoint and storing the result belong to
    CodeBrix.Ollama.ModelManager.
  - Talk to `ollama serve`, or be talked to. It is not a server and it has no
    client: no listener, no port, no /api/chat, no keep-alive, no model
    unloading protocol. It does not need Ollama installed and never looks for
    it.
  - RUN A MULTIMODAL MODEL. Vision and audio projectors are not loaded and
    images cannot be put into a message; ModelDetails reports what the file
    says about encoders, and that is all this version does with them.
  - SPECULATIVE DECODING with a draft model, or the multi-token-prediction
    layers some models carry. LoadMtpLayers exists, costs memory and is
    reserved for that path; nothing uses it in this version.
  - QUANTIZE, CONVERT OR WRITE a model file. The engine can do it; none of it
    is exposed here. No safetensors, no GGUF writing, no re-quantization.
    Nothing here writes an ONNX graph either: the ONNX road READS a graph and
    runs it, and making one smaller is the separate
    CodeBrix.Ollama.ModelManager package's work.
  - TRAIN, FINE-TUNE OR RUN AN ONNX GRAPH ON AN ACCELERATOR. The managed ONNX
    interpreter is CPU-only by design, because that is what runs everywhere
    with nothing installed.
  - TURN GENERATED NOTATION TEXT INTO MUSIC. A text model that writes a
    notation format writes TEXT and this package hands you the text; turning
    it into MIDI belongs to another library. The MIDI driver here generates
    MIDI events directly, from a MIDI model, and is a different thing.
  - SAVE OR RESTORE a session. There is no state serialization on the
    contract: a conversation is the messages you hold, not a blob the library
    hands back.
  - SERVE MANY CONVERSATIONS FROM ONE MODEL AT ONCE. A second generation on
    one instance is refused and the cache is one conversation deep. Several instances
    run in parallel happily; batching many sequences through one context is
    not in this version.
  - Offer synchronous APIs, or run on .NET below 10.0.

This package IS for: loading a GGUF file into your own process; completing a
prompt; holding a conversation through the model's own chat template, with
reasoning and tool calls separated out for you; embedding text; constraining
output to a grammar or a JSON schema; tokenizing; reading everything the engine
knows about a model file; and -- on the other road, with nothing installed at
all -- running an ONNX graph, generating streamed MIDI music from a MIDI model,
and generating text from an ONNX text bundle.


TROUBLESHOOTING
===============

"The native inference library could not be loaded"
    Read the list of paths in the message. The usual causes are a trimmed or
    single-file publish that dropped runtimes/, a manual copy of the managed
    assembly alone, and a runtime identifier the package ships no native for.
    ModelRunner.GetNativeRuntimeInfo().LoadedPath tells you where it came from
    when it did load.

"...reports that it was built for runtime identifier 'x', but this process
is 'y'"
    The wrong native reached the output folder. Check the publish's runtimes/
    layout, and whether a RuntimeIdentifier was set for a different platform.

The load fails with a message about memory, or the process is killed
    ContextSize. Leave it null and a modern model asks for its full trained
    context, whose cache can exceed the weights. Set it to 4096 and try again;
    then quantize the cache with KeyCacheType and ValueCacheType Q8_0; then
    set UseExtraBufferTypes to false. Keep LoadMode at MemoryMap throughout.
    ModelRunner.SetLogHandler shows the sizes the engine settled on.

The model loads but chat produces nonsense, or never stops
    The template is wrong for the model. Call RenderChatPromptAsync and look
    at the prompt. Check IRunningModel.ChatTemplateDialect: Native means no
    template was found and the engine is guessing from a fixed list. Supply a
    JinjaTemplate or an OllamaTemplate, or use a model file that embeds its
    own -- most do.

"ChatTemplateDialect.Jinja was asked for, but the model file embeds no chat
template..."
    Exactly what it says, raised at load rather than on the first request.
    Either supply JinjaTemplate, or leave the dialect at Auto.

Reasoning text appears inside Content instead of Thinking
    The tags could not be inferred from the template. ThinkingTags.Infer on
    the template text says whether they can be; a model whose template carries
    reasoning in a separate field rather than in tags has nothing to separate,
    and the reasoning is part of what the model wrote.

Tool calls come back as content
    The tool name in the model's output has to match a ToolDefinition.Name in
    the request; a block naming anything else is returned as content rather
    than dropped. Check the name, and check that Tools was non-empty -- with
    no tools offered, nothing is parsed as a call at all.

Every turn re-evaluates the whole prompt
    CachedPromptTokens is 0 when the prompt shares no prefix with the last
    one. An early message changed, ClearCacheAsync was called, another
    conversation used the same model in between, or the previous request was
    cancelled.

A generation reports that the model is already in use
    Another operation holds the model, possibly a streaming generation whose
    enumeration was never finished or disposed. Finish or dispose it first,
    queue complete generations in your application, or use separate models
    for parallel work. Native GGUF and managed ONNX both refuse a second
    concurrent generation with InferenceException.

A cache, embedding or adapter operation seems to hang
    These native operations wait for an active generation to finish or dispose
    its enumeration. Calling them from inside that enumeration instead gives
    InvalidOperationException to prevent waiting on itself.

Generation is much slower than expected
    Check GetNativeRuntimeInfo().SystemInfo for the CPU features in use and
    Devices for what the engine can see. Then check Threads -- if your code
    sets it at all, and especially if it sets it to
    Environment.ProcessorCount, which is the LOGICAL count and is measurably
    worse than leaving it alone; see THREADS: HOW MANY ARE USED, AND HOW TO
    CONTROL IT. Then check whether the machine is paging -- a model that does
    not fit runs at disk speed, not processor speed.

"...this engine does not implement the operator '...'"
    An ONNX graph asking for something the managed interpreter does not
    implement, refused at load with the node, the operator and the domain
    named, and a list of what it does implement. Nothing is half-loaded; see
    WHAT IS SUPPORTED AND WHAT IS REFUSED under RUNNING ONNX MODELS.

"...was built against CodeBrix.Ollama.Core contract revision N, but contract
revision M was loaded"
    The two CodeBrix.Ollama packages are installed at different versions.
    Install both at the same version. Nothing else causes this message.

An ONNX conversation re-reads its whole history every turn
    It is meant to. There is no prefix cache on that road and ClearCacheAsync
    has nothing to clear; the GGUF road is the one with the cache.


WORKING EXAMPLES ON GITHUB
==========================

The test suite is the largest body of compiling, working usage of this
package. The default suite is OFFLINE and downloads nothing: the template
engines, the parsers, the grammar converter and the option mapping are tested
over fixtures, and the decode path is tested against a synthetic conformance
model generated in the repository. Only the live tests touch the network, and
they are gated behind environment variables.

    https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelRunner.Tests

Feature-to-test-file map:

  The completion path against a real model: probe, tokenize, stream, stop
  sequences, grammars, cache reuse, embeddings, cancellation
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/SmolLmLiveTests.cs

  The chat path against a real model: the three dialects, rendering, the JSON
  response format, streaming, cache reuse across turns
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/SmolLmChatLiveTests.cs

  A twenty-gigabyte mixture-of-experts model on a processor: loading,
  thinking on and off, tools, and the UseExtraBufferTypes head-to-head
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/Qwen35LiveTests.cs

  Standing in for a real model: an IRunningModel implemented by hand, which
  is how to test an application without loading anything
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/FakeRunningModel.cs

  The decode loop against the synthetic conformance model, offline
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/RunningModelConformanceTests.cs

  Chat post-processing: reasoning and tool calls taken apart over a scripted
  token stream
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ChatPipelineTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ChatFunctionCallParserTests.cs

  Template rendering and the variables each dialect is given
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ChatTemplateRendererTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ChatJinjaVariablesTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ChatOllamaValuesTests.cs

  The Jinja engine, against real chat templates from Qwen, SmolLM, Mistral,
  Phi, DeepSeek, Granite and OLMo
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Templates/Jinja/JinjaFixtureTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Templates/Jinja/JinjaTemplateTests.cs

  The Ollama template engine, its built-ins and Ollama's own test data
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Templates/OllamaGo/OllamaTemplateRenderTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Templates/OllamaGo/OllamaTemplateBuiltInTests.cs

  The thinking and tool-call parsers, against Ollama's own cases
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Parsing/ThinkingParserTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Parsing/ToolCallParserTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Parsing/ToolCallFormatTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Parsing/ThinkingTagsTests.cs

  The JSON-schema-to-grammar converter, against llama.cpp's own case table
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Grammar/JsonSchemaGrammarTests.cs

  Option mapping and validation, without a model
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Engine/ParameterMapperTests.cs

  Finding and checking the native library
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Native/NativeLibraryLoaderTests.cs

  ON THE ONNX ROAD -- loading a graph, the three load forms, every refusal,
  and running one against recorded answers
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Onnx/OnnxModelTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Onnx/OnnxSessionTests.cs

  Generating MIDI: the whole driver over a small model, the streaming
  behaviours, and the Standard MIDI File round trip
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Drivers/SkyTnt/MidiGenerationModelTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Midi/MidiFileTests.cs

  Generating text from an ONNX bundle, including every refusal by name
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelRunner.Tests/Drivers/CausalLm/OnnxCausalLmModelTests.cs

  Both packages used together, which is the only place they meet
    https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.EndToEnd.Tests


QUICK REFERENCE CARD
====================

INSTALL     dotnet add package CodeBrix.Ollama.ModelRunner.MitLicenseForever
USING       using CodeBrix.Ollama.ModelRunner;
TARGET      .NET 10 or later   DEPENDENCIES  none   LICENSE  MIT
NULLABLE    off in the library -- read the per-member notes
NATIVE      codebrix_llama, shipped for win-x64, win-arm64, osx-x64,
            osx-arm64, linux-x64, linux-arm64, linux-riscv64, found under
            runtimes/<rid>/native/ beside the application
LOAD        await using IRunningModel model = await ModelRunner.LoadAsync(
                new ModelRunnerOptions { ModelPath = path,
                                         ContextSize = 4096 });
PROBE       ModelDetails d = await ModelRunner.ProbeAsync(path);  // no load
ENGINE      NativeRuntimeInfo i = ModelRunner.GetNativeRuntimeInfo();
LOG         ModelRunner.SetLogHandler((level, line) => ...);  null discards
COMPLETION  GenerateAsync(prompt, options, ct)      streaming, no template
            GenerateToEndAsync(prompt, options, ct)
CHAT        ChatAsync(request, ct) / ChatToEndAsync(request, ct)
            RenderChatPromptAsync(request, ct)      the prompt, no generation
OTHER       TokenizeAsync, DetokenizeAsync, EmbedAsync,
            SetLoraAdaptersAsync, ClearCacheAsync
QUANTIZE    QuantizeResult r = await ModelRunner.QuantizeAsync(
                inPath, outPath, GgufQuantizationType.Q4_K_M);
            nothing installed; byte for byte the engine's own tool's output;
            QuantizeOptions { Threads 0, AllowRequantize false, Pure false };
            the token is honoured BEFORE the engine starts, not during
OPTIONS     ModelPath, LoadMode (MemoryMap), ContextSize, GpuLayers, Threads,
            BatchThreads, MaxThreads, BatchSize 2048 / PhysicalBatchSize 512,
            KeyCacheType / ValueCacheType, UseExtraBufferTypes (true),
            EmbeddingsMode, EmbeddingPooling, ChatTemplateDialect,
            OllamaTemplate, JinjaTemplate, LoadProgress
SAMPLING    Ollama's defaults: temperature 0.8, top-k 40, top-p 0.9,
            repeat penalty 1.1 over the last 64 generated tokens.
            Temperature 0 is greedy; Seed null is random, fixed is repeatable.
            For comparisons keep model/settings/build/hardware fixed and clear
            native GGUF cache before each run. Seed 0xFFFFFFFF is rejected
TEMPLATES   Auto -> OllamaTemplate, else embedded/JinjaTemplate, else Native.
            Native cannot render tools. Think is bool?: null leaves the
            template's own default
OUTPUT      Grammar > JsonSchema > JsonMode > ChatRequest.ResponseFormat
STREAMING   every update until IsFinal; the final one carries FinishReason,
            Statistics and any ToolCalls
THREADS     native: one worker thread per model; both runners refuse concurrent
            generation with InferenceException; finish/dispose each stream.
            Native cache/embed/adapters wait (same-chain calls throw);
            tokenize, detokenize and render do not take the context gate
HOW MANY    say nothing -> GGUF one per PHYSICAL core, ONNX one per
            PERFORMANCE core; Threads (and BatchThreads) -> used EXACTLY, cap
            ignored; MaxThreads -> a ceiling on the library's own choice only.
            Below 1 is refused by name. Never set Threads to
            Environment.ProcessorCount. See THREADS: HOW MANY ARE USED, AND
            HOW TO CONTROL IT
ERRORS      ModelRunnerException: NativeLibraryException, ModelLoadException,
            InferenceException, ChatTemplateException, GrammarException.
            NotSupportedException (the ONNX road's refusals) is a FRAMEWORK
            exception and is not under that base type
ONNX        the other road, with nothing installed and no native library:
            OnnxModel (tensors in, tensors out), MidiGenerationModel (streamed
            MIDI events) and OnnxCausalLmModel (text, as IRunningModel). See
            RUNNING ONNX MODELS, which has a quick reference of its own
MANAGER     CodeBrix.Ollama.ModelManager is a separate package, with its own
            AGENT-README, that turns a model NAME into the file path this
            package loads. INSTALL BOTH AT THE SAME VERSION.


================================================================================
END OF AGENT-README


MUSECOCO MUSIC AND OPTIONAL ATTRIBUTE BERT
=========================================
See MUSECOCO-README.txt for the complete consumer guide and versioned bundle
contract. The public entry points are MuseCocoMusicModel and MuseCocoTextModel;
both load from a directory or logical-name-to-path map and execute in managed
.NET without Python, ModelManager or an ONNX Runtime installation.

MuseCocoTextModel.PredictAsync returns MusicAttributePrediction: immutable
MusicAttributes, classifier probabilities, WordPiece IDs and a truncation flag.
MuseCocoMusicModel.GenerateAsync accepts those attributes (or Schema-created
attributes), MuseCocoGenerationOptions, optional token-count progress and a
CancellationToken. It returns a MidiScore, generated IDs, effective seed and
prompt/generation timings. MidiFile.WriteAsync saves the score. BERT is optional;
callers can inspect or change attributes before passing them to music generation.

MuseCocoMusicModel.GenerateStreamingAsync takes the same arguments and returns
IAsyncEnumerable<MidiEvent>. It releases completed bars while later bars are being
generated, applying incomplete-position cleanup to the final bar. Events use 480
ticks per quarter note and arrive in timestamp order; play strictly before each
HorizonTicks (equal to Tick) until enumeration ends, then drain the player queue.
Channel/track assignments stay fixed as instruments first become playable, with
percussion on channel 9. Programs precede first notes at those notes' ticks.
These assignments can differ from GenerateAsync's completed-score layout.
Collect the events into new MidiScore(480, events) to save the streamed piece.

Each model can independently be FP32, weight-only INT8 or weight-only INT4.
Quantization belongs in the staging application. The runtime bundle is data-only.
The libraries do not reference one another. Existing llama.cpp assets are not
loaded for this managed ONNX path.

One request per instance; cancellation allows reuse, and disposal during a
request is refused. Recurrent state starts empty on every music request. Set
Seed=null for fresh randomness, or a fixed nonnegative seed other than
0xFFFFFFFF; negative seeds are rejected. Defaults are 2560 maximum tokens and
512 minimum tokens, not MIDI notes. Lower both for short excerpts. An event
enumeration owns its instance until it completes or is disposed; breaking await
foreach stops generation, and cancellation preserves events already yielded.
BERT truncates to 512 prompt tokens and reports that fact.

General ONNX additions used by these models: FP32 Elu, Erf, Tanh and standard
LayerNormalization (opset 17+, optional bias/statistics, FP32 stash). The matrix
path shares weight panels across activation rows for FP32, INT4 and INT8 on
AVX2/FMA processors. Portable vector/scalar fallbacks remain supported. The
MuseCoco driver reuses two private recurrent-state buffers; public OnnxModel
Run/RunAsync results retain their existing independent ownership contract.
