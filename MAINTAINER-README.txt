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

ModelManager ships no native library: it pulls models from any registry that
speaks Ollama's manifest-and-blob protocol, obtains the files of a model that no
such registry serves - a Hugging Face file repository, any list of HTTPS
addresses, a folder on disk - into the same store, keeps all of it in Ollama's
own on-disk blob and manifest layout, lists, shows, copies, deletes and creates
models from Modelfiles, reads GGUF metadata, resolves a model name to the files
on disk, and lays a bundle out again as its publisher's own file tree.
ModelRunner loads a GGUF file and runs it in-process over a self-built
llama.cpp engine bound through hand-written P/Invoke. An application uses the
first to get a path and hands that path to the second.

THE STATE OF THINGS, 2026-09-17
-------------------------------
  * ModelManager is written and tested.
  * ModelManager NOW MANIPULATES WHAT IT OBTAINED, as of 2026-09-17: it exports
    a bundle to ONNX - passing a publisher's own graphs through, or running the
    ONNX Runtime GenAI model builder or Hugging Face Optimum in a CPython the
    machine already has - and reduces the graphs a bundle holds to smaller ones,
    each result stored beside its source as a DERIVED BUNDLE recording what made
    it and from what. Reduction has TWO ENGINES: ONNX Runtime's own Python
    tools, and this library's own managed ONNX codec and quantizer, which needs
    nothing installed and writes the same bytes those tools write - checked file
    by file against real published models. The library therefore carries
    EXACTLY ONE NuGet dependency, CodeBrix.Python, inert until a Python feature
    runs.
  * ModelManager NOW OBTAINS BUNDLES as well as GGUF models: a Hugging Face
    file repository, any list of HTTPS addresses (the objects under a public
    storage bucket included) or a folder already on disk, into the same
    content-addressed store, with list, show, resolve, copy, delete and prune
    working on them unchanged, a materialize call that writes the publisher's
    file tree back out, and the licence a publisher states reported and
    nothing more. Tested live on 2026-09-16 against four families of
    music-generation models - a SkyTNT MIDI model, MuPT, MuseCoco and
    Magenta's Music Transformer. THOSE ARE TEST SUBJECTS, test-side data and
    nothing else: the library names no model anywhere, and what the work
    widened is the KINDS of model it can obtain. The plan behind both halves of
    the work, obtaining and manipulating, is
    ~/ClaudeHome/PLAN_codebrix_ollama_music_models_2026-09-16.md, and all of its
    phases are done.
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
  * TWO OF THE SEVEN NATIVES HAVE RUN THROUGH THE MANAGED BINDING: osx-x64,
    where everything below about ModelRunner's behaviour was measured, on the
    Intel Mac mini; and linux-x64, where the offline ModelRunner suite loaded
    the committed native and passed on 2026-09-16 (see WHAT HAS BEEN
    VALIDATED). The other five natives have still never been loaded from
    .NET. See WHAT HAS NOT BEEN VALIDATED.


REPOSITORY LAYOUT
=================
    src/CodeBrix.Ollama.ModelManager/   the managed library
      Bundles/                   the vocabulary a bundle is described in and
                                 pulled with: BundleDefinition, BundleFile,
                                 BundleListing, FileFilter, LicenseRecord,
                                 PullOptions, PullSource, ImportOptions, the
                                 internal BundleFormatDetector that reads a
                                 model format out of a set of file names, and
                                 the internal DerivedProvenance, which is what
                                 a bundle this library produced itself records
                                 about how it was produced
      Common/                    the exception hierarchy (ModelManagerException
                                 and its subclasses) and ModelManagerJson,
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
      Python/                    the Python corner: the public PythonSupport,
                                 PythonOptions, PythonSupportReport,
                                 PythonModuleReport and the three enums
                                 (PythonLibrarySource,
                                 PythonVirtualEnvironmentSource,
                                 PythonEngineOwner); the internal
                                 PythonLibraryLocator and PythonResolution,
                                 which answer for a CPython with no
                                 interpreter anywhere in sight; PythonScripts
                                 and PythonScriptImports, the embedded-script
                                 reader and its import allow-list; and
                                 PythonHost, which is THE ONLY FILE IN THE
                                 LIBRARY THAT NAMES A CodeBrix.Python TYPE
      Python/Scripts/            the .py files this library runs, embedded as
                                 resources rather than shipped as files
      Export/                    exporting a model to ONNX: the public
                                 ExportOptions, ExportRoute and ExportResult,
                                 and the internal OnnxExport, which chooses a
                                 route, names the files a pass-through keeps,
                                 knows which modules each route imports and
                                 runs the two export scripts, with OnnxExportRun
                                 for what a script reported. Nothing here names
                                 a CodeBrix.Python type; the Python routes go
                                 through PythonHost like everything else
      Reduce/                    making an exported graph smaller: the public
                                 ReduceOptions, ReduceMode, ReduceEngine and
                                 ReduceResult, and the internal OnnxReduce,
                                 which resolves the engine in ONE method,
                                 names the derived bundle, decides which files
                                 are graphs and which are carried through
                                 beside them, runs the three reduction scripts
                                 and, for the managed engine, drives the codec
                                 and quantizers in Onnx/ directly, with
                                 OnnxReduceRun for what a script reported.
                                 Nothing here names a CodeBrix.Python type
                                 either
      Onnx/                      THE MANAGED ENGINE, and the reason a
                                 reduction can need nothing installed: a
                                 hand-written ONNX codec and a port of ONNX
                                 Runtime's quantizers. Protobuf/ is a
                                 forward-only reader, an append-only writer and
                                 the carrier that re-emits every field the
                                 codec does not model, so a file read and
                                 written back is byte for byte what it was;
                                 beside it are the ONNX message classes,
                                 OnnxModel (reading, writing, external data),
                                 OnnxSaveOptions and OnnxMetadataProbe, which
                                 answers whether a graph records having been
                                 through shape inference by walking the
                                 outermost message alone. Quantization/ is the
                                 ported code and is the subject of entry 15 of
                                 THIRD-PARTY-NOTICES.txt. All internal
      Registry/                  the HTTP side of a pull: RegistryClient,
                                 RegistryChallenge, RegistryManifestResponse,
                                 the resumable ranged downloader BlobDownload
                                 with BlobDownloadPart, BlobDownloadState and
                                 BlobDownloadResult, and FileDownloader, which
                                 drives that same downloader from an address
                                 and a stated hash rather than from a registry
                                 digest. All internal
      Sources/                   what a bundle is listed from before anything
                                 is fetched: IBundleSource,
                                 HuggingFaceHubSource (the Hub's own HTTP API,
                                 pinned to a commit), HttpFileListSource (a
                                 plain list of addresses) and the
                                 GoogleCloudStorageListing helper that turns a
                                 public bucket prefix into such a list
      Store/                     the store itself: IModelStore, ModelStore,
                                 ModelStoreOptions, ModelStorePaths, the
                                 manifest / layer / config / parameters /
                                 message DTOs, MediaTypes, ModelConfigKeys (the
                                 config-property names a consumer reads
                                 provenance out of), ManifestFiles,
                                 LayerFactory, LayerPruner, ModelLayerReader,
                                 ModelCapabilities, Sha256Digest, HumanFormat,
                                 and the bundle side of the store:
                                 ResolvedFile, MaterializeOptions,
                                 MaterializeLink, the internal
                                 BundleMaterializer that writes a publisher's
                                 tree back out and the internal HardLink it
                                 does that with
      AGENT-README.txt           this package's consumer guide (packed)
      InternalsVisibleTo.cs      grants CodeBrix.Ollama.ModelManager.Tests and
                                 CodeBrix.Ollama.ModelManager.Python.Tests

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
                                 by default, and needing no CPython
      Bundles/ Export/ Gguf/ Modelfile/ Names/ Onnx/ Python/ Reduce/ Registry/
      Sources/ Store/
      Onnx/Fixtures/             the ONNX ORACLE FIXTURES and the script that
                                 writes them - see THE TEST INFRASTRUCTURE
      Infrastructure/            EnvGatedFactAttribute, FakeHttpHandlerBase
                                 and the three service doubles built on it
                                 (FakeRegistryHandler, FakeHubHandler,
                                 FakeBucketHandler), GgufTestFileBuilder,
                                 FakeModelBuilder, TempStoreDirectory, the
                                 live-bundle data MusicModelDefinitions with
                                 MusicModel and ExpectedBundleFile, and
                                 ProbeProcess with ProbeRun, which run the
                                 probe console application as a child process,
                                 and RecordingProgress, a synchronous
                                 IProgress<PullProgress> the progress-stream
                                 tests use because Progress<T> DOES NOT KEEP
                                 ORDER
      probe/                     the built probe, copied beside the test
                                 assembly by this project's CopyProbeToOutput
                                 target
      xunit.runner.json          copied to output by an explicit csproj item

    tests/CodeBrix.Ollama.ModelManager.Probe/   a console application, NOT a
                                 test project: one whole-process observation
                                 per run, printed as key: value lines. Modes:
                                 inert (a full import, list, resolve and
                                 materialize cycle, then whether the
                                 CodeBrix.Python assembly is loaded), export
                                 (the same question of a whole pass-through
                                 export, which must also load nothing), reduce
                                 (a real graph quantized through the Python
                                 engine in a process that must still end by
                                 itself; the graph is named by
                                 CODEBRIX_OLLAMA_PROBE_MODEL), reduce-managed
                                 (the same graph quantized through the MANAGED
                                 engine, which must load no CodeBrix.Python at
                                 all), own,
                                 hostowned, noshutdown and hostcode (the
                                 interpreter-ownership outcomes, the last of
                                 them a host that names its own library in code
                                 with every variable cleared). Referenced by
                                 both ModelManager test projects with
                                 ReferenceOutputAssembly="false" so that it is
                                 built, not linked

    tests/CodeBrix.Ollama.ModelManager.Python.Tests/   the xunit.v3 suite that
                                 needs a real CPython, gated by
                                 CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1. A SECOND
                                 EXECUTABLE rather than more classes beside the
                                 offline suite, because an interpreter belongs
                                 to a process, is started once and cannot be
                                 restarted
      Infrastructure/            its own copies of EnvGatedFactAttribute,
                                 ProbeProcess and ProbeRun, TestGates,
                                 PythonTestFixture - the assembly fixture that
                                 starts the one interpreter and shuts it down
                                 exactly once - ExportTestStore, which keeps
                                 the export and reduction tests' store in the
                                 test-model cache so a second run downloads
                                 nothing, TempExportDirectory, VenvPython with
                                 VenvPythonRun, which run one of this project's
                                 own .py files with the virtual environment's
                                 interpreter as a child process, a copy of
                                 RecordingProgress, and ReduceTestModels, which
                                 is the one place that says how each model this
                                 suite reduces gets into the store - pulled,
                                 exported, or prepared by the Python engine.
                                 MusicModelDefinitions, MusicModel and
                                 ExpectedBundleFile are LINKED from the other
                                 ModelManager test project rather than copied:
                                 they are pinned publisher data generated from
                                 one spike record, and two copies of that would
                                 be two things to keep true instead of one.
                                 Onnx/OnnxModelComparison.cs is linked for the
                                 same reason: the rules for "these two files
                                 are the same model" are one set of rules, used
                                 by the fixture comparison offline and by the
                                 two-engine comparison here
      Python/                    TEST-SIDE scripts, copied beside the test
                                 assembly and run by the virtual environment's
                                 own interpreter, never by the library:
                                 validate_outputs.py, which runs a graph and
                                 its reduced form over the same inputs and
                                 reports how far apart they are, and
                                 make_probe_model.py, which writes the one
                                 small graph the probe's reduce mode needs
      AssemblyFixtures.cs        the [assembly: AssemblyFixture] declaration
      probe/                     the built probe, as above
      xunit.runner.json          copied to output by an explicit csproj item

    tests/CodeBrix.Ollama.ModelRunner.Tests/    the xunit.v3 suite, offline
      Native/ Templates/Jinja/ Templates/OllamaGo/ Parsing/ Grammar/ Engine/
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

    .gitattributes               LF line endings on every checkout, on every
                                 platform. The built-in chat templates are
                                 embedded resources and the ModelRunner
                                 fixtures are compared byte for byte, so a
                                 CRLF checkout (core.autocrlf=true) would fail
                                 the suite and pack CRLF templates into the
                                 assembly; the file itself says why in full

    CodeBrix.Ollama.slnx         the solution. Its Solution Items folder carries
                                 .gitignore, AGENT-README.txt,
                                 EXTRAS-README.txt, global.json,
                                 icon-codebrix-128.png, LICENSE,
                                 MAINTAINER-README.txt, README-INDEX.txt,
                                 README.md and THIRD-PARTY-NOTICES.txt; its
                                 Tests folder carries the two test projects and
                                 the probe console application; the two
                                 packable projects sit at the top level

THE FLAT-NAMESPACE RULE - DO NOT "FIX" IT
------------------------------------------
Every public and internal type in ModelManager declares `namespace
CodeBrix.Ollama.ModelManager;`, and every file in its test project declares
`namespace CodeBrix.Ollama.ModelManager.Tests;`. ModelRunner does exactly the
same with `namespace CodeBrix.Ollama.ModelRunner;` and `namespace
CodeBrix.Ollama.ModelRunner.Tests;`, however many folders deep the library is.
The two projects beside ModelManager.Tests follow the same rule in their own
names: `namespace CodeBrix.Ollama.ModelManager.Python.Tests;` and `namespace
CodeBrix.Ollama.ModelManager.Probe;`. The folders above are FILE ORGANIZATION
ONLY.

This is deliberate and load-bearing: the public API is a single using directive
for a consumer, which is what the AGENT-READMEs promise. Do not add
folder-scoped namespaces to this repository. One type per file, named after the
type.


BUILDING
========
Standard SDK build from the repository root:

    dotnet restore CodeBrix.Ollama.slnx
    dotnet build   CodeBrix.Ollama.slnx -c Release

Target framework: net10.0 only on every project in the solution; both libraries
set LangVersion latest.
Both libraries set AllowUnsafeBlocks: ModelRunner for its P/Invoke layer and
the engine code that walks a logits array or a batch, ModelManager for its
source-generated platform calls; ModelRunner.Tests matches it. ModelRunner also
carries an EmbeddedResource item over Templates\OllamaGo\BuiltIn\**, which is
how Ollama's 41 built-in template files come to travel inside the assembly, and
ModelManager carries one over Python\Scripts\*.py, which is how the scripts it
runs travel inside its own.

ModelRunner HAS ZERO PackageReference ENTRIES AND ModelManager HAS EXACTLY ONE,
CodeBrix.Python. Verify that after any change. It is a contract with consumers,
not a preference (see CODING CONVENTIONS), and the packed nuspec is where it is
checked - see PACKAGING AND PUBLISHING.

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
    SilverAssertions.ApacheLicenseForever 1.0.248.1071. 70 test classes and
    1,550 test cases as of 2026-09-17, of which 13 are [EnvGatedFact] members
    that skip unless their variable is set. `-list classes` on the built
    executable prints the class names, which is the quickest way to check this
    against the tree.

    tests/CodeBrix.Ollama.ModelRunner.Tests -- the same four packages at the
    same versions. 44 test classes, 481 test members, 1,258 test cases: 391
    [Fact] or [EnvGatedFact] members, 87 [Theory] members carrying 752
    [InlineData] rows between them, and 3 more [Theory] members whose
    [MemberData] enumerates a fixture folder and contributes 115 rows. 30 of
    the members are [EnvGatedFact] and are skipped unless their variable is
    set. NOTHING IN THIS PLAN TOUCHES THIS SUITE, and its three numbers are
    the fence that says so.

    tests/CodeBrix.Ollama.ModelManager.Python.Tests -- the same four packages
    at the same versions. 6 test classes and 43 test cases as of 2026-09-17,
    EVERY ONE of them [EnvGatedFact]: 25 need the Python gate alone, and the
    four export tests, the automatic-route test, the seven reduction tests and
    the six engine comparisons need the live gate as well. It is a separate
    executable because a CPython interpreter belongs to a process, is started
    once and cannot be restarted: run beside the offline suite, it would leave
    every later test in that run with an interpreter nobody asked for.

ALL THREE SUITES ARE OFFLINE BY DEFAULT. None needs a daemon, a server, a model
file or a network: ModelManager's runs in a few seconds on a warm machine
and ModelRunner's in under three, and ModelRunner's three seconds include
loading the tiny conformance model through the real native library and checking
the logits it produces. The Python suite with its gate closed runs in a quarter
of a second and starts nothing. The tests that are exceptions are gated - see
THE ENVIRONMENT GATE below.

HOW TO RUN IT
-------------
    dotnet test CodeBrix.Ollama.slnx

RUNNER NOTE: the family records that on SDK 10.0.400 `dotnet test` can report
ZERO TESTS for an xunit.v3 project. It did not happen here - on SDK 10.0.401
`dotnet test --solution` found and ran every test in the solution - but running
the built entry point directly is still the better habit while iterating. It is
a normal executable, it gives clean per-class counts, and it takes filters:

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
SEVEN VARIABLES, read by each test project's own
Infrastructure/EnvGatedFactAttribute.cs - a FactAttribute subclass that sets
Skip unless the variables it names hold the expected value - and named as
consts, in ModelRunner.Tests in Infrastructure/TestGates.cs, in
ModelManager.Tests on Infrastructure/MusicModelDefinitions.cs, and in
ModelManager.Python.Tests in Infrastructure/TestGates.cs:

    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1    the live tests in BOTH GGUF suites
    CODEBRIX_OLLAMA_RUN_QWEN35_TESTS=1  the Qwen 3.5 class, on top of that
    CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS=1    the two MuseCoco bundles, on top
                                        of the live gate
    CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS=1 the seven remaining large bundle
                                        definitions, on top of the live gate
    CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1  EVERY test in
                                        ModelManager.Python.Tests, and nothing
                                        anywhere else. The export tests there
                                        need the live gate as well, and are the
                                        only tests that need two gates neither
                                        of which is a music gate
    CODEBRIX_OLLAMA_TEST_MODEL_DIR      where ModelRunner's live tests cache
                                        the model files they download, and
                                        where the export tests keep the store
                                        they export from
    CODEBRIX_OLLAMA_PYTHON_VENV         the Python virtual environment the
                                        Python suite expects its modules in -
                                        read by the LIBRARY itself, not only by
                                        the tests

The first five are gates and take the value "1"; a test that names two of them
runs only when both are set. The last two are not gates: one moves a cache, and
the other is the library's own way of being told where CPython lives.

THE PYTHON GATE, IN FULL. The whole of ModelManager.Python.Tests is gated, and
the gate is checked in TWO places on purpose: on every test member, and in the
assembly fixture's constructor. The fixture is what starts the one interpreter
the assembly has, and it must start NOTHING in a run where every test is going
to skip - so with the gate closed the run is 43 skipped, a tenth of a second,
and no CPython is touched at all.

    CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 \
    CODEBRIX_OLLAMA_PYTHON_VENV=$HOME/venvs/codebrix-ollama \
    tests/CodeBrix.Ollama.ModelManager.Python.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelManager.Python.Tests

The virtual environment is deliberately given as an ENVIRONMENT VARIABLE and
NOT as a PythonOptions property, because that is the branch of the resolution
order these tests are there to exercise; the branch that takes it from code is
exercised by the probe's own modes, which name it outright.

WHAT THAT VENV MUST HOLD for the suite to pass: onnx, onnxruntime, torch and
transformers, importable by the interpreter the environment's pyvenv.cfg points
at. Jeremy installs them; no agent and no test ever does. On a distribution
that refuses a system-wide pip install, a virtual environment is the sanctioned
route and is what this design assumes.

A VENV DOES NOT SURVIVE A MINOR-VERSION MOVE of its base interpreter. 3.13 to
3.14 means recreating it and repointing PYTHONNET_PYDLL if it is set; point
releases inside a minor version arrive through the package manager and need
nothing. The symptom is a report whose Problems name a library that is not
there.

WHAT THE PYTHON SUITE COSTS. With the Python gate open and the live gate shut it
is around seven seconds of tests, of which about five are the noshutdown probe
waiting out the bounded process-exit timeout in a child process, plus about four
seconds AFTER the last test for the fixture's PythonEngine.Shutdown() - torch
and transformers leave a large object graph behind and the shutdown collects it.
That tail is the shutdown really running, not a hang; a run that skipped it
would end sooner, not later.

THE EXPORT AND REDUCTION TESTS NEED TWO GATES, the Python one and the live one:

    TMPDIR=$HOME/Temp/codebrix-ollama-tmp \
    CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 \
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 \
    CODEBRIX_OLLAMA_PYTHON_VENV=$HOME/venvs/codebrix-ollama \
    tests/CodeBrix.Ollama.ModelManager.Python.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelManager.Python.Tests -showLiveOutput

They export two real models, and unlike every other live test in this
repository THEY KEEP WHAT THEY DOWNLOAD. The source bundles go into a store of
their own inside the test-model cache -
<cache>/export-store - and are pulled only when they are not already there, so
the first run pays for about 1.23 GiB and later runs pay for nothing. The
DERIVED bundles each test writes are removed when it ends; nothing else is
deleted, and nothing deletes the cache but a person.

    hf.co/skytnt/midi-model-tv2o-medium:onnx-only    894.9 MiB, 5 files
    hf.co/m-a-p/MuPT-v1-8192-190M                    364 MiB, 11 files

MEASURED ON THE DEBIAN 13 LAPTOP, 2026-09-17 (onnxruntime-genai 0.15.2, CPU):

    first run, both gates, including the downloads     99 s
    later runs, the models already cached             112 s, 43 / 0 / 0
      of which the six engine comparisons              57 s
    SkyTNT pass-through, 894.9 MiB registered          under 0.05 s - it
                                                       copies nothing
    MuPT 190M -> fp32, 728.6 MiB written               2.7 s
    MuPT 190M -> int4, 240.5 MiB written               1.1 s
    MuPT 190M -> Optimum, 725.4 MiB written            4.6 s

THE REDUCTION TESTS RUN BEHIND THE SAME TWO GATES and in the same store. They
reduce SkyTNT's published pair in all four modes, reduce one graph of that pair
on its own, and export MuPT at single precision and reduce that graph in the two
modes that make it smallest. Every one of them then RUNS both models - the
graph and its reduced form - through the virtual environment's own interpreter
over tests/.../Python/validate_outputs.py, and prints the differences.

MEASURED ON THE DEBIAN 13 LAPTOP, 2026-09-17 (onnxruntime 1.30.0, CPU). The
sizes are the graphs only; what a bundle carries through counts towards neither
number:

    SkyTNT pair, 938,375,268 bytes of graphs
      WeightOnlyInt4    152,180,536   6.17x   0.9 s
      WeightOnlyInt8    266,483,934   3.52x   1.0 s
      DynamicInt8       257,573,446   3.64x  11.9 s (of which the preparation
                                                     pass is most)
      PreprocessOnly    938,243,864   1.00x   9.0 s
      model_token.onnx alone, WeightOnlyInt4
                         28,229,659   4.13x   0.8 s
    MuPT 190M at fp32, 762,499,282 bytes of graph and its weights
      WeightOnlyInt4    237,012,363   3.22x   1.1 s
      DynamicInt8       307,555,299   2.48x   9.7 s
      PreprocessOnly    762,419,844   1.00x   7.0 s

    A PREPARED BUNDLE FROM AN EARLIER BUILD IS NOT INTERCHANGEABLE WITH ONE FROM
    THIS ONE: the preparation pass now records the shape inference a quantizer
    needs, which changed both sizes above by one metadata entry per graph.
    Delete a leftover prepared bundle rather than reducing it.

    FILE BY FILE                     source    dynamic    int8-wt    int4-wt
      SkyTNT onnx/model_base.onnx  821,713,887  217,495,614  225,400,689  123,950,877
      SkyTNT onnx/model_token.onnx 116,661,381   40,077,832   41,083,245   28,229,659
      MuPT model.onnx (+ .data)    762,499,282  307,555,299            -  237,012,363

WHAT THE REDUCED MODELS COMPUTE, measured the same day by running both models
over the same deterministic inputs (worst relative difference over every
output, and how often the largest logit is still the largest):

      SkyTNT model_base  dynamic INT8    0.100     top-1 agreed
      SkyTNT model_token dynamic INT8    0.042     top-1 agreed
      SkyTNT model_token int8 weights    0.011     top-1 agreed
      SkyTNT model_base  int4 weights    0.235     top-1 agreed
      SkyTNT model_token int4 weights    0.163     top-1 DISAGREED
      MuPT               dynamic INT8    0.110     top-1 agreed
      MuPT               int4 weights    0.544     top-1 agreed
      SkyTNT model_token prepared        0.000     top-1 agreed

    The prepared graph computing EXACTLY what it computed before - a difference
    of zero, not of nearly zero - is the check that says preprocessing changes
    the graph and not the arithmetic. The int4 disagreement is on RANDOM input:
    the logits of a model fed noise are nearly flat, so which one is largest is
    nearly arbitrary, and the number is recorded rather than asserted. The
    tests assert only that the reduced model ran, produced finite numbers of
    the same shape, and stayed inside a generous bound around these figures.

THE TWO ENGINES ARE COMPARED ON THOSE SAME REAL MODELS, which is what the
managed engine exists to pass. ReduceEngineComparisonLiveTests reduces each
bundle twice - once with ReduceEngine.Python and once left to the automatic
choice, which must reach the managed engine - and then reads both results with
the library's own codec and compares them: the graph structure, every node and
attribute, every initializer's raw bytes, and finally the encoded file. It then
runs BOTH reduced graphs through validate_outputs.py and requires the same
numbers to the last digit.

MEASURED ON THE DEBIAN 13 LAPTOP, 2026-09-17. Every file below is BYTE FOR BYTE
identical between the two engines; the two times are the Python engine's and
the managed engine's:

    SkyTNT pair WeightOnlyInt4    152,180,536     0.9 s  /  2.8 s
      onnx/model_base.onnx        123,950,877
      onnx/model_token.onnx        28,229,659
    SkyTNT pair WeightOnlyInt8    266,483,934     1.2 s  /  3.6 s
      onnx/model_base.onnx        225,400,689
      onnx/model_token.onnx        41,083,245
    SkyTNT pair DynamicInt8       257,573,446     3.5 s  /  1.5 s
      (from the PREPARED bundle)
      onnx/model_base.onnx        217,495,614
      onnx/model_token.onnx        40,077,832
    MuPT 190M WeightOnlyInt4      237,012,363     1.1 s  /  2.0 s
    MuPT 190M WeightOnlyInt8      313,428,041     1.3 s  /  2.2 s
    MuPT 190M DynamicInt8         307,555,299     3.1 s  /  1.1 s
      (from the PREPARED bundle)

THE DYNAMIC COMPARISON RUNS ON THE PREPARED BUNDLE, and that is the whole shape
of the feature rather than a convenience: dynamic quantization reads shapes that
a preparation pass infers, the managed engine infers none, and preparation is
the Python engine's work. reduce_preprocess.py therefore writes what a quantizer
is actually fed - quant_pre_process, then ONNX shape inference, then the
onnx.infer metadata entry that records it - so the prepared bundle on disk IS
the starting graph, for either engine.

EVERY ONE OF THOSE RATIOS WAS PREDICTED BEFORE IT WAS MEASURED, from the share
of each graph that is MatMul weights - 98.0 per cent of SkyTNT's larger graph,
87.5 per cent of its smaller one and 79.6 per cent of MuPT's - and the block
arithmetic of each mode. The predictions and the measurements agree to within a
tenth of a per cent, which is what says the tools did what was asked and not
something else. validate_outputs.py prints the inventory it predicts from.

-showLiveOutput is what prints those sizes and durations; without it the runner
keeps a passing test's output to itself.

ONNX RUNTIME'S NATIVE LIBRARY LEAVES AN EMPTY mat-debug-<pid>.log IN THE
TEMPORARY DIRECTORY - one per process that loaded it, zero bytes, written by
onnxruntime's own binary and not by anything in this repository. A gated run
leaves a handful behind; they are noise, not a leak of this library's.

A CONVERSION WRITES THROUGH THE SYSTEM TEMPORARY DIRECTORY. The export lays the
whole source bundle out in a temporary folder for the tool to read and the tool
writes its output beside it, so a machine whose /tmp is a RAM-backed tmpfs needs
TMPDIR pointed at a real file system first - the same rule the large bundle
gates follow. The pass-through route writes no temporary folder at all.
Unset, the cache is

    <LocalApplicationData>/CodeBrix.Ollama/test-models

which is ~/.local/share/CodeBrix.Ollama/test-models on macOS and Linux alike -
.NET maps LocalApplicationData to the XDG path on every Unix, macOS included,
NOT to ~/Library/Application Support - and
%LOCALAPPDATA%\CodeBrix.Ollama\test-models on Windows.

IN ModelManager.Tests THIRTEEN MEMBERS ARE GATED. One is
ModelStoreLiveTests.PullAsync_FromTheRealRegistry_DownloadsResolvesListsAnd
DeletesTheModel, which pulls a GGUF model from registry.ollama.ai. The other
twelve are MusicModelLiveTests, which pull real bundles from their publishers:

    LIVE alone                      3 definitions, about 1.05 GiB together
    LIVE + MUSECOCO                 2 more, about 14.8 GiB
    LIVE + LARGE_MUSIC              7 more, about 10.4 GiB

The default run is therefore 1550 total / 1537 passed / 13 skipped as of
2026-09-17; with the live gate alone it is 1550 / 1541 / 9, and with all three
open it is 1550 / 1550 / 0. The Python tests are NOT in this suite and no gate
of theirs changes these numbers.

A THIRTEENTH DEFINITION, the ONNX-only view of the SkyTNT tv2o-medium
repository, carries the live gate but has NO live test in this suite: it exists
for the export tests in the Python project, which pull it themselves. Adding it
therefore changed none of the numbers above and none of the budgets below.

WHAT A BUNDLE LIVE TEST DOES, in one helper every one of the twelve calls:
pull the definition into a fresh TempStoreDirectory, check that the progress
stream starts at a "listing" status and ends at "success", then check the
licence ShowAsync reports and the commit the config recorded, then every
expected file's size and sha256 through ResolveAsync and on the blob itself,
then the tree MaterializeAsync writes into a second temporary directory, then
the listing, then DeleteAsync and that no blob is left behind. What each
definition expects is DATA - MusicModelDefinitions, generated from the spike
record ~/ClaudeHome/SPIKE_codebrix_ollama_music_models_2026-09-16.json - so a
publisher who moves a file, a hash or a licence tag fails a test loudly rather
than quietly.

NOTHING STAYS ON THE MACHINE. Both temporary directories are removed however
the test ends, and no model file is cached between runs: a bundle run
downloads everything it needs each time, which is the price of not keeping
gigabytes of somebody else's model files around.

THE TEMPORARY STORE MUST BE ON DISK. TempStoreDirectory sits under the system
temporary directory, and on a machine whose /tmp is a RAM-backed tmpfs - which
is most Linux desktops - a 14 GB pull would be a 14 GB allocation of memory.
Point TMPDIR at a real file system before opening the MuseCoco or the large
gate, and count on about 25 GB free transiently:

    TMPDIR=/var/tmp/codebrix-ollama-tests \
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 \
    CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS=1 \
    tests/CodeBrix.Ollama.ModelManager.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.ModelManager.Tests

Those runs belong in a backgrounded run of the BUILT EXECUTABLE with its
output kept in a log - the large gate is tens of gigabytes over a domestic
connection - never inside one short-lived tool call.

THE REGISTRY LIVE TEST REALLY DOWNLOADS TOO. It pulls smollm:135m - about
92 MB - from registry.ollama.ai into a fresh TempStoreDirectory, asserts the
progress stream starts at "pulling manifest" and ends at "success", resolves
the model, reads its GGUF metadata back, checks the resolved file's length
against the manifest layer's size, lists it, deletes it and confirms the blobs
directory is empty again.

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

TIMINGS ON THE LINUX WORKSTATION (i7-12850HX, 16 physical cores, 62 GiB, CPU
only, 2026-09-16): the whole suite with both gates open ran in 43 s with the
Qwen file warm in the page cache - a 4.9 s load and 16.5 to 17.5 tokens a
second - so the class is not slow where the hardware is not.

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
ModelManager.Tests has its pieces under Infrastructure/, and each exists so
that a whole tier can be tested without a network or a real model:

  EnvGatedFactAttribute.cs   the gate above, in two forms: one variable with
                             the value it must carry (default "1"), and a
                             string[] of variables that must ALL carry "1",
                             which is how a MuseCoco or large-bundle test asks
                             for its own gate on top of the live gate. Either
                             way it forwards the compiler-supplied file path
                             and line number to FactAttribute, so a gated test
                             keeps its location.

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

  FakeHttpHandlerBase.cs     what every fake service needs and none of them
                             should write twice: the record of the requests it
                             saw, the byte-range serving the download engine is
                             exercised against, and the failure a test injects
                             into one attempt at one range - a dropped
                             connection, a stall until cancelled, a chosen
                             status. Attempts are counted per address AND per
                             range, so several files in flight each get their
                             own attempt numbers. FakeRegistryHandler,
                             FakeHubHandler and FakeBucketHandler all derive
                             from it; a derived handler decides what lives at
                             which address and this decides how the bytes are
                             served once it has found them.

  FakeHubHandler.cs          an in-memory Hugging Face Hub: the repository
                             document with its commit, its licence tag and its
                             card, the recursive tree of one commit with
                             files, nested directories and large-file entries,
                             and the resolve addresses that answer a redirect
                             to a content delivery host which then serves the
                             bytes. Its knobs cover what the source has to
                             cope with - a gated or private repository, one
                             that answers 404 without a token, a Hub that
                             demands a token, a tree that arrives in pages.

  FakeBucketHandler.cs       an in-memory public storage bucket: the XML
                             listing of the objects under a prefix, paged when
                             a test asks for it, and the objects themselves
                             with the two x-goog-hash headers a real bucket
                             sends - a CRC32C and an MD5, both base64 - which
                             is how the md5 verification path is reached
                             offline.

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

  ProbeProcess.cs            how a test runs the probe console application:
  ProbeRun.cs                start it (the native launcher first, the dotnet
                             muxer as the fallback), hand it any environment
                             variables it needs, poll on the test's own
                             cancellation token until it ends or the wait runs
                             out, and KILL IT IN A FINALLY however the test
                             ends, so a probe that hangs can never hang the
                             suite. Draining its output happens only once the
                             process is gone, because a live child's redirected
                             stream would block the reader forever. ProbeRun is
                             what came back: whether it exited, its code, its
                             output and how long it took, with a Printed(line)
                             helper for the key: value lines the probe writes.
                             Both ModelManager test projects carry their own
                             copy, like EnvGatedFactAttribute, because no test
                             project references another

  MusicModelDefinitions.cs   the live-bundle data: thirteen BundleDefinitions
  MusicModel.cs              with, for each, the gate that opens it, the
  ExpectedBundleFile.cs      commit it is pinned to, the licence identifier
                             ShowAsync is expected to report and every file it
                             should produce with that file's size and sha256.
                             MusicModel is one such record and
                             ExpectedBundleFile one expected file. It is
                             GENERATED from the spike record
                             ~/ClaudeHome/SPIKE_codebrix_ollama_music_models_
                             2026-09-16.json, and it is the only place in the
                             repository where a particular model is named: the
                             library ships no definitions and knows nothing
                             about any of these models. Regenerate it, do not
                             hand-edit hashes into it.

THE ONNX ORACLE FIXTURES - tests/CodeBrix.Ollama.ModelManager.Tests/Onnx/
Fixtures/ - are how the managed engine is held to ONNX Runtime's own output
without ONNX Runtime being anywhere near the offline suite. Every .onnx file in
that folder was WRITTEN BY THOSE TOOLS: an input model, and beside it the file
they produce from it in one mode, named <model>.<mode>.onnx. The tests quantize
the input with the managed engine and compare the result with the file beside
it - every field, then the encoded bytes.

    generate_fixtures.py   writes every one of them, and is the only thing that
                           may. Run it with the reference virtual environment's
                           own interpreter, from that folder:

                               ~/venvs/codebrix-ollama/bin/python \
                                   generate_fixtures.py

                           It prints every file it wrote with its size. NEVER
                           hand-edit a fixture and never write one from the
                           managed engine: a fixture the port produced would
                           prove only that the port agrees with itself.

    the mode suffixes      nbits_b<bits>_bs<block>_<sym|asym>[_acc<level>] is
                           the weight-only quantizer; dyn_<i8|u8>[_pc][_rr] is
                           quantize_dynamic with its own default set of
                           operator types; dyn_i8_mm is quantize_dynamic asked
                           for MatMul and Gemm ALONE, which is what this
                           library asks it for, and is therefore the oracle a
                           real model's shape is checked against; .inferred is
                           the input after shape inference, which is what
                           quantize_dynamic hands its quantizer and what the
                           managed engine has to be given instead.

    what each one is for   the plain matmul_* models pin the packing kernel at
                           every block size, bit width and symmetry, in single
                           and half precision, including a row count that is
                           not a multiple of the block; gemm_* pin the rewrite
                           into a MatMul; unsupported_op and non_constant_b pin
                           what must be left alone; unknown_fields pins the
                           codec's opaque carrier; external_data and
                           external_gather pin a model whose weights are in a
                           file beside it; gather_transpose pins an operator
                           the wider default set would quantize and this
                           library never asks about, together with the explicit
                           default data location a surviving weight carries;
                           and branch_matmuls pins the node ORDER the tools'
                           own sort produces, which a graph with a branch does
                           not come in with.

THE PROBE CONSOLE APPLICATION - tests/CodeBrix.Ollama.ModelManager.Probe - is
the piece that is NOT a test project and not packed. It exists because two of
the things this repository has to be sure of are facts about a WHOLE PROCESS,
and neither can be observed from inside a test run that has already done other
things:

  inert        creates a store in a temporary folder, writes a small folder of
               its own, imports it, lists, resolves and materializes it - the
               whole of what a consumer does to obtain a model - and then walks
               AppDomain.CurrentDomain.GetAssemblies() and prints
               "loaded: False". It is the fence for the inert dependency, it
               needs no Python of any kind, and the DEFAULT suite runs it on
               every machine.
  export       imports a bundle holding an .onnx file and a checkpoint, exports
               it with the automatic route, and asks the same question of the
               same process. The pass-through route converts nothing, so a
               WHOLE EXPORT must also print "loaded: False" - which is the
               fence for the claim that obtaining and exporting a publisher's
               own graphs need no Python at all. The DEFAULT suite runs it too.
  reduce-managed
               imports a bundle holding one real graph, reduces it to four-bit
               weights through the MANAGED engine, and asks the inert question
               of that process: "loaded: False" after a whole quantization is
               the fence for the promise that making an existing graph smaller
               needs nothing installed. The graph is named by
               CODEBRIX_OLLAMA_PROBE_MODEL, and the DEFAULT suite runs this
               mode over one of the checked-in ONNX fixtures, so it needs no
               Python and no network on any machine.
  reduce       the same shape of run through the PYTHON engine, named outright:
               what it observes is the opposite fact, that a process which
               started an interpreter to quantize a graph still ends by itself.
               The Python suite runs it, over a graph make_probe_model.py
               writes.
  own          this library starts the interpreter, owns it, shuts it down and
               is then refused further Python work. Prints the owner and the
               message that refusal carries.
  hostowned    the probe itself starts an interpreter first, the way a host
               application would, and then asks this library to Check. The
               owner must be Host, and PythonSupport.Shutdown() must leave the
               interpreter RUNNING.
  noshutdown   this library starts the interpreter and nobody shuts it down.
               The process must still end, which is what the bounded
               process-exit mode is for; it takes about five seconds to do it.
  hostcode     the probe starts an interpreter naming its own environment in
               code, with every variable this library reads cleared first. The
               report must then say LibrarySource Host and still carry a path -
               the one the running interpreter states about itself - because
               nothing this library reads could have named it.

The four ownership modes and reduce are run by the Python suite, one child
process each, and are gated with everything else there; inert, export and
reduce-managed are run by the default suite on every machine. Each mode prints
one "key: value" line per fact and ends with READY, so a test asserts on lines
rather than on parsing.
An unknown mode exits 2.

PythonTestFixture in ModelManager.Python.Tests is the assembly fixture that
owns that suite's one interpreter: it checks the gate in its constructor and
does nothing at all when the gate is closed, calls PythonSupport.Check once
when it is open, and calls PythonSupport.Shutdown() exactly once in Dispose.
Nothing else in that assembly shuts Python down, and the refusal that follows a
shutdown is watched from the own probe rather than in-process, because in the
suite's own process it would end the interpreter every later test needs.

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

THE DEPENDENCY GROUP IS A RELEASE CHECK. After packing, confirm that each
nuspec still carries exactly what it should, and nothing more:

    unzip -p <nupkg> '*.nuspec'

ModelRunner:

    <dependencies><group targetFramework="net10.0" /></dependencies>

ModelManager - ONE dependency, and only this one:

    <dependencies>
      <group targetFramework="net10.0">
        <dependency id="CodeBrix.Python.MitLicenseForever" version="..."
                    exclude="Build,Analyzers" />
      </group>
    </dependencies>

Anything else in either group means something added a PackageReference to a
library project, and the promise both AGENT-READMEs make is broken.

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

The Go STANDARD LIBRARY files that ModelRunner ports DO carry one, and the
family rule is to preserve an upstream header verbatim whenever there is one
(and never to write one when there is not). So the files under
src/CodeBrix.Ollama.ModelRunner/Templates/OllamaGo/ whose marker names golang/go
begin with the three-line "Copyright 20xx The Go Authors. All rights reserved."
notice exactly as their upstream file has it, then one blank line, then the
using block or the namespace line. Nothing else in either library carries a
header, because no other upstream file does.
ModelManager ALSO PORTS ONNX RUNTIME, and that is a second upstream in the same
library: the eight files under Onnx/Quantization/ are a hand port of ONNX
Runtime's quantization tools at tag v1.30.0 - matmul_nbits_quantizer.py,
quantize.py, onnx_quantizer.py, quant_utils.py and onnx_model.py, with
base_quantizer.py, registry.py, operators/matmul.py and operators/gemm.py read
into them, and core/mlas/lib/q4_dq.cpp for the packing kernel itself (its
header, q4common.h and the pybind wrapper were read beside it). Those files
keep the upstream MIT header VERBATIM above their using block, because ONNX
Runtime's sources carry one and the family rule is to preserve a header
wherever there is one; their marker names onnxruntime/<path>@v1.30.0. Entry 15
of THIRD-PARTY-NOTICES.txt is the full record, and

    grep -rl "was previously: onnxruntime/" src/

must list exactly those eight files. THE REST OF Onnx/ IS NOT A PORT: the
protocol buffer reader and writer and the ONNX message classes were written
against the published onnx.proto schema and carry no marker, which is correct
and is not an oversight.

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
FIFTEEN numbered entries: 1 llama.cpp and ggml, 2 Intel's SYCL and OpenVINO
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

BUNDLE MANIFESTS AND OLLAMA INTERCHANGE - WHAT WAS CHECKED FIRST
----------------------------------------------------------------
A bundle's manifest goes in the SAME manifests tree as a GGUF model's and its
blobs in the same blobs directory, with one layer per file carrying a media
type of this library's own, application/vnd.codebrix.model.file. That was only
done after Ollama's own source was read at the commit this port is pinned to,
a43fad18, to see what such a manifest does to an Ollama install sharing the
directory:

  - `ollama list` reads only the config blob, so it lists a model it could
    never load;
  - `ollama show` reads a weights file only when a GGUF model layer exists,
    and a bundle has none;
  - the layer walk has NO DEFAULT CASE, so a media type it does not know is
    carried along and ignored rather than rejected;
  - `ollama rm` prunes generically, by digest, so it removes a bundle cleanly;
  - `ollama run` fails on such a model, which is the right answer - there is
    nothing there for it to load;
  - the name grammar accepts every name shape a bundle uses, the host "local"
    included.

The files read were server/model_list.go (listModels, describeModel),
server/images.go (GetModel), server/routes.go (GetModelInfo),
manifest/manifest.go (Manifests, ParseNamedManifest), manifest/layer.go and
types/model/name.go (isValidPart, isValidLen). Jeremy installs Ollama nowhere -
this repository is what replaces it - but another user may well run both
against one store, and the compatibility costs nothing. A future Ollama release
could tighten its manifest reading; the family's periodic re-sync of the
reference commit is what would surface it.

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

  - ZERO NUGET DEPENDENCIES IN ModelRunner, AND EXACTLY ONE IN ModelManager.
    In ModelManager all JSON goes through the in-box System.Text.Json via
    Common/ModelManagerJson.cs, HTTP through HttpClient and SocketsHttpHandler,
    and hashing through System.Security.Cryptography; in ModelRunner,
    System.Text.Json again, plus Parsing/CompactJson.cs where the exact
    byte-for-byte shape of what is written matters. This is not a preference:
    it is verifiable in the packed nuspec, and it is why there is no
    Microsoft.Extensions.AI adapter, no OllamaSharp reference and no LLamaSharp
    reference anywhere.

    THE ONE EXCEPTION is ModelManager's reference to CodeBrix.Python, which the
    Python corner of the library needs and which the family rule allows because
    it is a CodeBrix.* package. It is INERT, and that is a rule with a fence
    rather than an intention:

      * Python/PythonHost.cs is THE ONLY FILE IN THE LIBRARY ALLOWED TO NAME A
        CodeBrix.Python TYPE - in a using directive, in a signature or in a
        body. Everything public talks to it through .NET-only types, so the
        runtime loads that assembly when it compiles a method of PythonHost and
        at no other time.
      * `grep -rn --exclude-dir=bin --exclude-dir=obj "CodeBrix.Python" src/`
        must match Python/PythonHost.cs, the csproj and documentation text,
        and nothing else.
      * The fence is a TEST, not a convention: PythonInertFenceTests runs the
        probe console application, which imports a folder, lists, resolves and
        materializes it in a process of its own and then asks
        AppDomain.CurrentDomain.GetAssemblies() whether anything named
        CodeBrix.Python is loaded. It must print "loaded: False". That check
        cannot be made in-process, because by then the suite has already used
        Python for other tests.
      * The three "zero dependency" statements that used to be in this file,
        README.md and the ModelManager csproj Description were reworded when
        this landed; keep them honest.

    PythonSupport is also the one deliberate exception to the async-only rule
    below. An interpreter belongs to a process, its lifetime calls are
    synchronous by nature, and nothing may be awaited while the interpreter
    lock is held, so Check, Require and Shutdown are synchronous. The internal
    script runner is async, because waiting for the one-run-at-a-time lock is
    real waiting; the run itself, once the lock is taken, is synchronous
    throughout.

  - THE LIBRARY WRITES OUTSIDE THE STORE IN EXACTLY TWO PLACES, and they are
    the export and the reduction: each lays the source bundle out in a folder
    under the SYSTEM TEMPORARY DIRECTORY for the tools to read, lets them write
    their output beside it, collects that into the store and removes the whole
    folder in a finally. Nothing else in either library creates a temporary
    file of its own - a download's sidecars live beside the blob they belong
    to - and nothing ever writes to the current directory. A machine whose
    temporary directory is RAM-backed needs TMPDIR pointed at a real file
    system before exporting or reducing anything of size; that is the caller's
    decision and the AGENT-README says so.

  - NO NEW THIRD-PARTY NUGETS ANYWHERE, per the family rule: CodeBrix.* and
    Microsoft packages are fine; xUnit and SilverAssertions in the .Tests
    projects are the standing exception.

  - THE P/INVOKE RULES. Nearly every platform call in this repository lives
    in ModelRunner's Native/ folder and the rules below are written for it;
    ModelManager makes one small platform call of its own, to make a hard
    link, and the first two rules reach it as well:

      * [LibraryImport], NEVER [DllImport], in both libraries. Every import
        is source-generated, so its marshalling is visible in generated code
        rather than inferred at run time; both libraries enable unsafe code,
        which the generator needs. The only DllImport-shaped thing in the
        tree is NativeLibrary.SetDllImportResolver in ModelRunner's
        NativeLibraryLoader.cs, which is what finds runtimes/<rid>/native/ in
        the first place.
      * A PLATFORM CALL IN ModelManager ANSWERS, IT DOES NOT THROW. The one it
        makes says "it worked" or "it did not" and catches every failure the
        runtime can raise on the way, because the caller's next step - copy
        the bytes instead - is the same whatever went wrong.
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
      * unsafe IS FOR THE BINDING and for the engine code that walks a logits
        array or a batch, and nowhere else. It is not a licence to use it in a
        parser or a template engine. ModelManager enables it too, because the
        source generator behind [LibraryImport] needs it, and for nothing else.
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
    //Arrange //Act //Assert comments (or the combined //Act and assert), a
    single-statement test is expression-bodied (=> x.Should().Be(y);), and
    TestContext.Current.CancellationToken is passed to every cancellable call
    (xUnit1051). Assertions are SilverAssertions fluent style throughout -
    .Should().Be(), .BeNull(), .BeTrue(), .HaveCount(), .BeEmpty() - and an
    expected exception is an Action or Func<Task> named under //Arrange or
    //Act with act.Should().Throw<T>() / await act.Should().ThrowAsync<T>()
    under //Assert (.Which reaches the exception). No raw Assert.* calls.

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
Everything below was done on the Intel Mac mini (2018), i7-8700B, macOS 15.8,
x86_64, .NET SDK 10.0.401, except where a bullet names another machine. (Later
the same day, on the Windows 11
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
    running the built executable directly. That was the suite before the
    bundle work; the counts to expect now are under THE ENVIRONMENT GATE.
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
  * ModelManager'S LIVE PULL TEST, RUN FOR THE FIRST TIME on 2026-09-16, on the
    Linux workstation, with CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1: the whole
    ModelManager suite 789 total / 789 passed / 0 skipped in 12.5 s, the one
    gated test pulling smollm:135m from registry.ollama.ai into a temporary
    store, resolving it, reading its GGUF metadata back, listing it and
    deleting it. ModelRunner's two SmolLM live classes ran on the same machine
    the same day, through the built executable: 1258 total / 1249 passed /
    9 skipped (the Qwen class) in 46 s, including the 368 MiB download into
    a cache that had been empty.
  * ModelManager'S FIRST LIVE BUNDLE RUN, 2026-09-16, on the Linux
    workstation, with CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1: the three gate-A
    definitions pulled from their publishers - two Hugging Face file
    repositories and one public storage bucket - 1,132,311,807 bytes in 75 s.
    Every pinned size, every pinned sha256, the commit each Hugging Face pull
    resolved to and the licence identifier each ShowAsync reported matched the
    definitions exactly; each bundle was then resolved, materialized as its
    publisher's file tree and deleted, and the store's blobs directory was
    empty afterwards. Later the same day all three gates were opened together
    on the same machine and all twelve definitions passed the same checks:
    28,218,571,513 bytes (26.3 GiB) in 1,416 s, about 20 MB/s, the 14.5 GB
    MuseCoco checkpoint included, with the temporary store on disk (TMPDIR)
    because /tmp there is RAM-backed. Nothing was left on the machine.
  * ModelRunner'S LIVE TESTS ON linux-x64, 2026-09-16, BOTH GATES OPEN, on the
    Linux workstation (16 physical cores, 62 GiB): 1258 total / 1258 passed /
    0 skipped in 43 s, as the built executable with -showLiveOutput. The Qwen
    file was fetched by hand into the cache first (20.5 GiB in 17 minutes,
    sha256 verified), the SmolLM file by the suite itself. Qwen 3.5 35B-A3B
    Q4_K_M loaded in 4.9 s warm (0.7 s without the extra buffer types),
    generated at 16.5 to 17.5 tokens a second on the default thread count
    (the physical cores), evaluated a 288-token tool prompt in 4.5 s, answered
    the tool call as get_weather({"city":"Paris"}), and kept its reasoning
    apart from a one-word answer. The engine reported a 20,985.64 MiB
    CPU_Mapped model buffer plus an 11,520 MiB CPU_REPACK buffer with the
    extra buffer types on.
  * THE WHOLE STACK ON linux-x64, 2026-09-16, on a Debian-family x64
    workstation (i7-12850HX, 16 physical cores, 62 GiB, no GPU, .NET SDK
    10.0.401): `dotnet build CodeBrix.Ollama.slnx -c Release --no-incremental`
    0 warnings / 0 errors; the ModelManager executable 789 total / 788 passed
    / 1 skipped; the ModelRunner executable 1258 total / 1228 passed / 30
    skipped - which means the binding loaded the committed libcodebrix_llama.so
    out of runtimes/linux-x64/native/, the NativeDefaultsTests size checks
    passed against that compiler's layouts, and the conformance logits matched
    EXPECTED.txt. `dotnet test --solution CodeBrix.Ollama.slnx -c Release
    --no-build` on that SDK found and ran all 2046 (2015 passed, 31 skipped).
  * Both packages packed and unzipped. Each carries the five packed root files
    and lib/net10.0, and the ModelRunner package carries all seven
    runtimes/<rid>/native/ folders - fourteen entries - with no doubled path
    (2026-09-16). The dependency groups were re-checked on 2026-09-17:
    ModelRunner's is still empty, and ModelManager's carries exactly one entry,
    CodeBrix.Python.MitLicenseForever.
  * THE PYTHON CORNER, 2026-09-17, on the Linux laptop (Debian 13, x86_64,
    .NET SDK 10.0.401, system CPython 3.13.5 at
    /usr/lib/x86_64-linux-gnu/libpython3.13.so, apt-managed). The virtual
    environment ~/venvs/codebrix-ollama - created from /usr/bin/python3 and
    holding onnx, onnxruntime, onnxruntime-genai, torch (CPU build),
    transformers, optimum, optimum-onnx, onnx-ir, huggingface_hub, safetensors,
    tokenizers and numpy - was reached by the EMBEDDED interpreter with nothing
    but CODEBRIX_OLLAMA_PYTHON_VENV set: sys.prefix came back as the
    environment, sys.base_prefix as the base installation, and onnx,
    onnxruntime, torch and transformers all imported.
      - `dotnet build CodeBrix.Ollama.slnx -c Release --no-incremental`
        0 warnings / 0 errors.
      - ModelManager.Tests 1550 / 0 failed / 13 skipped, about three seconds,
        including the inert fence, which needs no CPython.
      - ModelRunner.Tests 1258 / 0 failed / 30 skipped, unchanged.
      - ModelManager.Python.Tests with the gate closed: 43 / 0 / 43 skipped in
        under a tenth of a second, no interpreter started. With the Python gate
        open and the environment named: 43 / 0 / 12, about ten seconds. With
        the live gate as well: 43 / 0 / 0 in under two minutes.
      - EVERY PROBE MODE, run by hand as the built console application on
        2026-09-17 at the end of the work: inert 0.12 s, export 0.09 s and
        reduce-managed 0.11 s (all three printing "loaded: False", none of them
        needing a CPython), own 0.22 s, hostowned 0.22 s, hostcode 0.22 s
        (reporting LibrarySource Host with a path), reduce 1.36 s (a real graph
        quantized to four-bit weights through the Python engine, the
        interpreter shut down afterwards) and noshutdown 5.11 s - the last
        being the bounded process-exit timeout doing exactly what it is there
        for. All eight exited 0 on their own; none was killed.
      - `grep -rn --exclude-dir=bin --exclude-dir=obj "CodeBrix.Python" src/`
        matches Python/PythonHost.cs, the csproj and documentation text, and
        nothing else.
  * EXPORTING TO ONNX, 2026-09-17, on the same Linux laptop, with the Python
    gate and the live gate open and TMPDIR on a real file system.
      - THE PASS-THROUGH. hf.co/skytnt/midi-model-tv2o-medium:onnx-only - the
        publisher's two graphs with their two configuration files and the model
        card, 938,378,632 bytes - was pulled and exported with the AUTOMATIC
        route. The route chosen was PublisherOnnx, the derived bundle
        ...:onnx appeared with all five files, and every one of them matched
        the source by size and by sha256 BECAUSE IT IS THE SAME BLOB: the
        export copied nothing and took under a twentieth of a second. The
        provenance read back through ShowAsync named the source, the tool
        "publisher" and the route; the licence came over as apache-2.0; and the
        bundle listed, resolved and materialized like any other.
      - THE GenAI BUILDER, on hf.co/m-a-p/MuPT-v1-8192-190M (380,166,726-byte
        checkpoint, LlamaForCausalLM), with onnxruntime-genai 0.15.2 and
        AllowRemoteCode set because the checkpoint ships its own tokenizer
        class. fp32 wrote 763,970,549 bytes in 2.7 s and int4 252,221,771 bytes
        in 1.0 s, each as model.onnx with model.onnx.data, genai_config.json
        and the tokenizer files beside them. The builder did NOT refuse the
        model, so the Optimum fallback was not needed for it.
      - The automatic route was checked against the same checkpoint's real
        config.json: architectures names LlamaForCausalLM, and the choice is
        GenAiBuilder.
      - THE OPTIMUM ROUTE, on the same MuPT checkpoint: optimum 2.1.0 traced it
        and wrote model.onnx with config.json and generation_config.json,
        760,584,810 bytes. Optimum prints its own validation warning on this
        model - the exported logits differ from the checkpoint's by 2.96e-05
        against a 1e-05 tolerance - and exports it anyway; that is Optimum's
        judgement, not this library's, and it is why the GenAI builder is the
        automatic choice for an architecture the builder writes.
      - The whole gated suite passed with both gates open, the first run paying
        for 1.23 GiB of downloads and later runs for nothing; the counts and
        timings are under THE ENVIRONMENT GATE.
  * REDUCING AN ONNX MODEL, 2026-09-17, on the same Linux laptop, with both
    gates open and TMPDIR on a real file system. onnxruntime 1.30.0 did the
    arithmetic through the embedded interpreter; every size, ratio, duration
    and difference is in the table under THE ENVIRONMENT GATE.
      - ALL FOUR MODES on SkyTNT's published pair, reduced from the
        PASS-THROUGH bundle rather than from the pulled one, which is what says
        a derived bundle can be derived from again: 894.9 MiB of graphs became
        145.1 MiB at four bits, 254.1 MiB at eight, 245.6 MiB dynamically, and
        stayed the same size when only prepared. Each derived bundle listed,
        showed its provenance, resolved and materialized, and carried the model
        card and the two configuration files through untouched.
      - ONE GRAPH OF TWO, reduced on the bundle AS IT WAS PULLED, with no
        export anywhere: model_token.onnx became four-bit and model_base.onnx
        came through with the same sha256 it was pulled with.
      - THE MuPT GRAPH the builder writes at fp32, which keeps its weights in a
        file beside it: 727.2 MiB became 226.0 MiB at four bits and 293.3 MiB
        dynamically.
      - EVERY RATIO WAS PREDICTED FIRST from the share of each graph that is
        MatMul weights, and every prediction was right to within a tenth of a
        per cent. The two engines' defaults were pinned against each other by a
        test that reads the managed engine's own option classes.
      - The probe's reduce mode quantized a real graph in a process of its own,
        shut the interpreter down and exited 0 - which is what says a process
        that has run a quantizer still ends by itself.
  * THE MANAGED ENGINE AGAINST THE PYTHON ONE, ON REAL MODELS, 2026-09-17, same
    laptop, both gates open. Six comparisons - SkyTNT's published pair and the
    MuPT fp32 export, each in WeightOnlyInt4, WeightOnlyInt8 and DynamicInt8 -
    each reducing the same bundle twice and comparing what came out.
      - ALL NINE FILES ARE BYTE FOR BYTE IDENTICAL between the two engines, and
        every one of them is the size the Python engine wrote in Phase 6. The
        comparison is not only of the bytes: the structure, every node, every
        attribute and every initializer are compared field by field first, so a
        failure names what drifted rather than an offset.
      - BOTH RESULTS WERE THEN RUN. validate_outputs.py ran each pair of
        reduced graphs over the same deterministic inputs, and the managed
        engine's numbers equal the Python engine's to the last digit printed -
        the same worst absolute difference, the same relative one and the same
        top-1 agreement, which are also the numbers recorded in the table under
        THE ENVIRONMENT GATE.
      - THE AUTOMATIC CHOICE REACHED THE MANAGED ENGINE in all six, which is
        the rule being tested and not an incidental: weight-only always, and
        dynamic because the prepared bundle records its shape inference.
      - A WHOLE MANAGED REDUCTION LOADS NO CodeBrix.Python. The probe's
        reduce-managed mode ran an import, a four-bit reduction and a
        materialize in a process of its own and printed "loaded: False"; the
        default suite runs it on every machine, over a checked-in fixture.
      - THE BIGGEST GRAPH, SkyTNT's 821,713,887-byte model_base.onnx, was read,
        quantized and written by the managed engine in 3.9 s at a peak of 2.9
        GiB resident, measured outside the suite with the probe.
  * THE RELEASE CHECK AT THE END OF THE ONNX WORK, 2026-09-17, Debian 13
    laptop, .NET SDK 10.0.401, run in one sitting: `dotnet build
    CodeBrix.Ollama.slnx -c Release --no-incremental` 0 warnings / 0 errors;
    ModelManager.Tests 1550 / 0 / 13; ModelRunner.Tests 1258 / 0 / 30;
    ModelManager.Python.Tests 43 / 0 / 43 with the gates shut and 43 / 0 / 0
    with both gates open; every probe mode exiting on its own; both nupkg files
    unzipped - the five packed root files, lib/net10.0 with its assembly and
    XML documentation, ModelRunner's dependency group empty and ModelManager's
    carrying CodeBrix.Python.MitLicenseForever and nothing else.
  * The osx-x64 native: built, full gate passed (architecture, 248/248 required
    exports, exact export surface, install name, system-only dependencies,
    minos 13.3, signature, smoke test, byte-identical model regeneration, and
    conformance at max |diff| 4.98e-08), adopted, twin and dSYM stored. The
    osx-arm64 native was built and gated the same way on the Apple Silicon
    Mac mini, including the Metal conformance pass; its record is in
    BUILD-PROVENANCE.txt.

WHAT HAS NOT BEEN VALIDATED
---------------------------
  * EXPORTING MuseCoco - ATTEMPTED 2026-09-17 AND BLOCKED, and the reason is
    the model, not this library. The smaller half of the pipeline,
    hf.co/XinXuNLPer/MuseCoco_text2attribute (1.25 GiB, pulled and kept in the
    export store), is a BERT-large checkpoint whose config.json names the
    architecture "BertForAttributModel" - a class that lives in the MuseCoco
    project's own source tree and NOT in transformers, and NOT in the
    checkpoint either, so trust_remote_code has nothing to import. Optimum
    therefore refuses it before any tracing, saying it cannot infer a task from
    a local directory and listing every task it knows. Naming a task by hand
    would be worse than the refusal: transformers would fall back to the plain
    BERT model for that model_type and quietly load a different head. The other
    half, MuseCoco_attribute2music, is a bare 13.5 GiB fairseq .pt with no
    configuration file at all, and fairseq is not installed in the virtual
    environment and is not to be installed by an agent; it was therefore NOT
    downloaded, because nothing about it could have succeeded that the smaller
    half did not already settle. Exporting MuseCoco is not promised by any
    route and no test names it.
  * EXPORTING MAGENTA'S MUSIC TRANSFORMER - NOT POSSIBLE IN THIS VIRTUAL
    ENVIRONMENT, checked 2026-09-17 without installing anything. The definition
    is a TensorFlow 1 checkpoint triple (.ckpt.data / .ckpt.index / .ckpt.meta)
    and the route for it would be tf2onnx over a restored TF1 graph, which
    needs the archived tensor2tensor operators as well. None of tensorflow,
    tf2onnx, tensor2tensor, magenta or note_seq is in ~/venvs/codebrix-ollama,
    and the plan's rule is that an agent installs nothing. The automatic route
    sends such a bundle to Optimum, which cannot read a TF1 checkpoint either,
    so the honest state is: no route, and none promised.
  * THE OPTIMUM ROUTE ON ANYTHING BUT A DECODER-ONLY MODEL. The task an export
    asks Optimum for is worked out from the architecture suffix in the
    checkpoint's config.json, and only the text-generation case has been run
    against a real model. The other suffixes are unit-tested mappings, not
    exports that happened.
  * EXPORTING ON WINDOWS OR macOS. Every export in this repository has been run
    on Linux only. Nothing in the feature is platform-specific - the temporary
    folder comes from the system temporary directory and the tools are the
    publisher's own - but it has not been watched anywhere else.
  * THE ModelManager SUITE ON WINDOWS - RUN ONCE, 2026-09-15; THE ModelRunner
    SUITE ON WINDOWS - NEVER. On the Windows 11 x64 machine (.NET SDK
    10.0.401) the solution built 0 warnings / 0 errors in Release, and the
    built executable
    tests/.../bin/Release/net10.0/CodeBrix.Ollama.ModelManager.Tests.exe ran
    789 total / 788 passed / 1 skipped in 3.7 s - the same result as macOS
    and Linux, so the Path.Combine argument now has evidence behind it.
    `dotnet test tests/CodeBrix.Ollama.ModelManager.Tests -c Release` on that
    machine reported "Zero tests ran", exit code 5, with and without
    --no-build. That is a known Microsoft.Testing.Platform behaviour across
    the CodeBrix family, not a fault in this repository: on some SDK and
    machine combinations the runner mode that global.json selects finds no
    tests in an xunit.v3 project, while the same assembly run as the
    executable runs every one. The executable's counts are authoritative;
    quote those. (On the Linux workstation the same SDK found and ran all
    2046 through `dotnet test --solution`, so it varies by machine.) The
    ModelRunner suite's first Windows run is also the first execution of the
    Windows branch of Engine/EnginePhysicalCores.cs
    (GetLogicalProcessorInformationEx filtered to RelationProcessorCore),
    which was written on Linux, compiles everywhere and has run nowhere;
    ParameterMapperTests.ResolveThreads_falls_back_to_the_physical_core_count
    is the test that exercises it, and on a hyper-threaded machine the count
    it reports should be half of Environment.ProcessorCount.

  * THE MANAGED ENGINE ON A GRAPH TOO LARGE FOR ONE MESSAGE. It decides to
    write the weights into a file of their own by the same rule the Python
    engine uses - the size of the graph it was handed, plus a second look at
    what it is about to write - and both halves of that rule are unit-tested,
    but no graph in this repository is anywhere near the two-gibibyte limit, so
    the branch has never been taken by a real model. The largest real graph run
    through it is 822 MB.
  * THE MANAGED ENGINE ON WINDOWS OR macOS. It is pure managed code with no
    platform call of its own, and the offline oracle comparison would catch a
    difference immediately, but every run so far has been on Linux.
  * A SUB-GRAPH, A bfloat16 WEIGHT, A RANK-THREE WEIGHT OR A TRANSPOSED
    NON-CONSTANT GEMM through the managed engine. Each is refused with a
    message naming it, each refusal is unit-tested, and no real model here has
    one - so what the Python engine makes of those graphs is known and what the
    managed engine would make of them is not, because it declines to.

  * THE WIN-ARM64 NATIVE SLICE HAS NEVER BEEN EXECUTED: cross-built and
    statically checked on x64, adopted by decision, as described under THE
    NATIVE LIBRARIES. Its first run on real ARM64 hardware is its smoke test.
    The three Linux slices were built and gated on 2026-09-15 (arm64 and
    riscv64 under qemu-user emulation) but have not yet run on real arm64 or
    riscv64 hardware. win-x64 is fully gated and adopted.

  * FIVE OF THE SEVEN NATIVES HAVE NEVER BEEN LOADED FROM .NET. ModelRunner's
    suite has run on two runtime identifiers - osx-x64 on the Intel Mac mini
    and linux-x64 on a Debian-family workstation, both CPU only, no Metal.
    osx-arm64, linux-arm64, linux-riscv64, win-x64 and win-arm64 have been
    verified only by llama-native-tools' own gate - by C programs - and
    win-arm64 not even by that. The first thing to run
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

THREE DESIGN CHOICES, SO THEY ARE NOT MISTAKEN FOR OVERSIGHTS
-------------------------------------------------------------
  - THE PACKAGES DO NOT REFERENCE EACH OTHER. It would be convenient for
    ModelRunner to take a model name and resolve it, and that is precisely what
    is not wanted: coupling them would force every consumer of one to carry the
    other, and would put a registry client inside an application whose model
    files may have arrived by any means at all. The composition is the
    consumer's - ModelManager resolves a name to a GGUF path, ModelRunner loads
    a path.

  - BOTH REDUCTION ENGINES ASK THE SAME QUESTION ABOUT EXTERNAL DATA, and the
    managed one asks it of the INPUT rather than of its own output. A graph too
    large for one protocol buffer message keeps its weights in a file beside it,
    and the Python tools decide that from the graph they were handed; if the
    managed engine decided it from what it is about to write, the two engines
    would write the same model as a different set of FILES - one file against a
    graph and a file of weights - and the bit-for-bit comparison that the
    managed engine exists to pass would fail on a model neither engine got
    wrong. So the managed engine asks the Python engine's own question about the
    same input, and keeps a second guard of its own for an output that could not
    be one message. Quantizing never grows a model, so the second can only fire
    where the first already has.

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
