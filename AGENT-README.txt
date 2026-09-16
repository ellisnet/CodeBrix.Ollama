================================================================================
AGENT-README: CodeBrix.Ollama.ModelRunner
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Ollama.ModelRunner.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Ollama.ModelRunner is a cross-platform, zero-dependency .NET 10
library that loads a GGUF model file and runs it IN YOUR OWN PROCESS, over a
llama.cpp engine this repository builds itself and ships inside the package.
There is no server to start, no daemon to talk to, no Ollama installation to
find, no HTTP anywhere, and no NuGet dependency of any kind.

What it does, through one interface:

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
references the other -- and the seam between them is a FILE PATH:

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
type lives in it. The repository folders (Contracts/, Options/, Common/,
Native/, Engine/, Templates/Jinja/, Templates/OllamaGo/, Parsing/, Grammar/)
are FILE ORGANIZATION ONLY, not namespaces: `using
CodeBrix.Ollama.ModelRunner.Engine;` is a CS0246 error. Everything under
Native/ and Engine/ is internal -- the P/Invoke surface, the worker thread and
the decode loop are reached only through ModelRunner and IRunningModel. You
will also want the ordinary framework usings: System,
System.Collections.Generic, System.Threading and System.Threading.Tasks.

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
    THE CACHE, CANCELLATION AND THREADS   what is coordinated, what is not
    LOGGING                         SetLogHandler and what the engine says
    MEMORY AND SPEED                what to set for a model that barely fits
    THE QWEN 3.5 35B-A3B RECIPE     a 20 GiB model on a CPU, measured
    UTILITIES                       the public template and parser types
    THE ERROR MODEL                 every exception and when it is thrown
    THE NATIVE LIBRARY              names, paths and the failure message


THE ModelRunner STATIC CLASS
============================
Four members, and they are the only way into the engine.

    static Task<IRunningModel> LoadAsync(ModelRunnerOptions options,
                                         CancellationToken)
    static Task<ModelDetails>  ProbeAsync(string modelPath,
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
    int?     Threads            = null    null -> the physical core count
    int?     BatchThreads       = null    null -> the same as Threads
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
BatchSize, a ContextSize of 0, a Threads or BatchThreads below 1, and a
LoraAdapterOptions with no path, are all ArgumentException. A ModelPath or an
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

THREADS. The default is the number of PHYSICAL cores, not logical ones,
because hyperthreads do not help a memory-bound matrix multiply and usually
hurt. Set Threads explicitly when the process shares the machine.

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
request evaluates its whole prompt again. See THE CACHE below.

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
                                   every time, and reproducible
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

REPRODUCIBLE OUTPUT needs Temperature 0 (which selects greedily and ignores
the seed entirely) or a fixed Seed with the same build and hardware. Note that
0xFFFFFFFF is the engine's own "draw a random seed" marker, so setting Seed to
that value is not reproducible either.

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
  - REQUESTS ON ONE MODEL ARE SERIALIZED. A request holds a gate for its whole
    duration -- a streaming one until the enumeration completes or is disposed
    -- because the context's memory is request state and two interleaved
    requests would read each other's history. Concurrent callers WAIT; they do
    not fail, and they do not corrupt anything.
  - THREE MEMBERS DO NOT WAIT: TokenizeAsync, DetokenizeAsync and
    RenderChatPromptAsync read the vocabulary and the template, not the
    context, so they stay answerable while a generation is running.
  - DO NOT CALL THE SAME MODEL FROM INSIDE ITS OWN await foreach. A gated
    member called from within an enumeration of GenerateAsync or ChatAsync on
    the same instance would wait on a gate that enumeration is holding. The
    library detects this and throws InvalidOperationException rather than
    waiting on itself; finish the enumeration, or dispose it, first.
  - SEVERAL MODELS IN PARALLEL IS FINE. Each has its own thread, context and
    cache, and they share only the native library and the log handler. Memory
    is the only limit.
  - THE LOG HANDLER IS PROCESS-WIDE, and may be called on any engine thread.


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
                    right; lower it when the process shares the machine
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
                                 behind it; an adapter would not load or apply
      InferenceException         a decode failed, was aborted, or ran out of
                                 context memory; a prompt longer than the
                                 context; an empty prompt; an embedding input
                                 that is too long, tokenizes to nothing, or
                                 comes from a model with no embedding output
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
a blank probe path), ObjectDisposedException (any member after Dispose, and an
enumeration that was open when the model was disposed),
OperationCanceledException, InvalidOperationException (calling a member of a
model from inside an await foreach over that same model), and IOException and
UnauthorizedAccessException from the file system, unwrapped.

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


WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this package to:

  - DOWNLOAD a model. There is no registry client, no HTTP and no model store
    here: this package takes a path to a file that already exists. That is the
    separate CodeBrix.Ollama.ModelManager package, whose ResolveAsync hands
    you exactly the path this one wants.
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
  - SAVE OR RESTORE a session. There is no state serialization on the
    contract: a conversation is the messages you hold, not a blob the library
    hands back.
  - SERVE MANY CONVERSATIONS FROM ONE MODEL AT ONCE. Requests on one instance
    are serialized and the cache is one conversation deep. Several instances
    run in parallel happily; batching many sequences through one context is
    not in this version.
  - Offer synchronous APIs, or run on .NET below 10.0.

This package IS for: loading a GGUF file into your own process; completing a
prompt; holding a conversation through the model's own chat template, with
reasoning and tool calls separated out for you; embedding text; constraining
output to a grammar or a JSON schema; tokenizing; and reading everything the
engine knows about a model file.


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

A request seems to hang
    Another request on the same model is still running, and requests are
    serialized -- including a streaming one whose enumeration was never
    finished or disposed. If the waiting call is made from inside that
    enumeration you get InvalidOperationException instead.

Generation is much slower than expected
    Check GetNativeRuntimeInfo().SystemInfo for the CPU features in use and
    Devices for what the engine can see. Then check Threads (the default is
    the physical core count) and whether the machine is paging -- a model that
    does not fit runs at disk speed, not processor speed.


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
OPTIONS     ModelPath, LoadMode (MemoryMap), ContextSize, GpuLayers, Threads,
            BatchSize 2048 / PhysicalBatchSize 512, KeyCacheType /
            ValueCacheType, UseExtraBufferTypes (true), EmbeddingsMode,
            EmbeddingPooling, ChatTemplateDialect, OllamaTemplate,
            JinjaTemplate, LoadProgress
SAMPLING    Ollama's defaults: temperature 0.8, top-k 40, top-p 0.9,
            repeat penalty 1.1 over the last 64 generated tokens.
            Temperature 0 is greedy and reproducible
TEMPLATES   Auto -> OllamaTemplate, else embedded/JinjaTemplate, else Native.
            Native cannot render tools. Think is bool?: null leaves the
            template's own default
OUTPUT      Grammar > JsonSchema > JsonMode > ChatRequest.ResponseFormat
STREAMING   every update until IsFinal; the final one carries FinishReason,
            Statistics and any ToolCalls
THREADS     one worker thread per model; requests serialized; tokenize,
            detokenize and render do not wait; never call a member from
            inside that model's own await foreach
ERRORS      ModelRunnerException: NativeLibraryException, ModelLoadException,
            InferenceException, ChatTemplateException, GrammarException
MANAGER     CodeBrix.Ollama.ModelManager is a separate package, with its own
            AGENT-README, that turns a model NAME into the file path this
            package loads.


================================================================================
END OF AGENT-README
