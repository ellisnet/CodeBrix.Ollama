================================================================================
MAINTAINER-README: CodeBrix.Ollama
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING one of the NuGet packages, stop reading and open the
AGENT-README file for that package instead - README-INDEX.txt maps them.
Everything below is about the repository itself: how it is laid out, how it
builds, how it is tested, how it is packaged, where its ported code came from,
how the native libraries are produced, and the conventions the source follows.


PURPOSE AND SCOPE
=================
This repository produces exactly two NuGet packages. They are complementary and
INDEPENDENT: neither references the other, and neither is or contains an HTTP
server.

    PackageId:  CodeBrix.Ollama.ModelManager.MitLicenseForever
    Assembly:   CodeBrix.Ollama.ModelManager
    Namespace:  CodeBrix.Ollama.ModelManager  (one namespace; folders are not
                namespaces)
    Project:    src/CodeBrix.Ollama.ModelManager/
                  CodeBrix.Ollama.ModelManager.csproj
    License:    MIT
    Consumer documentation:
                src/CodeBrix.Ollama.ModelManager/AGENT-README.txt

    PackageId:  CodeBrix.Ollama.ModelRunner.MitLicenseForever
    Assembly:   CodeBrix.Ollama.ModelRunner
    Namespace:  CodeBrix.Ollama.ModelRunner
    Project:    src/CodeBrix.Ollama.ModelRunner/
                  CodeBrix.Ollama.ModelRunner.csproj
    License:    MIT
    Consumer documentation: AGENT-README.txt (repo root)

ModelRunner is the PRIMARY package, which is why its AGENT-README sits at the
repository root and ModelManager keeps its own inside its src/ project folder.
That is the family's multi-package convention; do not move either file.

ModelManager is pure managed code with zero native content: it pulls models from
any registry that speaks Ollama's manifest-and-blob protocol, keeps them in
Ollama's own on-disk blob and manifest layout, lists, shows, copies, deletes and
creates models from Modelfiles, reads GGUF metadata, and resolves a model name
to the files on disk. ModelRunner loads a GGUF file and runs it in-process over
a self-built llama.cpp engine bound through hand-written P/Invoke. An
application uses the first to get a path and hands that path to the second.

THE STATE OF THINGS, 2026-09-16
-------------------------------
  * ModelManager is written and tested.
  * ModelRunner IS NOW WRITTEN AND TESTED, as of 2026-09-16. The library is
    240 .cs files and about 29,500 lines: the hand-written P/Invoke binding
    over the native engine, two chat-template engines (a clean-room Jinja and
    a port of Go's text/template carrying Ollama's template package), the
    thinking and tool-call parsers, the JSON-schema-to-GBNF converter, and the
    inference engine behind ModelRunner.LoadAsync and IRunningModel. Its suite
    is 44 test classes and 1,258 test cases. Its AGENT-README.txt at the
    repository root is a full consumer guide and no longer a placeholder.
  * The native build tooling under llama-native-tools/ is complete, and all
    SEVEN runtime identifiers are adopted. Six were built and fully gated:
    osx-x64, osx-arm64, linux-x64, linux-arm64, linux-riscv64 and win-x64.
    win-arm64 was cross-built on the x64 Windows machine (static checks
    passed, never executed) and adopted by Jeremy's decision on 2026-09-15,
    overruling the gate rule; a native rebuild on an ARM64 machine is the
    plan if it misbehaves.
  * ONLY osx-x64 HAS EVER RUN THROUGH THE MANAGED BINDING. Everything below
    about ModelRunner's behaviour was measured on one machine, the Intel Mac
    mini; the other six natives have still never been loaded from .NET. See
    WHAT HAS NOT BEEN VALIDATED.


REPOSITORY LAYOUT
=================
    src/CodeBrix.Ollama.ModelManager/   the managed library (54 .cs files)
      Common/                    the exception hierarchy (ModelManagerException
                                 and its six subclasses) and ModelManagerJson,
                                 the shared JsonSerializerOptions that makes
                                 what this library writes byte-compatible with
                                 what Ollama reads
      Gguf/                      the GGUF reader: GgufMetadata (the public
                                 entry point), the internal GgufReader,
                                 GgufValue / GgufValueType, GgufTensorInfo /
                                 GgufTensorType / GgufTensorTypes,
                                 GgufFileType / GgufFileTypes, GgufReadOptions
      Modelfile/                 the Modelfile parser: Modelfile,
                                 ModelfileCommand, the internal
                                 ModelfileParserState and GoBool
      Names/                     ModelName, a readonly struct that parses
                                 [scheme://][host/][namespace/]model[:tag]
                                 [@digest]
      Registry/                  the HTTP side of a pull: RegistryClient,
                                 RegistryChallenge, RegistryManifestResponse,
                                 and the resumable ranged downloader
                                 BlobDownload with BlobDownloadPart and
                                 BlobDownloadState. All internal
      Store/                     the store itself: IModelStore, ModelStore,
                                 ModelStoreOptions, ModelStorePaths, the
                                 manifest / layer / config / parameters /
                                 message DTOs, MediaTypes, ManifestFiles,
                                 LayerFactory, LayerPruner, ModelLayerReader,
                                 ModelCapabilities, Sha256Digest, HumanFormat
      AGENT-README.txt           this package's consumer guide (packed)
      InternalsVisibleTo.cs      grants CodeBrix.Ollama.ModelManager.Tests

    src/CodeBrix.Ollama.ModelRunner/    the managed library (240 .cs files)
      Contracts/                 the public surface the engine implements: the
                                 static entry point ModelRunner, IRunningModel,
                                 ModelDetails, the chat types (ChatMessage,
                                 ChatRole, ChatRequest, ChatUpdate,
                                 ChatResponse, ToolDefinition, ToolCall,
                                 ResponseFormat), the completion types
                                 (GenerationUpdate, GenerationResult,
                                 GenerationStatistics, FinishReason),
                                 EmbeddingResult, NativeRuntimeInfo /
                                 NativeDeviceInfo and ModelRunnerLogLevel
      Options/                   ModelRunnerOptions, SamplingOptions,
                                 GenerationOptions, LoraAdapterOptions and the
                                 enums ModelLoadMode, FlashAttentionMode,
                                 KvCacheType, EmbeddingPooling and
                                 ChatTemplateDialect
      Common/                    the exception hierarchy: ModelRunnerException
                                 and its five subclasses
                                 NativeLibraryException, ModelLoadException,
                                 InferenceException, ChatTemplateException and
                                 GrammarException
      Native/                    the binding, 68 files and 302 [LibraryImport]
                                 declarations: NativeLibraryLoader (the
                                 resolver), the NativeMethods.* partials, the
                                 blittable structs and enums transcribed from
                                 llama.h and the ggml headers, the four
                                 SafeHandles, LlamaBatchBuffer, NativeText,
                                 NativeDefaults, NativeLog and NativeRuntime.
                                 All internal
      Templates/Jinja/           50 files: an original Jinja implementation -
                                 lexer, parser, renderer, value model, filters,
                                 tests, methods and functions - behind the
                                 public JinjaTemplate
      Templates/OllamaGo/        48 files: a port of Go's text/template (lexer,
                                 parse tree, executor, function table, and the
                                 fmt / encoding-json / strconv helpers it
                                 needs) with Ollama's template package on top,
                                 behind the public OllamaTemplate and
                                 OllamaTemplateValues
      Templates/OllamaGo/BuiltIn/  Ollama's 20 built-in .gotmpl templates,
                                 their 20 .json companions and index.json,
                                 byte for byte, embedded as assembly resources
      Parsing/                   ThinkingParser / ThinkingParserState /
                                 ThinkingTags, ToolCallParser /
                                 ToolCallParserState / ToolCallFormat, the
                                 structural GoTemplateOutline used to read a
                                 template's shape, and CompactJson
      Grammar/                   the JSON-schema-to-GBNF converter:
                                 JsonSchemaGrammar (public),
                                 JsonSchemaConverter, GrammarBuiltinRule and
                                 GrammarTrieNode
      Engine/                    25 files, all internal: RunningModel (the
                                 IRunningModel implementation), ModelEngine
                                 (the bodies behind LoadAsync / ProbeAsync),
                                 EngineWorker (the one thread every native call
                                 for a model runs on), EngineAbortFlag,
                                 ParameterMapper, ModelDetailsBuilder,
                                 EngineSamplerChain, EngineGrammar, EngineLog,
                                 PrefixCache, StopSequenceDetector,
                                 Utf8Assembler, EngineRequestScope and the chat
                                 layer (ChatPipeline, ChatTemplateStrategy,
                                 ChatTemplateRenderer, ChatJinjaVariables,
                                 ChatOllamaValues, ChatBosRule,
                                 ChatToolCallReader, ChatFunctionCallParser)
      runtimes/<rid>/native/     the COMMITTED native libraries, each beside a
                                 copy of llama.cpp's LICENSE as
                                 LICENSE-LlamaCpp.txt. Today that is all
                                 seven RIDs
      InternalsVisibleTo.cs      grants CodeBrix.Ollama.ModelRunner.Tests

    tests/CodeBrix.Ollama.ModelManager.Tests/   the xunit.v3 suite, offline
      Gguf/ Modelfile/ Names/ Registry/ Store/  27 test classes in all
      Infrastructure/            EnvGatedFactAttribute, FakeRegistryHandler,
                                 GgufTestFileBuilder, FakeModelBuilder,
                                 TempStoreDirectory
      xunit.runner.json          copied to output by an explicit csproj item

    tests/CodeBrix.Ollama.ModelRunner.Tests/    the xunit.v3 suite, offline
      Native/ Templates/Jinja/ Templates/OllamaGo/ Parsing/ Grammar/ Engine/
                                 44 test classes in all
      Infrastructure/            EnvGatedFactAttribute, TestGates, TestVectors,
                                 ModelDownloader, EngineExpectedLogits and
                                 EngineExpectedLogitRow
      Fixtures/                  real chat templates and expected renderings,
                                 copied beside the test assembly: Jinja/ (61
                                 files, ten public models' templates with their
                                 inputs and expected output, and SOURCES.txt),
                                 OllamaGo/ (61 files, Ollama's template/testdata
                                 verbatim), Grammar/ (148 files, llama.cpp's
                                 schema-to-grammar cases) and Parsing/ (2)
      xunit.runner.json          copied to output by an explicit csproj item
      test-vectors/              the conformance model and EXPECTED.txt, LINKED
                                 from llama-native-tools rather than copied

    llama-native-tools/          everything needed to build the native
                                 libraries. Nothing here is compiled by a
                                 `dotnet build`; see its own README.txt

    CodeBrix.Ollama.slnx         the solution. Its Solution Items folder carries
                                 .gitignore, AGENT-README.txt,
                                 EXTRAS-README.txt, global.json,
                                 icon-codebrix-128.png, LICENSE,
                                 MAINTAINER-README.txt, README-INDEX.txt,
                                 README.md and THIRD-PARTY-NOTICES.txt; its
                                 Tests folder carries both test projects; the
                                 two packable projects sit at the top level

THE FLAT-NAMESPACE RULE - DO NOT "FIX" IT
------------------------------------------
Every public and internal type in ModelManager declares `namespace
CodeBrix.Ollama.ModelManager;`, and every file in its test project declares
`namespace CodeBrix.Ollama.ModelManager.Tests;`. ModelRunner does exactly the
same with `namespace CodeBrix.Ollama.ModelRunner;` and `namespace
CodeBrix.Ollama.ModelRunner.Tests;` - all 240 library files and all 55 test
files, nine folders deep though the library is. The folders above are FILE
ORGANIZATION ONLY.

This is deliberate and load-bearing: the public API is a single using directive
for a consumer, which is what the AGENT-READMEs promise. Do not add
folder-scoped namespaces to this repository. One type per file, named after the
type.


BUILDING
========
Standard SDK build from the repository root:

    dotnet restore CodeBrix.Ollama.slnx
    dotnet build   CodeBrix.Ollama.slnx -c Release

Target framework: net10.0 only, LangVersion latest, on all four projects.
ModelRunner additionally sets AllowUnsafeBlocks - load-bearing for the P/Invoke
layer, where 36 of Native/'s 68 files and 7 of Engine/'s 25 use pointers - and
ModelRunner.Tests matches it. ModelRunner also carries an EmbeddedResource item
over Templates\OllamaGo\BuiltIn\**, which is how Ollama's 41 built-in template
files come to travel inside the assembly.

BOTH LIBRARIES HAVE ZERO PackageReference ENTRIES. Verify that after any change.
It is a contract with consumers, not a preference (see CODING CONVENTIONS), and
the packed nuspec is where it is checked - see PACKAGING AND PUBLISHING.

GenerateDocumentationFile is ON for both libraries, so every public member must
carry an XML doc comment. Fix CS1591 at the source; there is no <NoWarn>
anywhere in this repository and none is to be added, and never suppress it
inline.

The Release build is 0 warnings / 0 errors and must stay that way; pass a single
csproj path instead of the .slnx to build one project on its own.

A `dotnet build` NEVER COMPILES C. The native libraries are built by
llama-native-tools/ on a machine that can build them, and committed. Nothing is
downloaded at build time.


TESTING
=======
    tests/CodeBrix.Ollama.ModelManager.Tests -- xunit.v3 4.0.1,
    Microsoft.NET.Test.Sdk 18.10.1, xunit.runner.visualstudio 4.0.0 and
    SilverAssertions.ApacheLicenseForever 1.0.248.1071. 27 test classes,
    332 test members, 789 test cases (the [Theory] members contribute 511
    [InlineData] rows between them).

    tests/CodeBrix.Ollama.ModelRunner.Tests -- the same four packages at the
    same versions. 44 test classes, 481 test members, 1,258 test cases: 391
    [Fact] or [EnvGatedFact] members, 87 [Theory] members carrying 752
    [InlineData] rows between them, and 3 more [Theory] members whose
    [MemberData] enumerates a fixture folder and contributes 115 rows. 30 of
    the members are [EnvGatedFact] and are skipped unless their variable is
    set.

BOTH SUITES ARE OFFLINE BY DEFAULT. Neither needs a daemon, a server, a model
file or a network: ModelManager's runs in under two seconds on a warm machine
and ModelRunner's in under three, and ModelRunner's three seconds include
loading the tiny conformance model through the real native library and checking
the logits it produces. The tests that are exceptions are gated - see THE
ENVIRONMENT GATE below.

HOW TO RUN IT
-------------
    dotnet test CodeBrix.Ollama.slnx

RUNNER NOTE: the family records that on SDK 10.0.400 `dotnet test` can report
ZERO TESTS for an xunit.v3 project. It did not happen here - on SDK 10.0.401
`dotnet test` reported all 789 - but running the built entry point directly is
still the better habit while iterating. It is a normal executable, it gives
clean per-class counts, and it takes filters:

    dotnet build tests/CodeBrix.Ollama.ModelManager.Tests -c Release
    tests/CodeBrix.Ollama.ModelManager.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelManager.Tests
    tests/CodeBrix.Ollama.ModelManager.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelManager.Tests \
        -class CodeBrix.Ollama.ModelManager.Tests.ModelNameTests

and the same for the other suite:

    dotnet build tests/CodeBrix.Ollama.ModelRunner.Tests -c Release
    tests/CodeBrix.Ollama.ModelRunner.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelRunner.Tests
    tests/CodeBrix.Ollama.ModelRunner.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelRunner.Tests \
        -class CodeBrix.Ollama.ModelRunner.Tests.JinjaFixtureTests

`-list classes` on either executable prints the class names, which is the
quickest way to check a count in this file against the tree.

global.json at the repository root sets "test": { "runner":
"Microsoft.Testing.Platform" } and nothing else - no SDK pin. MSBuild finds
it by walking up.

xunit.runner.json IS COPIED TO OUTPUT BY AN EXPLICIT CSPROJ ITEM in both test
projects:

    <None Update="xunit.runner.json" CopyToOutputDirectory="PreserveNewest" />

Without that item the file stays beside the source and the runner never sees it.
(CodeBrix.Docker carries an xunit.runner.json that is NOT copied, and is
therefore inert there. This repository does not repeat that.) What it configures
is parallelism: parallelizeAssembly false, parallelizeTestCollections false,
maxParallelThreads 1, because tests may share on-disk state. Keep it serial.

THE ENVIRONMENT GATE
--------------------
THREE VARIABLES, read by each test project's own
Infrastructure/EnvGatedFactAttribute.cs - a FactAttribute subclass that sets
Skip unless the variables it names hold the expected value - and, in
ModelRunner.Tests, named as consts in Infrastructure/TestGates.cs:

    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1    the live tests in BOTH suites
    CODEBRIX_OLLAMA_RUN_QWEN35_TESTS=1  the Qwen 3.5 class, on top of that
    CODEBRIX_OLLAMA_TEST_MODEL_DIR      where ModelRunner's live tests cache
                                        the model files they download

The first two are gates and take the value "1". The third is not a gate: it
only moves the cache. Unset, the cache is

    <LocalApplicationData>/CodeBrix.Ollama/test-models

which is ~/.local/share/CodeBrix.Ollama/test-models on macOS and Linux alike -
.NET maps LocalApplicationData to the XDG path on every Unix, macOS included,
NOT to ~/Library/Application Support - and
%LOCALAPPDATA%\CodeBrix.Ollama\test-models on Windows.

IN ModelManager.Tests the first variable gates EXACTLY ONE test -
ModelStoreLiveTests.PullAsync_FromTheRealRegistry_DownloadsResolvesListsAnd
DeletesTheModel. Nothing else in that suite is gated. The default run is
therefore 789 total / 788 passed / 1 skipped; with the gate open it is
789 / 789 / 0.

THAT TEST REALLY DOWNLOADS. It pulls smollm:135m - about 92 MB - from
registry.ollama.ai into a fresh TempStoreDirectory, asserts the progress stream
starts at "pulling manifest" and ends at "success", resolves the model, reads
its GGUF metadata back, checks the resolved file's length against the manifest
layer's size, lists it, deletes it and confirms the blobs directory is empty
again.

IN ModelRunner.Tests 30 members are gated, in three classes:

    SmolLmLiveTests        14   probe, load, tokenize round trip, completion,
                                streaming, stop sequences, grammars, the
                                prefix cache, embeddings, cancellation
    SmolLmChatLiveTests     7   the chat layer: both dialects, streaming,
                                thinking, tools, JSON response format, cache
                                reuse on a second turn
    Qwen35LiveTests         9   the 35B hybrid MoE: probe, a one-word answer,
                                reasoning, tool calling, a tool result handed
                                back, and the UseExtraBufferTypes comparison

The first two classes need only CODEBRIX_OLLAMA_RUN_LIVE_TESTS; Qwen35LiveTests
needs BOTH gates. So the three runs are:

    no variables set     1258 total / 1228 passed / 30 skipped   (~2.6 s)
    LIVE only            1258 total / 1249 passed /  9 skipped
    LIVE and QWEN35      1258 total / 1258 passed /  0 skipped

WHAT THE LIVE TESTS DOWNLOAD, through Infrastructure/ModelDownloader.cs, into
the cache directory above:

    smollm-360m-instruct-add-basics-q8_0.gguf     386,405,440 bytes (368 MiB)
        https://huggingface.co/HuggingFaceTB/smollm-360M-instruct-v0.2-Q8_0-GGUF
        /resolve/main/smollm-360m-instruct-add-basics-q8_0.gguf
        sha256 b5a2e94a0be8c047bccc3f52bc2f27b79b0dbc688ef3102de54fa6a33b20198c

    Qwen3.5-35B-A3B-Q4_K_M.gguf                22,016,023,168 bytes (20.5 GiB)
        https://huggingface.co/unsloth/Qwen3.5-35B-A3B-GGUF/resolve/main/
        Qwen3.5-35B-A3B-Q4_K_M.gguf
        sha256 3b46d1066bc91cc2d613e3bc22ce691dd77e6f0d33c9060690d24ce6de494375

The downloader is resumable: bytes go into a <name>.partial file beside the
target and a restart asks for a byte range starting where that file ends, so an
interrupted 20 GB transfer is not repeated. A file ALREADY in the cache is
checked BY SIZE ONLY - hashing twenty gigabytes takes minutes and would
dominate every run - and the sha256 above is verified exactly once, right after
a fresh download, which is when a truncated transfer would show. A cached file
of the wrong size is an error telling you to delete it, never a silent
re-download.

TIMINGS ON THE INTEL MAC MINI (six cores, 32 GiB, CPU only). The two SmolLM
classes add a few seconds once the file is cached. Qwen35LiveTests loads the
20 GB file ONCE for the whole class through Engine/Qwen35ModelFixture.cs and
takes about 150 seconds with the file warm in the page cache - about 31 to 44
seconds of that is the load itself, and about 86 seconds if the file is cold.
Do not put a test timeout on that class.

THE DEFAULT SUITES DOWNLOAD NOTHING. The family's "nothing downloaded at test
time" rule therefore applies TO THE DEFAULT SUITES ONLY, and this is the
deliberate deviation: downloading models is what ModelManager is for and
running them is what ModelRunner is for, so libraries never once exercised
against a real registry and a real model are libraries whose central promise is
untested.

TREAT THE GATED RUNS AS PART OF A RELEASE CHECK, NOT AN OPTIONAL EXTRA.

THE TEST INFRASTRUCTURE
-----------------------
ModelManager.Tests has FIVE pieces under Infrastructure/, and each exists so
that a whole tier can be tested without a network or a real model:

  EnvGatedFactAttribute.cs   the gate above. It takes the variable name and the
                             expected value (default "1") and forwards the
                             compiler-supplied file path and line number to
                             FactAttribute, so a gated test keeps its location.

  FakeRegistryHandler.cs     an in-memory Docker-distribution v2 registry behind
                             an HttpMessageHandler: manifests by
                             <namespace>/<model>:<tag>, blobs by digest with
                             full byte-range support (206, Content-Range, 416),
                             an optional bearer challenge, an optional redirect
                             of blob requests to a second host standing in for a
                             content delivery network, and per-attempt failure
                             injection for any range (drop after n bytes, stall
                             until cancelled, or answer a chosen status). Every
                             request is recorded. It reaches the library through
                             the PUBLIC ModelStoreOptions.HttpMessageHandler
                             surface - there is no test-only hook.

  GgufTestFileBuilder.cs     builds small synthetic GGUF files in memory:
                             header, key-values, tensor descriptors and a
                             tensor-data section padded to exactly the right
                             length. It also writes version 1 (32-bit counts,
                             null-terminated strings) and the big-endian form
                             whose magic reads FUGG, which is how the reader's
                             error paths are reached without a real model.

  FakeModelBuilder.cs        composes a complete fake model - a small GGUF, the
                             text and JSON layers that go with it, a config and
                             a manifest - and registers all of it in a
                             FakeRegistryHandler, so a store test can pull what
                             looks exactly like a real model.

  TempStoreDirectory.cs      a unique empty store directory under the system
                             temporary directory, removed on Dispose, with a
                             ModelStorePaths already rooted at it. Every store
                             test works inside one. Its Dispose swallows
                             IOException and UnauthorizedAccessException: a
                             leftover directory must never fail a test.

ModelRunner.Tests has SIX, and the rule they follow is the same one: the
offline suite must be able to exercise every tier without a network and without
a real model, and the live tests must not pay for anything twice.

  EnvGatedFactAttribute.cs   the same idea as ModelManager's, in ModelRunner's
                             own namespace, and it takes ONE OR MORE variable
                             names and skips unless every one of them is "1" -
                             which is how the Qwen class asks for both gates at
                             once. It is duplicated rather than shared because
                             the two test projects do not reference each other
                             and neither library may grow a dependency to let
                             them.

  TestGates.cs               the three environment variable NAMES as consts,
                             so a gate is never spelled out in a test file.

  TestVectors.cs             the paths of the linked conformance assets beside
                             the test assembly - test-vectors/
                             codebrix-conformance-tiny.gguf and EXPECTED.txt.

  ModelDownloader.cs         the resumable ranged downloader and the model
                             cache described under THE ENVIRONMENT GATE.
                             Nothing outside the gated tests calls it.

  EngineExpectedLogits.cs    the parser for EXPECTED.txt and one row of it.
  EngineExpectedLogitRow.cs  Both the raw-native conformance test and the
                             one that goes through the whole managed engine
                             compare against the same parsed rows.

Beside them, Fixtures/ holds the data the offline suite runs on, copied to the
output folder by one csproj Content glob:

  Fixtures/Jinja/       ten public models' chat templates, each with one or
                        more <name>.<case>.input.json / .expected.txt pairs -
                        60 files - plus SOURCES.txt. THAT FILE IS THE PROVENANCE
                        RECORD FOR ALL OF THEM: for every template it names the
                        Hugging Face repository, the file fetched
                        (chat_template.jinja first, tokenizer_config.json's
                        "chat_template" member second), the commit the resolve
                        URL redirected to, and the repository's licence. It
                        also lists what was fetched and NOT kept and why - a
                        gated repository, a licence that is neither Apache-2.0
                        nor MIT - and what each template exercises. Entries 8
                        to 14 of THIRD-PARTY-NOTICES.txt are compiled from it;
                        if a template is ever added here, that file gains an
                        entry in the same commit.
  Fixtures/OllamaGo/    Ollama's template/testdata, verbatim: 20 <name>.gotmpl
                        FOLDERS of three expected renderings each, plus
                        templates.jsonl - 61 files.
  Fixtures/Grammar/     llama.cpp's schema-to-grammar cases written out as
                        files: 73 .schema.json / .expected.gbnf pairs and two
                        schemas that must be refused - 148 files.
  Fixtures/Parsing/     the two templates the thinking and tool-call parsers
                        are pinned against, qwen3.gotmpl and qwen3-tools.jinja.

Two helpers read those folders: Templates/Jinja/JinjaFixtureLoader.cs (which
also turns a case's JSON into the Jinja engine's own value model, so that
| tojson reproduces the file's key order) and Templates/OllamaGo/
OllamaTemplateFixtures.cs. The Jinja cases are named one by one in
[InlineData] rows; the three OllamaGo [MemberData] members enumerate their
folder instead, so adding a fixture there adds test cases with no code change.

Engine/SmolLmModelFixture.cs and Engine/Qwen35ModelFixture.cs are the live
suites' one-load fixtures. Neither does anything in its constructor: a gated
class that never runs downloads nothing and loads nothing. The Qwen fixture
also installs a log handler around the load so the class can report the buffer
sizes the engine settled on, and removes it afterwards.

THE LINKED CONFORMANCE MODEL
----------------------------
ModelManager.Tests LINKS, rather than copies, llama-native-tools/test-vectors/
codebrix-conformance-tiny.gguf into its output as test-vectors/. It is the SAME
FILE that every native build regenerates and checks byte for byte, and the same
file CodeBrix.Ollama.ModelRunner.Tests links (with EXPECTED.txt) to run its
logits against. One asset, three consumers, so the managed GGUF reader, the
native gate and the managed binding can never drift on to different files. Do
not copy it into either test project.

ModelRunner.Tests uses it TWICE, which is the point of having it: NativeConfor-
manceTests drives the raw P/Invoke layer with it, and RunningModelConformance-
Tests drives the whole engine - load, decode, logits - through
ModelRunner.LoadAsync with it. Both compare against EXPECTED.txt within the
same tolerance the native gate uses. A model with 64 vocabulary entries, 32
embedding dimensions and 2 layers cannot tokenize anything, so the managed test
feeds the 12 conformance token ids straight in; that is what
RunningModel.GenerateFromTokensAsync exists for, and it is internal.


PACKAGING AND PUBLISHING
========================
GeneratePackageOnBuild is true on both libraries, so every build emits a fresh
.nupkg. To pack deliberately:

    dotnet pack src/CodeBrix.Ollama.ModelManager/\
CodeBrix.Ollama.ModelManager.csproj -c Release -o <dir>

VERSIONING is the CodeBrix date-stamped scheme, computed in each csproj from
System.DateTime.UtcNow as

    1.<whole years since _VersionBaseYear>.<day of year>.<minute of day UTC>

with _VersionBaseYear = 2026, and assigned to Version, AssemblyVersion and
FileVersion alike. Math.Floor on the minutes keeps the last field in 0..1439.
It is monotonically increasing but it is NOT SemVer: major is pinned to 1 and
minor encodes the year, so neither says anything about API compatibility. Two
builds inside the same UTC minute produce the SAME version - never publish two
packages from one minute. To re-baseline the minor number, change
_VersionBaseYear. Do not replace the version block with a literal <Version>.

Both csproj files carry the identical commented block; keep it that way.

WHAT SHIPS INSIDE EACH NUPKG, declared as <None ... Pack="true" PackagePath="">:

    icon-codebrix-128.png    (PackageIcon, from the repo root)
    README.md                (PackageReadmeFile, from the repo root)
    AGENT-README.txt         (that package's own consumer guide - the root one
                             for ModelRunner, the src/ one for ModelManager)
    THIRD-PARTY-NOTICES.txt  (from the repo root)
    LICENSE                  (from the repo root)

MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt are repo-only and
are NOT packed. Neither is anything under tests/ or llama-native-tools/.

PackageLicenseExpression is MIT and PackageRequireLicenseAcceptance is true on
both.

THE EMPTY DEPENDENCY GROUP IS A RELEASE CHECK. After packing, confirm each
nuspec still carries

    <dependencies><group targetFramework="net10.0" /></dependencies>

If a dependency ever appears in either one, something added a PackageReference
to a library project and the zero-dependency promise is broken.

THE NATIVE PACKING BLOCK IN ModelRunner - CHECK THE PACKAGE AFTER ANY CHANGE
---------------------------------------------------------------------------
ModelRunner.csproj carries the Dav1d-style packing block, and it has three parts
that only work together:

  1. DefaultItemExcludes adds runtimes\**. Without it the committed natives
     arrive as None items with no metadata AS WELL AS through the explicit items
     below, the metadata-less copies win, and nothing reaches the package.

  2. A _LlamaNativeLibrary item over runtimes\**\* names them once, and

         <None Include="@(_LlamaNativeLibrary)" Pack="true"
               PackagePath="runtimes\" />

     packs them. THE PACKAGE PATH IS THE BARE "runtimes\" FOLDER. NuGet appends
     the item's own %(RecursiveDir) and file name to a package path that names a
     folder, so spelling %(RecursiveDir) out as well produces
     runtimes/linux-x64/native/linux-x64/native/libcodebrix_llama.so.

  3. A target named CodeBrixOllamaModelRunnerStageNativeLibraries, running
     BeforeTargets="AssignTargetPaths", adds the same items as
     ContentWithTargetPath with TargetPath runtimes\%(RecursiveDir)%(Filename)
     %(Extension) and CopyToOutputDirectory PreserveNewest. That copies them
     into this project's output AND into the output of anything that references
     the project, keeping the runtimes/<rid>/native/ layout, so the test suite
     exercises exactly the layout the library's own probing will have to find
     rather than a flattened one that could never fail. It goes through
     ContentWithTargetPath and not a second Content item because pack collects
     None and Content together and de-duplicates by identity: a Content item for
     the same file would silently win and the natives would vanish from the
     package.

AFTER ANY CHANGE TO THAT BLOCK, UNZIP THE PACKAGE AND LOOK (`unzip -l <nupkg> |
grep runtimes`). What you want to see is runtimes/<rid>/native/ with no doubled
path - today, exactly two entries under each of the seven RIDs'
runtimes/<rid>/native/: the library (libcodebrix_llama.dylib, .so or
codebrix_llama.dll) and LICENSE-LlamaCpp.txt. Checked 2026-09-15 on Windows
after adopting win-x64 and win-arm64, and again on 2026-09-16 on the Intel Mac
mini after the ModelRunner build-out: fourteen entries both times, no doubled
path, lib/net10.0 carrying the assembly and its XML documentation, the five
packed root files present, and the nuspec's net10.0 dependency group still
empty.


THE NATIVE LIBRARIES
====================
The natives are built ONLY by llama-native-tools/, NEVER by a dotnet build, and
the built files are committed to the repository. The rule that folder exists for
is Jeremy's: everything needed to rebuild them a year from now, with no other
repository available, must live inside that folder - source, headers, scripts,
the smoke test and the conformance assets. The only things outside it are the
tools installed on the build machine (cmake, ninja, the compilers, a container
engine) and, on Linux, the digest-pinned container images. The Linux build
proves the rule by running with --network none and printing the interface
list.

ADOPTION is a copy of the built library plus a verbatim copy of llama.cpp's
LICENSE, named LICENSE-LlamaCpp.txt, into

    src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/

with the pre-strip twin going to llama-native-tools/unstripped/<rid>/
(xz-compressed on Linux, where the raw twin is 80-115 MB; its SHA256SUMS
extended) and a BUILD-PROVENANCE.txt entry written from that build's
output/<rid>/BUILD-INFO.txt, all together.

The shipped file names are unversioned and package-unique on purpose -
libcodebrix_llama.dylib, libcodebrix_llama.so, codebrix_llama.dll - because a
plain "llama" name would collide with LLamaSharp's or a stock Ollama's llama.dll
on a user's PATH. The per-native licence file name is package-unique for the
same kind of reason: these files land in a consuming application's OUTPUT
FOLDER, where a file named plainly LICENSE collides with any other package that
ships one there.

WHICH RIDS EXIST TODAY, per llama-native-tools/BUILD-PROVENANCE.txt: OSX-X64
and OSX-ARM64, both built, gated and adopted on 2026-09-15 - x64 on the Intel
Mac mini (CPU only), arm64 on the Apple Silicon Mac mini (CPU + Metal). Both
carry a macOS floor of 13.3. The arm64 run found that the original 11.0 floor
was never real - both slices weak-imported a 13.3-only Accelerate symbol and
would have crashed at the first BLAS matmul on macOS 11.0 to 13.2 - so the pin
was raised, the wrapper now fails the build on any unguarded use of an API
newer than the floor, and the x64 slice was rebuilt and re-adopted the same
day. LINUX-X64, LINUX-ARM64 and LINUX-RISCV64 were built, gated and adopted
the same evening on the x86_64 LMDE laptop through the manylinux container
route (x64 natively; arm64 and riscv64 under qemu-user emulation, the way
dav1d's were), with glibc floors of 2.27, 2.27 and 2.38 and only glibc's own
libraries as dependencies. Their first runs found three things to fix, none of
them in the build scripts: the wrapper's Linux link carried
-Wl,--exclude-libs,ALL, which on ELF hides every archive symbol and defeated
the version script; the aarch64 Containerfile's toolchain probe expected the
wrong dot-product value; and the riscv64 image (Rocky Linux 10, system gcc)
lacked the static libstdc++ that its AlmaLinux siblings get from
gcc-toolset-14. Each fix is in the file it belongs to and recorded in
BUILD-PROVENANCE.txt. Nothing has yet run the arm64 or riscv64 files on real
hardware. WIN-X64 was built, gated and adopted the same night on the Windows
11 x64 machine (Visual Studio 2026, MSVC 14.51, static CRT, AVX2 baseline;
dependencies KERNEL32 and ADVAPI32 only). Its scripts, written on the Intel
mini, found five things on their first real runs, as dav1d's had: the gate's
export parser, the compiler-banner capture, the clang-cl host directory, the
allowed-dependents list, and - the substantive one - the EXPORT SURFACE:
dllexport through upstream's LLAMA_API / GGML_API macros also exported 22
internal C++ functions, which the Unix pattern lists never match, so on
Windows the wrapper now generates a .def file from the built archives
(llama-native-tools/wrapper/exports-windows.cmake). Every fix is in the file
it belongs to and recorded in BUILD-PROVENANCE.txt. WIN-ARM64 was
CROSS-BUILT on that x64 machine (clang-cl targeting aarch64-pc-windows-msvc),
passed the three static checks, and by design could not run the smoke test,
model regeneration or conformance there. Jeremy adopted it anyway on
2026-09-15, overruling the gate rule, so it is the one shipped native that
has NEVER BEEN EXECUTED; BUILD-PROVENANCE.txt says so in capitals. The plan
if it misbehaves is a native run of windows\build-win-arm64.ps1 on an ARM64
Windows machine, which runs the whole gate and replaces the cross build.

EVERYTHING ELSE ABOUT THE NATIVES LIVES IN THAT FOLDER, and is not duplicated
here on purpose:

    llama-native-tools/README.txt
        the rule and how it is enforced, the folder map, the seven-RID table,
        the vendored commit, which per-platform README to read, and a
        one-minute tour.
    llama-native-tools/BUILD-PROVENANCE.txt
        per-RID record of what was built, when, by what, with hashes, gate
        results and reproducibility notes.
    llama-native-tools/test-vectors/README.txt
        the synthetic conformance model, why the check is a tolerance and not a
        hash, and how the reference logits were established.

Three facts from those files are worth knowing before you touch anything here.
The conformance model is NOT third-party content - it is random weights from a
fixed seed generated in-repo - and it is deliberately absent from
THIRD-PARTY-NOTICES.txt, because listing it there would be a false statement
about its origin. The osx-x64 slice is CPU-only by decision AND by necessity: at
the vendored commit, ggml-metal returned NaN for every logit on that machine's
Intel UHD 630. And osx-x64 is reproducible EXCEPT for its LC_UUID, so a rebuild
legitimately produces a different sha256 - compare size, exports and gate
result, not the hash; the Linux slices behave the same way, differing between
runs only in the build timestamp baked into codebrix_llama_build_info() and the
GNU build-id derived from it. The macOS floor is a symbol question, not a stamp: the
minos value says where dyld will load the file, and only the compile-time
availability check proves that every symbol it calls exists there.


PROVENANCE AND PORTED SOURCES
=============================
CodeBrix.Ollama.ModelManager IS A PORT of parts of the Ollama server: Go source,
MIT licensed, Copyright (c) Ollama, at commit a43fad18 (full hash
a43fad18b088095de20fbd7a8f0de50824cf5d27), which `git describe` calls
v0.34.2-rc0, dated 2026-09-15.

Nothing from Ollama is vendored, compiled, linked or downloaded. What was taken
is LOGIC: a set of Go files was read and re-expressed by hand as C#, organised
around this library's own public API and using the base class library
throughout.

EVERY PORTED FILE CARRIES THE FAMILY'S PROVENANCE MARKER ON ITS NAMESPACE LINE:

    namespace CodeBrix.Ollama.ModelManager; //was previously: ollama/ollama <upstream path>;

There is no banner comment above the using block - ported files follow the same
top-of-file layout as every other file (Ollama's Go sources carry no per-file
licence header, so there is nothing to preserve verbatim), and the licence and
copyright attribution live in THIRD-PARTY-NOTICES.txt, not in each file.
27 files in the library carry the marker today, naming types/model/name.go, the
five fs/gguf files, parser/parser.go with api/types.go, the three manifest/
files, server/images.go, server/create.go, server/model.go,
x/create/manifest.go, server/download.go and format/format.go. Eleven files in
the test project carry it too, naming the upstream *_test.go the case tables
came from (or the source file whose tables they exercise).

FILES WITH NO OLLAMA LOGIC CARRY NO MARKER, and that is not an oversight: the
exception types and the JSON helper under Common/, the public DTOs and options
under Store/, Modelfile/GoBool.cs and Registry/RegistryManifestResponse.cs. The
on-disk FORMAT those DTOs describe is Ollama's, kept identical on purpose so a
store directory is interchangeable with a real Ollama install - but the C# is
this repository's.

THIRD-PARTY-NOTICES.txt at the repository root holds the full attribution: the
upstream-file-to-our-file scope list from which the marker list above was
compiled, the modifications made during the port, and Ollama's MIT licence
verbatim. It also covers llama.cpp and the licences that appear in the vendored
snapshot, and everything ModelRunner ported. As of 2026-09-16 it carries
FOURTEEN numbered entries: 1 llama.cpp and ggml, 2 Intel's SYCL and OpenVINO
backends, 3 Ollama, 4 LLamaSharp (read, not copied), 5 the Go standard library,
6 agnivade/levenshtein, 7 the Jinja project (read, not copied), and 8 to 14 the
chat-template fixtures by licensor - Alibaba/Qwen, HuggingFaceTB, Mistral AI,
Microsoft, DeepSeek, IBM and the Allen Institute for AI. Extend it whenever you
port anything further; if you rewrite a file until nothing of the original
remains, remove the marker rather than leave a false attribution.

THE REFERENCE CLONE lives at ~/GitHome/ollama, at that same commit. It is a
REFERENCE ONLY - not vendored, not a submodule, and there is deliberately no
NuGet reference to anything Ollama. Read it beside the C#; do not copy it into
the tree.

WHAT ModelRunner PORTED, AND FROM WHOM
--------------------------------------
ModelRunner takes code from four upstreams and reads three more without taking
anything. 129 of its 240 files carry the marker; the marker names the upstream
project first, so a grep over src/CodeBrix.Ollama.ModelRunner is the index:

    grep -rh 'was previously:' src/CodeBrix.Ollama.ModelRunner | sort | uniq -c

  llama.cpp (MIT, ggml-org/llama.cpp, commit 815a2a5915f22ce6a760c676389c5dfe
  8535c08f - the same commit the vendored snapshot is at). Native/ transcribes
  include/llama.h and ggml/include/{ggml.h, ggml-backend.h, ggml-cpu.h,
  ggml-opt.h, gguf.h} into C# structs, enums and LibraryImport declarations -
  declarations only, no algorithm. Grammar/ is a real port of
  common/json-schema-to-grammar.cpp, rule for rule, so the GBNF this library
  writes for a schema is the GBNF llama.cpp writes; grammars/json.gbnf is the
  verbatim JsonSchemaGrammar.JsonGrammar literal; and tests/test-json-schema-
  to-grammar.cpp supplied Fixtures/Grammar. Engine/EngineSamplerChain.cs takes
  the ORDER of the sampler chain from common/sampling.cpp and nothing else.
  Everything except the headers was read in a separate clone, because common/,
  grammars/ and tests/ are not in the vendored subset.

  The Go standard library (BSD-3-Clause, Copyright (c) 2009 The Go Authors,
  tag go1.25.0). Templates/OllamaGo/ is a faithful port of text/template:
  text/template/parse/{lex,node,parse}.go, text/template/{exec,funcs,template,
  option}.go, and the helpers those need - fmt/{print,format}.go as
  GoFormat.cs, encoding/json/encode.go as GoJson.cs, strconv/quote.go as
  GoQuote.cs. It exists because Ollama's templates are Go templates and
  nothing in .NET runs them.

  Ollama (MIT, Copyright (c) Ollama, commit a43fad18, the same commit
  ModelManager ports). template/template.go and the tool and message shapes of
  api/types.go became OllamaTemplate, OllamaNamedTemplate, OllamaTemplateValues,
  OllamaTemplateBuiltIns, OllamaTemplateBinder and OllamaTemplateFuncs;
  thinking/parser.go and
  thinking/template.go became Parsing/ThinkingParser, ThinkingParserState and
  ThinkingTags; tools/tools.go and tools/template.go became
  Parsing/ToolCallParser, ToolCallParserState and ToolCallFormat. Ollama's 20
  built-in .gotmpl templates, their 20 .json companions and index.json are
  COPIED BYTE FOR BYTE into Templates/OllamaGo/BuiltIn/ and embedded as
  assembly resources, so they ship inside the package; template/testdata (61
  files) and the Qwen 3 template written out inside thinking/template_test.go
  are copied into the test project only.

  agnivade/levenshtein (MIT) - one function, Templates/OllamaGo/
  GoLevenshtein.cs, because Ollama's template.Named matches a model's embedded
  template to a built-in by edit distance and the threshold is part of the
  behaviour.

  LLamaSharp (MIT, Copyright (c) 2025 SciSharp STACK, commit 59dc9752) was
  READ AND NOT COPIED. Six of its files - NativeLogConfig.cs,
  SafeLlamaModelHandle.cs, SafeLLamaContextHandle.cs, SafeLLamaSamplerHandle.cs,
  LoraAdapter.cs, LLamaBatch.cs - were consulted as a second opinion on struct
  field order, the SafeHandle shape, the negative-return resize protocol and
  the newline-buffering of the log callback. Every file that consulted them
  says so with a "(reference only)" marker. No line of its code is here, and
  there is no LLamaSharp package reference anywhere.

  The Jinja project (Pallets, BSD-3-Clause) was READ AND NOT COPIED.
  Templates/Jinja/ is an ORIGINAL implementation of the Jinja template
  language, written against Jinja's published documentation and against the
  fixture templates it has to render, plus CPython's documented str / list /
  dict behaviour for the methods those templates call. Its 50 files carry NO
  marker, and that is correct - do not add one.

  nlohmann/json was consulted for the exact escaping its dump() performs, so
  that Parsing/CompactJson.cs writes what llama.cpp's grammar converter writes.
  Reference only, marked as such.

  TWO MARKERS NAME THIS FAMILY'S OWN CODE and are not third-party attribution:
  Native/NativeLibraryLoader.cs names CodeBrix.VideoPlayback.Dav1d's
  Interop/Dav1dLibrary.cs, whose loader structure it copies, and
  Native/NativeMethods.CodeBrix.cs names this repository's own
  llama-native-tools/wrapper/codebrix_llama.c. Both are deliberately absent
  from THIRD-PARTY-NOTICES.txt, for the same reason the conformance model is.

NO ModelRunner TEST FILE CARRIES A MARKER, though four of them are ports of
Ollama's thinking and tools test tables. That is a known gap that was closed in
prose instead: the paragraph in THIRD-PARTY-NOTICES.txt entry 3 naming
ThinkingParserTests, ThinkingTagsTests, ToolCallParserTests and
ToolCallFormatTests IS the record for them. If you add markers to those four
later, that paragraph can go.

THE DELIBERATE DEVIATIONS FROM OLLAMA
-------------------------------------
All of them are recorded in THIRD-PARTY-NOTICES.txt under MODIFICATIONS MADE
DURING THE ModelManager PORT, and most of them again in the XML <remarks> at
the place they happen. They are decisions, not omissions, and they are the
ones most likely to be "fixed" by mistake.

  - OUR OWN DOWNLOAD SIDECARS. A blob being downloaded is written to
    <blob>.codebrix-partial with one JSON document at <blob>.codebrix-parts.json
    recording which byte ranges have landed. Ollama uses <blob>-partial and one
    <blob>-partial-<n> file per part. The ".codebrix-" segment is what keeps the
    two downloaders off each other's files when this library and a real Ollama
    install share ~/.ollama/models. Ollama's own partial files are never read.

  - A NARROWED PRUNE. Ollama's PruneLayers deletes every file in blobs/ whose
    name is not a digest. LayerPruner does not, because the directory may belong
    to a real Ollama install whose sidecars would then vanish underneath it.
    Only this library's own leftovers are cleaned up, and they are not reported
    in the returned digest list because they are not blobs.

  - NO PROCESS-WIDE DOWNLOAD DE-DUPLICATION. Ollama keeps a package-level
    blobDownloadManager (a sync.Map with acquire/release reference counting) so
    two concurrent pulls of the same blob share one download. That, and the
    sparse-file hint, were not ported. One call downloads one blob; a
    BlobDownload instance is used once.

  - NO ed25519 REGISTRY AUTHENTICATION. Ollama's auth package signs a registry
    challenge with the user's ed25519 key. That is not ported. What IS ported is
    the bearer-token retry: ModelStoreOptions.BearerToken is sent as Bearer
    ONLY after a registry answers 401 with a challenge, never before one, and
    never to a host a download was redirected to. Public models need no
    credentials at all.

  - NO SAFETENSORS. A manifest carrying a safetensors tensor layer is refused by
    PullAsync with a message naming the model, and safetensors import is out of
    CreateAsync (Ollama converts through Python).

  - NO DRAFT CREATE. A Modelfile with a DRAFT command is refused by CreateAsync.
    A draft model already in a manifest is still resolved, reported as
    ResolvedModel.DraftPath and rendered back as a DRAFT line by ShowAsync; it
    is only creation that is refused.

  - NO TEMPLATE RENDERING AND NO TEMPLATE AUTO-DETECTION. ModelManager stores
    and returns template TEXT exactly as written. Ollama's built-in template
    auto-detection (template.Named) and its Go-template validation are not
    ported, and rendering belongs to ModelRunner. The consequence reaches
    capability inference: ModelCapabilities keeps the GGUF- and
    template-text-based rules and drops the renderer/parser and model-family
    special cases. Thinking is the one rule deliberately looser than upstream -
    Ollama parses the Go template into a syntax tree to find the nodes around a
    .Thinking field, and here a template that mentions a think tag or thinking
    at all is taken to support it.

  - INT32 PARAMETER RANGE. Typed Modelfile parameter values use int where Go
    uses int64. A value that parses but does not fit is a
    ModelfileParseException saying so, not a silent truncation.

  - CRLF LINE-NUMBER DOUBLING IS FAITHFUL, NOT A DEFECT. The Modelfile parser
    counts a carriage return and a line feed as one line each, so an error in a
    file with Windows line endings reports twice the line number. That MIRRORS
    UPSTREAM and is pinned by
    ModelfileTests.Parse_WithCarriageReturnLineFeed_CountsBothCharactersAsLines,
    which expects line 3 for an error on the second line. Do not "fix" it
    without deciding to diverge from Ollama's error text on purpose.

Three further differences, less likely to be mistaken for defects: manifests and
blobs are written to a temporary file and renamed into place where Ollama writes
in place; GGUF reading is one eager forward pass producing an immutable snapshot
rather than the lazy iterator machinery; and LoRA ADAPTER files ARE accepted,
which is older Ollama behaviour - the upstream commit rejects them.

THE DELIBERATE DEVIATIONS IN ModelRunner
----------------------------------------
Same rule: decisions, not omissions, and the ones most likely to be "fixed" by
mistake. They are recorded in THIRD-PARTY-NOTICES.txt under each entry's
MODIFICATIONS heading, and again in the XML <remarks> where they happen.

THE BINDING (Native/)
  - Opaque upstream pointers are bound as IntPtr, and const char* RETURNS are
    bound as byte* because the memory belongs to the engine.
  - The 36 functions llama.h marks DEPRECATED are not bound at all, and
    llama_opt_params' optimizer callback is a void* because ggml-opt.h is not
    bound either.
  - LlamaBatchBuffer.Add writes ALL FIVE fields of the batch row. This is not
    redundancy: llama_batch_init leaves them uninitialized, and a row that is
    only partly written is a source of results that look plausible.
  - The log callback is installed when the library loads and DISCARDS by
    default, so a consumer who never calls SetLogHandler gets a silent
    library; llama.cpp writes to stderr by default.
  - THE SAFEHANDLES ARE NEVER PASSED TO AN IMPORT. Every LibraryImport takes
    IntPtr, and the owner (RunningModel) is what keeps the handle reachable
    for the duration of a call. That was reviewed and deliberately left
    alone; the rule is written on SafeLlamaModelHandle. If you ever change an
    import to take a SafeHandle, change all of them, or the mixture is worse
    than either.

THE GO TEMPLATE ENGINE (Templates/OllamaGo/)
  - `call` is not registered as a builtin, complex constants are rejected and
    hexadecimal float literals are not parsed. Nothing in a chat template uses
    them.
  - break and continue are return signals through the executor rather than
    panics, and printf implements the verbs v s q t d b o x X c U f F e E g G
    with the flags - + # 0 and space, plus width and precision, but NOT `*`
    width.
  - A missing map key yields Go's invalid value ("<no value>"), matching Go,
    and a tool-properties map that is missing yields a zero ToolProperty.
  - OllamaTemplateValues carries a System property that Ollama's Values struct
    does not, rendered as a system message prepended to the collated messages,
    and collating COPIES the message list instead of mutating the caller's.
  - The legacy tail (Ollama's pre-messages rendering path) is rendered with
    the full function table rather than a reduced one.

THE PARSERS (Parsing/)
  - ThinkingParser.Flush RETURNS the text it is still holding, as content;
    Ollama's parser drops it at end of stream. The same applies to
    ToolCallParser: text after a tag that never became a call is emitted as
    content rather than vanishing.
  - ToolCallParser searches a document for a call's arguments IN DOCUMENT
    ORDER, where Go iterates a map, so the result is deterministic; it
    preserves property order in ArgumentsJson; and ToolCall.Id is null from
    the parser, because an id is the chat layer's to assign.
  - Both parsers CAP what they will hold (about 1 MiB for a tool call, 4 KiB
    of whitespace for thinking) and emit the excess as content. A model that
    opens a tag and never closes it must not grow a buffer without limit.
  - ToolCallFormat.FromRenderedToolCall may differ from FromGoTemplateText for
    DeepSeek-style templates that write the call across two literals.

THE GRAMMAR CONVERTER (Grammar/)
  - A REMOTE $ref - one pointing at another document over http - is refused
    with GrammarException rather than fetched. Converting a schema does no
    I/O.
  - Every JSON value kind is guarded and a wrong kind raises GrammarException
    instead of silently widening the grammar; empty enum and empty anyOf are
    errors; const and enum NUMBERS keep the schema's own spelling.
  - What could not be expressed is reported as WARNINGS through the
    FromSchema overloads that take an out list, rather than being dropped.

THE JINJA ENGINE (Templates/Jinja/)
  - `x in undefined` is FALSE rather than an error. The Granite 3.3 template
    needs it and Jinja itself raises.
  - Tuples are modelled as lists, so items() prints [['k', v]] where Jinja
    prints [('k', v)]. No chat template depends on the difference.
  - `set` inside a loop is scoped to the iteration, which is Jinja's own
    documented behaviour; namespace() is what carries a value out.

THE ENGINE (Engine/)
  - PROBING USES no_alloc WITHOUT MEMORY MAPPING. The combination of mmap and
    no_alloc aborts the process inside llama-model.cpp, so ProbeAsync sets the
    load mode to none. Do not "restore" mmap there.
  - PENALTY SAMPLERS SEE ONLY THE TOKENS THIS REQUEST GENERATED, not the
    prompt, and they run over the full vocabulary before truncation as
    llama.h's own comment advises. Both differ from llama.cpp's main program.
  - Statistics come from a Stopwatch, not from llama_perf_context.
  - Token text is rendered with special=true so the parsers see the template's
    own markers; the end-of-generation token is caught BEFORE rendering; and
    the last generated token is emitted but not decoded, because nothing will
    sample after it.
  - An update is emitted only when a token produced visible text. Token ids
    that produced none accumulate onto the next update's Tokens, so no id is
    lost.
  - Embeddings are RAW: no L2 normalisation. Width is llama_model_n_embd_out,
    which is not always n_embd. An input longer than the embedding context's
    n_ubatch is an InferenceException naming the limit, because a non-causal
    model would otherwise abort the process inside ggml.
  - THE CHAT LAYER OWNS THE BOS RULE (Engine/ChatBosRule.cs), call ids are
    assigned call_1..n by this library, tool calls are delivered on the FINAL
    update only, an unknown tool name is returned as content rather than as a
    call, and the Native dialect refuses tools outright.
  - The reasoning stage is skipped when Think is false UNLESS the template has
    think tags and no enable_thinking switch, or the prompt ends with the
    opening tag.
  - Qwen 3.5 does not write JSON tool calls; Engine/ChatFunctionCallParser.cs
    reads the <function=NAME><parameter=P>...</parameter></function> form, and
    the engine chooses between it and Parsing/ToolCallParser from the template
    text. Duplicate tool names keep the first and ignore the rest.
  - RE-ENTRANCY IS AN EXCEPTION, NOT A DEADLOCK. Calling another member of the
    same IRunningModel from inside an await foreach over GenerateAsync or
    ChatAsync on the same call chain throws InvalidOperationException. The
    request gate is held across the yield, so without the AsyncLocal marker in
    EngineRequestScope it would simply hang.


CODING CONVENTIONS
==================
These are the repository-specific rules. They apply to the libraries and the
test projects alike.

  - NULLABLE REFERENCE TYPES ARE OFF, and so are implicit usings. Neither
    property appears in any of the four csproj files, and neither is to be
    added. Never use '?' on a reference type (string?, MyClass?), never use the
    null-forgiving '!' operator, and never write a #nullable directive.
    Value-type nullables (int?, long?, TimeSpan?, DateTimeOffset?, nullable
    enums) are Nullable<T> and are used freely - much of ModelParameters depends
    on them. Nullability of reference types is expressed in XML doc comments and
    enforced by runtime guards, not by the compiler.

  - EVERY FILE LISTS ITS OWN USING DIRECTIVES, System.* first and then the rest
    alphabetically. There are no global usings.

  - FILE-SCOPED NAMESPACES ONLY (namespace CodeBrix.Ollama.ModelManager; /
    namespace CodeBrix.Ollama.ModelRunner;), never block-scoped, and always the
    flat namespace. One type per file.

  - THE PUBLIC API IS ASYNC-ONLY WHERE IT DOES I/O. Every such operation returns
    Task, Task<T> or IAsyncEnumerable<T> and takes `CancellationToken
    cancellationToken = default` as its LAST parameter. No synchronous wrappers.
    Pure parsing that touches no I/O - Modelfile.Parse, ModelName.Parse - stays
    synchronous.

  - ZERO NUGET DEPENDENCIES IN BOTH LIBRARY PROJECTS. In ModelManager all JSON
    goes through the in-box System.Text.Json via Common/ModelManagerJson.cs,
    HTTP through HttpClient and SocketsHttpHandler, and hashing through
    System.Security.Cryptography; in ModelRunner, System.Text.Json again, plus
    Parsing/CompactJson.cs where the exact byte-for-byte shape of what is
    written matters. This is not a preference: it is verifiable in the packed
    nuspec, and it is why there is no Microsoft.Extensions.AI adapter, no
    OllamaSharp reference and no LLamaSharp reference anywhere.

  - NO NEW THIRD-PARTY NUGETS ANYWHERE, per the family rule: CodeBrix.* and
    Microsoft packages are fine; xUnit and SilverAssertions in the .Tests
    projects are the standing exception.

  - THE P/INVOKE RULES, which apply to ModelRunner's Native/ folder and
    nowhere else in this repository:

      * [LibraryImport], NEVER [DllImport]. All 302 imports are
        source-generated, so the marshalling is visible in generated code
        rather than inferred at run time. The only DllImport-shaped thing in
        the tree is NativeLibrary.SetDllImportResolver in
        NativeLibraryLoader.cs, which is what finds runtimes/<rid>/native/ in
        the first place.
      * ONE LIBRARY NAME, and it is the const NativeLibraryLoader.LibraryName
        ("codebrix_llama") in every single import - the resolver does the rest.
        Never hard-code a file name, an extension or a path in an import.
      * THE HEADERS ARE THE ONLY SOURCE OF TRUTH for a signature or a struct
        layout: llama-native-tools/llama.cpp/include/llama.h and
        .../ggml/include/*.h at the vendored commit. Not a blog post, not
        another binding, not a memory of what the function used to take.
      * A STRUCT BOUND FROM A HEADER IS SIZE-CHECKED BY A TEST.
        tests/.../Native/NativeDefaultsTests.cs pins the sizes and the default
        values the engine hands back. Add the check in the same commit as the
        struct; a layout that is wrong by four bytes fails nowhere obvious.
      * unsafe IS ALLOWED HERE, and only here and in the seven Engine/ files
        that walk a logits array or a batch. It is not a licence to use it in
        a parser or a template engine.
      * ONE TYPE PER FILE STILL APPLIES to every enum and every struct, which
        is why Native/ is 68 files. The exception the folder does make is
        NativeMethods, which is ONE partial class split by subject
        (NativeMethods.Model.cs, .Context.cs, .Sampler.cs and so on) - that is
        one type, spread over fourteen files, not fourteen types.

  - XML DOC COMMENTS ON EVERY PUBLIC AND PROTECTED MEMBER.
    GenerateDocumentationFile is on for both libraries; fix CS1591 at the source
    and never suppress it. There is no <NoWarn> in this repository.

  - TESTS are named <ClassUnderTest>Tests.cs, and test methods follow the
    family's two styles: <Member>_<snake_case_description> when the test
    targets one member (ReadAsync_reads_a_version_one_file,
    IsValid_with_null_returns_false), pure snake_case when it does not.
    The leading token matches the member's casing exactly; everything after
    the first underscore is lowercase snake_case. Multi-statement tests carry
    //Arrange //Act //Assert comments, and TestContext.Current.CancellationToken
    is passed to every cancellable call (xUnit1051). Assertions are
    SilverAssertions fluent style throughout - .Should().Be(), .BeNull(),
    .BeTrue(), .HaveCount(), .BeEmpty() - not raw Assert.* calls.

  - PROSE RULES for anything written in this repository: no firearm metaphors
    ("pitfall", "gotcha", "sharp edge" instead), and the family's banned-name
    rule applies - where the upstream UI framework behind some sibling CodeBrix
    repositories would be named, write "the upstream project".

THE AI-AGENT POINTER STUBS
--------------------------
AGENTS.md, CLAUDE.md, .clinerules, .cursorrules, .cursor/rules/agent-readme.mdc,
.windsurfrules, .github/copilot-instructions.md and .junie/guidelines.md all
point at README-INDEX.txt. They are byte-identical family-wide content, not
per-repo content - all eight are byte-for-byte identical to CodeBrix.Docker's
copies today. Keep them in sync with the canonical copies and never let a
scaffolding tool rewrite them.


NOTES
=====

NEVER `git commit` AND NEVER `git push` IN THIS REPOSITORY. Leave all changes in
the working tree; Jeremy handles every git operation. Read-only git (status,
log, diff, ls-tree) is fine. `git log` is the record of what has been committed;
do not describe the commit state in this file, because it goes stale.

WHAT HAS BEEN VALIDATED, AND ON WHAT
------------------------------------
Everything below was done on ONE machine: the Intel Mac mini (2018), i7-8700B,
macOS 15.8, x86_64, .NET SDK 10.0.401. (Later the same day, on the Windows 11
x64 machine with the same SDK: the solution built 0/0 in Release, the packed
ModelRunner listed all seven runtimes/<rid>/native/ pairs with no doubled
path, and the ModelManager executable ran 789/788/1 - but see the
`dotnet test` note under WHAT HAS NOT BEEN VALIDATED.)

  * `dotnet build CodeBrix.Ollama.slnx -c Release` - 0 warnings, 0 errors.
    Re-checked for ModelRunner alone on 2026-09-16 with --no-incremental after
    the build-out: 0 warnings, 0 errors over all 240 files, with
    GenerateDocumentationFile on and no <NoWarn> anywhere.
  * The ModelManager suite - 789 total, 788 passed, 1 skipped, about two
    seconds, with no network. Run both ways: through `dotnet test` and by
    running the built executable directly.
  * The ModelRunner suite, 2026-09-16, as the built executable - 1258 total,
    1228 passed, 0 failed, 30 skipped, 2.6 seconds, with no network. Opening
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS leaves 9 skipped (the Qwen class); opening
    CODEBRIX_OLLAMA_RUN_QWEN35_TESTS as well leaves none, and both gated runs
    passed during the build-out against the two cached model files.
  * THE WHOLE STACK, END TO END, ON osx-x64. The managed binding really loads
    the committed libcodebrix_llama.dylib out of runtimes/osx-x64/native/,
    really decodes, and the logits it produces from the conformance model match
    EXPECTED.txt - which is the thing that could not be said before this
    build-out. Two real models ran on it: SmolLM 360M at 69 to 74 tokens a
    second, and Qwen 3.5 35B-A3B Q4_K_M - 20.5 GiB, hybrid MoE, 248,320-entry
    vocabulary - at 8.8 to 9.3 tokens a second on six CPU cores, loading in 31
    to 44 seconds warm and about 86 cold, with a peak resident size of about
    23.5 GB on a 32 GiB machine.
  * Both packages packed and unzipped. Each carries the five packed root files
    and lib/net10.0, each nuspec carries an EMPTY net10.0 dependency group, and
    the ModelRunner package carries all seven runtimes/<rid>/native/ folders -
    fourteen entries - with no doubled path (2026-09-16).
  * The osx-x64 native: built, full gate passed (architecture, 248/248 required
    exports, exact export surface, install name, system-only dependencies,
    minos 13.3, signature, smoke test, byte-identical model regeneration, and
    conformance at max |diff| 4.98e-08), adopted, twin and dSYM stored. The
    osx-arm64 native was built and gated the same way on the Apple Silicon
    Mac mini, including the Metal conformance pass; its record is in
    BUILD-PROVENANCE.txt.

WHAT HAS NOT BEEN VALIDATED
---------------------------
  * THE MANAGED SUITE ON WINDOWS - RUN ONCE, 2026-09-15, WITH ONE OPEN
    QUESTION. On the Windows 11 x64 machine (.NET SDK 10.0.401) the solution
    built 0 warnings / 0 errors in Release, and the built executable
    tests/.../bin/Release/net10.0/CodeBrix.Ollama.ModelManager.Tests.exe ran
    789 total / 788 passed / 1 skipped in 3.7 s - the same result as macOS
    and Linux, so the Path.Combine argument now has evidence behind it. BUT
    `dotnet test tests/CodeBrix.Ollama.ModelManager.Tests -c Release`
    reported "Zero tests ran", exit code 5, with and without --no-build -
    the Microsoft.Testing.Platform runner mode that global.json selects did
    not find the tests on this machine, while the same assembly run directly
    found all 789. Not investigated further that night (the run was a
    by-product of adopting the win-x64 native); whoever next works on the
    test projects on Windows should find out why before trusting `dotnet
    test` there. (Linux IS covered: on 2026-09-15 the solution built 0/0 with
    --no-incremental and the suite ran 789 total / 788 passed / 1 skipped on
    a Debian-family x64 workstation with the same SDK, as the built
    executable.)

  * ModelManager'S LIVE PULL TEST HAS STILL NEVER BEEN RUN. Say that plainly
    rather than assuming it works. CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 HAS now
    been set in this repository - that is how ModelRunner's live tests ran on
    2026-09-16 - but only against ModelRunner's executable. The one test that
    reaches registry.ollama.ai has only ever been skipped. Running it is the
    first thing to do before any release.

  * THE WIN-ARM64 NATIVE SLICE HAS NEVER BEEN EXECUTED: cross-built and
    statically checked on x64, adopted by decision, as described under THE
    NATIVE LIBRARIES. Its first run on real ARM64 hardware is its smoke test.
    The three Linux slices were built and gated on 2026-09-15 (arm64 and
    riscv64 under qemu-user emulation) but have not yet run on real arm64 or
    riscv64 hardware. win-x64 is fully gated and adopted.

  * SIX OF THE SEVEN NATIVES HAVE NEVER BEEN LOADED FROM .NET. ModelRunner is
    written and its whole suite passes, but every run of it has been on ONE
    machine and ONE runtime identifier: osx-x64, the Intel Mac mini, CPU only,
    no Metal. osx-arm64, linux-x64, linux-arm64, linux-riscv64, win-x64 and
    win-arm64 have been verified only by llama-native-tools' own gate - by C
    programs - and win-arm64 not even by that. The first thing to run
    elsewhere is the OFFLINE ModelRunner suite: it loads the conformance model
    through the real native library, so it proves both that the
    runtimes/<rid>/native/ probing finds what the packing block puts there and
    that the struct layouts in Native/ are right for that platform's compiler.
    Expect the size checks in NativeDefaultsTests to be the first thing that
    fails if anything is wrong.

  * NO GPU PATH HAS EVER BEEN EXERCISED THROUGH THE BINDING. Every managed run
    so far set GpuLayers = 0. The osx-arm64 native carries Metal and its
    conformance was checked by the native gate on the Apple Silicon mini, but
    no managed test has offloaded a single layer.

  * ModelRunner'S LIVE TESTS HAVE RUN ON THIS MACHINE ONLY, and they are the
    only tests that download. Their two files were already cached here when
    they ran; a first run elsewhere fetches 368 MiB, or 20.5 GiB with the Qwen
    gate open.

TWO DESIGN CHOICES, SO THEY ARE NOT MISTAKEN FOR OVERSIGHTS
----------------------------------------------------------
  - THE PACKAGES DO NOT REFERENCE EACH OTHER. It would be convenient for
    ModelRunner to take a model name and resolve it, and that is precisely what
    is not wanted: coupling them would force every consumer of one to carry the
    other, and would put a registry client inside an application whose model
    files may have arrived by any means at all. The composition is the
    consumer's - ModelManager resolves a name to a GGUF path, ModelRunner loads
    a path.

  - THE STORE LAYOUT IS OLLAMA'S, EXACTLY. ModelStorePaths computes every path
    the way Ollama computes it - blobs at blobs/sha256-<hex>, manifests at
    manifests/<host>/<namespace>/<model>/<tag> - and ModelManagerJson is
    configured so the bytes written match what Go's json.Encoder emits. The
    point is interchange: a directory this library writes is one a local Ollama
    install can read, and the other way round. The default store directory is
    Ollama's own (OLLAMA_MODELS when set, otherwise ~/.ollama/models) and is
    always overridable through ModelStoreOptions. There is no cross-process
    locking, by design and following upstream: writes go to a temporary file
    and are renamed into place, and the manifest is written LAST, so a model is
    either fully present or absent.


================================================================================
END OF MAINTAINER-README
