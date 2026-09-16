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

THE STATE OF THINGS, 2026-09-15
-------------------------------
  * ModelManager is written and tested.
  * ModelRunner IS NOT WRITTEN YET. Its project contains a csproj, an
    InternalsVisibleTo.cs and the committed native library for one runtime
    identifier - no library code at all. Its AGENT-README.txt is a placeholder,
    and tests/CodeBrix.Ollama.ModelRunner.Tests builds, references the library
    and links the conformance vectors but holds no test files at all.
  * The native build tooling under llama-native-tools/ is complete, and ONE of
    the seven runtime identifiers has been built, gated and adopted.

Statements below about ModelRunner therefore describe its packaging and its
native payload, not an API that exists.


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

    src/CodeBrix.Ollama.ModelRunner/    the not-yet-written library
      runtimes/<rid>/native/     the COMMITTED native libraries, each beside a
                                 copy of llama.cpp's LICENSE as
                                 LICENSE-LlamaCpp.txt. Today that is
                                 runtimes/osx-x64/native/ and
                                 runtimes/osx-arm64/native/
      InternalsVisibleTo.cs      grants CodeBrix.Ollama.ModelRunner.Tests

    tests/CodeBrix.Ollama.ModelManager.Tests/   the xunit.v3 suite, offline
      Gguf/ Modelfile/ Names/ Registry/ Store/  26 test classes in all
      Infrastructure/            EnvGatedFactAttribute, FakeRegistryHandler,
                                 GgufTestFileBuilder, FakeModelBuilder,
                                 TempStoreDirectory
      xunit.runner.json          copied to output by an explicit csproj item

    tests/CodeBrix.Ollama.ModelRunner.Tests/    csproj and xunit.runner.json
                                 only; links the conformance vectors

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

There is no samples/ folder yet.

THE FLAT-NAMESPACE RULE - DO NOT "FIX" IT
------------------------------------------
Every public and internal type in ModelManager declares `namespace
CodeBrix.Ollama.ModelManager;`, and every file in its test project declares
`namespace CodeBrix.Ollama.ModelManager.Tests;`. ModelRunner's RootNamespace is
CodeBrix.Ollama.ModelRunner and its library will follow the same rule. The
folders above are FILE ORGANIZATION ONLY.

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
ModelRunner additionally sets AllowUnsafeBlocks, load-bearing for the coming
P/Invoke layer, and ModelRunner.Tests matches it.

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
    Microsoft.NET.Test.Sdk 18.10.0, xunit.runner.visualstudio 4.0.0 and
    SilverAssertions.ApacheLicenseForever 1.0.248.1071. 26 test classes,
    332 test members, 789 test cases (the [Theory] members contribute 511
    [InlineData] rows between them).

    tests/CodeBrix.Ollama.ModelRunner.Tests -- the same four packages at the
    same versions, no test files yet, reports zero tests.

THIS IS AN OFFLINE UNIT SUITE. It needs no daemon, no server and no network: the
whole of it runs in about two seconds on a warm machine. Exactly one test is an
exception, and it is gated - see THE ENVIRONMENT GATE below.

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
    CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1

gates EXACTLY ONE test -
ModelStoreLiveTests.PullAsync_FromTheRealRegistry_DownloadsResolvesListsAnd
DeletesTheModel - through Infrastructure/EnvGatedFactAttribute.cs, a
FactAttribute subclass that sets Skip unless the named variable equals the
expected value. Nothing else in the suite is gated. The default run is therefore
789 total / 788 passed / 1 skipped; with the gate open it is 789 / 789 / 0.

THAT TEST REALLY DOWNLOADS. It pulls smollm:135m - about 92 MB - from
registry.ollama.ai into a fresh TempStoreDirectory, asserts the progress stream
starts at "pulling manifest" and ends at "success", resolves the model, reads
its GGUF metadata back, checks the resolved file's length against the manifest
layer's size, lists it, deletes it and confirms the blobs directory is empty
again.

THE DEFAULT SUITE DOWNLOADS NOTHING. The family's "nothing downloaded at test
time" rule therefore applies TO THE DEFAULT SUITE ONLY, and this is the
deliberate deviation: downloading models is what ModelManager is for, so a
library that is never once exercised against a real registry is a library whose
central promise is untested.

TREAT THE GATED RUN AS PART OF A RELEASE CHECK, NOT AN OPTIONAL EXTRA.

THE TEST INFRASTRUCTURE
-----------------------
Five pieces under Infrastructure/, and each exists so that a whole tier can be
tested without a network or a real model:

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

THE LINKED CONFORMANCE MODEL
----------------------------
ModelManager.Tests LINKS, rather than copies, llama-native-tools/test-vectors/
codebrix-conformance-tiny.gguf into its output as test-vectors/. It is the SAME
FILE that every native build regenerates and checks byte for byte, and the same
file CodeBrix.Ollama.ModelRunner.Tests links (with EXPECTED.txt) to run its
logits against. One asset, three consumers, so the managed GGUF reader, the
native gate and the eventual managed binding can never drift on to different
files. Do not copy it into either test project.


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
path - today, exactly two entries under each of runtimes/osx-x64/native/ and
runtimes/osx-arm64/native/: libcodebrix_llama.dylib and LICENSE-LlamaCpp.txt.


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

with the pre-strip twin going to llama-native-tools/unstripped/<rid>/ (its
SHA256SUMS extended) and a BUILD-PROVENANCE.txt entry written from that build's
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
day. The other five - win-x64, win-arm64, linux-x64, linux-arm64 and
linux-riscv64 - are NOT BUILT, and their scripts, written on the Intel mini,
have NEVER BEEN RUN; each script says so in its header. From dav1d's
experience, each platform's first real run is expected to find something to
fix. Fix it IN THE SCRIPT, and rewrite that platform's status block.

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
result, not the hash. The macOS floor is a symbol question, not a stamp: the
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

EVERY PORTED FILE CARRIES THIS HEADER AS ITS FIRST LINE, on one line:

    // Ported from Ollama (https://github.com/ollama/ollama), MIT License,
    Copyright (c) Ollama. Source: <upstream path> at commit a43fad18.

27 files in the library carry it today, naming types/model/name.go, the five
fs/gguf files, parser/parser.go with api/types.go, the three manifest/ files,
server/images.go, server/create.go, server/model.go, x/create/manifest.go,
server/download.go and format/format.go. Ten files in the test project carry it
too, naming the upstream *_test.go the case tables came from.

FILES WITH NO OLLAMA LOGIC CARRY NO HEADER, and that is not an oversight: the
exception types and the JSON helper under Common/, the public DTOs and options
under Store/, Modelfile/GoBool.cs and Registry/RegistryManifestResponse.cs. The
on-disk FORMAT those DTOs describe is Ollama's, kept identical on purpose so a
store directory is interchangeable with a real Ollama install - but the C# is
this repository's.

THIRD-PARTY-NOTICES.txt at the repository root holds the full attribution: the
upstream-file-to-our-file scope list from which the header list above was
compiled, the modifications made during the port, and Ollama's MIT licence
verbatim. It also covers llama.cpp and the licences that appear in the vendored
snapshot. Extend it whenever you port anything further; if you rewrite a file
until nothing of the original remains, remove the header rather than leave a
false attribution.

THE REFERENCE CLONE lives at ~/GitHome/ollama, at that same commit. It is a
REFERENCE ONLY - not vendored, not a submodule, and there is deliberately no
NuGet reference to anything Ollama. Read it beside the C#; do not copy it into
the tree.

THE DELIBERATE DEVIATIONS FROM OLLAMA
-------------------------------------
All of them are recorded in THIRD-PARTY-NOTICES.txt under MODIFICATIONS MADE
DURING THE PORT, and most of them again in the XML <remarks> at the place they
happen. They are decisions, not omissions, and they are the ones most likely to
be "fixed" by mistake.

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

  - ZERO NUGET DEPENDENCIES IN BOTH LIBRARY PROJECTS. All JSON goes through the
    in-box System.Text.Json via Common/ModelManagerJson.cs; HTTP goes through
    HttpClient and SocketsHttpHandler; hashing through
    System.Security.Cryptography. This is not a preference: it is verifiable in
    the packed nuspec, and it is why there is no Microsoft.Extensions.AI
    adapter, no OllamaSharp reference and no LLamaSharp reference anywhere.

  - NO NEW THIRD-PARTY NUGETS ANYWHERE, per the family rule: CodeBrix.* and
    Microsoft packages are fine; xUnit and SilverAssertions in the .Tests
    projects are the standing exception.

  - XML DOC COMMENTS ON EVERY PUBLIC AND PROTECTED MEMBER.
    GenerateDocumentationFile is on for both libraries; fix CS1591 at the source
    and never suppress it. There is no <NoWarn> in this repository.

  - TESTS are named <ClassUnderTest>Tests.cs with PascalCase method names in the
    Member_Behaviour_Condition shape, //Arrange //Act //Assert comments in
    multi-statement tests, and TestContext.Current.CancellationToken passed to
    every cancellable call (xUnit1051). Assertions are SilverAssertions fluent
    style throughout - .Should().Be(), .BeNull(), .BeTrue(), .HaveCount(),
    .BeEmpty() - not raw Assert.* calls.

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
log, diff, ls-tree) is fine. As of this writing the repository has two commits -
the initial commit and the native-tools commit - and everything else, including
the whole of ModelManager, is uncommitted in the working tree.

WHAT HAS BEEN VALIDATED, AND ON WHAT
------------------------------------
Everything below was done on ONE machine: the Intel Mac mini (2018), i7-8700B,
macOS 15.8, x86_64, .NET SDK 10.0.401.

  * `dotnet build CodeBrix.Ollama.slnx -c Release` - 0 warnings, 0 errors.
  * The ModelManager suite - 789 total, 788 passed, 1 skipped, about two
    seconds, with no network. Run both ways: through `dotnet test` and by
    running the built executable directly.
  * Both packages packed and unzipped. Each carries the five packed root files
    and lib/net10.0, each nuspec carries an EMPTY net10.0 dependency group, and
    the ModelRunner package carries runtimes/osx-x64/native/ and
    runtimes/osx-arm64/native/ with no doubled path.
  * The osx-x64 native: built, full gate passed (architecture, 248/248 required
    exports, exact export surface, install name, system-only dependencies,
    minos 13.3, signature, smoke test, byte-identical model regeneration, and
    conformance at max |diff| 4.98e-08), adopted, twin and dSYM stored. The
    osx-arm64 native was built and gated the same way on the Apple Silicon
    Mac mini, including the Metal conformance pass; its record is in
    BUILD-PROVENANCE.txt.

WHAT HAS NOT BEEN VALIDATED
---------------------------
  * THE MANAGED SUITE HAS NEVER BEEN RUN ON WINDOWS OR LINUX. ModelManager is
    pure managed code and path handling goes through Path.Combine throughout,
    but that is an argument, not evidence. The store lays out
    manifests/<host>/<namespace>/<model>/<tag> as real directories, and nothing
    has yet proved that a Windows run agrees with this one. Anyone with those
    machines should run it and report back.

  * THE LIVE PULL TEST HAS NOT YET BEEN RUN IN THIS REPOSITORY. Say that plainly
    rather than assuming it works: CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1 has never
    been set here, so the one test that reaches registry.ollama.ai has only ever
    been skipped. Running it is the first thing to do before any release.

  * THE FIVE WINDOWS AND LINUX NATIVE SLICES ARE UNBUILT and their scripts
    unrun, as described under THE NATIVE LIBRARIES.

  * ModelRunner IS NOT WRITTEN. There is no managed binding, so the committed
    macOS libraries have been verified only by llama-native-tools' own gate - by
    C programs, never yet from .NET. The first thing the binding will prove or
    disprove is that the runtimes/<rid>/native/ probing finds the file the
    packing block puts there, which is exactly why that block also stages the
    natives into the test output.

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
