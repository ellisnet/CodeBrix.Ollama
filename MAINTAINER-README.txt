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

THE STATE OF THINGS, 2026-09-18
-------------------------------
  * ModelManager is written and tested.
  * ModelManager NOW CONVERTS A CHECKPOINT TO GGUF, as of 2026-09-18, and does
    it with NOTHING INSTALLED: ConvertToGgufAsync reads the weights a publisher
    ships - safetensors or a PyTorch zip pickle, one file or several shards -
    with this library's own readers (the pickle through a RESTRICTED
    interpreter that never imports and never evaluates), maps the tensors on to
    the names the inference engine expects, reads a GPT-2 byte-level or
    SentencePiece tokenizer out of the publisher's files, and writes an
    ordinary GGUF model into the same store. No CPython and no publisher
    tooling take any part in it. The port is byte-for-byte: every checked-in
    fixture is compared with what the inference engine's OWN converter produced
    from the same folder, over the whole file, and a real 190M checkpoint
    converted through the store matched the engine's output in all
    381,878,656 bytes on 2026-09-18. The plan is
    ~/ClaudeHome/PLAN_codebrix_ollama_pytorch_to_gguf_2026-09-17.md; phases G0
    to G5 are done.
  * BOTH PACKAGES NOW QUANTIZE, as of 2026-09-18, and THIS IS THE FIRST CHANGE
    TO ModelRunner SINCE IT WAS WRITTEN. ModelRunner.QuantizeAsync rewrites a
    GGUF file at a smaller type over the native llama_model_quantize call that
    was already bound and already in the package - no new dependency, nothing
    new to install - and the file it writes is byte for byte what the inference
    engine's OWN command-line quantizer writes, measured over fifteen
    comparisons on 2026-09-18 (see QUANTIZING (maintainer) below).
    ModelManager.QuantizeGgufAsync stores the result as a model of its own and
    takes the quantizer as a CONSUMER-SUPPLIED DELEGATE, so the two libraries
    still do not reference each other in any direction or at any level; they
    meet in the consumer's code and in tests/CodeBrix.Ollama.EndToEnd.Tests.
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
    is 46 test classes and 1,286 test cases as of 2026-09-18. Its
    AGENT-README.txt at the repository root is a full consumer guide and no
    longer a placeholder.
  * ModelRunner NOW RUNS ONNX MODELS WITH NOTHING INSTALLED, and as of
    2026-09-18 it GENERATES TEXT from one as well as music. The managed
    interpreter under Onnx/ runs 41 operators, contributed and quantized ones
    included, and two DRIVERS sit on it: Drivers/SkyTnt/ for the two-graph MIDI
    model (MidiGenerationModel) and Drivers/CausalLm/ for a single-graph decoder
    described by a genai_config.json (OnnxCausalLmModel, which hands back an
    IRunningModel, so the same calls that complete a prompt against a checkpoint
    work against a bundle). The tokenizer is managed too - a GPT-2 byte-level
    byte-pair encoder read out of the bundle's own vocab.json and merges.txt -
    so no runtime, no Python and no other package take any part in it. Neither
    driver knows anything about any particular model: what to do is read out of
    the bundle. Proved on 2026-09-18 against the bundle's own runtime: greedy
    generation from a 190M model, token for token identical over three prompts.
    The plan is ~/ClaudeHome/PLAN_codebrix_ollama_modelrunner_onnx_2026-09-17.md;
    phases O0 to O4 are done.
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
    src/CodeBrix.Ollama.Core/          THE SHARED PROJECT, never packed on its
                                 own. Both libraries reference it and both pack
                                 its DLL inside their own package; it
                                 references neither of them, no other project
                                 and no NuGet package. Everything in it is
                                 internal. See THE Core PROJECT below
      CoreContract.cs            the PERMANENT compatibility guard: the
                                 revision constant, the revision of the loaded
                                 copy, and Require, which turns a mixture of
                                 package versions into one clear sentence
      Onnx/                      the hand-written ONNX codec: the message
                                 classes, OnnxModel (reading, writing, external
                                 data), OnnxSaveOptions and OnnxMetadataProbe,
                                 which answers whether a graph records having
                                 been through shape inference by walking the
                                 outermost message alone. Protobuf/ is a
                                 forward-only reader, an append-only writer and
                                 the carrier that re-emits every field the
                                 codec does not model, so a file read and
                                 written back is byte for byte what it was
      Tokenizers/                the tokenizer PRIMITIVES both sides need:
                                 Gpt2MergeTable, which reads a GPT-2
                                 byte-level BPE merges.txt
      InternalsVisibleTo.cs      grants the two libraries, this project's own
                                 test suite, and the two ModelManager test
                                 assemblies that build graphs out of the
                                 codec's message classes

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
      Checkpoints/               reading what a training framework wrote:
                                 ICheckpointReader and CheckpointTensor,
                                 CheckpointDataType / CheckpointDataTypes,
                                 CheckpointRegionStream, SafetensorsReader,
                                 PyTorchZipReader with the RestrictedUnpickler
                                 and the four values it is allowed to build
                                 (PickleStorage, PickleTensor, PickleGlobal,
                                 PickleGlobalReference), CheckpointShardIndex
                                 and CheckpointWeights - the composite reader
                                 over one part or many, which is also where the
                                 order the parts are visited in lives. All
                                 internal
      Convert/                   turning a checkpoint into a GGUF file: the
                                 public ConvertOptions, ConvertResult,
                                 GgufOutputType and CheckpointArchitecture, and
                                 the internal GgufConversion (the conversion
                                 itself), GgufConvert (what the STORE needs to
                                 know about one - the tag, the refusals, the
                                 synthesized Modelfile, the provenance
                                 settings) and TensorDataConverter (widening,
                                 narrowing and the rotary permutation).
                                 Architectures/ holds LlamaArchitecture, the
                                 ported LlamaTensorNameMap and
                                 HuggingFaceConfig; Metadata/ holds ModelCard
                                 (a front-matter reader of our own),
                                 ModelIdComponents and ModelMetadata;
                                 Tokenizers/ holds VocabularyExport (the ROUTE
                                 order), Gpt2BpeTokenizerExport,
                                 SentencePieceTokenizerExport with
                                 SentencePieceModel / SentencePiecePiece /
                                 SentencePieceTokenType, TokenizerConfig,
                                 TokenizerExport, SpecialVocabulary, AddedToken
                                 and GgufTokenType. Most of it is the subject
                                 of entry 1 of THIRD-PARTY-NOTICES.txt
      Quantize/                  storing a quantized copy of a GGUF model the
                                 CONSUMER's own quantizer writes: the public
                                 QuantizeGgufOptions, QuantizeGgufResult and the
                                 GgufQuantizer delegate, and the internal
                                 GgufQuantize (what the STORE needs to know
                                 about one - the type-to-tag rule, the
                                 refusals, the provenance settings). Nothing
                                 here quantizes anything; see QUANTIZING
                                 (maintainer) below for why
      Gguf/                      the GGUF reader: GgufMetadata (the public
                                 entry point), the internal GgufReader,
                                 GgufValue / GgufValueType, GgufTensorInfo /
                                 GgufTensorType / GgufTensorTypes,
                                 GgufFileType / GgufFileTypes, GgufReadOptions;
                                 and the GGUF WRITER the conversion writes
                                 through - GgufWriter, GgufWriterTensor and
                                 GgufTensorDataWriter, all three INTERNAL (see
                                 the design note below for why)
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
      Onnx/Quantization/         HALF OF THE MANAGED ENGINE, and with the codec
                                 in CodeBrix.Ollama.Core the reason a reduction
                                 can need nothing installed: a port of ONNX
                                 Runtime's quantizers -
                                 OnnxBlockwiseQuantizer,
                                 OnnxMatMulNBitsQuantizer,
                                 OnnxDynamicQuantizer with
                                 OnnxDynamicQuantizationOptions and
                                 OnnxWeightOnlyQuantizationOptions,
                                 OnnxGraphEditor, OnnxQuantizationUtilities and
                                 OnnxQuantizedValue. It is the subject of entry
                                 15 of THIRD-PARTY-NOTICES.txt. The CODEC it
                                 reads and writes graphs with is the one in
                                 CodeBrix.Ollama.Core, because the ModelRunner
                                 side needs it too; nothing quantizes anything
                                 there. All internal
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

    src/CodeBrix.Ollama.ModelRunner/    the managed library (378 .cs files)
      Contracts/                 the public surface the engine implements: the
                                 static entry point ModelRunner, IRunningModel,
                                 ModelDetails, the chat types (ChatMessage,
                                 ChatRole, ChatRequest, ChatUpdate,
                                 ChatResponse, ToolDefinition, ToolCall,
                                 ResponseFormat), the completion types
                                 (GenerationUpdate, GenerationResult,
                                 GenerationStatistics, FinishReason),
                                 EmbeddingResult, QuantizeResult,
                                 NativeRuntimeInfo /
                                 NativeDeviceInfo and ModelRunnerLogLevel; and
                                 the ONNX surface - the static entry point
                                 OnnxModel, IOnnxModel, OnnxTensor,
                                 OnnxElementType, OnnxModelMetadata,
                                 OnnxValueMetadata, OnnxDimension and
                                 OnnxOpsetImport; and the MIDI surface - the
                                 static entry point MidiGenerationModel,
                                 IMidiGenerationModel, MidiGenerationMetadata,
                                 MidiEvent, MidiEventKind, MidiScore and the
                                 static MidiFile; and the TEXT surface - the
                                 static entry point OnnxCausalLmModel,
                                 IOnnxCausalLmModel (which IS an IRunningModel)
                                 and CausalLmMetadata
      Options/                   ModelRunnerOptions, SamplingOptions,
                                 GenerationOptions, LoraAdapterOptions,
                                 QuantizeOptions and the
                                 enums ModelLoadMode, FlashAttentionMode,
                                 KvCacheType, EmbeddingPooling,
                                 ChatTemplateDialect and GgufQuantizationType,
                                 plus OnnxRunnerOptions, OnnxKernelPath and
                                 MidiGenerationOptions
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
      Onnx/                      THE MANAGED ONNX ENGINE, 75 files: the graph
                                 loader and its execution plan, the tensor and
                                 buffer types, the arena, the broadcasting and
                                 shape arithmetic, the matrix kernels in their
                                 three arithmetic paths, and OnnxSession, which
                                 is what IOnnxModel is. All internal; it reads
                                 graphs through the codec in
                                 CodeBrix.Ollama.Core. See THE MANAGED ONNX
                                 ENGINE below
      Onnx/Kernels/              one file per operator, 37 of them, each a
                                 small class that says what it will accept when
                                 the model is loaded and what it computes when
                                 it runs. Six of the 37 are the CONTRIBUTED and
                                 QUANTIZED operators the model builders and the
                                 store's own reductions write - see THE
                                 CONTRIBUTED AND QUANTIZED OPERATORS below
      Drivers/SkyTnt/            18 files, all internal: the MIDI generation
                                 driver over the managed engine - the two-graph
                                 loop, both key-value caches, the MIDI
                                 tokenizer, the sampling and its masks, and the
                                 bundle reader that decides this driver applies
                                 at all. Every file carries its upstream
                                 provenance. See THE MIDI GENERATION DRIVER
                                 below
      Drivers/CausalLm/          9 files, all internal: the TEXT generation
                                 driver over the managed engine - the prefill
                                 and decode loop, the key-value cache, the
                                 managed sampler and its stream of random
                                 numbers, and the bundle reader that decides
                                 this driver applies at all. Nothing in it
                                 knows about any particular model. See THE
                                 TEXT GENERATION DRIVER below
      Tokenizers/                6 files, all internal: the managed GPT-2
                                 byte-level byte-pair encoder - the byte
                                 table, the pre-tokenizer, the encoder itself,
                                 what a bundle says about it, and the reader
                                 that builds one out of a bundle's files. See
                                 THE MANAGED BYTE-LEVEL TOKENIZER below
      Midi/                      5 files, all internal: a Standard MIDI File
                                 writer and reader written against the
                                 published format. NOT a port - see
                                 THIRD-PARTY-NOTICES entry 16
      Grammar/                   the JSON-schema-to-GBNF converter:
                                 JsonSchemaGrammar (public),
                                 JsonSchemaConverter, GrammarBuiltinRule and
                                 GrammarTrieNode
      Engine/                    26 files, all internal: RunningModel (the
                                 IRunningModel implementation), ModelEngine
                                 (the bodies behind LoadAsync / ProbeAsync),
                                 ModelQuantizer (the body behind
                                 QuantizeAsync),
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

    tests/CodeBrix.Ollama.Core.Tests/   the xunit.v3 suite of the shared
                                 project. Offline throughout, with no gate: the
                                 codec reads files and nothing more. It
                                 references the shared project and nothing else
                                 of this repository's
      Onnx/                      the codec's own tests: OnnxModelTests (the
                                 round trip of every fixture, byte for byte,
                                 the unmodelled fields, the side files),
                                 OnnxSaveOptionsTests, OnnxFixtureFiles and
                                 OnnxModelComparison, which the two ModelManager
                                 test projects LINK
      Onnx/Fixtures/             this project's OWN COPY of the ONNX ORACLE
                                 FIXTURES, with generate_fixtures.py and
                                 README.txt - see THE TEST INFRASTRUCTURE
      Tokenizers/                Gpt2MergeTableTests
      Infrastructure/            CoreSurface, which writes the whole non-private
                                 surface out in a canonical form and hashes it
      CoreContractTests.cs       the guard: the matching case and the message
                                 the mismatched case produces
      CoreSurfaceTests.cs        THE FENCE'S FENCE - the recorded
                                 (revision, hash) pair

    tests/CodeBrix.Ollama.ModelManager.Tests/   the xunit.v3 suite, offline
                                 by default, and needing no CPython
      Bundles/ Export/ Gguf/ Modelfile/ Names/ Onnx/ Python/ Reduce/ Registry/
      Sources/ Store/
      Onnx/Fixtures/             the QUANTIZER's copy of the ONNX ORACLE
                                 FIXTURES and the script that writes them - see
                                 THE TEST INFRASTRUCTURE
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
                                 same reason, from CodeBrix.Ollama.Core.Tests,
                                 where the codec's own tests live: the rules for
                                 "these two files are the same model" are one
                                 set of rules, used by the codec's round trip,
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
                                 Tests folder carries every test project and the
                                 probe console application; the two packable
                                 projects and the shared project sit at the top
                                 level

THE FLAT-NAMESPACE RULE - DO NOT "FIX" IT
------------------------------------------
Every public and internal type in ModelManager declares `namespace
CodeBrix.Ollama.ModelManager;`, and every file in its test project declares
`namespace CodeBrix.Ollama.ModelManager.Tests;`. ModelRunner does exactly the
same with `namespace CodeBrix.Ollama.ModelRunner;` and `namespace
CodeBrix.Ollama.ModelRunner.Tests;`, however many folders deep the library is.
The two projects beside ModelManager.Tests follow the same rule in their own
names: `namespace CodeBrix.Ollama.ModelManager.Python.Tests;` and `namespace
CodeBrix.Ollama.ModelManager.Probe;`. The shared project and its tests do the
same with `namespace CodeBrix.Ollama.Core;` and `namespace
CodeBrix.Ollama.Core.Tests;`. The folders above are FILE ORGANIZATION ONLY.

One consequence of that last pair is worth knowing before it surprises someone:
a file in `CodeBrix.Ollama.Core.Tests` needs no using directive to see a type of
`CodeBrix.Ollama.Core`, because C# searches the enclosing namespaces. Everywhere
else - both libraries, and the ModelManager test assemblies - a file that names a
shared type lists `using CodeBrix.Ollama.Core;` like any other using.

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
    tests/CodeBrix.Ollama.Core.Tests -- xunit.v3 4.0.1, Microsoft.NET.Test.Sdk
    18.10.1, xunit.runner.visualstudio 4.0.0 and
    SilverAssertions.ApacheLicenseForever 1.0.248.1071, the same four packages
    at the same versions as every other suite here. 5 test classes and 27 test
    cases as of 2026-09-18, NONE of them gated - the codec reads files and the
    contract guard compares two integers. 16 of those cases are the codec and
    merge-table tests that moved out of the ModelManager suite when the codec
    moved into the shared project; the other 11 are new with it.

    tests/CodeBrix.Ollama.ModelManager.Tests -- the same four packages at the
    same versions. 84 test classes and 1,823 test cases as of 2026-09-18, of
    which 13 are [EnvGatedFact] members that skip unless their variable is set.
    It stood at 86 classes and 1,839 cases until the codec moved into
    CodeBrix.Ollama.Core and took OnnxModelTests (11), OnnxSaveOptionsTests (3)
    and the two merge-table cases of Gpt2BpeTokenizerExportTests with it; the
    QUANTIZER's tests and their oracle fixtures stayed here. `-list classes` on
    the built executable prints the class names, which is the quickest way to
    check this against the tree.

    tests/CodeBrix.Ollama.ModelRunner.Tests -- the same four packages at the
    same versions. 81 test classes and 3,075 test cases as of 2026-09-18, of
    which 30 are [EnvGatedFact] members that skip unless their variable is set.
    The suite stood at 44 classes and 1,258 cases from 2026-09-16 until
    2026-09-18, when quantization added ModelQuantizerTests (25 cases) and
    GgufQuantizationTypeTests (3), and then the MANAGED ONNX ENGINE added eight
    more classes and 731 cases, the CONTRIBUTED AND QUANTIZED OPERATORS three
    more classes and 332, and THE MIDI GENERATION DRIVER twelve more classes
    and 269, and THE TEXT GENERATION DRIVER eight more classes and 366 -
    every one of those 635 offline, which is why the skipped count did not
    move. THE PERFORMANCE PASS then added two classes and 44 cases, also all
    offline: the load's memory accounting, the performance-core discovery, and
    the fences that hold a PROMPT's arithmetic to what the same rows give ONE AT
    A TIME, bit for bit, on all three arithmetic paths - which is what makes a
    kernel that reorders the WORK provably safe when the answers may not be
    reordered. THE THREAD CAP then added 47 more, also offline, across three
    new classes (EngineThreadCountTests, OnnxExecutionSettingsTests,
    ModelRunnerOptionsTests) and four existing ones, while
    OnnxPerformanceCoresTests became EnginePerformanceCoresTests and handed its
    two default-thread cases to OnnxExecutionSettingsTests. 796 of the total is
    one theory run four ways:
    every one of the 199 checked-in operator oracles is run on the widest
    kernels, again with buffer reuse switched off and compared bit for bit
    against the run that reused them, again on the portable vector path and
    again on the scalar path. The whole suite still runs in under two seconds and still
    reaches nothing outside the repository.

    tests/CodeBrix.Ollama.EndToEnd.Tests -- the same four packages at the same
    versions. THE ONLY PROJECT THAT REFERENCES BOTH LIBRARIES, and the only
    place where a model the store made is run by the runner; the libraries
    themselves must never reference each other, which is why this lives in a
    test project and not in either of them. 8 test classes and 30 test cases as
    of 2026-09-18, EVERY ONE of them gated: with
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS unset the whole assembly skips in under a
    tenth of a second and nothing is downloaded, converted or loaded. See
    CONVERTING TO GGUF, END TO END below for what the three GGUF classes do and
    what they measured, THE MANAGED ONNX ENGINE for the three that run real
    graphs through the managed interpreter and compare every output with
    onnxruntime's, THE MIDI GENERATION DRIVER for the seventh, which holds the
    MIDI driver to the music the publisher's own Python writes, and THE TEXT
    GENERATION DRIVER for the eighth, which holds the text driver to the tokens
    the bundle's own runtime writes.

    tests/CodeBrix.Ollama.ModelManager.Python.Tests -- the same four packages
    at the same versions. 6 test classes and 43 test cases as of 2026-09-17,
    EVERY ONE of them [EnvGatedFact]: 25 need the Python gate alone, and the
    four export tests, the automatic-route test, the seven reduction tests and
    the six engine comparisons need the live gate as well. It is a separate
    executable because a CPython interpreter belongs to a process, is started
    once and cannot be restarted: run beside the offline suite, it would leave
    every later test in that run with an interpreter nobody asked for.

ALL FIVE SUITES ARE OFFLINE BY DEFAULT. None needs a daemon, a server, a model
file or a network: the Core suite runs in a fifth of a second, ModelManager's in
a few seconds on a warm machine
and ModelRunner's in under three, and ModelRunner's three seconds include
loading the tiny conformance model through the real native library and checking
the logits it produces. The Python suite with its gate closed runs in a quarter
of a second and starts nothing, and the end-to-end suite with its gate closed
skips every test in under a tenth. The tests that are exceptions are gated - see
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
ELEVEN VARIABLES, read by each test project's own
Infrastructure/EnvGatedFactAttribute.cs - a FactAttribute subclass that sets
Skip unless the variables it names hold the expected value - and named as
consts, in ModelRunner.Tests in Infrastructure/TestGates.cs, in
ModelManager.Tests on Infrastructure/MusicModelDefinitions.cs, and in both
ModelManager.Python.Tests and EndToEnd.Tests in Infrastructure/TestGates.cs:

    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1    the live tests in BOTH GGUF suites, and
                                        EVERY test in EndToEnd.Tests
    CODEBRIX_OLLAMA_RUN_QWEN35_TESTS=1  the Qwen 3.5 class, on top of that
    CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS=1    the two MuseCoco bundles, on top
                                        of the live gate
    CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS=1 the seven remaining large bundle
                                        definitions, on top of the live gate -
                                        and in EndToEnd.Tests the 1.97B
                                        streaming proof, which pulls one of
                                        those seven and keeps it
    CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1  EVERY test in
                                        ModelManager.Python.Tests, and in
                                        EndToEnd.Tests the three that compare
                                        against the checkpoint's own framework
                                        and the two that compare the managed
                                        ONNX engine against onnxruntime on the
                                        two published SkyTNT graphs. It is also
                                        the second gate on the three MIDI
                                        identity tests, which need
                                        CODEBRIX_OLLAMA_SKYTNT_CLONE as well
                                        The export tests there need the live
                                        gate as well, and are the only tests
                                        that need two gates neither of which is
                                        a music gate
    CODEBRIX_OLLAMA_TEST_MODEL_DIR      where ModelRunner's live tests cache
                                        the model files they download, and
                                        where the export and end-to-end tests
                                        keep the store they work in
    CODEBRIX_OLLAMA_PYTHON_VENV         the Python virtual environment the
                                        Python suite expects its modules in -
                                        read by the LIBRARY itself, not only by
                                        the tests - and the environment whose
                                        interpreter EndToEnd.Tests spawns over
                                        its own oracle script
    CODEBRIX_OLLAMA_ENGINE_CLONE        a checkout of the inference engine at
                                        the vendored commit, WITH
                                        oracle-converter.patch APPLIED (see
                                        CONVERTING TO GGUF, END TO END). It
                                        opens ONE test,
                                        the one that converts a real checkpoint
                                        with the engine's OWN converter and
                                        compares the bytes; unset, that test
                                        skips and everything else runs
    CODEBRIX_OLLAMA_QUANTIZE_TOOL       the engine's own command-line quantizer,
                                        BUILT out of that checkout (see
                                        QUANTIZING (maintainer) below). It opens
                                        ONE test, the one that quantizes a real
                                        model five ways and compares the bytes;
                                        unset, that test skips and everything
                                        else runs
    CODEBRIX_OLLAMA_SKYTNT_CLONE        a checkout of the MIDI model
                                        publisher's repository at the commit
                                        THE MIDI ORACLE names. It opens THREE
                                        tests, the ones that hold the managed
                                        MIDI driver to the music the
                                        publisher's own Python writes; unset,
                                        those three skip and everything else
                                        runs
    CODEBRIX_OLLAMA_MIDI_OUTPUT_DIR     a folder to leave the generated music
                                        in, so that a maintainer can LISTEN to
                                        what a run produced. It opens nothing
                                        and closes nothing; unset, the files
                                        are written into the test's own
                                        temporary folder and go away with it

The first five are gates and take the value "1"; a test that names two of them
runs only when both are set. The last six are not gates: one moves a cache, one
is the library's own way of being told where CPython lives, two name a folder
and a file that this repository can never ship and never reaches for on its own,
one names a second such folder, and the last one only says where to leave
something behind.

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

CONVERTING TO GGUF, END TO END
------------------------------
tests/CodeBrix.Ollama.EndToEnd.Tests is where a model the store made is run by
the RUNNER. It is the only project that references both libraries. Nineteen
tests, all gated. Five are the GGUF conversion, described here; two are
QUANTIZING (maintainer) below; and twelve are the ONNX comparisons - two on the
published fp32 pair (SkyTntOnnxLiveTests), four on the store's reductions of it
and its packed-weight memory fence (ReducedSkyTntOnnxLiveTests), and six on the
model builder's exports of the MuPT checkpoint and the store's reductions of
those (MuPtOnnxLiveTests, whose sixth is the accuracy_level proof described
above). THE CONTRIBUTED AND QUANTIZED OPERATORS above records
what all eleven measured. The five conversion tests:

  the_converted_checkpoint_loads_in_the_runner_and_generates_abc
        LIVE gate only. Pulls (or reuses) the MuPT 190M checkpoint, converts it
        with ConvertToGgufAsync, resolves it, probes and loads the file through
        the runner and continues three prompts. What it asserts about the text
        is loose - that it reads as ABC - because what a language model writes
        is not a fact about a conversion.
  the_bf16_conversion_agrees_with_the_checkpoint_read_as_bfloat16
  the_f16_conversion_agrees_with_the_checkpoint_read_as_float32
        LIVE and PYTHON gates. THE CORRECTNESS GATE, and it is token
        IDENTIFIERS: each converts twice - with and without the supplied
        special tokens - and requires the identifiers the runner produces
        greedily to be the ones the checkpoint's own framework produces from
        the same prompts. Python/torch_oracle.py is spawned in the virtual
        environment's own interpreter to get them; nothing in either library
        runs a model.
  the_converted_file_is_what_the_engines_own_converter_writes
        LIVE and PYTHON gates plus CODEBRIX_OLLAMA_ENGINE_CLONE. Converts the
        real checkpoint both ways and compares the two files byte for byte. The
        checkpoint is materialized into a folder NAMED AFTER THE MODEL, because
        the engine's converter derives general.name, general.basename and
        general.size_label from the folder it is pointed at while the store
        derives them from the last segment of the model's name; a folder named
        anything else makes two correct files differ in three keys.
        THE CHECKOUT MUST CARRY oracle-converter.patch, the same patch the
        converter fixtures are generated with (see CONVERTING TO GGUF
        (maintainer) - HOW THE ORACLE FIXTURES ARE REBUILT). The test runs convert_hf_to_gguf.py as it finds it, and a
        PRISTINE checkout refuses MuPT: its tokenizer is a custom class named
        by tokenizer_config.json's auto_map, and the unpatched converter calls
        AutoTokenizer.from_pretrained without trust_remote_code, so the child
        process exits 1 with "ValueError: The repository ... contains custom
        code which must be executed". (On Linux transformers first tries to
        PROMPT for the answer; on Windows, which has no SIGALRM, it raises at
        once.) The patch also lets the slow tokenizer through and teaches the
        converter MuPT's pre-tokenizer fingerprint. The converter additionally
        needs sentencepiece in the virtual environment.
  the_largest_checkpoint_converts_without_being_held_in_memory
        LIVE gate plus CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS - the same
        large-repository gate the store's own suite uses, and never opened by
        accident. THE STREAMING PROOF: it pulls the 1.97B sibling, one PyTorch
        zip pickle of 3,931,517,782 bytes, converts it and reads the operating
        system's own high-water mark for the converting process out of
        /proc/self/status. The number it exists for is that PEAK RESIDENT SET:
        a conversion reads one tensor at a time and writes it as it is read, so
        its high-water mark is a function of the largest single tensor and not
        of the file. The test requires it to stay under a quarter of the
        checkpoint; the measurement below says how much room that leaves. It
        then loads the converted model through the runner and generates. The
        CHECKPOINT is kept in the test-model cache afterwards, because
        downloading it again is what costs; the converted file is removed.

The engine checkout, prepared once:

    git clone --depth 1 --branch b10221 <the engine> ~/Temp/engine-b10221
    git -C ~/Temp/engine-b10221 apply \
        tests/CodeBrix.Ollama.ModelManager.Tests/Convert/Fixtures/\
oracle-converter.patch

On Windows the clone fails with "Filename too long" under the engine's web UI
folder (tools/ui/...) unless long paths are allowed for that one command:
`git -c core.longpaths=true clone ...`, or a short target folder. That changes
no global configuration.

    export TMPDIR=$HOME/Temp/codebrix-ollama-tmp
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 \
    CODEBRIX_OLLAMA_RUN_PYTHON_TESTS=1 \
    CODEBRIX_OLLAMA_PYTHON_VENV=$HOME/venvs/codebrix-ollama \
    CODEBRIX_OLLAMA_ENGINE_CLONE=$HOME/Temp/engine-b10221 \
    tests/CodeBrix.Ollama.EndToEnd.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.EndToEnd.Tests -showLiveOutput

Add CODEBRIX_OLLAMA_QUANTIZE_TOOL=<the built quantizer> for the byte-identity
half of the quantization pair; without it that one test skips and the other six
run. Add CODEBRIX_OLLAMA_RUN_LARGE_MUSIC_TESTS=1 for the fifth test, and give it
`-class CodeBrix.Ollama.EndToEnd.Tests.MuPtLargeGgufLiveTests` when the peak
resident set is what is being measured: the number is the whole PROCESS's
high-water mark, so a run that has already loaded a smaller model into the same
process reports that model's footprint too.

MEASURED ON THE DEBIAN 13 LAPTOP, 2026-09-18, the four smaller gates open and
the checkpoint already cached: 4 / 0 / 0 in 24.6 s. (With the quantize tool's
path given as well the whole project is 6 / 0 / 1 in 38.6 s, the skip being the
large streaming proof.)

    MuPT 190M checkpoint             380,166,726 bytes, one zip pickle
    the GGUF it becomes              381,878,656 bytes, 111 tensors, BF16
    conversion, store to store       0.9 to 1.2 s (1.7 s at F16)
    the engine's own converter       2.8 s for the same file
    byte-for-byte against it         IDENTICAL, 0 of 381,878,656 bytes differ
    LoadAsync, warm                  174 to 177 ms
    greedy throughput                171 to 198 tokens a second on 16 threads
    the reference, torch             3.9 s (bfloat16) / 4.5 s (float32) for
                                     six probes and six continuations

THE 1.97B STREAMING PROOF, measured the same day with the large gate open as
well, run on its own so that the peak is the CONVERSION'S and not a model the
same process had already loaded. 1 / 0 / 0 in 225.8 s, of which the pull is 211.

    MuPT 1.97B checkpoint            3,931,517,782 bytes, one zip pickle
    pull, cold                       210.9 s (about 18.6 MB a second)
    the GGUF it becomes              3,933,403,424 bytes, 435 tensors, BF16
    conversion, store to store       10.1 s
    PEAK RESIDENT SET of the         205,160,448 bytes - 195.7 MiB, which is
    converting process               5.2% of the checkpoint. The process was at
                                     115.4 MiB before the call, so the
                                     conversion itself added about 80 MiB: one
                                     tensor's worth of buffers, not one file's.
    LoadAsync, cold                  309 ms
    greedy throughput                17.6 tokens a second on 16 threads
    the test-model cache afterwards  6,984,966,844 bytes (6.51 GiB), up from
                                     2.9 GB; the checkpoint is KEPT and the
                                     converted file is removed

    /usr/bin/time -v over the same run reports a maximum resident set of
    4,636,196 KB (4.42 GiB) for the WHOLE process, and that number is the model
    LOAD, not the conversion: the runner memory-maps the 3.66 GiB file it was
    given and the pages it touches count as resident. The two measurements do
    not disagree - they are of different things, and only the first is about
    converting. The in-process one is read from /proc/self/status's VmHWM
    immediately after the conversion returns and before anything is loaded.

WHAT THE TOKEN COMPARISON CAN AND CANNOT PROMISE. The engine holds the weights
at the type the file was written with but accumulates in float32, so its
arithmetic sits BETWEEN the checkpoint read as bfloat16 and the checkpoint read
as float32. Where the top two candidates are further apart than the smaller
type can resolve, all three agree exactly, and the asserted prompts are a set on
which they do: 32 of 32 identifiers on each of three prompts, both at BF16
against bfloat16 and at F16 against float32, with and without the supplied
special tokens. Three further prompts are run and their agreement is REPORTED
rather than asserted, and one of them shows the edge plainly: at BF16 it agrees
on 27 of 32 and then differs once, at a position where float32 ranks the
candidates 7.8219 / 7.7944 / 7.7359 and bfloat16 - whose step at that magnitude
is 0.0625 - collapses the first two into one value and puts the third on top.
The converted file follows FLOAT32 there, and the same prompt at F16 against
float32 agrees on all 32. A tie broken differently is arithmetic, not a
conversion, which is why it is recorded and not asserted.

ONNX RUNTIME'S NATIVE LIBRARY LEAVES AN EMPTY mat-debug-<pid>.log IN THE
TEMPORARY DIRECTORY - one per process that loaded it, zero bytes, written by
onnxruntime's own binary and not by anything in this repository. A gated run
leaves a handful behind; they are noise, not a leak of this library's.

EVERY DERIVING OPERATION WRITES THROUGH THE SYSTEM TEMPORARY DIRECTORY. An
export lays the
whole source bundle out in a temporary folder for the tool to read and the tool
writes its output beside it; a reduction does the same for the quantizers; a
GGUF conversion lays the checkpoint out for its OWN readers and writes the GGUF
file beside it. So a machine whose /tmp is a RAM-backed tmpfs needs
TMPDIR pointed at a real file system first - the same rule the large bundle
gates follow. The pass-through export route writes no temporary folder at all.
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

The default run is therefore 1815 total / 1802 passed / 13 skipped as of
2026-09-18; with the live gate alone it is 1815 / 1806 / 9, and with all three
open it is 1815 / 1815 / 0. The Python and end-to-end tests are NOT in this
suite and no gate of theirs changes these numbers.

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

THE ONNX ORACLE FIXTURES are how the managed engine is held to ONNX Runtime's
own output without ONNX Runtime being anywhere near the offline suite. Every
.onnx file in the fixture folder was WRITTEN BY THOSE TOOLS: an input model, and
beside it the file they produce from it in one mode, named <model>.<mode>.onnx.
The tests quantize the input with the managed engine and compare the result with
the file beside it - every field, then the encoded bytes.

THERE ARE TWO COPIES OF THE FOLDER, and they hold the same files:

    tests/CodeBrix.Ollama.Core.Tests/Onnx/Fixtures/          the CODEC's copy
    tests/CodeBrix.Ollama.ModelManager.Tests/Onnx/Fixtures/  the QUANTIZER's

The codec lives in CodeBrix.Ollama.Core and the quantizer in
CodeBrix.Ollama.ModelManager, so their tests are in two projects, and a test
project does not read another test project's assets. A README.txt in each folder
says the same. Regenerating means running the script ONCE and copying its output
into both folders; the two must never be allowed to differ.

    generate_fixtures.py   writes every one of them, and is the only thing that
                           may. Run it with the reference virtual environment's
                           own interpreter, from either folder:

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

CONVERTING TO GGUF (maintainer) - THE DESIGN
============================================
ConvertToGgufAsync takes a bundle holding a transformers checkpoint and puts an
ordinary GGUF model in the store. Everything it does is this library's own
managed code; NO CPython is started and no publisher tooling is run, which is
the whole point of it and is why the feature could be added without touching
the library's one inert dependency.

THE SHAPE OF ONE CONVERSION, in the order it happens (ModelStore.ConvertToGguf
Async, with GgufConvert holding what the STORE needs to know and GgufConversion
holding the conversion itself):

  1. REFUSE EARLY, from the stored blobs alone. A model with no publisher file
     tree, one that already holds a .gguf, one that holds an .onnx, one with no
     config.json, and one whose config.json names an architecture this version
     does not read are each refused before a byte is laid out. For a checkpoint
     of gigabytes that is the difference between a message in a moment and a
     message after a long copy. The architecture check calls the SAME method
     the conversion calls, so the two can never drift apart.
  2. MATERIALIZE the whole bundle into a folder under the system temporary
     directory, hard-linked, README.md included - the model card is where
     general.license, general.tags and general.languages come from.
  3. CONVERT to a temporary file. Progress: reading checkpoint, writing gguf.
  4. CREATE the model through the EXISTING create-from-Modelfile path, with a
     synthesized Modelfile and an internal CreateAsync overload that carries
     DerivedProvenance and the overwrite flag.
  5. REMOVE the working folder in a finally, on success and on failure alike.

THE PIECES

  Checkpoints/     the containers. SafetensorsReader and PyTorchZipReader each
                   enumerate tensors and open a stream over one, and
                   CheckpointWeights is the composite over one part or many -
                   which is where the ORDER the engine visits tensors in lives,
                   and it lives in exactly one place for a reason: a
                   byte-for-byte gate cannot survive that rule being written
                   twice. Parts in sorted file-NAME order (and when an index is
                   present, only the files the index names), safetensors in
                   tensor-name order inside a part, a zip pickle in
                   state-dictionary order.
  RestrictedUnpickler
                   THE SECURITY BOUNDARY, and the reason a pickle can be read
                   at all. A pickle is a program: the ordinary interpreter for
                   one imports any module the stream names and calls any
                   callable. This one implements the protocol 0-2 opcodes a
                   tensor state dictionary is built from and nothing else,
                   never imports and never evaluates, and resolves GLOBAL
                   against an allow-list of four kinds - the tensor and
                   parameter rebuilders, OrderedDict, and the ten torch storage
                   classes. A storage class may appear only inside a persistent
                   id; a persistent id must be the five-part tuple; a
                   dictionary key must be a string; there are caps on opcode
                   count, string length and item count. Everything else raises
                   PickleRefusedException with the construct named, and the
                   protocol 4 and 5 opcodes are named individually rather than
                   reported as unknown bytes. The refusals are unit-tested over
                   hand-built hostile pickles, which is the only honest way to
                   test a boundary of this kind.
  Gguf/GgufWriter  the mirror of the reader: GGUF v3, alignment 32, keys in the
                   order they were added, tensor infos then aligned data,
                   tensor payloads produced by a callback WHILE they are
                   written rather than held. Two engine behaviours are mirrored
                   deliberately: an empty string or empty array is not written
                   at all, and the padding after the last tensor is written
                   like the padding after every other one. It round-trips every
                   GGUF file checked into this repository byte for byte, the
                   native conformance model included.
  Convert/         the conversion. LlamaArchitecture writes the llama.* keys
                   and decides each tensor's type; LlamaTensorNameMap is the
                   llama slice of the engine's own table, generated by running
                   the engine's code and printing it so that no entry could be
                   mistyped; TensorDataConverter does the widening, the
                   narrowing and the rotary permutation of the query and key
                   projections (weights AND biases).
  Convert/Metadata the general.* keys. ModelIdComponents is the engine's naming
                   heuristic - general.name, general.basename, general.size_
                   label - driven by ConvertOptions.ModelId, which the STORE
                   fills with the last segment of the model's own name so that
                   those keys are a property of the model rather than of a
                   temporary folder whose name is a GUID. ModelCard reads a
                   model card's YAML front matter with a small reader of our
                   own, per key, skipping any key it cannot read: the library
                   gains no YAML dependency and a conversion never fails
                   because a card is unusual.
  Convert/Tokenizers
                   VocabularyExport chooses the road the way the engine chooses
                   it - by which files are present, not by what a configuration
                   says. A tokenizer.model makes it SentencePiece whatever else
                   is there; with none, a tokenizer.json is REFUSED by name;
                   with neither, the GPT-2 byte-level set. SentencePieceModel
                   reads tokenizer.model - which is a Protocol Buffers
                   ModelProto - with the repository's OWN protobuf codec, the
                   one written for ONNX, catching its InvalidDataException and
                   re-throwing a CheckpointFormatException because that codec's
                   messages name "the ONNX file". (That codec now lives in
                   CodeBrix.Ollama.Core, under Onnx/Protobuf/; nothing here
                   depends on where it sits, only on what it reads.) No code is
                   taken from the SentencePiece project and nothing depends on
                   it at run time - only the published wire layout of that
                   message is relied on.

  Quantize/        the store side of a quantization, and nothing that
                   quantizes: GgufQuantizer is the public delegate the CONSUMER
                   fills, GgufQuantize holds the type-to-tag rule (the type is
                   a TAG the store never interprets, only checks can be part of
                   a name), the three refusals (a bundle, a model with no
                   weights, a model whose weights are split) and the one
                   provenance setting. ModelStore.QuantizeGgufAsync hands the
                   delegate the stored blob's own path - nothing is copied -
                   and a path in a temporary folder it removes in a finally. It
                   builds the new model's Modelfile from
                   BuildModelfileCommands, the same method that renders a model
                   back to Modelfile TEXT, so a quantized model cannot quietly
                   lose the template or the stop parameters its source had.
                   See QUANTIZING (maintainer) for the whole design

THE SPECIAL-TOKEN GAP, AND WHY THE OPTION EXISTS. The engine's rule for writing
a token as CONTROL rather than NORMAL is "the ADDED vocabulary holds it and
marks it special", and the added vocabulary is read out of
tokenizer_config.json and added_tokens.json. A checkpoint whose tokenizer CLASS
declares its unknown, beginning-of-sequence, end-of-sequence and padding tokens
as default arguments inside its own .py file - which the engine's converter sees
because it EXECUTES that file, and which this library never will - leaves those
tokens undeclared on disk, and they are written as ordinary tokens. That is what
the files say they are, and the checked-in tinyllamabare-123k oracle is the
engine's own verdict that it says so too.

ConvertOptions.AddedSpecialTokens supplies the missing INPUT without touching
the RULE: a list of token CONTENTS the caller says are added and special.
Nothing model-specific enters the library - the four contents of the model this
was found on live in a test file. It moves token TYPES only; the identifiers
still come from config.json, so end-of-generation works either way. It is
REFUSED, not ignored, on the SentencePiece road (ArgumentException, ParamName
"options"), because that road always reads added tokens from files that are
always there, so a value is a caller believing something untrue. What was NOT
done is worth recording: the obvious rule - treat the tokens config.json names
through its <kind>_token_id entries as special - is not the engine's rule and
would not even have closed the case that prompted it, so a named gap with an
option was chosen over a rule that is wrong in a different way.

THE GAPS, EACH REFUSED OR NAMED IN THE CODE ITSELF

  the two vocabulary-SIZE rules   NOT PORTED, deliberately. After the
                                  vocabulary is read the engine gives a
                                  checkpoint of exactly 32,016 tokens four
                                  fixed infilling token identifiers, and one of
                                  exactly 49,152 tokens a false add_bos_token.
                                  Both write keys whose POSITION among the
                                  others decides the bytes of the file, and the
                                  only checkpoints that could show that
                                  position are model families far too large to
                                  check in as a fixture. A checkpoint of either
                                  size still converts to a correct, loadable
                                  file that is NOT byte-identical to the
                                  engine's. Closing it needs a real subject and
                                  a gated test - a phase of its own. Recorded
                                  in VocabularyExport's own documentation and
                                  in THIRD-PARTY-NOTICES.
  base_model and datasets         NOT READ from a model card. The engine
                                  expands them into general.base_model.* and
                                  general.dataset.* keys; they need nested
                                  structures outside what the front-matter
                                  reader takes, so those keys are not written.
                                  A card-rich checkpoint therefore differs from
                                  the engine's output by exactly those keys.
  scaled rotary embedding         REFUSED BY NAME. A config.json declaring a
                                  rope_type raises NotSupportedException naming
                                  it. Linear and YARN scaling write extra keys
                                  and llama3 scaling makes the converter
                                  SYNTHESIZE a rope_freqs tensor that is not in
                                  the checkpoint; a file quietly lacking it
                                  would load and be wrong.
  tokenizer.json (the llama-hf    REFUSED BY NAME. Reading it is not nearly
  route) and tekken.json          free: the engine's LlamaHfVocab pre-checks
                                  the JSON and then loads the tokenizer through
                                  transformers, asserts it is a FAST tokenizer
                                  and reads its added vocabulary and special
                                  tokens off the loaded object. It is a
                                  tokenizer-library wrapper, not a file reader,
                                  so porting it means porting a tokenizer.
  two shard-index corruptions     REFUSED BY NAME although the engine tolerates
                                  them silently: a tensor physically present in
                                  TWO parts (the engine's dict assignment keeps
                                  the later one) and a weight map that lists
                                  one tensor TWICE (Python's JSON reader keeps
                                  the last entry). In both, which copy is the
                                  model's cannot be decided from the files, and
                                  silently picking one produces a GGUF whose
                                  provenance nobody can reconstruct. The two
                                  cases the engine DOES refuse are refused here
                                  with the same meaning.
  a non-normalized added token    NOT re-normalized. The engine re-encodes and
                                  re-decodes such a token through the tokenizer
                                  before deciding its type. For a token that IS
                                  in the added vocabulary that round trip is
                                  the identity, which is why it changes nothing
                                  here, but an exotic tokenizer could differ.

NO MODELFILE TEMPLATE IS SYNTHESIZED, and it is a decision rather than an
omission. What tokenizer_config.json supplies is a JINJA chat template, and in
this repository a Modelfile TEMPLATE layer is the OTHER dialect - ModelRunner's
own message says so, and its ChatTemplateStrategy picks the Ollama dialect over
the Jinja one whenever a TEMPLATE layer is present. Copying a Jinja template
into that layer would hand a consumer a template the renderer it then selects
cannot render, silently. The Jinja template IS carried over: into the GGUF file,
as tokenizer.chat_template, where a runner reads it as the embedded template. A
stop PARAMETER is written from tokenizer_config.json's eos_token and from
nothing else.

THE GGUF WRITER STAYS INTERNAL IN V1. That is the reviewer's decision, taken
with this documentation: a public GGUF writer would be a supported way to
produce model files, with its own compatibility surface and its own design
pass, and nothing in the conversion needs it public. Making it public later is a
separate, additive decision; making it public now would be a commitment made by
accident. GgufWriter, GgufWriterTensor and GgufTensorDataWriter are therefore
internal, and the public conversion surface is ConvertToGgufAsync,
ConvertOptions, ConvertResult, GgufOutputType and CheckpointArchitecture.

MEASURED, 2026-09-18, on the Debian 13 laptop (the numbers the end-to-end
section above does not already carry). The SPIKE that proved the idea before
any code was written converted the real 190M checkpoint with the engine's own
PYTHON converter in 2.4 to 2.8 s and a peak resident set of about 990 MiB; the
managed converter does the same file in about 1 s at a peak of 104 MiB, and the
two files are identical. The same spike measured BF16 and F16 GGUFs of that
model generating at 182 to 188 tokens a second alike on 16 threads, so the risk
that BF16 would be materially slower on this CPU was not observed.

CONVERTING TO GGUF (maintainer) - HOW THE ORACLE FIXTURES ARE REBUILT
=====================================================================
tests/CodeBrix.Ollama.ModelManager.Tests/Convert/Fixtures/ holds a folder per
variant, each a tiny synthetic Llama checkpoint, and beside each of them the GGUF
file that the INFERENCE ENGINE'S OWN converter produced from it. The managed converter is
compared with those files over the whole file, byte for byte; that is what makes
the conversion a port rather than an interpretation.

Rebuilding them needs a checkout of the engine and a Python environment. Neither
is needed to RUN the suite: the offline tests read only what is checked in.

    git clone --depth 1 --branch b10221 <the engine> ~/Temp/engine-b10221
    git -C ~/Temp/engine-b10221 apply <fixtures>/oracle-converter.patch
    export TMPDIR=$HOME/Temp/codebrix-ollama-tmp
    cd <fixtures>
    ~/venvs/codebrix-ollama/bin/python generate_fixtures.py \
        --engine ~/Temp/engine-b10221

--variant <name>, repeatable, writes ONE of them and leaves the rest as they
are, which is what adding a variant should do: every other oracle in that folder
stays the file it was generated as rather than being written again.

The virtual environment must hold torch, safetensors, transformers, regex and
SENTENCEPIECE, which is needed twice over. The converter imports sentencepiece
before it checks whether a tokenizer.model exists, so without that module every
Llama checkpoint it is given dies with ModuleNotFoundError, which it does not
catch; and the script TRAINS the SentencePiece variant's own tokenizer.model with
it, from the same corpus the byte-level BPE is trained on.

    oracle-converter.patch   the recorded change that makes the converter usable
                             as an oracle. It touches tokenizer LOADING and the
                             pre-tokenizer recognition table and nothing that
                             writes a tensor, a key or a vocabulary - so when the
                             managed output and the oracle disagree, the patch is
                             the LAST suspect, not the first. Its header says
                             what each hunk is for.

    a new BYTE-LEVEL         vocabulary needs a new entry in that recognition
                             table. The converter hashes the token ids of a fixed
                             check text and refuses a hash it does not know,
                             printing it; run it once, read the hash off the
                             "chkhsh:" line and add it in the same shape.

    a new SENTENCEPIECE      vocabulary needs NOTHING added. That road never
                             fingerprints a pre-tokenizer - it writes
                             tokenizer.ggml.pre as the constant "default" - so
                             the SentencePiece fixture was produced with the
                             patch exactly as the byte-level ones left it, and
                             the patch has not changed since G1.

    the folder names         are part of the oracle. general.name,
                             general.basename and general.size_label are derived
                             from the model DIRECTORY'S name, so renaming a
                             fixture folder changes the bytes of its GGUF. The
                             store supplies that string itself - the last
                             segment of the model's name - so a conversion done
                             through the store never depends on what a temporary
                             folder happens to be called.

    tinyllamabare-123k       is the fixture behind
                             ConvertOptions.AddedSpecialTokens. Its
                             tokenizer_config.json declares NO special token:
                             no added-token table, and every <kind>_token null.
                             That is the shape a publisher leaves on disk when
                             the tokenizer class names its special tokens inside
                             its own Python, which a converter that reads files
                             never sees and must never run. The engine writes
                             the four tokens as ORDINARY tokens, and so does the
                             port - the oracle says so. Supplying their contents
                             through the option moves exactly those four token
                             types to CONTROL and changes four bytes of the
                             file; the test asserts the count.

    tinyllamasp-132k         is the SENTENCEPIECE fixture: a tokenizer.model the
                             script trains with byte fallback on, an
                             added_tokens.json, and an added-token table in
                             tokenizer_config.json. It is the only fixture that
                             carries token SCORES and no merge table, and every
                             token type is reachable in it - normal, unknown,
                             control, user-defined, unused and byte. Its 384
                             pieces sit inside a configured vocabulary of 392,
                             so the padding rule fires too, and one of its scores
                             is negative zero, which a float that was widened and
                             narrowed carelessly would lose.

    tinyllamasplit-123k and  are the SHARDED fixtures: the same weights as
    tinyllamabinsplit-123k   tinyllama-123k and tinyllamabin-123k, split over
                             three files with a *.index.json beside them. The
                             split is deliberately in neither useful order - the
                             SECOND block is in the first shard, the model-level
                             tensors in the second and the FIRST block in the
                             last - so the tensor order in the oracle is evidence
                             that the parts are walked the way the engine walks
                             them: shards in sorted file-NAME order, and inside a
                             shard a safetensors container in tensor-name order
                             and a zip pickle in state-dictionary order. A reader
                             that sorted the whole set by name, or concatenated
                             the shards in the order they were written, would
                             produce a different file and the oracle would say so.

    NEVER hand-edit a GGUF fixture, and never write one with the managed writer:
    a fixture the port produced would prove only that the port agrees with
    itself. Everything in that folder is ours and MIT licensed - the weights are
    pseudo-random numbers from a pinned seed and BOTH tokenizers are trained on
    a corpus inside the script - and no third-party model file is checked in.

QUANTIZING (maintainer) - THE DESIGN, THE ORACLE AND WHAT IT MEASURED
=====================================================================
Quantizing is the one feature this repository splits across BOTH packages, and
the split is the whole point of the design.

WHY IT IS SPLIT. R7 of the plan, restated by Jeremy as a MUST: the two
libraries and the two NuGets must not depend on each other - not a
ProjectReference, not a PackageReference, not a nuspec dependency, and not a
public signature of one naming a type of the other. A quantizer is an inference
engine's work, and the inference engine lives in ModelRunner. So:

  ModelRunner       does the work. ONE new public member,
                    ModelRunner.QuantizeAsync, over llama_model_quantize - a
                    call that was ALREADY BOUND (Native/NativeMethods.Model.cs)
                    with its parameter struct and its defaults already
                    size-fenced by NativeDefaultsTests. NOTHING NATIVE WAS
                    REBUILT for this and nothing was added to the package: no
                    PackageReference, nothing new to install. Options/
                    GgufQuantizationType.cs, Options/QuantizeOptions.cs and
                    Contracts/QuantizeResult.cs are the public types;
                    Engine/ModelQuantizer.cs is the work.
  ModelManager      does everything AROUND the work and none of the work:
                    IModelStore.QuantizeGgufAsync finds the stored blob, names
                    the result, carries the source's layers over, records the
                    provenance and cleans up. The quantizer arrives as
                    Quantize/GgufQuantizer.cs - a public DELEGATE taking two
                    paths and a token - so the store names no type of the
                    runner's and loads no engine.
  the consumer      writes the lambda that joins them. So does
                    tests/CodeBrix.Ollama.EndToEnd.Tests, which is the only
                    project in the repository that may.

WHAT QuantizeAsync IS CAREFUL ABOUT. Three things, each fenced by a test:

  * THE ENGINE NEVER WRITES AT THE OUTPUT PATH. It is given a file of its own
    beside the output, named "<output>.quantizing-<8 hex>", and that file is
    MOVED into place only after the native call has returned success. A failure
    removes it; a file already at the output path is untouched. The temporary
    file is beside the output rather than under TMPDIR precisely so that the
    move is a rename within one directory and costs nothing at any model size.
  * THE DEFAULTS ARE THE ENGINE'S. QuantizeOptions exposes Threads,
    AllowRequantize and Pure, every one defaulting to what
    llama_model_quantize_default_params writes, and every other field of the
    struct is left at that default. That is what makes a call with no options
    the engine tool's own call with no switches - which is the whole basis of
    the byte-for-byte comparison. A test asserts the three defaults against the
    struct the engine hands back, so the claim cannot rot.
    KEEP-SPLIT IS DELIBERATELY NOT EXPOSED: it makes the engine write a
    NUMBERED SET of files instead of the one file the API promises, which would
    make the output path a lie and the result's OutputBytes meaningless. Nor is
    the importance matrix, the per-tensor and per-layer overrides, the metadata
    overrides, layer pruning, the dry run or leave-output-tensor: each needs a
    file format of its own or changes what one call produces.
  * CANCELLATION IS HONEST. The native call has no abort hook, so the token is
    checked before the work starts and cannot reach it afterwards. The XML
    documentation, the AGENT-README and pitfall 15 all SAY SO rather than
    implying a token that works.

THE PUBLIC ENUMERATION IS FENCED 1:1. GgufQuantizationType carries the engine's
own numbers, and Options/GgufQuantizationTypeTests.cs compares every member with
the internal LlamaFtype member it stands for, checks that the mapping covers
every public member exactly once, and checks that the public set is the native
set MINUS exactly two: Guessed (what the engine records when a file does not say
what it is) and MostlyNvfp4 (a type llama_ftype_get_default_type has no mapping
for at the vendored commit, so the native call refuses it as "invalid output
file type"). A type added to the engine's enumeration therefore FAILS that test
until someone decides which side of the line it is on.

THE ORACLE IS BUILT, NOT INSTALLED
----------------------------------
The vendored subset under llama-native-tools/ holds no tool sources, so the
engine's command-line quantizer can only come from a checkout. Building it is a
BUILD and not an install: nothing is added to the machine and nothing leaves the
folder it is built in.

    git clone --depth 1 --branch b10221 <the engine> ~/Temp/engine-b10221
    cmake -S ~/Temp/engine-b10221 -B ~/Temp/quantize-build \
      -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
      -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF \
      -DGGML_CPU_KLEIDIAI=OFF -DGGML_BACKEND_DL=OFF -DGGML_AVX512=OFF \
      -DLLAMA_OPENSSL=OFF -DLLAMA_CURL=OFF \
      -DLLAMA_BUILD_COMMON=ON -DLLAMA_BUILD_TOOLS=ON \
      -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_TESTS=OFF \
      -DLLAMA_BUILD_APP=OFF -DLLAMA_BUILD_SERVER=OFF
    make -C ~/Temp/quantize-build -j llama-quantize

THE OPTIONS ARE THE SHIPPED linux-x64 NATIVE'S OWN, from llama-native-tools/
linux/pins.env and BUILD-PROVENANCE.txt, with EXACTLY TWO CHANGED:
LLAMA_BUILD_COMMON and LLAMA_BUILD_TOOLS go from OFF to ON, because the shipped
native is a library that needs neither and the tool is an executable that needs
both. That matters more than it looks. The plan's risk 7.9 is that a
byte-for-byte comparison of two BUILDS of the same floating-point quantizer
could differ by one rounding if the compilers were told different things, so
every option that reaches the COMPILER is kept identical - GGML_NATIVE=OFF and
GGML_AVX512=OFF in particular, which is what pins the CPU baseline. cmake prints
the baseline it chose, and it must read
"-msse4.2;-mf16c;-mfma;-mbmi2;-mavx;-mavx2" - AVX2, FMA, F16C, BMI2, no
AVX-512, which is exactly what BUILD-PROVENANCE records for linux-x64. There is
no ninja on the Debian laptop; the Makefile generator builds it in about a
minute on 16 threads.

WHAT WAS MEASURED, 2026-09-18, ON THE DEBIAN 13 LAPTOP
------------------------------------------------------
The build host is gcc 14.2.0 (Debian) against the container's gcc 14.2.1 (Red
Hat) that produced the shipped native - a different toolchain, and the files
still agree.

FIFTEEN COMPARISONS, ALL BYTE FOR BYTE IDENTICAL, ZERO DIFFERING BYTES:

    subject                        Q8_0     Q4_0    Q4_K_M   Q5_K_M    Q6_K
    conformance-tiny.gguf (F32)     ok       ok       ok       ok       ok
    MuPT 190M, BF16 source          ok       ok       ok       ok       ok
    MuPT 190M, F16 source           ok       ok       ok       ok       ok

The first row is the offline fence and is checked in as
tests/CodeBrix.Ollama.ModelRunner.Tests/Fixtures/Quantize/ (five files, 120 KB
in all, with a README.txt recording the commands above); the other two rows are
the gated test, which quantizes the store's own converted MuPT five ways and
runs the tool against the same file. RISK 7.9 WAS NOT OBSERVED.

    MuPT 190M :gguf (BF16)           381,878,656 bytes
      -> Q8_0                        203,710,336   53.3%   0.29 s
      -> Q6_K                        157,683,520   41.3%   0.61 s
      -> Q5_K_M                      139,893,088   36.6%   0.86 s
      -> Q4_K_M                      123,149,152   32.2%   0.97 s
      -> Q4_0                        118,587,232   31.1%   0.31 s
    the engine's own tool, same file and type: about 1.0 s throughout

    quantized model, loaded and generating (16 threads, 2048 context)
      Q8_0     load 169 ms    305 tokens a second   greedy agreement 32/32
      Q4_K_M   load 187 ms    425 tokens a second   greedy agreement  1/32

    the unquantized BF16 model, for comparison: load 174 ms, 171-198 tokens/s

THE GREEDY AGREEMENT IS RECORDED AND NOT ASSERTED, and the two numbers are why
that is the right call rather than a soft one. Q8_0 is close enough to the
original that all 32 identifiers match; Q4_K_M diverges at the FIRST token and
therefore, being greedy, at every token after it - which is what four-bit
weights do to a 190M model and is not a defect. What the file IS, is proved by
the byte comparison above; what it GENERATES is measured and printed. Pinning
either agreement number would pin a number with no right answer.

    export TMPDIR=$HOME/Temp/codebrix-ollama-tmp
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 \
    CODEBRIX_OLLAMA_QUANTIZE_TOOL=~/Temp/quantize-build/bin/llama-quantize \
    tests/CodeBrix.Ollama.EndToEnd.Tests/bin/Release/net10.0/\
CodeBrix.Ollama.EndToEnd.Tests -showLiveOutput

WHEN THE VENDORED COMMIT MOVES, the checked-in fixtures must be written again
with a tool built at the NEW tag, and Fixtures/Quantize/README.txt updated with
it. A rebuild at the SAME tag that produces different bytes is a finding to
chase, not a fixture to update.


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


THE THREAD COUNTS: TWO DEFAULTS AND A CAP, MEASURED 2026-09-18
==============================================================
Both engines let a caller name a thread count and both work one out when the
caller does not. THE TWO ROADS WORK IT OUT DIFFERENTLY, and this section is why,
because the difference looks like an inconsistency until the measurement is in
front of you.

    ModelRunnerOptions.Threads       unset -> EnginePhysicalCores.Count()
    ModelRunnerOptions.BatchThreads  unset -> whatever Threads resolved to
    OnnxRunnerOptions.Threads        unset -> EnginePerformanceCores.Count()
                                     when that is above nought, else
                                     EnginePhysicalCores.Count()

    ModelRunnerOptions.MaxThreads    unset -> no cap; otherwise the resolved
    OnnxRunnerOptions.MaxThreads     AUTOMATIC count is the smaller of the
                                     detected count and this. It NEVER touches
                                     a count the caller stated.

WHERE THE RULE LIVES. Engine/EngineThreadCount.cs, one method, and both engines
call it: ParameterMapper.ResolveThreads / ResolveBatchThreads on the native road
and OnnxExecutionSettings.Resolve on the managed one. The DETECTED count is a
PARAMETER of the rule rather than something the rule discovers, which is the
seam the tests use: EngineThreadCount, ParameterMapper and OnnxExecutionSettings
each have an overload taking a stated count, so the cap is proven on a four-core
machine and a sixty-four-core one without owning either. No public member
exposes a detected core count, deliberately: MaxThreads is how a consumer uses
the detection.

THE MEASUREMENT (Precision 7770, i7-12850HX: 8 performance cores, 8 efficient,
24 logical; Debian 13; Release; machine otherwise idle). Five GGUF subjects, all
made from what the test-model cache already held. GENERATION is 128 greedy
tokens; PROMPT PROCESSING is a 256-token prompt built from the model's own
vocabulary, with the prefix cache cleared before each run so the whole prompt is
evaluated; five runs after a warm-up, MEDIAN of the five; ContextSize 1024.
Threads and BatchThreads were set to the same value except in the last two rows.

  GENERATION, tokens a second (median of five)
  THREADS            4        6        8       12       16       24   BEST
  SmolLM 360M Q8_0   94.03   114.17   128.65   137.50   132.94   44.06   12
  MuPT 190M BF16    123.23   153.31   174.57   188.91   182.05   87.89   12
  MuPT 190M Q4_K_M  270.49   325.76   383.86   411.30   406.09   60.69   12
  MuPT 1.97B Q4_K_M  34.72    39.33    41.50    46.47    47.47   18.79   16
  MuPT 1.97B BF16    13.28    15.61    15.71    16.82    16.95   12.80   16

  PROMPT PROCESSING, tokens a second (median of five)
  THREADS            4        6        8       12       16       24   BEST
  SmolLM 360M Q8_0  437.17   591.04   691.27   430.85   518.00  638.34    8
  MuPT 190M BF16   1295.31  1746.51  2117.00  2384.09  2485.49 2452.21   16
  MuPT 190M Q4_K_M 1767.57  2370.94  2547.98  3214.66  3400.60 2966.32   16
  MuPT 1.97B Q4_K_M 105.95   148.75   183.80   194.93   224.09  200.87   16
  MuPT 1.97B BF16    81.27   110.53   134.60   146.24   157.33  155.93   16

  THE TWO SPLIT SETTINGS, generation / prompt processing
  Threads 8, BatchThreads 16   129.48 / 526.04    172.29 / 2461.73
                                40.13 / 222.66     16.03 /  162.14
                               384.10 / 3349.88
  Threads 16, BatchThreads 8   135.84 / 715.06    184.21 / 1826.67
                                46.61 / 171.29     17.05 /  131.79
                               403.00 / 2460.31
  They confirm that the two counts act on the two phases independently:
  generation follows Threads and prompt processing follows BatchThreads, and
  neither leaks into the other.

TOKEN IDENTITY: at every thread count, on every subject, the generated token
ids were IDENTICAL to the eight-thread run - 40 of 40 configurations, no
difference at any position. The thread count is a speed setting and nothing
else, which is what lets the EndToEnd identity oracles stand whatever it is.

THE DECISION, AND THE RULE IT WAS MADE BY. The question was whether the native
engine should take the managed one's performance-core default. The rule, written
before the measurement: change it if, FOR GENERATION, the performance-core count
(8 here) is no more than 2% slower than the physical-core count (16 here) on
EVERY subject AND more than 3% faster on at least one.

    SmolLM 360M Q8_0    128.65 / 132.94 = 0.9677   8 threads 3.2% SLOWER
    MuPT 190M BF16      174.57 / 182.05 = 0.9589   8 threads 4.1% SLOWER
    MuPT 190M Q4_K_M    383.86 / 406.09 = 0.9453   8 threads 5.5% SLOWER
    MuPT 1.97B Q4_K_M    41.50 /  47.47 = 0.8742   8 threads 12.6% SLOWER
    MuPT 1.97B BF16      15.71 /  16.95 = 0.9268   8 threads 7.3% SLOWER

Five of five fail the first clause and none meets the second, so THE NATIVE
ENGINE'S DEFAULT WAS NOT CHANGED: it is still EnginePhysicalCores.Count().
Prompt processing was decided separately by the same rule and gives the same
answer - eight threads is 14 to 25% slower than sixteen on four of the five
subjects - so an unset BatchThreads, which follows Threads, is also right where
it was. EnginePhysicalCores.cs was not touched at all.

WHAT THE NUMBERS SAY BEYOND THE DECISION, worth keeping:

  * TWENTY-FOUR THREADS - one per LOGICAL processor - IS A CLIFF, not a
    plateau: generation falls to between a third and a sixth of its best and
    the five runs of one configuration swing wildly (MuPT 190M Q4_K_M gave
    290.4, 48.5, 60.7, 61.7 and 53.0 tokens a second). Prompt processing is
    much less affected, which fits: a prompt has enough work per thread to
    absorb a bad schedule and a single token does not.
  * THE GENERATION PEAK IS AT TWELVE THREADS ON THE THREE SMALL SUBJECTS and at
    sixteen on the two 1.97B ones, but the gap between twelve and sixteen is
    under 4% either way everywhere. That is inside what a default should care
    about, and choosing twelve would mean writing three-quarters-of-the-cores
    into the library on the evidence of one machine.
  * SmolLM's PROMPT PROCESSING IS THE ONE ODDITY IN THE SET: 691 tokens a
    second at eight threads against 518 at sixteen and 431 at twelve, and the
    five runs of each are tight (516-520 at sixteen, 691-715 at eight), so it
    repeats and is not noise. It is also the only subject here with 32 layers
    over a 960-wide hidden state, which makes each parallel piece small. It is
    recorded rather than explained, and it is why the consumer documentation
    tells a reader with long prompts to measure BatchThreads rather than
    promising that more is better.
  * THE NATIVE ENGINE AND THE MANAGED ONE REALLY DO DIFFER on this processor.
    The managed engine at sixteen threads is never faster than at eight and up
    to 8% slower (see THE MANAGED ONNX ENGINE); the native engine at sixteen is
    3 to 13% FASTER than at eight on every subject. Whatever the native engine
    does with an efficient core, it is not waiting for it.

THE HARNESS IS NOT IN THE REPOSITORY. It is a scratch console application under
~/Temp/codebrix-ollama-work/t1/, referencing both libraries as a consumer would,
with the logs beside it. It only ever sets an EXPLICIT Threads and BatchThreads,
which is the part of the library this work did not change, so its numbers are
valid before and after.


THE ONNX ROAD, AT A GLANCE
==========================
Running ONNX models is the second road through ModelRunner and it was built in
seven pieces. The sections that describe them run from here to PERFORMANCE
BUDGETS below; this is the map, so that nobody has to read all of them to find
one thing.

  WHAT IT IS MADE OF, bottom to top:
    CodeBrix.Ollama.Core        the ONNX codec and its protobuf reader and
                                writer, shared with the store's reduction and
                                embedded in BOTH packages rather than published
                                as a third one. THE Core PROJECT below is the
                                whole of that decision, including the contract
                                guard, when to bump it, the surface hash and
                                the two-package release check
    ModelRunner/Onnx/           the interpreter: the graph loader, the plan, the
                                buffer arena, and 37 kernels. THE MANAGED ONNX
                                ENGINE (next) and THE CONTRIBUTED AND QUANTIZED
                                OPERATORS
    ModelRunner/Drivers/SkyTnt/ the MIDI driver, and ModelRunner/Midi/ the
                                Standard MIDI File writer and reader, which is
                                OURS and not a port. THE MIDI GENERATION DRIVER
    ModelRunner/Drivers/CausalLm/ and ModelRunner/Tokenizers/
                                the text driver and the managed byte-level BPE
                                tokenizer. THE TEXT GENERATION DRIVER and THE
                                MANAGED BYTE-LEVEL TOKENIZER

  WHAT HOLDS IT UP, and what each one needs to be rebuilt:
    THE ONNX OPERATOR ORACLES        199 checked-in cases and 18 refusals, made
                                     once by a script through the venv. OFFLINE
    THE MIDI DRIVER'S FIXTURES       a tiny two-graph model and every sampling
                                     mask, made once. OFFLINE
    THE TEXT DRIVER'S FIXTURES       a tiny bundle and a tokenizer corpus, made
                                     once. OFFLINE
    THE MIDI ORACLE                  the publisher's own Python, lifted from a
                                     clone. GATED, needs the clone
    THE TEXT DRIVER'S ORACLE         the bundle's own runtime and the
                                     publisher's own tokenizer. GATED, needs the
                                     venv
    THE FOUR TOLERANCE CLASSES       what a comparison is allowed to assert, and
                                     the evidence under each bar

  WHAT IS TRUE OF ALL OF IT, and must stay true:
    * ModelRunner declares ZERO NuGet dependencies and loads no native library
      on this road. Nothing about that is negotiable; it is the reason the road
      exists.
    * NOTHING MODEL-SPECIFIC LIVES OUTSIDE Drivers/. `grep -ril skytnt src/`
      must match Drivers/SkyTnt/ and ONE other file - Contracts/
      MidiGenerationModel.cs, the public entry point, where five lines name the
      internal driver types it constructs and no doc comment, message or
      signature does. `grep -ril mupt src/` must match exactly one file and it
      is ModelManager's, not this road's: Bundles/BundleDefinition.cs gives a
      real published bundle name as the EXAMPLE in a doc comment on the store's
      own API, which predates all of this work and is what documentation of a
      naming scheme does. It is recorded here so that the grep's one match is
      expected rather than alarming. `grep -rilw abc
      src/` and `grep -rn "<n>" src/` must find nothing: a model's notation
      format and its prompt conventions belong to the consumer.
      `grep -ril skytnt src/CodeBrix.Ollama.ModelRunner/Onnx/` and the same for
      midi and mupt must find nothing at all - the engine knows no model.
    * EVERY REFUSAL IS AT LOAD and names what it refused. A graph that loads
      runs.
    * The two libraries never reference each other, in either direction and at
      either level. Only test projects do.

THE MANAGED ONNX ENGINE
=======================
ModelRunner runs ONNX graphs in managed code, in this process, with nothing
installed: no runtime to fetch, no Python, no native library of its own. It is
what OnnxModel.LoadAsync gives you, and it is entirely separate from the GGUF
side of this library, which goes through the native inference engine.

WHY MANAGED AT ALL. The models this was built for decompose everything into
about thirty standard operators - attention, layer normalization and the rotary
embedding are all written out as ordinary arithmetic in the graph - so a small
interpreter runs a whole transformer. A managed one runs on every platform this
package supports, including the ones no upstream native build covers, and it
costs the package nothing: ModelRunner still declares ZERO NuGet dependencies.
It is slower than a tuned native runtime on wide prefill work and close to it on
the token-by-token decoding these models actually do; the numbers are below.

HOW A LOAD WORKS
    1  The file is read and parsed through the codec in CodeBrix.Ollama.Core,
       the same codec the store's reduction uses.
    2  Every weight is converted into the engine's own tensor and the codec's
       copy of its bytes is RELEASED AS EACH ONE IS CONVERTED, so the two are
       never both held for the whole model.
    3  A matrix multiply whose right-hand side is a CONSTANT takes that weight
       into its own state, turned round from the stored [K, N] into [N, K], and
       the stored layout is let go the moment the last node that wanted it has
       been served. The layout is a threading decision: turned round, each
       thread owns whole output elements and never writes into another thread's
       cache line. It is a CACHE decision too - one column of a result is then
       one contiguous weight row, which is what lets a prompt read each weight
       row once instead of once per position.
    4  Everything the engine will not do is REFUSED HERE, naming the node and
       the operator: an operator it does not implement, a vendor's domain, an
       operator set older than 13, an element type it does not compute in, an
       attribute it would otherwise ignore, a node reading a tensor nothing has
       produced yet. A graph that loads runs.
    5  What comes out is an execution plan: the nodes in the order they run, a
       slot per named tensor, and the node at which each tensor is last read.

    BEFORE LIFETIMES ARE ASSIGNED, OnnxPlanOptimizer combines a right-hand
    Transpose (last two axes only), optionally followed by Mul, with MatMul.
    Both intermediates must have one consumer and neither may be a graph
    output. OnnxFusedMatMulKernel reads the original [N,K] rows and applies a
    scalar scale to each value while multiplying, eliminating the full-cache
    transpose and scale buffers. A nonscalar scale or a scale introducing a
    broadcast axis executes the original kernels instead. Runtime ranks are
    still validated. The metadata reports the original graph's operators;
    the execution plan identifies the combined nodes as MatMulTranspose[Scale].

    THE LOAD COUNTS WHAT IT HAS THROWN AWAY and asks the collector for it when
    it is worth a collection (OnnxLoadReclaim, threshold 128 MiB). A load
    abandons a model's worth of memory three times over in a few hundred
    milliseconds - the file's own bytes once the message is parsed, the codec's
    copy of every weight once the engine has its own, and the stored layout of
    every matrix once a kernel has turned it round - while never holding more
    than one copy live. The collector cannot know that and grows its budget
    instead, so an 822 MB graph used to peak at 3,156 MiB and settle at 782; it
    now peaks at about 1,790 and settles at the same place, and the load is no
    slower for it. A graph smaller than the threshold never asks for a
    collection at all, which is why the hundreds of tiny fixture graphs the
    offline suite loads pay nothing for this.

    THE WHOLE LOAD IS SYNCHRONOUS INSIDE ONE Task.Run, and that is deliberate.
    Nearly all of it is arithmetic, so there is nothing for an awaiting thread
    to wait on - and a local of an async method lives in a state-machine object
    that the returned task keeps alive, so the file's bytes would stay reachable
    for as long as the CALLER held the task it awaited. That was measured, not
    guessed: it was worth 784 MiB on the larger of the two graphs below.

HOW A RUN WORKS
    Tensors in by name, tensors out by name, and NOTHING CARRIED BETWEEN RUNS.
    A decoder's key/value cache belongs to whatever drives the model: feed the
    previous run's `present` outputs back as this run's `past` inputs, passing
    the very same OnnxTensor instances, and nothing is copied.

    NO SHAPE IS WORKED OUT IN ADVANCE. The graph asks its own tensors how long
    they are - Shape, Gather, Concat, Range, ConstantOfShape - as ordinary int64
    tensor arithmetic on the same execution plan as everything else, so the same
    plan runs a prompt of five hundred positions and a cached step of one. An
    EMPTY tensor is an ordinary case and not an edge case: the first cached step
    of the token graph is handed a token sequence of length nought and a past of
    length nought, and every operator answers for it.

    BUFFERS COME FROM AN ARENA. The plan knows which node last reads each
    tensor, so a buffer goes back into the pool the moment that node has run,
    and a second step of the same model allocates almost nothing. Reshape,
    Unsqueeze and Identity share a buffer rather than copy one, and a reference
    count is what makes that safe. A tensor a run HANDS BACK never comes from
    the pool, which is what lets a driver hold it and feed it back. Buffer reuse
    can be switched off (OnnxRunnerOptions.ReuseBuffers); the two settings must
    produce identical numbers, and the suite runs every oracle both ways to say
    so.

    Reusable arrays of at least 1,024 elements receive 25 percent growth
    headroom, capped without integer overflow. This prevents an increasing
    context from retaining another exact-sized intermediate at every step.
    Fresh element arrays use GC.AllocateUninitializedArray: every kernel must
    overwrite or explicitly clear the values it exposes, just as it already
    must for a reused buffer. Outputs handed to a caller remain exact-sized
    and are never recycled. Large Concat outputs (at least 262,144 elements)
    divide independent outer slices across the configured threads.

    Scalar float broadcasting uses the portable vector arithmetic path, with
    operand order preserved for Sub and Div. The scalar kernel setting still
    selects scalar arithmetic. These changes, and the transpose fusion, also
    apply to full-precision graphs; they do not require quantized weights.

    ONE RUN AT A TIME per loaded model, and the second caller is REFUSED rather
    than queued. Load a second model to run two at once.

THE THREAD DEFAULT IS THE PERFORMANCE-CORE COUNT, not the physical-core count,
and that is a measurement rather than a preference. A matrix multiply is split
into equal pieces and the node is not finished until its slowest piece is, so a
thread running on an efficient core holds up every thread running on a fast one.
On the laptop everything here was measured on - eight performance cores, eight
efficient ones, twenty-four logical processors - the engine at sixteen threads
is never faster than at eight and is up to eight per cent slower, for twice the
processor; the curve is flat from about six threads upwards.

    EnginePerformanceCores (Engine/EnginePerformanceCores.cs) asks the operating
    system and answers NOUGHT when it cannot tell, in which case the default
    falls back to EnginePhysicalCores. It reads /sys/devices/cpu_core/cpus on an
    Intel-style hybrid Linux machine and counts those logical processors as
    physical cores through /proc/cpuinfo; the largest cpu_capacity and how many
    processors report it on an ARM big.LITTLE one; hw.perflevel0.physicalcpu on
    macOS; and on Windows the cores whose EfficiencyClass is the highest any
    core reports. A processor whose cores are all alike answers nought by
    design, because there is then nothing to say.

    IT IS A SEPARATE TYPE FROM EnginePhysicalCores ON PURPOSE, and the two
    platform calls are written out twice rather than shared: they answer two
    different questions about the processor and either is wanted without the
    other. It lives in Engine/ under an ENGINE-NEUTRAL name because it describes
    the machine rather than a road through the library - but only the ONNX road
    calls it. THE NATIVE ENGINE WAS MEASURED ON THE SAME HYBRID PROCESSOR AND
    WANTS THE OTHER ANSWER: it is faster on every physical core than on the
    performance cores alone, so ModelRunnerOptions.Threads still defaults to
    EnginePhysicalCores. The measurement, and the rule that decided it, are under
    THE THREAD COUNTS: TWO DEFAULTS AND A CAP, MEASURED 2026-09-18 below.

THE THREE ARITHMETIC PATHS. The matrix kernels are written three times: 256-bit
AVX2 with fused multiply-add, .NET's portable Vector<T>, and one element at a
time with no vector instruction at all. OnnxKernelPath.Automatic picks the
widest the processor offers and is what a caller should use; the other two exist
so the three can be held against each other. They do not sum in the same ORDER,
so a long dot product can differ in its last bits between them - that is
floating-point reassociation, not a defect, and the suite pins the difference
rather than pretending it is not there. THIS IS ALSO WHAT MAKES ARM64 AND
RISC-V CORRECTNESS TESTABLE ON AN x86 MACHINE: every oracle is run on all three.

WHAT IT DOES NOT DO, deliberately, at this stage: no sub-graphs (If, Loop,
Scan), no training operators, no accelerator, and no contributed operator
outside the six named below. Each of those is refused by name at load time
rather than half implemented.

THE CONTRIBUTED AND QUANTIZED OPERATORS
---------------------------------------
Six of the engine's operators are not plain ai.onnx arithmetic. They are what a
model BUILDER writes, and what this library's own ReduceOnnxAsync writes, and a
decoder produced either way cannot be run without them.

  com.microsoft:MatMulNBits
        A float matrix multiplied by a weight quantized to four or eight bits
        in blocks down the reduction, each block with its own scale and zero
        point. THE WEIGHT STAYS PACKED FOR THE LIFE OF THE MODEL: it is taken
        into the kernel's state exactly as the file holds it and the block being
        multiplied is turned back into floats INSIDE the loop that reads it. The
        four-bit unpack is vectorised - eight values out of one 32-bit load,
        shifted lane by lane and masked - because the obvious nibble-at-a-time
        version is about ten times slower, which is the difference between a
        quantized model being faster than the one it came from and being several
        times slower. `bias` is implemented; `g_idx`, `weight_prepacked`, a bit
        width other than four or eight, and a block size that is not a power of
        two of at least sixteen are refused by name.
        On AVX2 with FMA, decode calls with fewer than four input rows process
        four output columns together when the reduction is a multiple of eight.
        The columns share activation loads and keep independent accumulators;
        each column keeps its original accumulation order. Remainder columns,
        partial reductions and processors without those instructions retain
        their existing paths. Both four-bit and eight-bit weights have this
        grouped path; the eight-bit load reads exactly eight bytes. No extra
        activation quantization or expansion of the stored weights is introduced.
        accuracy_level IS READ AND IGNORED, and that is the deliberate choice
        recorded as D3 in the plan. It is not a description of the weight: it is
        the LOWEST precision a runtime may compute the ACTIVATIONS at, and 4
        means "you may quantize input A to 8-bit integers too". This engine
        keeps A in floats, which is more accurate than anything the attribute
        permits. What it costs is measured below.
  ai.onnx:DynamicQuantizeLinear and ai.onnx:MatMulInteger
        The dynamic 8-bit path, which is what ReduceOnnxAsync's DynamicInt8 mode
        writes: the activations become bytes and a scale as the graph runs, the
        bytes are multiplied by the weight's bytes into 32-bit integers, and a
        Cast and two Mul nodes turn those back into floats. The rounding is the
        specification's - to nearest, ties to EVEN, saturating - and it is the
        whole operator: (int)(x + 0.5f) would send 0.5 to 1 and 2.5 to 3 where
        the answer is 0 and 2. A constant weight is turned round to [N, K] at
        load time and kept ONE BYTE PER ELEMENT; an unsigned weight is stored
        signed with its zero point shifted by 128, which is the same arithmetic
        and leaves one kernel instead of four. On AVX2, four output columns
        share each activation load and zero-point subtraction. Products widen
        into 32-bit integers without saturation; row boundaries, worker
        boundaries and leftover elements retain exact integer arithmetic.
  com.microsoft:GroupQueryAttention
        A whole attention block in one operator: the rotary embedding, the
        key-value cache, the causal mask, the softmax and both matrix products.
        Its semantics are PORTED from onnxruntime v1.30.0 rather than derived,
        because three of them are counter-intuitive and each gives a model that
        runs and writes nonsense when it is guessed at. seqlens_k is the total
        length MINUS ONE, per batch, not the past length. total_sequence_length
        is a scalar and it decides what KIND of step this is: equal to the
        query's own length means a first prompt, where nothing is taken from the
        cache however long the cache is and the causal mask starts at nought.
        The present cache is as long as the LONGER of the total and the past
        buffer, and the rows past what the step filled stay at nought. A local
        window, a softcap, a smooth softmax, a sliding-window or quantized
        cache, a query/key normalization, an attention bias, a head sink,
        caller-supplied position identifiers and the score output are each
        refused by name; the exports carry the first two as their defaults (-1
        and nought), which mean the operator is not doing either, and those are
        accepted.
  com.microsoft:SkipSimplifiedLayerNormalization
        Residual, optional bias, then root-mean-square normalization. ITS FOURTH
        OUTPUT IS NOT OPTIONAL IN PRACTICE: input_skip_bias_sum is the residual
        stream, and the builders wire it into the next block, so a kernel that
        produced only the normalized tensor would load and then compute the
        wrong thing from the second layer on.
  SimplifiedLayerNormalization
        The same normalization without the residual - and it lives in the
        DEFAULT domain although no release of the ONNX specification defines it
        there, which is where the builders put it, so that is where this engine
        answers for it. stash_type other than 1 is refused.

    THE ORDER OF THE NORMALIZATION'S ARITHMETIC IS PART OF THE PORT. Upstream
    sums the squares, takes sqrt(sum / n + epsilon), and writes
    value / deviation * gamma - a division then a multiplication, not a
    multiplication by a reciprocal. The two are algebraically the same and
    differ in the last bits of every element of every layer.

    THE PORTABLE VECTOR PATH IS THE SCALAR PATH for MatMulInteger. Its
    arithmetic is exact, so all three paths give identical integers; the wide
    path is 256-bit AVX2, which widens sixteen bytes and sums their products in
    pairs in one instruction, and .NET's portable Vector<T> offers no 8-bit
    widening multiply-add to build the same thing out of. Making that path fast
    on a processor without AVX2 is a performance question, not a correctness
    one.

WHAT WAS MEASURED, 2026-09-18, on the quantized subjects
--------------------------------------------------------
Same laptop. Each subject is produced by this library's own store - a reduction
of the published MIDI pair, or an ONNX Runtime GenAI builder export of the MuPT
190M checkpoint and the store's reductions of it - and compared against
onnxruntime 1.30.0 on identical inputs, one step and a cached second step. The
`answer` column is the largest relative difference over the outputs that are NOT
the key-value cache; `cache` is the same over the cache.

    SUBJECT                        answer      cache       greedy choices
    skytnt int4 (both graphs)      8.0e-07     1.7e-06     0 differ
    skytnt int8 (both graphs)      5.0e-07     9.3e-07     0 differ
    skytnt dynamic int8            4.2e-03     1.2e-02     0 differ
    mupt builder fp32              2.1e-06     2.3e-06     0 differ
    mupt builder int4              2.2e-02     3.4e-02     2 of 5 differ
    mupt reduced int4              4.2e-06     4.0e-06     0 differ
    mupt reduced int8              2.1e-06     2.9e-06     0 differ
    mupt reduced dynamic int8      1.9e-02     2.6e-02     0 differ

    QUANTIZING A WEIGHT COSTS NOTHING IN AGREEMENT: every weight-only subject
    matches onnxruntime to about a millionth, which is the fp32 figure. The same
    packed bytes and the same scales are being read on both sides, so it is the
    same arithmetic in a different language.

    QUANTIZING AN ACTIVATION MAKES A STEP CHAOTIC, and that is a property of the
    graph rather than of either engine. A tensor's scale comes from its own
    largest and smallest element, so a difference in the LAST BIT of one element
    moves the scale, which moves every value that was sitting halfway between
    two integers by a whole count, and a dozen layers multiply that up. It is
    measurable from one side alone: THIS ENGINE'S OWN TWO ARITHMETIC PATHS -
    which differ only in the order they add floats in - disagree with EACH OTHER
    by up to 3.7e-2 on the dynamic subjects, while agreeing to 1e-6 on every
    weight-only one. The bar for a dynamic subject is therefore 6e-2, measured
    and not chosen; holding it tighter would be pinning noise.

    THE mupt builder int4 ROW IS accuracy_level 4 AND NOTHING ELSE. That export
    asks a runtime to quantize the activations to 8-bit integers as well, and
    onnxruntime takes the option while this engine keeps floats. THAT PROOF IS A
    TEST, not a note:
    MuPtOnnxLiveTests.Run_matches_onnxruntime_on_the_builder_four_bit_export_without_its_accuracy_level
    gives the ORACLE a copy of the graph with the attribute taken off all 61 of
    its MatMulNBits nodes - written into the test's own temporary folder beside
    the model, never into the store - and gives THIS ENGINE the original,
    unmodified graph, because it reads the attribute and ignores it and so
    answers the same either way. The two then agree to 2.9e-06 on step 0 and
    1.1e-06 on the cached step, with no greedy choice differing, at one thread
    and at eight; the test holds them to the ordinary quantized-weight bar of a
    hundredth. SO THE ENGINE IS THE MORE EXACT PARTY HERE and the wide bar on
    the row above is the price of onnxruntime taking a shortcut the attribute
    offers it - not a looser standard for this engine. The offline oracle
    matmul_nbits_4bit_accuracy_level pins the same difference on one node.
    ONLY THE BUILDER'S EXPORT CARRIES THE ATTRIBUTE; ReduceOnnxAsync leaves it
    off unless ReduceOptions.AccuracyLevel is set, so the store's own reductions
    need no equivalent test.

    MEMORY, the point of the whole quantized path: the published 822 MB graph
    holds 782 MiB of managed objects once it has loaded, and this library's
    four-bit reduction of it holds 117 MiB - a factor of 6.7. That is measured
    as the managed heap either side of the load, with a full collection at both
    ends, and it is what the loader would throw away by expanding a packed
    weight.

    MILLISECONDS PER STEP, managed at 1 and at 8 threads against onnxruntime at
    8, on the quantized subjects, from one run of the suite:
    SUBJECT                    step 0  @1 / @8   (ort)    step 1  @1 / @8
    skytnt int4   base graph    48.1 / 15.1  (ort  8.4)    24.3 /  7.3
    skytnt int8   base graph    63.1 / 16.9  (ort 37.2)    31.5 / 10.1
    skytnt dyn8   base graph    28.7 / 10.0  (ort  4.5)    14.9 /  7.4
    mupt int4     (builder)     84.3 / 27.5  (ort  3.1)    20.8 /  7.5
    mupt int4     (reduced)    127.0 / 83.4  (ort  6.4)    63.4 / 25.8
    mupt int8     (reduced)     94.6 / 19.9  (ort 35.0)    26.3 /  5.7
    mupt dyn8     (reduced)     53.9 / 12.1  (ort  3.3)    21.4 /  4.3
    THOSE ARE THE FIGURES AS THIS PHASE MEASURED THEM and the step-0 column is
    now out of date: step 0 is a PROMPT of several positions, which is the shape
    the performance pass rewrote, and the reduced int4 row - the slowest of the
    eleven, at 127.0 / 83.4 ms - is now 74.9 / 15.5. What made it the slow one
    was that its 50,000-wide output projection unpacked every four-bit column
    again for every position of the prompt; a column is now unpacked once for
    all of them. PERFORMANCE BUDGETS below has the current numbers for every
    subject. The onnxruntime column is not a like-for-like comparison of the
    engines either: it is one process's measurement of one step, and the two
    35-millisecond figures are its own first-touch cost on a 313 MB graph.

    THE SUBJECTS ARE KEPT IN THE TEST-MODEL CACHE under the names
    onnx-oracle/skytnt-tv2o-medium:{int4,int8,dynamic-int8} and
    onnx-oracle/mupt-190m:{fp32,int4,fp32-int4,fp32-int8,fp32-dynamic-int8}.
    The first run of the suite makes them, which costs minutes and, for the two
    builder exports and the dynamic reductions, an interpreter; every run after
    it finds them. Delete one to have it made again.

THE FOUR TOLERANCE CLASSES, AND THE EVIDENCE UNDER EACH (plan D6 and D6a)
-------------------------------------------------------------------------
This is the one place the whole tolerance story is stated together. The classes
are the enumeration OnnxSubjectKind in
tests/CodeBrix.Ollama.EndToEnd.Tests/Infrastructure/, the bars are constants in
OnnxSubjectComparison beside it, and a comparison names the class it belongs to
rather than a number. NOTHING IN THIS TABLE IS A PREFERENCE: each bar is either
the plan's or a measurement, and the "worst seen" column is what the gated suite
reproduced on 2026-09-18.

  CLASS                     BAR     WORST SEEN   GREEDY      WHY THAT BAR
  FullPrecision             1e-4    2.1e-06      must match  D6. Two matrix
                                                             libraries differ
                                                             only in the order
                                                             they add floats up
  QuantizedWeights          1e-2    4.2e-06      must match  D6. The same
                                                             packed bytes and
                                                             the same scales on
                                                             both sides, so it
                                                             is the fp32 figure
  QuantizedActivations      6e-2    1.9e-02      must match  D6a, MEASURED.
                                                             This engine's own
                                                             AVX2 and scalar
                                                             paths disagree
                                                             with EACH OTHER by
                                                             up to 3.7e-2 on
                                                             these graphs, so
                                                             ~1.6x headroom
                                                             over the worst
                                                             self-disagreement
  LowerPrecisionByRequest   6e-2    2.2e-02      reported    D6a. The graph
                                                             asks for 8-bit
                                                             activations and
                                                             this engine keeps
                                                             floats, so the two
                                                             are answering
                                                             different
                                                             questions

  THE EVIDENCE, and where each piece lives:
    * D6's two bars are met with orders of magnitude to spare by every subject
      that meets them at all - the two tables above this section are the whole
      of it, and the greedy choice is IDENTICAL everywhere it is required.
    * The QuantizedActivations bar rests on a measurement of the engine against
      ITSELF, not on a comparison with anything: holding two engines closer
      together than one engine agrees with its own two arithmetic paths would be
      pinning noise. The layer-by-layer picture is the signature - layers 0 to 8
      of the dynamic MuPT graph agree with onnxruntime to 1e-7 and the
      divergence appears at layer 9 and grows, which is one quantization
      decision flipping and not a wrong kernel - and the CACHED step of the same
      subject agrees to 2.0e-07, because with a real cache the activations are
      not on the knife edge.
    * The LowerPrecisionByRequest class is the one where this engine is the MORE
      exact party, and the proof is the stripped-attribute TEST named above
      rather than a note. It is the only comparison in the suite where the two
      engines are given different files, and the only fixture in the offline
      suite that states a tolerance of its own is
      matmul_nbits_4bit_accuracy_level, which pins the same effect on one node.
    * A DRIVER-level check is stronger than any of them and is what the two
      driver sections below record: 384 of 384 greedy MIDI events identical to
      the publisher's own Python over three prompts, and 96 of 96 greedy tokens
      plus 118 of 118 prompt tokens identical to the bundle's own runtime, both
      on 2026-09-18. A tolerance is what a NUMBER is held to; an identical
      generation is what a MODEL is held to, and both are kept.

HOW TO ADD AN OPERATOR
----------------------
The engine is meant to grow one operator at a time, and the shape of the work is
the same every time:

  1  WRITE THE KERNEL, one file under Onnx/Kernels/, named
     Onnx<Operator>Kernel.cs, deriving from OnnxKernel. State its OpType, its
     Domain where it is not the standard one, its input and output counts, and
     THE ATTRIBUTE NAMES IT UNDERSTANDS - the loader refuses any attribute not
     on that list, which is what stops a graph being silently misread.
  2  DO THE WORK THAT CAN BE DONE ONCE IN Prepare, at load time - turning a
     constant weight round, settling a normalization's axis, deciding a cache
     layout - and only the per-run arithmetic in Run. A weight folded at load is
     let go from the plan as soon as the last node that wanted it has been
     served, so Prepare is also where a load's memory behaviour is decided.
  3  REGISTER IT in OnnxKernels, which is keyed by (domain, operator). An
     operator that is not there is refused at the NODE, naming the node, the
     operator and the domain - never at the opset import, because a graph may
     declare a contributed domain it never uses.
  4  USE THE SHARED MACHINERY rather than writing a loop: OnnxElementwise for
     anything with numpy broadcasting (implement the static-abstract arithmetic
     interface on the kernel class itself, so the arithmetic compiles INTO the
     shared loop), OnnxDataMovement for a strided walk, OnnxReduction for a
     total, OnnxGemm / OnnxBlockGemm / OnnxIntegerGemm for a matrix multiply.
     Three arithmetic paths come free that way, and so does the RISC-V answer.
  5  THE OPERATOR-SET RANGE IS OnnxKernels.MinimumOpset 13 TO MaximumOpset 23.
     Raise the maximum only after checking the semantics of the new set, and
     never as a convenience: a graph written against a set nobody has checked
     would be run under guessed meanings. DO NOT LOWER THE MINIMUM. Three
     operators changed meaning at opset 13 WITHOUT CHANGING SHAPE - Unsqueeze
     and ReduceSum moved their axes from an attribute to an input, and Softmax
     stopped flattening the tensor at its axis - so an opset-12 graph cannot be
     told from an opset-13 one by looking at the node, and running it under the
     newer meaning would give a wrong answer silently. Refusing it says so.
  6  FIXTURE IT. Add cases to
     tests/CodeBrix.Ollama.ModelRunner.Tests/Onnx/Fixtures/generate_fixtures.py
     - every attribute, every broadcast and axis variant, every element type it
     accepts, and the empty-tensor case - and a REFUSAL case for each thing the
     kernel refuses. Run the generator once, by hand, in the venv; no test ever
     runs it. THE ONNX OPERATOR ORACLES below has the rules that bite.
  7  IF YOU PORTED IT, put the `//was previously:` marker on the namespace line
     and add the file to THIRD-PARTY-NOTICES entry 15's scope table in the same
     change. PROVENANCE AND PORTED SOURCES has the greps that must then agree.

THE MIDI GENERATION DRIVER
--------------------------
src/CodeBrix.Ollama.ModelRunner/Drivers/SkyTnt/ (18 files, all internal) is the
first DRIVER over the managed ONNX engine: it turns a published two-graph MIDI
model into music. The engine itself knows nothing about it, and that is the
rule the whole arrangement rests on - `grep -ri skytnt
src/CodeBrix.Ollama.ModelRunner/Onnx/` finds nothing.

WHAT A DRIVER IS. The engine runs a graph: tensors in by name, tensors out by
name, no state kept. Everything a family of model needs around that - what its
tokens mean, how many graphs it has and in what order, which token may follow
which, where the key-value caches live, how its output becomes something a
caller can use - is a driver. Which driver runs is decided by what the BUNDLE
says about itself: a config.json naming this family's architecture gets this
one, and a bundle that names something else is refused by name.

HOW THE TWO GRAPHS FIT TOGETHER. An event - a note, an instrument change, a
tempo - is a row of up to eight tokens: what kind it is, then its parameters.
The BASE graph reads the events so far and produces one hidden state per event;
the TOKEN graph reads one of those states and spells it out as the row, one
token at a time. So an event costs one large step and about seven small ones
rather than eight large ones, and it is why the published model is two files.

    SkyTntGenerator.cs      the loop: a base step, then up to eight token steps,
                            an event handed to the caller, and round again
    SkyTntCache.cs          one graph's key-value cache. The base graph's grows
                            by a position per EVENT and lasts the whole piece;
                            the token graph's is emptied before every event and
                            grows by a position per TOKEN. A graph's `present`
                            output becomes the next step's `past` input - the
                            same tensor instance, nothing copied
    SkyTntMask.cs           which tokens may be answered with at each position
    SkyTntSampler.cs        temperature, nucleus and top-k sampling, the mask
                            applied AFTER the softmax as upstream does it
    SkyTntRandom.cs         the stream of random numbers, ours, so that a seed
                            means one thing for ever
    SkyTntPrompt.cs         the caller's settings turned into the rows the model
                            reads first
    SkyTntTokenizer.cs      the vocabulary and the two token-row conversions
    SkyTntTokenizer.Midi.cs the same type, split by subject: a piece of music
                            read IN, and a generation turned back out
    SkyTntBundle.cs         what a bundle says about itself, and the refusals
    SkyTntGenerationModel.cs  the IMidiGenerationModel a caller holds

THE MASKS ARE THE PART THAT LOOKS OPTIONAL AND IS NOT. At the first token of an
event only the six kinds of event and the ending token are allowed; at the
token after it, only the values of whatever parameter that kind puts first. Get
one wrong and nothing fails: the model still answers, the row still decodes, and
the music is quietly not the music the publisher's code would have written.
They are pinned against the publisher's own tokenizer in masks.json - see THE
MIDI DRIVER'S FIXTURES below.

EVENTS ARE HANDED OVER AS THEY ARE MADE. GenerateAsync yields each event the
moment its last token is sampled, so a caller can start a player while the rest
of the piece is still being written. Each event carries an ABSOLUTE position in
ticks, a note's own length, and a HORIZON - the start of the beat it sits in.
The beat only ever moves forward, so nothing yielded later is earlier than a
horizon already given; within one beat the offsets can arrive out of order, and
that is why the horizon is a beat and not the event's own tick. Channels are
0 to 15 with 9 for percussion, and tempo is in quarter notes per minute.

WHAT THE STREAM CANNOT DO, and it is stated in the API's own remarks: the
tokenizer shortens a note that the NEXT note of the same pitch cuts off, which
needs the future. ToScore does it when the piece is gathered up; a player
sounding the stream holds such a note a little longer than the saved file does.

THE FILE WRITER IS OURS. src/CodeBrix.Ollama.ModelRunner/Midi/ (5 files) is a
Standard MIDI File writer and reader written against the published format, not
ported from anything - THIRD-PARTY-NOTICES entry 16 says why, at length, and
that paragraph is the one to read before anybody is tempted to "just port the
Python one".

WHAT IS DELIBERATELY NOT HERE. Version one of the tokenizer (the published model
asks for version two and says so; another version is refused by name), writing
several pieces at once (upstream's batch, which this driver reduces to one), and
the publisher's own random number generator (only GREEDY generation is claimed
to be identical, and that is what is proved).

THE MIDI DRIVER'S FIXTURES
--------------------------
tests/CodeBrix.Ollama.ModelRunner.Tests/Drivers/SkyTnt/Fixtures/ holds what the
driver is tested against with nothing installed and nothing downloaded:

    tiny-model/            a bundle with the SAME CONTRACT as the published one
                           - the same input and output names, the same two
                           caches, the same 3406 tokens, the same eight-token
                           rows - and 70 KB of made-up weights. One number of
                           every state it produces is the LENGTH of the base
                           cache, and the ending token's answer is the only
                           thing that reads it, so a greedy generation stops by
                           itself after about a dozen events - which is also the
                           fence for the cache, because a driver that lost it
                           would never stop
    masks.json             every sampling mask, computed by running the
                           PUBLISHER'S OWN tokenizer through the branches of its
                           own loop

    generate_tiny_model.py        writes the bundle; needs onnx and numpy
    generate_mask_fixtures.py     writes masks.json; needs a checkout of the
                                  publisher's repository, named on its command
                                  line

Both are run BY HAND, once, and NEITHER IS EVER RUN BY A TEST. README.txt beside
them says who owns what.

THE MIDI ORACLE, AND HOW TO REBUILD IT
--------------------------------------
tests/CodeBrix.Ollama.EndToEnd.Tests/Python/skytnt_generate_oracle.py is the
publisher's own generation loop, run through onnxruntime, so that the managed
driver can be held to the music it writes. It LIFTS generate(), sample_top_p_k()
and softmax() out of app_onnx.py rather than importing them, because that module
imports a web framework and a synthesizer binding at module scope; its header
says so and names the commit. The tokenizer and the MIDI writer are imported
from a checkout as they stand.

    git clone --depth 1 https://github.com/SkyTNT/midi-model <somewhere>
    cd <somewhere> && git checkout f504d5cb58f769ab0f2909c679238f6621034573

NOTHING IS INSTALLED to run it. Its one unsatisfiable import is PIL, which
midi_tokenizer.py imports at module scope and uses only in midi2img, which the
generation path never calls; a six-line stub stands in for it and the script
says whether it did. Point CODEBRIX_OLLAMA_SKYTNT_CLONE at the checkout and the
three identity tests run; leave it unset and they skip.

tests/CodeBrix.Ollama.EndToEnd.Tests/Python/midi_parse_check.py reads a file
this library WROTE with the publisher's own MIDI.py, out of the same checkout -
a second opinion on the file format from something that had no part in writing
it.

THE TEXT GENERATION DRIVER
--------------------------
src/CodeBrix.Ollama.ModelRunner/Drivers/CausalLm/ (9 files, all internal) is the
SECOND driver over the managed ONNX engine: it turns a single-graph decoder into
text. Like the first, the engine knows nothing about it - and unlike the first,
nothing in it knows about any particular model either. `grep -ril
"mupt\|abc\|<n>" src/` finds nothing, and that is a standing check.

WHAT DECIDES EVERYTHING IT DOES is the bundle's own genai_config.json. The
decoder block names the graph file, says what every tensor is called (including
the `%d` patterns the per-layer cache tensors are numbered with), and states the
layer count, the head counts, the head size, the context length and the token
numbers that begin and end a sequence. Nothing is assumed and nothing is
guessed: a bundle from another publisher filling the same block in differently
is driven by exactly the same code.

    CausalLmBundle.cs       what the configuration says, and the refusals: an
                            encoder, an encoder-decoder initializer, a vision,
                            speech, audio or embedding block, or a decoder made
                            of a PIPELINE of several graphs, is refused by the
                            name the file gives it, because each of those is a
                            different loop and not a different setting
    CausalLmDecoder.cs      the decoder block, read as it stands
    CausalLmCache.cs        the key-value cache: two tensors per layer, grown by
                            one position a token. A run's `present` output
                            becomes the next run's `past` input - the same
                            tensor instance, nothing copied. What the graph
                            declares is checked against what the configuration
                            states, so a bundle whose two halves disagree fails
                            at load with a sentence
    CausalLmGenerator.cs    THE LOOP: the whole prompt in one run, then one
                            token at a time. Four things end it - an
                            end-of-sequence token, a stop sequence in the text,
                            the request's token limit, and the context filling
                            up - and cancellation is honoured between steps
    CausalLmSampler.cs      the penalties, the truncations, the temperature and
                            the draw, in the order EngineSamplerChain builds for
                            the native engine, so that the same SamplingOptions
                            mean the same thing on both routes
    CausalLmCandidate.cs    one token the sampler is still considering
    CausalLmRandom.cs       the stream of random numbers, ours, so that a seed
                            means one thing for ever
    CausalLmPlan.cs         one request, settled and checked before any of it
                            runs
    CausalLmGenerationModel.cs  the IOnnxCausalLmModel a caller holds

THE ATTENTION MASK IS HOW THE GRAPH LEARNS ITS LENGTHS, and it is the part that
looks like bookkeeping and is not. These decoders work out INSIDE the graph how
many positions are in play (`seqlens_k` is the total less one, per batch) and
which the last one is (`total_sequence_length` is the mask's own width), by
totalling the mask and by taking its shape. So the mask a run is given has one
entry per position, past and new alike; there is nothing else to tell the graph
how far along the generation is, and a mask that is one entry short produces a
model that runs and writes nonsense.

IT IS AN IRunningModel, which is the point. The four text members -
TokenizeAsync, DetokenizeAsync, GenerateAsync and GenerateToEndAsync - work with
the same GenerationOptions, SamplingOptions, GenerationUpdate and
GenerationResult the native route uses, so a consumer holding an IRunningModel
can be handed either. ClearCacheAsync completes at once, because every request
on this route evaluates its whole prompt already and nothing is held between
them.

WHAT IT REFUSES, each with a NotSupportedException naming the member or the
setting, and each for a reason rather than a shortage of time:

    ChatAsync, ChatToEndAsync, RenderChatPromptAsync
                            a bundle carries no chat template and this driver
                            applies none, so a conversation is the CALLER's to
                            render into a prompt. (This library does carry a
                            Jinja engine; wiring it to a tokenizer_config.json
                            chat template would be a phase of its own, and the
                            subject this one was proved against has no such
                            template to test it with.)
    EmbedAsync              a decoder graph answers with one score per token of
                            the vocabulary and hands back no hidden state to
                            pool into a vector
    SetLoraAdaptersAsync    an adapter is applied to a checkpoint's weights as
                            they are loaded, and a graph's weights are already
                            baked into it
    GenerationOptions.Grammar / .JsonSchema / .JsonMode
                            constraining output needs a grammar engine, which
                            the native half of this library gets from the engine
                            it binds to and this driver has none of. Ignoring
                            the setting would hand back text that does not obey
                            the schema the caller was relying on

Details and Options are FILLED IN rather than refused, because a property that
throws is worse than one that is documented: Details carries the graph's path
and size, the architecture the bundle names, the layer, head, vocabulary and
context numbers, and a ParameterCount of nought - a generation configuration
does not state one. Options carries the three settings the shared contract has a
place for (the graph's path, the thread count in use and the context length);
RunnerOptions is what the model was really loaded with. ChatTemplateDialect
answers Auto because the enum has no "none" and chat is refused anyway.

SAMPLING: every field of SamplingOptions is implemented - temperature (nought is
greedy), top-k, top-p, min-p, locally typical, the repeat, presence and
frequency penalties with their window, and the seed. Seed = null requests a
fresh seed; the numeric native sentinel 0xFFFFFFFF is rejected by the shared
SamplingOptions setter. Only GREEDY generation is claimed to
match the other route or another engine: the draw comes from this library's own
generator, which is not llama.cpp's, and the penalties see the tokens a request
GENERATED and never the tokens of its prompt (the same choice the native path
makes, and it makes a repeat penalty act on a shorter history than the same
number would elsewhere).

THE MANAGED BYTE-LEVEL TOKENIZER
--------------------------------
src/CodeBrix.Ollama.ModelRunner/Tokenizers/ (6 files, all internal) reads a
bundle's vocab.json and merges.txt and turns text into token numbers and back,
with nothing installed. It is a GPT-2 byte-level byte-pair encoder: text becomes
UTF-8 bytes, each byte becomes one of 256 printable symbols, and the merge table
joins neighbouring symbols in rank order until no pair of them is in the table.

    Gpt2ByteTable.cs        the byte-to-symbol table, DERIVED in the published
                            code's own steps rather than written out, because
                            the derivation is what makes it checkable
    Gpt2PreTokenizer.cs     the first cut - see the next paragraph
    Gpt2ByteLevelTokenizer.cs  the encoder: the merges with a per-piece cache,
                            the added tokens matched as text before anything
                            else, encode, decode, and the bytes one token
                            contributes to a stream
    Gpt2TokenizerSettings.cs / Gpt2AddedToken.cs   what the configuration files
                            said
    Gpt2TokenizerFiles.cs   builds one out of a bundle's files, and refuses by
                            name a tokenizer of another kind: a SentencePiece
                            model, a tokenizer.json with no byte-level pair
                            beside it, or no tokenizer at all

THE PRE-TOKENIZER IS A SCAN AND NOT A REGULAR EXPRESSION, and that is a
CORRECTNESS decision rather than a performance one. The published rule is one
pattern - 's|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+
- run on an engine that works in CODE POINTS. .NET's works in UTF-16 units,
where a character outside the basic multilingual plane is a surrogate pair and
\p{L} matches neither half of it; a Chinese character from the extension B block
or a mathematical italic letter is a LETTER to the published engine and
"anything else" to a .NET regular expression, which is different pieces,
different merges and different token numbers. The scan reads one code point at a
time and so agrees everywhere. The test suite holds it to a compiled .NET
regular expression of exactly that pattern over the basic plane, and holds its
three character classes to the framework's own \s, \p{L} and \p{N} over every
one of the 65,536 characters of that plane; two further tests show the astral
case where the regular expression and the scan really do part company.

THE MERGES RULE, AND WHY IT IS THE ENGINE'S. Core's Tokenizers/Gpt2MergeTable is
what reads merges.txt, here and in the checkpoint conversion on the other side
of the repository: it drops a first line only when it begins with `#`. The
published Python tokenizer instead does read().split("\n")[1:-1] and drops the
first line WHATEVER IT IS. The two rules can therefore disagree by one merge -
and on the bundles this driver reads they never do, because a bundle written by
a model builder carries the header: the builder saves the tokenizer through the
publisher's own save path, and that path writes "#version: 0.2" and then the
merges the tokenizer was holding. Measured on the real subject: the source
checkpoint's merges.txt has 41,710 lines and no header, and the builder's export
of it has the header and 41,709 merges - the same 41,709 the publisher's own
tokenizer held, entry for entry. The gated test proves what follows from that:
the managed tokenizer's token numbers are the publisher's Python's, exactly, on
every prompt. A merges.txt with no header is the case where the two would
differ, and the offline suite records what this rule does with one.

ADDED TOKENS are matched as text before the text is cut into pieces, longest
first, and only when the caller asks for it (parseSpecialTokens); a bundle
declaring a token with lstrip, rstrip or single_word set is refused by the name
of the rule, because each of those changes WHERE a token matches and none of
them is implemented. Decoding gathers the bytes of consecutive ordinary tokens
and decodes the run in one go, so a character whose bytes were split across two
tokens comes out whole; a special token is written out or dropped as the caller
asked, and it interrupts the run rather than being decoded as bytes.

THE TEXT DRIVER'S FIXTURES, AND ITS ORACLE
------------------------------------------
tests/CodeBrix.Ollama.ModelRunner.Tests/Drivers/CausalLm/Fixtures/ holds what the
driver is tested against with nothing installed and nothing downloaded:

    tiny-bundle/           a bundle with the SAME CONTRACT as a builder's
                           export - the same configuration shape, the same
                           contributed operators (GroupQueryAttention with its
                           rotary embedding and cache,
                           SkipSimplifiedLayerNormalization with its fourth
                           output carrying the residual, and
                           SimplifiedLayerNormalization in the default domain),
                           the same mask arithmetic - in 160 KB. The LENGTH of
                           the sequence so far is added to the end-of-sequence
                           token's score and to nothing else, and that length is
                           read out of the attention mask INSIDE the graph, so a
                           greedy generation stops by itself after a dozen or two
                           tokens - which is also the fence for the mask, because
                           a driver that built it wrongly would never stop
    tokenizer-cases.json   a corpus and the token numbers, pieces and decoded
                           text the PUBLISHED Python tokenizer produced for every
                           string in it - ASCII, contractions, digits, runs of
                           whitespace, multi-byte UTF-8, characters outside the
                           basic multilingual plane, emoji and the special tokens

    generate_causal_lm_fixtures.py   writes both; needs onnx, numpy, regex and
                                     transformers. Run BY HAND, once, and NEVER
                                     BY A TEST

THE CACHE'S FENCE is worth knowing about because it is not the obvious one.
Comparing a step-by-step generation against ONE run over the whole sequence does
not work on this fixture - its scores depend on the width of the attention mask,
and a whole-sequence run gives every position the same width. The comparison the
test makes instead is stronger: generating with the cache fed forward gives the
same token as re-reading the whole prefix from nothing at every step, which is
the only thing a cache is for.

tests/CodeBrix.Ollama.EndToEnd.Tests/Python/causal_lm_oracle.py is the gated
oracle. It uses the publisher's own tokenizer (through transformers, with the
bundle's own tokenization module, which is why trust_remote_code is required)
for the token numbers, and onnxruntime-genai - the runtime the bundle's
generation configuration was written FOR - to generate from them. Its own
tokenizer is not used: it wants a tokenizer.json these bundles do not carry, so
the token numbers are handed to it directly and read back directly, which keeps
the comparison to the loop and the arithmetic. The same script also does TEACHER
FORCING on request, through onnxruntime directly: given a sequence, it reports
the most likely token at every position of it, which is how a quantized variant
is compared without letting one different choice send the two runs down
different paths. NOTHING IS INSTALLED to run it, and no new environment variable
was added for it - it rides on the gates that were already there.

THE ONNX OPERATOR ORACLES
-------------------------
tests/CodeBrix.Ollama.ModelRunner.Tests/Onnx/Fixtures/ holds one small graph per
operator - and per awkward variant of it - with its inputs and the outputs ONNX
Runtime produced from them. They are OURS (MIT), generated from seeded random
numbers; no publisher's data is among them. README.txt beside them says what
each file is.

    generate_fixtures.py   writes every one of them, and is the only thing that
                           may. Run it BY HAND with the reference virtual
                           environment's own interpreter, from that folder:

                               ~/venvs/codebrix-ollama/bin/python \
                                   generate_fixtures.py

                           NO TEST EVER RUNS IT. The suite reads the files as
                           they are checked in, which is the whole point: the
                           numbers it compares against were computed by another
                           implementation of the specification. Regenerating
                           rewrites every output file, so it should be something
                           somebody meant to do.

    a subtlety worth       An output left UNTYPED comes back from onnxruntime as
    knowing before you     a one-element array even when the operator's answer
    change the script      is a scalar, so the declared shapes come from ONNX's
                           OWN shape inference rather than from a trial run.
                           Where inference cannot settle it - an axis list that
                           arrives as an input - the script falls back to a
                           trial run, and REFUSES to guess when that run holds a
                           single element: such a case states its shape by hand.

    Refusals/              graphs the engine has to turn away, with the text its
                           message must carry. They are not run through
                           onnxruntime and not put through the ONNX checker:
                           several are invalid on purpose.

    a contributed graph    is declared against a newer operator set and a newer
    skips the checker      file format - which is what the builders that emit
                           these operators write - and is NOT put through the
                           ONNX checker or its strict shape inference, because
                           the ONNX package has no schema for an operator a
                           runtime vendor contributed. onnxruntime is what says
                           such a graph is valid, and it says so by running it.
                           SimplifiedLayerNormalization skips them for the same
                           reason even though it sits in the default domain.

    one case states its    matmul_nbits_4bit_accuracy_level, and only that one.
    own tolerance          Its attributes ask onnxruntime to compute in lower
                           precision than this engine does, so the two differ by
                           about three thousandths where every other case agrees
                           to a hundred-thousandth. The case exists to PIN that
                           difference rather than to hide it.

WHAT WAS MEASURED, 2026-09-18, on the two published SkyTNT graphs
-----------------------------------------------------------------
Dell Precision 7770, 12th Gen Core i7-12850HX (8 performance cores + 8
efficient), AVX2 and FMA, no AVX-512. Both graphs are the publisher's own fp32
files, unconverted: model_base.onnx 822 MB / 954 nodes and model_token.onnx
117 MB / 271 nodes, both opset 14. One step and then a cached second step,
against onnxruntime 1.30.0 on identical inputs.

    NUMERICS   worst difference, as a fraction of the tensor's own scale:
               token  step 0  9.194e-07     base  step 0  6.697e-07
               token  step 1  7.607e-07     base  step 1  5.577e-07
               and the same greedy choice in every one of 25, 49, 770 and 1,153
               rows of logits. The bar is 1e-4 and no argmax may differ.

    SPEED      milliseconds per step, managed at 1 and at 8 threads, against
               onnxruntime at 8:
               token  step 0   7.6 / 4.5   (onnxruntime 2.2)
               token  step 1   8.3 / 3.4   (onnxruntime 1.6)
               base   step 0  79.0 / 24.6  (onnxruntime 14.2)
               base   step 1  42.9 / 16.1  (onnxruntime 12.9)
               Within 1.7x of onnxruntime on the cached step of the large graph,
               which is the shape a generation actually runs.

    LOADING    model_token.onnx 193 ms, model_base.onnx 977 ms.

    MEMORY     measured on the large graph alone in a process of its own, not
               inside the test host: 782 MiB of managed objects once the load
               has finished and its garbage has been collected - ONE copy of an
               822 MB model's weights - and, AS THIS PHASE MEASURED IT, a
               high-water mark of 3,160 MiB during the load itself. The
               difference was garbage the collector had not yet been asked for:
               the file's bytes, the codec's copy of each weight and the stored
               layout of each one that was turned round. THE PERFORMANCE PASS
               CLOSED MOST OF THAT - the load now counts what it has abandoned
               and asks for it (see HOW A LOAD WORKS) - and the figures to use
               are in PERFORMANCE BUDGETS below.

LONG-CONTEXT MANAGED PERFORMANCE, 2026-09-21
------------------------------------------
The Intel Core i7-12850HX investigation of the reduced SkyTNT pair measured
1,000 Club Arrangement events, greedy sampling, seed 20260921, four threads:

    original managed engine               53.29 s     peak 9.63 GiB
    optimized managed engine, profiled    19.77 s     peak 1.44 GiB
    optimized, two unprofiled repeats     18.83 s, 19.71 s
                                                    peak 1.54 GiB, 1.49 GiB
    native ONNX Runtime CPU               17.19 s

All 1,000 event lines agreed exactly with the original and native runs. The
FP32 SkyTNT pair also improved, from 66.62 s to 39.58 s, with exact event
agreement and peak resident memory reduced from 13.97 to 3.87 GiB. A fresh
native FP32 run took 37.66 s, with all 1,000 events matching both managed
builds; the optimized managed run was 5.1% slower than native. These are
whole-generation measurements after loading, without synthesis. They describe
this model, prompt and processor, not a promise for every ONNX graph.

The INT8 follow-up used the same prompt and settings on both reductions:

    weight-only INT8: original 70.41 s; optimized 19.24 s, 20.25 s;
                     native ONNX Runtime 111.73 s
    dynamic INT8:    original 41.92 s; optimized 25.59 s;
                     native ONNX Runtime 13.31 s

All final runs preserve the original/native 1,000 event lines within each
variant. Weight-only INT8 took 38.71 s with the general improvements before
its new four-column kernel. Native runtime logs confirm that this weight-only
INT8 configuration falls back to expanding weights to FP32 for each multiply.

Dynamic quantization can amplify tiny floating-point reassociation into a
different byte. An initial fused trial took 16.64-17.04 s but changed generated
events, so plans containing DynamicQuantizeLinear retain their original
transpose/scale/matmul operations. The final 25.59 s result keeps the new exact
integer kernel and the general allocation, scalar broadcasting and copy gains.

The changes are the arena growth policy, uninitialized primitive allocation,
vector scalar broadcasting, right-transpose/scale fusion, four-column INT4
and INT8 multiplication, and parallel large Concat copies described above.
Quantized multiplication and output-cache copying remain the largest costs;
the output-cache allocations still grow with the sum of context lengths.

Validation: Release build 0 warnings/0 errors; offline runner suite 3,199
passed and 30 gated skips. The ONNX session/fusion/block/integer subset passed
1,118 tests with AVX2 disabled and again with all hardware intrinsics disabled.
The AVX2/FMA path is guarded; other processors retain portable vector/scalar
execution. Actual ARM64 and Apple Silicon performance was not measured here.
The package still has zero NuGet dependencies and no native ONNX backend.

Detailed evidence on Jeremy's machine:
    ~/ClaudeHome/RESULT_codebrix_ollama_managed_onnx_optimizations_2026-09-21.md
    ~/ClaudeHome/benchmarks/skytnt-managed-optimization-2026-09-21/


PERFORMANCE BUDGETS FOR THE MANAGED ONNX ENGINE, 2026-09-18
-----------------------------------------------------------
Measured on the Dell Precision 7770 (12th Gen Core i7-12850HX: 8 performance
cores, 8 efficient ones, 24 logical processors; AVX2 and FMA, no AVX-512;
62 GiB, Debian 13), .NET 10 Release, the machine otherwise idle, medians of five
timed runs after a warm-up. THESE ARE THE NUMBERS TO COMPARE A CHANGE AGAINST.
Where a figure moved in the performance pass, the earlier one is in brackets.

WHAT A MODEL COSTS TO LOAD AND TO HOLD, each graph loaded ALONE in a process of
its own - not inside the test host, whose figures also carry the oracle's
tensors and whatever the previous test left:

    GRAPH                            FILE    LOAD   PEAK RSS   RESIDENT   HELD
    SkyTNT model_base.onnx  fp32    822 MB  970 ms    1,788      898       782
                                                     (3,156)   (3,111)
    SkyTNT model_token.onnx fp32    117 MB  186 ms      390      387       111
                                                       (463)     (418)
    SkyTNT model_base.onnx  int4    124 MB   64 ms      387      252       117
                                                                 (345)
    SkyTNT model_token.onnx int4     28 MB   19 ms      112      107        27
    MuPT model.onnx         fp32    764 MB  432 ms    1,150    1,084       727
                                                     (2,067)   (1,876)
    MuPT builder int4               109 MB   63 ms      422      324       239
                                                       (515)
    MuPT store's int4               226 MB   80 ms      634      427       226
                                                       (710)     (652)
    (megabytes for the file, MiB for the rest. LOAD is the graph alone, from the
    gated suite. PEAK RSS is the process's high-water mark over the whole load;
    RESIDENT is its resident set once the load's garbage has been collected;
    HELD is the managed heap, which is the model itself and does not move. The
    load is no slower for the collections - the pair of SkyTNT graphs loads in
    1.13 s either way - because the memory it does not commit is memory it does
    not have to fault in.)

WHAT A 16 GiB MACHINE CAN HOLD AT ONCE, plainly. The engine's own cost is HELD;
the peak is transient and lasts about as long as the load. The largest thing
here - the published SkyTNT pair at full precision, both graphs loaded together
as the MIDI driver loads them - holds 893 MiB and peaks at about 1.8 GiB while
the larger graph is being read. On a 16 GiB machine with a couple of gigabytes
already spoken for, that leaves room for several such models at once, and the
four-bit reductions cost a sixth of it. The rule of thumb: BUDGET THE MODEL'S
STEADY COST, AND HAVE THE SIZE OF THE LARGEST SINGLE GRAPH FREE ON TOP OF IT
while it loads. Loading two large graphs at the SAME TIME on a small machine is
what to avoid; loading them one after another is not.

WHAT A STEP COSTS, milliseconds per step at 1 and at 8 threads, with
onnxruntime 1.30.0 at 8 threads for scale. Step 0 is a PROMPT of several
positions and step 1 is one cached position, which is what generation does:

    SUBJECT                       step 0  @1 / @8   (ort)   step 1  @1 / @8
    SkyTNT base graph  fp32        58.3 / 18.6  (14.2)      41.0 / 15.0
                                  (79.0)/(24.6)           (42.9)/(16.1)
    SkyTNT token graph fp32         6.8 /  4.7   (2.4)       7.7 /  3.7
    SkyTNT base graph  int4        49.5 / 16.7   (8.3)      25.1 /  8.2
    SkyTNT base graph  int8        69.3 / 15.3  (38.3)      33.8 /  8.9
    SkyTNT base graph  dynamic 8   27.7 / 10.4   (4.5)      14.8 /  7.6
    MuPT builder       fp32        42.6 / 15.5  (11.1)      20.4 / 10.7
                                  (72.7)/(19.7)
    MuPT builder       int4        77.9 / 25.9   (3.0)      20.7 /  6.6
    MuPT store's       int4        71.8 / 20.9   (6.4)      18.7 /  7.0
                                 (127.0)/(83.4)          (63.4)/(25.8)
    MuPT store's       int8        74.4 / 21.5  (34.9)      26.7 /  6.3
    MuPT store's       dynamic 8   37.6 / 13.1   (3.4)      10.7 /  4.2

    The bracketed figures are what the earlier phases recorded for the same
    subject, and each of those was a single measurement of a single step rather
    than a median; treat a few per cent either way as noise and the large moves
    - the two SkyTNT and MuPT full-precision prompts, and the reduced four-bit
    subject that used to be the slowest of the eleven - as real.

WHAT A GENERATION COSTS, end to end, at 1 and at 8 threads:

    SkyTNT, events a second, 64 events greedy
        full precision      19.0 / 32.7   (15.4 / 31.2)
        the store's int4    21.4 / 55.3   (21.4 / 53.8)
        the publisher's own Python through onnxruntime: 28.4 to 32.2 at eight
    MuPT, tokens a second while generating, and time to the first token of a
    48-token prompt
        full precision      44.5 / 71.0 tokens/s   (39.2 / 70.0)
                               390 / 108 ms first  (633 / 163)
        builder's int4      45.3 / 117.7           (42.9 / 121.0)
                               638 / 166 ms first  (1,038 / 216)
        the store's int4    49.2 / 120.4           (48.8 / 117.8)
                               639 / 164 ms first  (873 / 195)
        THE SAME MODEL THROUGH THE GGUF ROUTE runs at about 190 tokens a second
        on 16 threads. The managed ONNX route is about 2.7 times slower on this
        model and needs nothing installed.

    FOUR-BIT WEIGHTS ARE STILL THE SLOWER PROMPT AND THE FASTER GENERATION, and
    the performance pass moved both without changing which is which: a four-bit
    graph reaches its first token in about 1.6 times the full-precision graph's
    time, and then generates at 1.7 times its rate on eight threads (1.1 times
    on one), holding a quarter of the memory. Where that leaves a caller depends
    on the thread count, and the arithmetic is worth stating: on eight threads
    the four-bit graph is ahead after about TEN generated tokens, on one thread
    after about a hundred and twenty. The prompt is the part with room left in
    it - see the kernel note below.

THE KERNELS THEMSELVES, GFLOPS, on the shapes these models run (m is the number
of positions: 1 is generating a token, 48 is evaluating a prompt):

    SHAPE                              1 thread       8 threads
    fp32  m=1   k=1024 n=1024          37.8 (30.1)    37.9 (33.1)
    fp32  m=1   k=768  n=50000         15.6 (13.5)    32.4 (36.5)
    fp32  m=2   k=1024 n=1024          48.8 (35.5)    39.6 (40.2)
    fp32  m=48  k=1024 n=1024          37.9 (33.3)   136.5 (194.7)
    fp32  m=48  k=768  n=50000         37.7 (13.6)   257.5 (61.3)
    fp32  m=128 k=1024 n=1024          38.0 (33.2)   219.8 (168.4)
    fp32  m=512 k=1024 n=4096          37.5 (33.0)   237.6 (199.4)
    int4  m=1   k=1024 n=1024 b128     15.1           44.2
    int4  m=48  k=1024 n=1024 b128     24.4 (18.0)    98.9 (80.7)
    int4  m=48  k=768  n=50000 b32     24.8 (15.0)   156.9 (96.8)

    THE ONE-THREAD COLUMN IS THE TRUSTWORTHY ONE. Each figure is the median of
    three processes, each itself the median of eleven timed calls; at one thread
    the three agree to a few per cent, and at eight the small shapes do not - a
    whole call takes under a millisecond there and the readings swing by half
    again, which is thread placement rather than the kernel. Where the three
    readings ARE tight at eight threads - the 50,000-wide prompt, and the 128-
    and 512-row ones - the direction is the same as at one thread. Anything
    measured on a shape whose call takes less than a millisecond wants five
    repeats and a look at the spread before it is believed.

    THE FOUR-BIT KERNEL IS THE ONE WITH ROOM LEFT IN IT. It reaches 24 GFLOPS
    on a prompt where the full-precision kernel reaches 38 on the same shape,
    and the reason is structural rather than arithmetic: each output element
    accumulates into ONE vector register, so the chain of fused multiply-adds
    runs at the instruction's LATENCY rather than its throughput, where the
    full-precision dot product keeps four accumulators going at once. Giving the
    four-bit dot the same four accumulators is the obvious next thing to try; it
    changes the order the products are summed in, so it is a change to make with
    the whole gated suite in front of you.

WHAT A PARALLEL REGION COSTS, since every matrix-multiply node enters and leaves
one: 1.4 microseconds at 2 workers, 2.9 at 8, 4.7 at 16, measured on an empty
region. The larger SkyTNT graph runs about 120 of them per step, so at 8 threads
they account for roughly a third of a millisecond in an 18-millisecond step -
about two per cent. A PERSISTENT WORKER POOL WOULD THEREFORE BUY AT MOST TWO OR
THREE PER CENT and would cost a hand-written barrier and the concurrency bugs
that come with one. Measured, considered and NOT DONE; the number is here so
that whoever wonders again does not have to measure it a second time.

THE HARNESS that produced the load, generation and kernel figures is a throwaway
console application, not part of this repository; the per-step figures come from
the gated suite itself with -showLiveOutput.

THE Core PROJECT
================
src/CodeBrix.Ollama.Core is the third project in this repository and the only
one that is not packed. It exists because of one rule and one refusal.

THE RULE: CodeBrix.Ollama.ModelManager and CodeBrix.Ollama.ModelRunner MUST NOT
DEPEND ON EACH OTHER - in either direction and at either level. No
ProjectReference, no PackageReference, no nuspec dependency, and no public API
of one that names a type of the other. What connects them is the CONSUMER's own
code - paths and strings it passes from one to the other - and the test project
that references both.

THE REFUSAL: there is NO third CodeBrix.Ollama package "at this time". A
consumer installs the two packages it already knows about and nothing else.

So code they both need cannot live in either of them and cannot be a package.
It lives here, and BOTH libraries reference this project and pack ITS DLL INSIDE
THEIR OWN NUPKG:

    lib/net10.0/CodeBrix.Ollama.ModelManager.dll
    lib/net10.0/CodeBrix.Ollama.Core.dll          <- the same assembly
    lib/net10.0/CodeBrix.Ollama.ModelRunner.dll
    lib/net10.0/CodeBrix.Ollama.Core.dll          <- in both packages

THE ARROWS POINT ONLY INWARDS. Each library references this project; this
project references NEITHER of them, no other project and no NuGet package. Its
csproj has no ItemGroup at all, and that is the invariant to check first when
anything here changes.

WHAT MAY LIVE IN IT
-------------------
Only what BOTH sides genuinely need:

  - the ONNX codec and its protobuf reader and writer (Onnx/, Onnx/Protobuf/).
    ModelManager reads and rewrites graphs to reduce them; a ModelRunner that
    RUNS an ONNX graph has to read the same files with the same reader, and two
    readers of one format would be two things to keep true.
  - the tokenizer PRIMITIVES both sides need (Tokenizers/): today
    Gpt2MergeTable, the reading of a GPT-2 merges.txt, which the checkpoint
    conversion writes into a GGUF vocabulary and a managed tokenizer has to
    re-derive.
  - CoreContract, below.

WHAT MAY NOT: anything only one side needs. The ONNX QUANTIZER stayed in
ModelManager (Onnx/Quantization/) because ModelManager never runs a model and
ModelRunner never quantizes a graph. An interpreter, kernels and drivers belong
to ModelRunner. Nothing here may know about the store, about the registry or
about a native library. When in doubt, leave it where it is: moving code in
later costs one build; moving it out again costs a contract revision.

EVERYTHING IN IT IS INTERNAL, and a test in CodeBrix.Ollama.Core.Tests says so.
This matters more than it looks: the assembly sits in lib/, so a consuming
application DOES receive it as a reference. Internal-only is what keeps it out
of that application's public API and out of its IntelliSense - and it is also
what makes a public signature naming a shared type impossible, because the
compiler refuses one (CS0050 / CS0051) rather than leaving it to a review.
InternalsVisibleTo.cs names the two libraries, this project's own test suite and
the two ModelManager test assemblies that build graphs out of the codec's
message classes. Keep that list short; add a name only when something cannot
compile without it.

THE PACK RECIPE, which is in BOTH library csprojs, word for word:

    <PropertyGroup>
      <TargetsForTfmSpecificBuildOutput>
        $(TargetsForTfmSpecificBuildOutput);IncludeCoreInPackage
      </TargetsForTfmSpecificBuildOutput>
    </PropertyGroup>

    <Target Name="IncludeCoreInPackage" DependsOnTargets="ResolveReferences">
      <ItemGroup>
        <BuildOutputInPackage Include="@(ReferenceCopyLocalPaths->
          WithMetadataValue('ReferenceSourceTarget', 'ProjectReference'))" />
      </ItemGroup>
    </Target>

with the reference itself carrying PrivateAssets="all":

    <ProjectReference Include="..\CodeBrix.Ollama.Core\
      CodeBrix.Ollama.Core.csproj" PrivateAssets="all" />

PrivateAssets="all" is what keeps the project out of the generated nuspec, so
NEITHER dependency group changes: ModelRunner's stays empty and ModelManager's
still lists CodeBrix.Python and nothing else. The target is what puts the DLL in
lib/. Note that PrivateAssets does NOT stop the assembly being copied into the
output of anything that references a library, so every test project in this
repository gets it without asking; the two that COMPILE against shared types
(ModelManager.Tests and ModelManager.Python.Tests) name the project themselves,
because a private reference does not flow as a compile-time one.

THE CONTRACT GUARD - CoreContract
---------------------------------
The two packages are published TOGETHER, at the same version, in the same
minute. Nothing stops an application from installing two different versions of
them anyway, and that is the one hazard this arrangement has. It was measured
before the arrangement was adopted: with mixed versions the application STILL
BUILDS 0 warnings / 0 errors, because the SDK silently keeps the
higher-versioned copy of the shared assembly, and the older library then dies at
run time with a MissingMethodException from somewhere that looks unrelated.

CoreContract turns that into one sentence. It is PERMANENT - never renamed,
never removed, never reshaped - because it is the one thing an older library
still knows how to call.

    internal const int Revision = 1;              baked into each library by
                                                  ITS OWN compiler
    internal static readonly int LoadedRevision;  read from the copy that was
                                                  actually loaded
    internal static void Require(int builtAgainst, string library)

Each library calls Require(CoreContract.Revision, "<its own assembly name>") at
the PUBLIC ENTRY POINTS that reach shared code - not from a module initializer,
so the consumer sees the message itself rather than a type-initializer wrapper
around it. Today those entry points are:

    ModelStore.ReduceOnnxAsync      drives the codec
    ModelStore.ConvertToGgufAsync   reads tokenizer.model through the protobuf
                                    reader
    ModelRunner.LoadAsync           the two doors a model comes in through
    ModelRunner.ProbeAsync

Add the call to any new public entry point that reaches shared code; leave it
off the ones that do not. It costs one integer comparison.

IT IS REVISION-BASED AND MUST NEVER COMPARE VERSIONS. Every assembly here is
stamped to the minute it was built, so inside a development tree this project's
version differs from both libraries' after any incremental build. A version
comparison would fail constantly and mean nothing.

WHEN TO BUMP Revision: by one, whenever anything a library can reach changes in
a way an older library would not survive - a type or member renamed or removed,
a signature changed, a constant's value changed, a behaviour redefined. Adding a
brand-new type or member that nothing older calls does not strictly need a bump,
but bumping costs nothing and guessing does.

THE FENCE'S FENCE - the surface hash
------------------------------------
A forgotten bump would be silent, so CoreSurfaceTests holds a recorded
(Revision, hash) pair and CoreSurface writes the whole non-private surface out in
a canonical form - every type and member of the CodeBrix.Ollama.Core namespace,
sorted with the ordinal comparer, formatted with the invariant culture - and
hashes it with SHA-256. Nothing that legitimately differs between builds goes
into that form, so the same source gives the same hash on any machine. Compiler-
written types, nested private types and private members are left out, because a
library cannot reach them.

When the test fails it prints the whole surface. Read the difference, decide
whether it is the kind of change that needs a bump, then update BOTH constants
in CoreSurfaceTests - and Revision in CoreContract if it needs it. Never update
the hash without reading what changed.

THE TWO-PACKAGE RELEASE CHECK
-----------------------------
Run this before publishing, and after any change to either pack recipe. It takes
a couple of minutes and it is the only thing that proves what a consumer gets.

  1. Pack both libraries into a scratch feed, and point NUGET_PACKAGES at a
     scratch folder too, so the machine's real package cache is left alone:

         export NUGET_PACKAGES=/tmp/twopkg/packages
         dotnet pack src/CodeBrix.Ollama.ModelManager/\
     CodeBrix.Ollama.ModelManager.csproj -c Release -o /tmp/twopkg/feed
         dotnet pack src/CodeBrix.Ollama.ModelRunner/\
     CodeBrix.Ollama.ModelRunner.csproj -c Release -o /tmp/twopkg/feed

  2. Unzip each one and confirm it holds ITS OWN DLL AND CodeBrix.Ollama.Core.dll
     under lib/net10.0/, and that the dependency groups are the two shown under
     PACKAGING AND PUBLISHING below - unchanged.

  3. Build a console application that references BOTH packages from that feed
     and nothing else (a nuget.config with <clear /> and the local folder;
     nuget.org is allowed only so ModelManager's one dependency can restore).
     Expect 0 warnings / 0 errors, and count the shared assembly in the output:

         find bin -name CodeBrix.Ollama.Core.dll | wc -l     -> 1

  4. Call one guarded entry point in each library from that application, and
     one on each of ModelRunner's two ROADS. A matched pair gets each library's
     OWN answer - ModelManager's "model not found" from an empty store,
     ModelRunner's details from a tiny GGUF file - which is the proof that the
     guard let the call through. THE THIRD CALL IS AN ONNX LOAD AND RUN:
     OnnxModel.LoadAsync on a tiny graph, then Run on it, with the answers
     checked. It matters because that road ships no native library of its own
     and is the one that would fail silently if the managed interpreter did not
     reach the consumer intact - the raw fixture
     tests/CodeBrix.Ollama.ModelRunner.Tests/Onnx/Fixtures/add_same_float/
     model.onnx is 143 bytes and does the job.

  5. Prove the guard itself. Copy src/ to a scratch folder, bump Revision in
     THAT copy (never in the repository), pack ModelRunner from it at a higher
     version, put that nupkg in a feed beside the REPOSITORY's ModelManager, and
     build the same application against the mismatched pair. It still builds
     0 warnings / 0 errors - that is the hazard - and ModelManager's call now
     stops with:

         CodeBrix.Ollama.ModelManager was built against CodeBrix.Ollama.Core
         contract revision 1, but contract revision 2 was loaded. ... Install
         the SAME version of every CodeBrix.Ollama package.

     Both outcomes were recorded on 2026-09-18 and both are what is expected.


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

Version stamping is per csproj ON PURPOSE and there is no Directory.Build.props:
all three projects carry the same block, the packages are packed in one minute
and therefore share one version. Do not centralise it and do not "improve" it.

WHAT ELSE SHIPS IN lib/: CodeBrix.Ollama.Core.dll, inside BOTH packages. It is a
project, not a package, and neither dependency group mentions it - see THE Core
PROJECT above for the recipe, the guard and the release check.

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

must list exactly those eight files, and all eight are in ModelManager: the
quantizer stayed there when the codec moved into CodeBrix.Ollama.Core. THE CODEC
IS NOT A PORT: the protocol buffer reader and writer and the ONNX message
classes, now in src/CodeBrix.Ollama.Core/Onnx/, were written against the
published onnx.proto schema and carry no marker, which is correct and is not an
oversight.

ModelManager ALSO PORTS THE INFERENCE ENGINE'S OWN CHECKPOINT CONVERTER, which
is a THIRD upstream in the same library and the newest one (2026-09-18). It is
llama.cpp again - the same MIT licence and the same commit the vendored
snapshot is at, 815a2a5915f22ce6a760c676389c5dfe8535c08f, which `git describe`
calls b10221 - but it is the PYTHON half of that project, which is not in the
vendored subset and was read in the same separate clone: convert_hf_to_gguf.py
and the conversion/ package under it (base.py, llama.py), with gguf-py/gguf/
{gguf_writer, tensor_mapping, metadata, vocab, quants, utility, constants}.py.
Twenty files carry the marker, and

    grep -rl "was previously: .*@b10221" src/

must list exactly those twenty: in ModelManager, Gguf/GgufWriter.cs, the ported
files under Convert/ and its three sub-folders, and Checkpoints/
{CheckpointWeights, CheckpointShardIndex}.cs; and in CodeBrix.Ollama.Core,
Tokenizers/Gpt2MergeTable.cs, which is the merge-table reading of vocab.py,
lifted out of SpecialVocabulary because a tokenizer on the ModelRunner side has
to read the same file the same way. They carry NO verbatim header, because those
upstream Python files carry none - the family rule is to preserve a header where
there is one and never to invent one.

THEIR MARKER IS SPELLED DIFFERENTLY from the other two upstreams in this
library, and it is worth knowing before writing a grep: it names the upstream
PATH and the tag and NOT the project,

    namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/llama.py@b10221;

where the Ollama port writes `ollama/ollama <path>` and the ONNX Runtime port
writes `onnxruntime/<path>@v1.30.0`. The plan that commissioned the work
prescribed that shape and three phases were gated on it, so it was left as it
is rather than rewritten across nineteen files during a documentation pass; the
tag is unambiguous on its own. Unifying it is a decision for whoever next
touches those files, not a defect. Entry 1 of THIRD-PARTY-NOTICES.txt is the
full record, file by file, and states what the port does NOT reproduce.

THE PORTED FILES ARE NOT THE WHOLE OF Convert/ AND Checkpoints/: the container
readers (safetensors, the zip pickle and the restricted unpickler), the model
card reader, the public options and result types and the store-side GgufConvert
carry no marker, and that is correct. They read published file formats, or they
are this repository's own API, where the Python leans on libraries it imports.

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

ModelRunner ALSO PORTS THE GPT-2 BYTE-LEVEL TOKENIZER, and that is its newest
upstream (2026-09-18). It is Hugging Face's transformers, Apache-2.0, and the
file is src/transformers/models/gpt2/tokenization_gpt2.py at v4.57.6 - read in
the maintainer's virtual environment, where that version is installed. Three
files carry the marker,

    grep -rl "was previously: src/transformers/" src/

and all three are under ModelRunner/Tokenizers/: Gpt2ByteTable.cs,
Gpt2PreTokenizer.cs and Gpt2ByteLevelTokenizer.cs. The other two files in that
folder and the reader beside them carry none, and that is correct. THE UPSTREAM
IS transformers AND NOT ANY MODEL'S OWN FILE: every bundle of this family ships
a COPY of that one Python file as remote code, renamed and with different
default token names, and one copy was diffed against the installed library to
confirm the algorithm is identical - so recording transformers is both the true
source and the one that does not tie this library to a model. The ported files
do NOT carry the upstream header verbatim, because they take named functions out
of one file into three of their own rather than taking a file whole; entry 17 of
THIRD-PARTY-NOTICES.txt says so and lists the four modifications.

THIRD-PARTY-NOTICES.txt at the repository root holds the full attribution: the
upstream-file-to-our-file scope list from which the marker list above was
compiled, the modifications made during the port, and Ollama's MIT licence
verbatim. It also covers llama.cpp and the licences that appear in the vendored
snapshot, and everything ModelRunner ported. As of 2026-09-18 it carries
SEVENTEEN numbered entries - 16 is the SkyTNT MIDI model and 17 is Hugging
Face's transformers; the first fifteen are, as of 2026-09-16: 1 llama.cpp and ggml, 2 Intel's SYCL and OpenVINO
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
ModelRunner takes code from five upstreams and reads three more without taking
anything. 151 of its 360 files carry the marker; the marker names the upstream
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

  The SkyTNT MIDI model (Apache-2.0, SkyTNT/midi-model, commit f504d5cb58f769
  ab0f2909c679238f6621034573), the newest upstream here and the only one that
  is Python. Drivers/SkyTnt/ is thirteen files: eight from midi_tokenizer.py -
  MIDITokenizerV2's vocabulary, its two token-row conversions, and the whole of
  the reading-in and writing-out of a piece of music - and five from
  app_onnx.py: the two-graph generation loop, the sampling masks it narrows the
  model's answer with at every token, its temperature/nucleus/top-k sampling,
  the prompt its web page builds, and the two key-value caches. The markers
  name the upstream FILE rather than a project path, because that repository is
  flat and the file is the only address there is: `midi_tokenizer.py@<commit>`
  and `app_onnx.py@<commit>`.

  WHAT IS NOT A PORT, in the same library and easily mistaken for one: Midi/ is
  a Standard MIDI File writer and reader written against the published format,
  and it carries NO marker on purpose. The publisher's repository vendors a
  Python MIDI library that states no licence at all, so nothing was taken from
  it; THIRD-PARTY-NOTICES entry 16 records that decision in full, including the
  one behavioural fact that WAS taken from reading it and where that fact is
  written down in our own words.

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
  - ONE ACTIVE GENERATION PER INSTANCE, for native GGUF and managed ONNX.
    Native ExclusiveGenerationAsync takes the context gate with WaitAsync(0)
    at enumeration start, before chat preparation or tokenization. A busy
    model throws InferenceException, on the same or another call chain. The
    gate stays held across every yield, including the final one, and is freed
    on completion, disposal, cancellation or failure. An unused enumerable
    does not reserve the model. Already-cancelled entrants get cancellation
    without disturbing the active generation, on both runners.
  - Native cache, embedding and adapter operations still wait for the gate on
    another call chain. From inside the same model's await foreach they throw
    InvalidOperationException: the AsyncLocal EngineRequestScope marker
    prevents waiting on their own enumeration. Tokenize, detokenize and
    render do not take this gate.
  - SEEDS: null is the default and the explicit request for a fresh seed.
    SamplingOptions rejects uint.MaxValue (including unchecked -1). MIDI
    rejects negative long seeds and 4294967295; its other nonnegative 64-bit
    seeds remain valid. Validation happens on assignment, before generation.
    Keep the native sentinel INTERNAL to EngineSamplerChain's null mapping.
    A fixed seed selects a repeatable random stream, not identical inference
    across hardware or runners. Native GGUF comparisons must clear the prefix
    cache before each run: prompt batching can change rounding and output.


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

  - ModelManager WRITES OUTSIDE THE STORE IN EXACTLY FOUR PLACES, and they are
    the export, the reduction, the GGUF conversion and the GGUF quantization:
    each makes a folder
    under the SYSTEM TEMPORARY DIRECTORY, lets the work write its output there,
    collects that into the store and removes the whole
    folder in a finally - on success and on failure alike. The first two lay the
    source bundle out in it for external TOOLS to read; the conversion lays it
    out for its own readers, and writes the GGUF file there before the existing
    create path takes it in; the QUANTIZATION lays nothing out at all - the
    stored blob is read where it lies and only the output is written - which is
    why it is the one of the four that costs no extra disk for its input.
    Nothing else in either library creates a temporary
    file of its own - a download's sidecars live beside the blob they belong
    to - and nothing ever writes to the current directory. A machine whose
    temporary directory is RAM-backed needs TMPDIR pointed at a real file
    system before exporting, reducing, converting or quantizing anything of
    size; that is the caller's decision and the AGENT-README says so.

    ModelRunner writes outside nothing at all until QuantizeAsync is called,
    and that one writes only in the OUTPUT's own directory: the engine fills a
    "<output>.quantizing-<8 hex>" file there and it is moved on to the output
    path when the call has succeeded, so the move is a rename within one
    directory whatever the model's size. TMPDIR is not involved.

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
  * CONVERTING A CHECKPOINT TO GGUF, 2026-09-18, on the same Debian 13 laptop.
      - OFFLINE, ON EVERY MACHINE: twelve synthetic checkpoints and the
        fourteen GGUF files the inference engine's OWN converter produced from
        them are checked in, and the managed converter equals every one of them
        over the WHOLE FILE, byte for byte - not key by key and not tensor by
        tensor. The variants cover bfloat16, float16 and float32 weights, both
        containers, sharded and unsharded, grouped-query attention, tied
        embeddings, attention bias, a padded vocabulary, a checkpoint that
        declares no special token, and both tokenizer roads. The GGUF writer
        also round-trips every GGUF file in the repository byte for byte, the
        native conformance model included.
      - THE REAL MODEL, THROUGH THE STORE. hf.co/m-a-p/MuPT-v1-8192-190M, a
        380,166,726-byte PyTorch zip pickle, converted with ConvertToGgufAsync
        to 381,878,656 bytes and 111 tensors in about a second, and compared
        with what the engine's own converter wrote from the same folder for the
        same model identifier: 0 of 381,878,656 bytes differ. The engine's
        Python takes 2.8 to 3.4 s over the same file.
      - THE RUNNER LOADS IT AND GENERATES. Resolved, probed, loaded through
        ModelRunner and generating real multi-voice ABC, and its greedy
        identifiers are the checkpoint's own framework's: 32 of 32 on each of
        three prompts, at BF16 against torch-bfloat16 and at F16 against
        torch-float32, with and without the supplied special tokens, with
        tokenization agreeing on all six probe strings including multi-byte
        UTF-8 and runs of whitespace. Three further prompts are run and
        recorded rather than asserted - see WHAT THE TOKEN COMPARISON CAN AND
        CANNOT PROMISE above.
      - IT STREAMS. The 1.97B sibling, 3,931,517,782 bytes in one zip pickle,
        converted in 10.1 s at a peak resident set of 195.7 MiB - 5.2% of the
        checkpoint - and the converted file loaded and generated.
      - NO CPython IS INVOLVED AT ANY POINT. The conversion names no
        CodeBrix.Python type, starts no interpreter and imports no module; the
        offline conversion tests run on a machine with no Python at all.
  * THE RELEASE CHECK AT THE END OF THE ONNX WORK, 2026-09-17, Debian 13
    laptop, .NET SDK 10.0.401, run in one sitting: `dotnet build
    CodeBrix.Ollama.slnx -c Release --no-incremental` 0 warnings / 0 errors;
    ModelManager.Tests 1550 / 0 / 13; ModelRunner.Tests 1258 / 0 / 30;
    ModelManager.Python.Tests 43 / 0 / 43 with the gates shut and 43 / 0 / 0
    with both gates open; every probe mode exiting on its own; both nupkg files
    unzipped - the five packed root files, lib/net10.0 with its assembly and
    XML documentation, ModelRunner's dependency group empty and ModelManager's
    carrying CodeBrix.Python.MitLicenseForever and nothing else.
  * THE RELEASE CHECK AT THE END OF THE GGUF WORK, 2026-09-18, Debian 13
    laptop, .NET SDK 10.0.4xx, run in one sitting: `dotnet build
    CodeBrix.Ollama.slnx -c Release --no-incremental` 0 warnings / 0 errors;
    ModelManager.Tests 1815 / 0 / 13; ModelRunner.Tests 1258 / 0 / 30 - the
    fence that says the GGUF work never touched it; ModelManager.Python.Tests
    43 / 0 / 43 with the gates shut; EndToEnd.Tests 5 / 0 / 5 with the gates
    shut. Both nupkg files unzipped: ModelManager is the five packed root files
    plus lib/net10.0 with its assembly and XML documentation, and its
    dependency group carries CodeBrix.Python.MitLicenseForever and nothing
    else; ModelRunner's dependency group is still empty and it still lists all
    seven runtimes/<rid>/native/ pairs with their per-native licence files.
  * QUANTIZING A GGUF MODEL, 2026-09-18, on the same Debian 13 laptop. The five
    types the plan named (Q8_0, Q4_0, Q4_K_M, Q5_K_M, Q6_K) written by
    ModelRunner.QuantizeAsync with no options are BYTE FOR BYTE what the
    engine's own command-line quantizer writes for the same file - on the
    repository's own conformance model and on the real MuPT 190M GGUF from
    both a BF16 and an F16 source, fifteen comparisons and zero differing
    bytes. The quantized MuPT loads through the runner and generates ABC at
    305 (Q8_0) and 425 (Q4_K_M) tokens a second against 171-198 unquantized.
    QUANTIZING (maintainer) has the whole table, the cmake options the oracle
    tool was built with and the greedy-agreement numbers.
  * THE RELEASE CHECK AT THE END OF THE QUANTIZATION WORK, 2026-09-18, Debian
    13 laptop, run in one sitting: `dotnet build CodeBrix.Ollama.slnx -c
    Release --no-incremental` 0 warnings / 0 errors; ModelManager.Tests
    1839 / 0 / 13; ModelRunner.Tests 1286 / 0 / 30 - the first time that number
    has moved since 2026-09-16; ModelManager.Python.Tests 43 / 0 / 43 with the
    gates shut; EndToEnd.Tests 7 / 0 / 7 with the gates shut, and 6 / 0 / 1
    with the live and quantize-tool gates open. Both nupkg files unzipped:
    ModelManager's dependency group carries CodeBrix.Python.MitLicenseForever
    and nothing else, and ModelRunner's is STILL EMPTY - which is the check
    that says a public quantization API cost the package nothing.
  * THE CONTRIBUTED AND QUANTIZED OPERATORS, 2026-09-18, Debian 13 laptop. Six
    operators added to the managed engine - MatMulNBits, GroupQueryAttention,
    SkipSimplifiedLayerNormalization, SimplifiedLayerNormalization,
    DynamicQuantizeLinear and MatMulInteger - and ELEVEN REAL SUBJECTS run
    against onnxruntime 1.30.0, every one of them produced by this library's own
    store: the two published SkyTNT graphs reduced three ways, and the MuPT 190M
    checkpoint as the GenAI builder exports it at fp32 and int4 plus the store's
    three reductions of that export. THE CONTRIBUTED AND QUANTIZED OPERATORS
    above has the table. Every weight-only subject agrees to about a millionth
    with no greedy choice differing; the subjects that quantize their
    ACTIVATIONS agree to a few percent, which is what the graph allows - this
    engine's own two arithmetic paths disagree with each other by as much. The
    822 MB graph's four-bit reduction holds 117 MiB of managed objects against
    the original's 782 MiB, a factor of 6.7, which is the fence that says a
    packed weight is never expanded at load.
  * THE MIDI GENERATION DRIVER AGAINST THE PUBLISHER'S OWN PYTHON, 2026-09-18,
    same Debian 13 laptop, with the live gate, the Python gate and
    CODEBRIX_OLLAMA_SKYTNT_CLONE all open. What is proved is IDENTITY, not
    closeness: asked for the most likely answer every time, the managed driver
    writes the music the publisher's own generation loop writes.
      - THREE COMPARISONS, 128 EVENTS EACH, EVERY EVENT THE SAME - the same
        kinds, in the same order, at the same positions, with the same pitches,
        loudnesses, lengths, instruments and signatures. From nothing at all;
        from a described piece (three instruments, a drum kit, 100 beats a
        minute, 3/4, three flats minor); and CONTINUING a piece of music read
        from a file, which is the tokenizer's other direction - a prompt this
        library wrote, read by both sides from the same bytes, and continued
        the same way for 128 events.
      - Each of the three files this library wrote was then READ BACK BY THE
        PUBLISHER'S OWN MIDI.py out of the same checkout: a second opinion on
        the file format from something that had no part in writing it.
      - THE THREE REDUCTIONS the store makes all write valid music and write
        the same music twice. Where each parts company with the full-precision
        run is a fact about quantization and is RECORDED rather than asserted:
        four-bit weights at event 2, eight-bit weights NOWHERE - the whole
        128-event piece is the same, which is a stronger result than the plan
        expected and says the store's eight-bit reduction changes no greedy
        choice at all over a whole piece on this model - and the dynamic
        eight-bit rewrite at event 18. That last is the D6a class, whose arithmetic is chaotic by
        construction.
      - SPEED, 64 events greedy, over two runs hours apart: full precision
        14.8 to 15.6 events/s at one thread and 31.4 to 32.9 at eight; the
        four-bit reduction 19.9 to 20.1 and 53.9 to 55.6. The publisher's
        Python through onnxruntime writes 28.4 to 32.2 events/s at eight
        threads on the same machine, so the managed engine is within a few per
        cent of it on this model.
      - SECONDS OF MUSIC PER SECOND OF WAITING, which is the number a consumer
        streaming the result needs, measured on 384 events at eight threads
        with the DEFAULT sampling settings and a fixed seed: full precision
        6.2 to 6.5 (one instrument), 3.5 to 3.8 (three) and 2.0 to 2.1 (five
        and a drum kit); the four-bit reduction 1.5 to 1.6, 3.0 to 3.3 and 1.3
        to 1.5. Music comes out faster than it is played on every prompt tried,
        by between 1.3 and 6.5 times. Greedy
        settings are NOT the ones to measure this on: taking the most likely
        answer every time is what makes the comparison above possible and is
        also what makes a model of this family repeat itself, and a greedy
        piece can spend hundreds of events setting itself up and never start.
      - Peak resident memory in the test host, which is a ceiling and not the
        model's footprint: 3.7 to 5.0 GiB with the full-precision pair loaded,
        2.3 to 2.9 GiB with the four-bit one.
  * THE TEXT GENERATION DRIVER AGAINST THE BUNDLE'S OWN RUNTIME, 2026-09-18,
    Debian 13 laptop, on the 190M ABC-notation model the store exported and
    reduced. Every comparison is against the publisher's own tokenizer
    (transformers, with the bundle's own tokenization module) for the token
    numbers and onnxruntime-genai 0.15.2 - the runtime the bundle's generation
    configuration was written FOR - for the generation, at eight threads, three
    prompts of 28, 42 and 48 tokens continued by 32 tokens each, greedy.
      - TOKENIZING: 118 of 118 prompt tokens identical, on every one of the
        five subjects. The managed byte-level encoder is the publisher's Python,
        exactly.
      - FULL PRECISION: 96 of 96 generated tokens IDENTICAL, prompt for prompt,
        and 0 of 214 positions differ under teacher forcing. That is the
        phase's done criterion and it is asserted.
      - THE STORE'S FOUR-BIT AND EIGHT-BIT REDUCTIONS, which quantize weights
        only: 0 of 214 positions differ under teacher forcing - asserted - and
        both also reproduced the full-precision engine's own 96 tokens without
        being asked to.
      - THE BUILDER'S FOUR-BIT EXPORT: 2 of 214 positions differ under teacher
        forcing, RECORDED rather than asserted. It carries accuracy_level on
        every quantized matrix multiply, which asks a runtime to quantize the
        activations too; onnxruntime does and this engine keeps floats, which
        is the D6a class. Its free-running generation still agreed with the
        oracle on all 96 tokens.
      - THE STORE'S DYNAMIC EIGHT-BIT REWRITE: 6 of 214 positions differ under
        teacher forcing, RECORDED. Free-running, one prompt of the three was
        identical for all 32 tokens and the other two parted company at token 2
        and token 17. That is the D6a class doing what D6a says it does.
      - SPEED, 64 tokens greedy from a 48-token prompt, one load each:
        full precision 40.1 tokens/s at one thread and 75.5 at eight; the
        builder's four-bit export 43.4 and 77.2; the store's four-bit reduction
        36.5 and 77.1. TIME TO THE FIRST TOKEN is the prompt's own evaluation
        and nothing else: 654 ms at one thread and 190 ms at eight for full
        precision, and about 1,240 ms and 362 ms for both four-bit forms - the
        block-quantized matrix multiply is SLOWER than the full-precision one on
        a wide prefill, which is a tuning item and not a defect. Loading takes
        390 ms full precision and 70 to 114 ms four-bit. Peak resident memory in
        the test host, which is a ceiling and not the model's footprint: 2.0 to
        2.2 GiB full precision, 1.5 to 2.3 GiB four-bit.
      - THE OTHER ROUTE, for comparison and measured the same day: the SAME
        model through the GGUF path and the native engine runs at about 190
        tokens/s on 16 threads. The managed ONNX route is therefore about two
        and a half times slower on this model, and it is the route that needs
        nothing installed and runs wherever .NET runs.
  * THE RELEASE CHECK AT THE END OF THE CONTRIBUTED-OPERATOR WORK, 2026-09-18,
    Debian 13 laptop, run in one sitting: `dotnet build CodeBrix.Ollama.slnx -c
    Release --no-incremental` 0 warnings / 0 errors; Core.Tests 27 / 0 / 0;
    ModelManager.Tests 1823 / 0 / 13 - unmoved, which is the fence that says
    this work never touched the store; ModelRunner.Tests 2349 / 0 / 30;
    ModelManager.Python.Tests 43 / 0 / 43 with the gates shut; EndToEnd.Tests
    19 / 0 / 19 with the gates shut and 19 / 0 / 1 with them open, the skip
    being the large streaming proof. ModelRunner's nuspec dependency group is
    STILL EMPTY and both nupkg files still carry CodeBrix.Ollama.Core.dll.
  * THE RELEASE CHECK AT THE END OF THE TEXT-DRIVER WORK, 2026-09-18, Debian 13
    laptop, run in one sitting: `dotnet build CodeBrix.Ollama.slnx -c Release
    --no-incremental` 0 warnings / 0 errors; Core.Tests 27 / 0 / 0 - UNMOVED,
    and no file under src/CodeBrix.Ollama.Core or its test project was touched,
    so CoreContract.Revision is still 1 and the recorded surface hash still
    matches; ModelManager.Tests 1823 / 0 / 13 - unmoved, which is the fence that
    says this work never touched the store; ModelRunner.Tests 2984 / 0 / 30 -
    366 new cases, EVERY ONE of them offline, which is why the skipped count did
    not move; ModelManager.Python.Tests 43 / 0 / 43 with the gates shut;
    EndToEnd.Tests 30 / 0 / 30 with the gates shut and 30 / 0 / 1 with them open
    in 258 s, the skip being the large streaming proof. ModelRunner's nuspec
    dependency group is STILL EMPTY, ModelRunner still has ZERO
    PackageReferences, both nupkg files still carry CodeBrix.Ollama.Core.dll,
    and reflection over both libraries' 144 exported types finds 0 mentions of a
    CodeBrix.Ollama.Core type and 0 of the other library's.
  * THE PERFORMANCE PASS OVER THE MANAGED ONNX ENGINE, 2026-09-18, Debian 13
    laptop, machine otherwise idle. What was PROVED, rather than measured, is
    that nothing moved: every numeric oracle reproduced its recorded figure to
    the last digit - the four SkyTNT one-step differences (9.194E-07, 7.607E-07,
    6.697E-07, 5.577E-07), all eleven quantized subjects, the 384 of 384 events
    of greedy identity with the publisher's own Python, and the 118 prompt
    tokens and 96 generated tokens of identity with the bundle's own runtime,
    teacher forcing included. No tolerance, fixture or expected output was
    touched; the 1,678 checked-in fixture files are byte-identical. The changes
    are memory accounting at load time, a thread default that follows the
    PERFORMANCE cores on a hybrid processor, and three kernel changes that
    reorder the WORK without reordering any answer's arithmetic - a four-bit
    column unpacked once for a whole prompt instead of once per position, a
    result walked by column and in cache-sized blocks of rows, and two weight
    rows worked out at a time when there is only one row of input. The numbers
    are in PERFORMANCE BUDGETS above; the shortest statement of them is that the
    822 MB graph's load peak fell from 3,156 MiB to 1,788 and the resident set
    it leaves behind from 3,111 to 898, time to the first token fell by between
    a sixth and two-fifths on every subject, and the slowest of the eleven
    subjects' prompt steps went from 127.0 / 83.4 ms to 71.8 / 20.9.
    Build 0 warnings / 0 errors; Core.Tests 27 / 0 / 0 UNMOVED with
    CoreContract.Revision still 1; ModelManager.Tests 1823 / 0 / 13 unmoved;
    ModelRunner.Tests 3028 / 0 / 30, 44 new cases every one offline;
    ModelManager.Python.Tests 43 / 0 / 0 live in 110.6 s; EndToEnd.Tests
    30 / 0 / 1 with the gates open. ModelRunner still has ZERO
    PackageReferences, its nuspec dependency group is still empty, both nupkg
    files still carry CodeBrix.Ollama.Core.dll, and reflection over both
    libraries' 144 exported types - unchanged - finds 0 mentions of a
    CodeBrix.Ollama.Core type and 0 of the other library's.
  * The osx-x64 native: built, full gate passed (architecture, 248/248 required
    exports, exact export surface, install name, system-only dependencies,
    minos 13.3, signature, smoke test, byte-identical model regeneration, and
    conformance at max |diff| 4.98e-08), adopted, twin and dSYM stored. The
    osx-arm64 native was built and gated the same way on the Apple Silicon
    Mac mini, including the Metal conformance pass; its record is in
    BUILD-PROVENANCE.txt.

WHAT HAS NOT BEEN VALIDATED
---------------------------
  * QUANTIZING ON WINDOWS OR macOS, or on any native but linux-x64. The
    byte-for-byte comparison against the engine's own tool was made on the
    Debian laptop, with a tool built by the host's own gcc 14.2.0 against a
    shipped native built by a container's gcc 14.2.1. Whether a quantization
    made by the win-arm64 or osx-arm64 native is byte-identical to one made by
    this one is UNKNOWN and, given that the reference quantizers are
    floating-point C, is not something to assume: the files are expected to be
    equivalent, not necessarily equal.
  * QUANTIZING ANY TYPE BUT THE FIVE COMPARED. Q8_0, Q4_0, Q4_K_M, Q5_K_M and
    Q6_K are checked against the engine's own tool; the other thirty members of
    GgufQuantizationType are passed through the same one native call with the
    same parameter struct and are fenced only by the 1:1 enumeration test. The
    IQ family in particular has never been run here, and without an importance
    matrix - which this API does not take - the engine writes some of its
    tensors at more bits than the name suggests.
  * QUANTIZING A MODEL WITH A PROJECTOR OR AN ADAPTER. The store carries those
    layers over to the quantized model unchanged, and nothing has been stored
    that way.
  * THE GGUF CONVERSION ON WINDOWS OR macOS. It is pure managed code and the
    offline oracle comparison would catch a difference anywhere, but it has run
    on Linux only.
  * THE GGUF CONVERSION OF A REAL SENTENCEPIECE OR SHARDED CHECKPOINT. Both
    roads are proven against the engine's own output on SYNTHETIC checkpoints,
    byte for byte, and the real model that has been converted end to end is a
    single-file GPT-2 byte-level one. The first real llama-family checkpoint
    with a tokenizer.model, and the first published one split over shards, are
    still to come.
  * A CHECKPOINT OF EXACTLY 32,016 OR 49,152 TOKENS. Those two sizes take rules
    of the engine's that this port deliberately does not reproduce; such a
    checkpoint converts to a correct, loadable file that is NOT byte-identical
    to the engine's. See CONVERTING TO GGUF (maintainer) - THE DESIGN.
  * A CHECKPOINT WHOSE MODEL CARD NAMES base_model OR datasets. Those keys are
    not read, so the general.base_model.* and general.dataset.* keys are not
    written and such a checkpoint would differ from the engine's output by
    exactly them. Neither test subject has one.
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

  * A 16-BIT FLOAT WEIGHT IN A REAL MODEL, through the managed ONNX engine.
    Such a weight is widened as the graph is read and the behaviour is covered
    by the cast_float16_weight fixture alone; none of the eleven real subjects
    carries one. A 16-bit float at a graph's own EDGE is refused by name, and
    that refusal is fixtured.

  * MatMulNBits AT TWO BITS, which ReduceOnnxAsync can write and the managed
    engine refuses by the bit width. Four and eight are implemented and
    fixtured at every block size the quantizer writes.

  * THE INTEGER PATH OF ReduceSum, which the five gated MuPT subjects prove and
    nothing offline does. It is three lines in generate_fixtures.py to add an
    int64 case and it has not been added.

  * THE MANAGED ONNX ENGINE'S THREAD DEFAULT ON ANY PROCESSOR BUT THIS LAPTOP'S.
    EnginePerformanceCores reads the operating system's own answer on Linux
    (Intel-style hybrid and ARM big.LITTLE), macOS and Windows, and every
    format it parses is unit-tested from strings - but only the Linux
    Intel-hybrid branch has ever answered on real hardware. The macOS and
    Windows branches have never been executed. Everywhere the detection answers
    nought the fallback is the physical core count.

  * THE THREAD CAP ON A MACHINE THAT IS NOT THIS ONE. ModelRunnerOptions
    .MaxThreads and OnnxRunnerOptions.MaxThreads are unit-tested against a
    STATED detected count, so the rule is proven on a four-core machine and a
    sixty-four-core one without owning either; what has actually RUN is this
    laptop, where an uncapped default is sixteen threads on the native road and
    eight on the managed one.

  * THE ONNX ROAD ON A 16 GiB MACHINE. The memory budgets in PERFORMANCE
    BUDGETS were measured on a 62 GiB laptop and reasoned down; no run has been
    made on a machine where the load peak actually matters.

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


MUSECOCO VALIDATION (2026-09-22)
================================
MUSECOCO-README.txt documents consumer APIs and the versioned bundle. The new
exporter is Python staging code inside ModelManager; the runtime drivers,
WordPiece, REMIGEN2 decoder and new ONNX kernels are managed C# in ModelRunner.
No Core source/ABI changes, new package dependencies, cross-library references,
or new native runtime are required. Both NuGets include the MuseCoco guide.

MUSECOCO MIDI STREAMING
  GenerateStreamingAsync shares GenerateTokensAsync with the completed-score
  API; it does not change the exported graph or sampling. Remigen2Reader holds
  the shared token interpretation. Remigen2StreamDecoder buffers the unfinished
  bar and releases events strictly before the next bar's start, sorted by tick.
  Positions beyond that boundary remain pending. Final-position cleanup is shared
  with Remigen2Decoder, so incomplete chords are not emitted prematurely.
  Tracks/channels are assigned on first playable use; program changes occur at
  first-note ticks. Completed decoding retains its sorted-instrument mapping.
  Streaming adds initial 120 BPM / 4/4 when no explicit tick-zero metadata exists.
  A stream owns the model until enumeration finishes or is disposed, including
  while final buffered events are yielded. Cancellation preserves delivered
  events and drops the rest. The stream does not accumulate all generated tokens.

REPRODUCIBLE SOURCES
  Muzic: 2b8739671ba06f819f31f568b8a79da581aaf6f9.
  Music checkpoint: XinXuNLPer/MuseCoco_attribute2music, revision
    8d9df584e3e09da88601546e7980323221f12644, attribute2music.pt SHA256
    b3d99658eee895f8773a86d1f41e32e9e11df7e0c4b0e899e175e31b9f62955f.
  Custom BERT: XinXuNLPer/MuseCoco_text2attribute, pytorch_model.bin SHA256
    85dd23b1a3b6ea8927b77cdb4a011d8396c4f2f23d8808938369dbe58e1ea71b.
  Reference fairseq: 83e615d66905b8ca7483122a37da1a85f13f4b8e (0.10.2).
  Reference fast-transformers: 2ad36b97e64cb93862937bd21fcc9568d989561f.
  Validation environment: Python 3.13, torch 2.14.0+cpu, transformers 4.57.6,
    onnx 1.22.0, onnxruntime 1.30.0, numpy 2.5.3; .NET 10.0.12.
  Original source classes were used only in research oracles. Production staging
    imports neither fairseq nor the publisher's custom Python modules.

FP32 FIDELITY GATE
The spike's original-vs-export discrepancy was isolated to negative ELU
rounding: PyTorch uses expm1-like evaluation, while ONNX Runtime computes a float
exp followed by subtracting one. Recurrent linear attention amplifies very small
changes near zero. Replacing ONLY that component in the PyTorch reference cut a
saved-step max logit difference from 0.022852 to 0.0000143. The export preserves
the original architecture, including 24 x 85 (2040) attention projections inside
a 2048-wide music model and the leading-EOS positional convention.

The acceptance gate was fixed before quantization: maximum next-token total
variation <=0.02 and argmax agreement >=99% on 34 sampled-history checks from
the original publisher's full-sequence model. Result: maximum TV 0.006819,
maximum KL 0.0002502, all 34 argmax choices equal. A sampled note can still
change at a close probability boundary; bitwise publisher output is not promised.
The final standard LayerNormalization export was checked against the accepted
spike over all 256 history steps: maximum TV 0.00004174, 256/256 argmax agreement.
Saved step-0/23 logit errors vs the earlier graph were below 0.000008, with
relative recurrent-state error below 0.000022.

BERT's final export was checked against the original custom publisher class on
10 prompts (600 classifier decisions), including empty text, Chinese, accented
text and special tokens. All decisions agreed; maximum logit error was below
0.00002. WordPiece IDs matched, with 15 additional checked-in tokenizer cases
covering Unicode, punctuation, special tokens, long words and truncation.

QUANTIZATION AND OUTPUT QUALITY
Both models were exported and independently reduced using the public Manager
APIs. Block size 128, asymmetric weights, no accuracy_level attribute, no node
exclusions in this baseline. Music has 145 quantized constant matrices; BERT
has 205. Embeddings and other non-MatMul weights remain FP32.

                       FP32 bytes       INT8 bytes       INT4 bytes
  music graph+weights  4,915,899,268    1,336,746,301      727,348,867
  BERT graph+weights   1,341,630,968      443,732,575      290,916,386

Small JSON companions are additional. The music INT4 size is about 0.68 GiB;
INT8 about 1.25 GiB. BERT INT4 is about 277 MiB and INT8 about 423 MiB.
ModelManager exports took about 27s (music) and 5s (BERT). Managed quantization
took about 25s per music model and 5s per BERT model in this environment.

All 600 BERT decisions also agreed with the original FP32 reference at INT8 and
INT4. Managed-vs-native classifier probability differences were below 0.000003.
This is a small prompt set, not a labeled accuracy benchmark or a claim that
quantization never changes an attribute.

For music, feeding the same 256-token sampled history to every precision gave:
                  mean TV vs FP32   max TV vs FP32   argmax agreement
  INT8               0.000795          0.008697          100%
  INT4               0.017795          0.131887           97.65625%
INT4 has measurable changes near some choices. On separate free-running seeded
piano samples, FP32/INT8/INT4 generated 61/63/60 complete notes from 256 tokens.
Every note had valid MIDI pitch, velocity and positive duration; all programs
remained piano and all pitches belonged to the requested major-mode diatonic
pitch set in these samples. Pitch ranges were 40-71, 40-71 and 48-84; distinct
pitches 16, 15 and 10; distinct durations 12, 9 and 5. INT4 changed the musical
trajectory and diversity, which is why file-size reduction is an option.

For EACH precision, managed and native generated exactly the same 256 sampled
tokens with the same xoshiro256** stream, seed 20260921, top-k 15, temperature 1,
top-p 1, EOS suppressed for the measured 256 tokens, and four threads. The
managed MidiScore, and independently reparsed saved MIDI, agreed with the
publisher REMIGEN2 decoder's notes and tempo/time-signature events.
A longer INT8-BERT + INT4-music jazz prompt generated 1024 tokens, 253 notes and
about 24.83 seconds of MIDI, including piano, bass and percussion. Its managed
decode took 49.53s. Its score and saved MIDI also independently matched the
publisher decoder, including notes, programs, tempo and time signatures.
Subjective listening quality has not been certified; these
checks measure fidelity, valid music structure, conditioning and diversity.

CPU PERFORMANCE
Intel Core i7-12850HX, AVX2/FMA, four inference threads, CPU only. Native is the
same graph and precision through ORT 1.30.0 CPUExecutionProvider with
ORT_ENABLE_ALL, intra_op_num_threads=4 and inter_op_num_threads=1. The filesystem
cache was warm; each measured model starts in a new process. Managed loading is
eager; native can defer external-file costs until first inference. Loading,
attribute-prefix processing and generated-token processing are separate below.

  Music: 256 generated tokens (255 subsequent recurrent graph calls)
                load seconds       prefix seconds       decode seconds
                managed/native     managed/native       managed/native
  FP32             4.86 / 2.06        6.96 / 6.06         26.27 / 23.84
  INT8             0.91 / 0.05        3.79 / 40.88        13.01 / 159.92
  INT4             0.56 / 0.52        3.65 / 2.23         11.70 / 8.83

  Peak process working set (MiB), including load and generation:
                managed/native
  FP32            5145 / 4738
  INT8            1491 / 1388
  INT4             907 / 729

Native's weight-only INT8 path was much slower on this CPU/ORT build. That is a
measurement of this operator configuration, not a general claim about native
INT8 inference or quantized activations. Native INT4 remains faster than managed.

  BERT: median of 10 prompts in one process, including the first-call sample
                  prediction seconds   load seconds    peak MiB
                    managed/native     managed/native  managed/native
  FP32                 .259 / .101        1.45 / .70      1553 / 1235
  INT8                 .377 / .175         .35 / .37       904 / 608
  INT4                 .362 / .099         .26 / .41       611 / 462

BERT quantization reduces size and load memory, not prediction latency in these
managed measurements. FP32 is the fastest managed BERT option; INT8 BERT + INT4
music is the initial compact combined configuration, not a speed guarantee.

OPTIMIZATIONS AND THEIR SCOPE
Profiling the initial BERT runner put about 74% of inference time in MatMul;
Erf and decomposed normalization were additional costs. Managed FP32 BERT
improved from roughly .41-.50s per prompt to roughly .24-.28s after warmup:
  - FP32/INT4/INT8 matrix prompts share sixteen-column weight panels and four
    activation rows with AVX2/FMA. Packing and 128-element reduction slices
    bound scratch storage and improve cache reuse. Small-row decode paths
    remain in the existing kernels; no full float copy of a quantized model
    is retained. Scalar/vector paths remain available.
  - Portable Vector<double> intermediates accelerate the FP32 Erf approximation.
  - Standard LayerNormalization removes temporary graph tensors while keeping
    explicit epsilon and FP32-stash semantics.
  - MuseCoco reuses two private recurrent-state banks (about 17 MiB each). This
    reduced INT4 peak memory from 3196 to 907 MiB and decode from 12.62 to 11.70s;
    INT8 peak fell from 5796 to 1491 MiB. All sampled tokens remained identical.
    Internal RunIntoAsync rejects aliased input/output buffers and validates
    result shapes. Public Run/RunAsync outputs remain independently owned.

Portable driver/output-buffer tests also passed with COMPlus_EnableHWIntrinsic=0.
No ARM device was benchmarked; hardware-disabled Intel validates fallback
correctness, not ARM performance. Matrix reassociation can change low bits and
seeded samples across builds even when every kernel is numerically correct.

TESTS AND ARTIFACTS
The offline suites cover tokenizer/reference IDs, all schema heads, direct and
text-derived attributes, MIDI notes/percussion and truncation, seeds, cancellation,
concurrency/disposal, invalid metadata and paths, nested/shared external files,
node exclusions and dependency boundaries. Standard activation/normalization
fixtures come from ORT; matrix tests compare double-precision references and
thread-count stability. The real local-checkpoint test in EXTRAS-README passed:
public FP32 exports, all four reductions, predictions and generation at all
precisions, mixed INT8-BERT/INT4-music and repeated seeded generation.

Final validation totals:
  ModelRunner: 3287 passed, 30 gated skips, zero failures (3317 total).
  ModelManager: 1822 passed, 13 gated skips, zero failures (1835 total).
  Real-checkpoint MuseCocoLiveTests: 1 passed, zero failures (149.894s).
  Real Python node-exclusion test: 1 passed, covering INT4, INT8 and dynamic INT8.
  Hardware-intrinsics-disabled driver/output-buffer checks: 33 passed.
Other live/Python suites remain opt-in; a gated skip is not a validation claim.
The existing SkyTNT 1000-event regression completed in 18.65647s with exact
event agreement against the prior optimized baseline.

Release packages were built locally with validation BuildVersion=1.0.265.0 and
their contents inspected. Runner has no NuGet dependencies; Manager's sole
package dependency is CodeBrix.Python.MitLicenseForever 1.0.260.1181. Both include
the internal Core assembly and MUSECOCO-README.txt; Runner retains its existing
native assets. The current MuseCoco exporter is embedded in Manager. No Core
source changes, library cross-reference, or additional native engine was added.
The packages are in /tmp/codebrix-musecoco-packages and were not published.
Manager pack reported NU1900 because the NuGet vulnerability feed was unavailable;
package creation succeeded, but that run did not establish a vulnerability audit.
Test/pack logs are /tmp/musecoco-*-tests.log, /tmp/musecoco-live-tests.log,
/tmp/musecoco-python-exclusions.log and /tmp/musecoco-*-pack.log.

Research evidence on Jeremy's machine:
  ~/ClaudeHome/musecoco-a2m-spike-2026-09-21/
    elu-rounding-diagnostic.json, fidelity-distribution.json, original references.
  ~/ClaudeHome/musecoco-implementation-2026-09-21/
    bert-export-fidelity.json, music-export-fidelity.json,
    music-quantization-quality.json, midi-quality.json,
    text-{managed,native}-final-{fp32,int8,int4}.json,
    music-{managed,native}-final-{fp32,int8,int4}.json,
    pipeline-int8-int4-1024.json and its .mid file, pipeline-quality.json,
    skytnt-after-musecoco.json, staging/reduction reports,
    native_check.py, verify_quality.py and the public-API validation harnesses.
The native oracle and original-publisher code are research tooling, not runtime
or package dependencies. Managed validation ran with PATH=/nonexistent and
checked loaded modules for Python, torch and ONNX Runtime; none were present.

STAGING LIFETIME FINDING
An exporter process that relied only on automatic process-exit Python shutdown
hung after completing torch work in this environment. Explicit
PythonSupport.Shutdown() completed and allowed normal exit. Examples and the
end-to-end assembly fixture now own that lifetime explicitly. No change to the
CodeBrix.Python package was made; the automatic fallback should not be treated
as a guarantee for arbitrary native Python modules.
