================================================================================
AGENT-README: CodeBrix.Ollama.ModelManager
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Ollama.ModelManager.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Ollama.ModelManager is a cross-platform .NET 10
library that maintains a local store of large language models in exactly
Ollama's on-disk layout, pulls models into it from any registry that speaks
Ollama's manifest-and-blob protocol, obtains the files of a model that no such
registry serves, and resolves a model name to the files on disk that an
in-process runner loads.

It does five things, and they build on one another:

  1. NAMES -- Ollama's model-name syntax, parsed and validated by Ollama's own
     rules, completed from configurable defaults, and turned into the
     four-level relative path a manifest lives at.
  2. THE STORE -- pull, list, exists, show, resolve, copy, delete, create.
     Blobs live at <store>/blobs/sha256-<hex> and manifests at
     <store>/manifests/<host>/<namespace>/<model>/<tag>, byte for byte what
     Ollama writes. Downloads are split into byte ranges, fetched
     concurrently, resumed after an interruption and verified against their
     sha256 digest. registry.ollama.ai is the default registry, and a name
     such as hf.co/<user>/<repo>:<quant> reaches Hugging Face through the
     same code.
  3. BUNDLES -- the same store, filled from where a publisher actually keeps
     a model that no such registry serves: a Hugging Face file repository, a
     plain list of HTTPS addresses (the objects under a public storage bucket
     among them), or a folder already on disk. The files are fetched over
     plain HTTPS, verified against whatever the publisher stated, and stored
     as ordinary blobs and layers, so listing, showing, resolving, copying,
     deleting and pruning work on them unchanged. What a publisher states
     about the licence is reported, and MaterializeAsync writes the files
     back out as the file tree the publisher wrote.
  4. DERIVED BUNDLES -- a model this library made from one already in the
     store, stored beside it and recording what made it and from what:
     ExportToOnnxAsync turns a checkpoint into ONNX (or registers the graphs a
     publisher already ships), and ReduceOnnxAsync makes those graphs several
     times smaller by quantizing their weights. Converting a checkpoint needs
     a CPython on the machine, because that work only exists in Python;
     REDUCING AN EXISTING GRAPH TO FOUR- OR EIGHT-BIT WEIGHTS NEEDS NOTHING
     INSTALLED, because this library does it itself. A reduced model is an
     approximation of the one it came from.
  5. THE FILE FORMATS -- a Modelfile parser that accepts what Ollama's parser
     accepts, with the same messages and line numbers, and a GGUF header
     reader that returns every key-value and tensor descriptor without ever
     touching tensor data.

Everything that touches the disk or the network is async and takes a
CancellationToken last, with a default; name parsing, Modelfile parsing and the
tensor-type arithmetic are ordinary synchronous methods, and there are no
synchronous wrappers over the async work.

The package has exactly ONE NuGet dependency, CodeBrix.Python, and it is INERT:
nothing of it is loaded until a Python feature actually runs. Obtaining a model,
listing, showing, resolving, materializing, importing a folder, parsing a
Modelfile, reading GGUF metadata, passing a publisher's own ONNX graphs through
and reducing an ONNX graph to four- or eight-bit weights are not Python
features and never load it. Converting a checkpoint to ONNX is. See PYTHON
below, which says feature by feature which is which.

Target framework: .NET 10 or later; no netstandard and no .NET Framework
target. Source: https://github.com/ellisnet/CodeBrix.Ollama

HOW THIS RELATES TO CodeBrix.Ollama.ModelRunner
-----------------------------------------------
The same repository produces a second, SEPARATE package,
CodeBrix.Ollama.ModelRunner.MitLicenseForever, which loads a GGUF file and runs
it in-process. It has its own AGENT-README, and nothing in this document
describes its API. The two packages are independent -- neither references the
other, and neither is or contains an HTTP server. The seam between them is
ResolveAsync: the paths in the ResolvedModel
it returns (ModelPath, ModelShardPaths, ProjectorPaths, AdapterPaths and
DraftPath) are ordinary files, and they are what ANY in-process GGUF runner
loads -- ModelRunner, another binding, or your own.


INSTALLATION
============
PackageId: CodeBrix.Ollama.ModelManager.MitLicenseForever

    dotnet add package CodeBrix.Ollama.ModelManager.MitLicenseForever

IMPORTANT: the ".MitLicenseForever" suffix belongs to the PACKAGE ID only. It
never appears in a namespace, a using directive, an assembly name or a type
name; the assembly is CodeBrix.Ollama.ModelManager and the single namespace is
CodeBrix.Ollama.ModelManager. NuGet dependencies: EXACTLY ONE,
CodeBrix.Python, and it is inert -- nothing of it is loaded until a Python
feature is used, and obtaining, listing, resolving and materializing models are
not Python features, nor is making an existing ONNX graph smaller.
Everything else is in-box: JSON goes through
System.Text.Json and hashing through System.Security.Cryptography, and no
native library ships in the package. License: MIT, with license acceptance
required.

A CPython SHARED LIBRARY is needed ONLY for the Python features -- and only at
the moment one runs. Nothing here ships, installs or downloads CPython or any
pip module; the store, the registry, Modelfiles, GGUF metadata and the managed
reduction engine need none of it. See PYTHON below for which features are which,
how a CPython is found, and what is reported when there is none.

NULLABLE REFERENCE TYPES ARE OFF in the library, so the compiler will not warn
you about anything it hands back. Where a member can be null its XML
documentation says so, and this file repeats it: treat ModelInfo.Template,
ModelInfo.System, ModelInfo.Parameters, ModelInfo.Metadata,
ResolvedModel.ModelPath, ResolvedModel.DraftPath, Modelfile.From,
Modelfile.Template, GgufMetadata.ChatTemplate and GgufValue.RawValue as
nullable however your project is configured.


KEY NAMESPACES / USINGS
=======================

    using CodeBrix.Ollama.ModelManager;   // EVERY public type in the package

That is the whole story. The library declares ONE namespace, and every
public type lives in it - the bundle vocabulary (BundleDefinition,
BundleFile, BundleListing, FileFilter, LicenseRecord, PullOptions, PullSource,
ImportOptions, MaterializeOptions, MaterializeLink, ResolvedFile), the two
sources (HuggingFaceHubSource, HttpFileListSource, with the helper
GoogleCloudStorageListing), the conversion and reduction vocabulary
(ExportOptions, ExportRoute, ExportResult, ReduceOptions, ReduceMode,
ReduceEngine, ReduceResult, ModelConfigKeys) and the Python corner
(PythonSupport, PythonOptions, PythonSupportReport, PythonModuleReport,
PythonLibrarySource, PythonVirtualEnvironmentSource, PythonEngineOwner) among
them. The repository folders (Common/, Names/, Gguf/, Modelfile/, Store/,
Registry/, Bundles/, Sources/, Python/, Export/, Reduce/, Onnx/) are FILE
ORGANIZATION ONLY, not namespaces:
`using CodeBrix.Ollama.ModelManager.Store;` is a CS0246 error.
Everything under Registry/ is internal -- the registry client, the blob
downloader and the challenge parser are reached only through PullAsync. You
will also want the ordinary framework usings: System,
System.Collections.Generic, System.IO, System.Linq, System.Threading and
System.Threading.Tasks.

NAMING SHARP EDGES. ModelInfo, ResolvedModel and Modelfile each have a
property called System (the system-prompt layer); in a file that also has
`using System;` the expression `info.System` is a member access, not a type
name, so no alias is needed, and only a local variable named System would be a
problem. Modelfile has a static method Parse AND an unrelated instance property
Parser (the PARSER command's argument). ModelName is a readonly struct, and
default(ModelName) has every part null and IsValid false.


CORE API REFERENCE
==================
The entry point is ModelStore, the one implementation of IModelStore:

    using var store = new ModelStore();                     // Ollama's defaults
    using var store = new ModelStore(new ModelStoreOptions  // a store of your own
    {
        StoreDirectory = "/data/models",
    });


ModelStore is IDisposable: disposing releases the registry client and its
pooled connections and leaves the store directory alone. Create one and keep it
-- every operation after Dispose throws ObjectDisposedException.

Every operation takes a model NAME as a string, and every operation that
touches the disk or the network is async and takes a CancellationToken last,
with a default. The reference below is organised by what you hand the store
and what it hands back:

    WHERE THE STORE LIVES           the directory, and sharing it with Ollama
    MODEL NAMES                     the name grammar and the ModelName struct
    CONFIGURING THE STORE           every ModelStoreOptions property
    THE IModelStore OPERATIONS      pull, list, exists, show, resolve, copy,
                                    delete, prune and create, one by one
    MODELFILES                      Modelfile.Parse and the typed views
    GGUF METADATA                   GgufMetadata.ReadAsync and the header model
    THE DATA TYPES                  manifests, layers, config and parameters
    BUNDLES                         models that no Ollama-protocol registry
                                    serves, and the pull that fetches them
    THE SOURCES                     the Hugging Face Hub, a list of addresses,
                                    a storage bucket, and the file filter
    DESCRIBING A MODEL              BundleDefinition and LicenseRecord
    MATERIALIZING                   writing a bundle out as the publisher's
                                    own file tree
    IMPORTING A FOLDER              a folder already on disk as a bundle
    RESOLVING A BUNDLE              ResolvedModel.Format and .Files
    DERIVED BUNDLES                 a bundle this library made, and what it
                                    records about how it was made
    SHOWING AND LISTING             what a bundle reports about itself
    PYTHON                          which features need a CPython, which never
                                    do, and how one is found
    EXPORTING TO ONNX               ExportToOnnxAsync, its four routes and what
                                    each one needs
    REDUCING AN ONNX MODEL          ReduceOnnxAsync, its four modes and its two
                                    engines - one of which needs nothing
    THE ERROR MODEL                 every exception and when it is thrown
    THREAD SAFETY AND CONCURRENCY   what is and is not coordinated

WHERE THE STORE LIVES
---------------------
With default options the directory is resolved once, in the ModelStore
constructor, by ModelStoreOptions.ResolveDefaultStoreDirectory: the
OLLAMA_MODELS environment variable when it is set and not blank, otherwise
<user profile>/.ollama/models. That is deliberately the directory a local
Ollama install uses, and the two can share it in both directions. Blob file
names are identical (sha256-<64 lowercase hex>), so a model pulled by Ollama is
a cache hit here and the other way round. The manifest layout and the manifest
BYTES are identical: a pull writes the exact bytes the registry served, and a
create serializes JSON the way Go's encoder does, compact with a trailing
newline. The sidecars this library writes during a download carry a
".codebrix-" segment -- <blob>.codebrix-partial and <blob>.codebrix-parts.json
-- while Ollama's own are <blob>-partial and <blob>-partial-N, so the two
downloaders can never write to the same file.

Set ModelStoreOptions.StoreDirectory for a private store; the path is expanded
with Path.GetFullPath, exposed as IModelStore.StoreDirectory, and need not
exist, since the two subdirectories are created on first write.


MODEL NAMES
===========
Every IModelStore method takes a name string and parses it the same way:

    [scheme://][host/][namespace/]model[:tag]

    smollm:135m                     library/smollm, tag 135m
    smollm                          tag defaults to latest
    myorg/mymodel:v3                namespace myorg
    hf.co/user/repo:Q8_0            a Hugging Face repository
    registry.local:5000/team/m:v1   a registry on a port (the colon is host)
    http://registry.local/t/m:v1    plain HTTP; see AllowInsecureHttp

Absent parts come from the store's options, whose defaults are Ollama's: host
registry.ollama.ai, namespace library, tag latest, scheme https.

Two splitting rules matter. The last ":" only starts a tag when it comes AFTER
the last "/", which is what lets a host keep its port. A separator that
promises a part which turns out to be empty ("mm:", "//", "n/m:") stores
ModelName.MissingPart ("!MISSING!") there, and that never passes validation. An
"@digest" suffix is NOT a part of its own: as in Ollama it stays attached to
whichever part it follows, making that part invalid, so "model@sha256:..." is
rejected with InvalidModelNameException. Name a tag, not a digest.

PART GRAMMAR: host, 1 to 350 characters, first alphanumeric or "_", then
alphanumeric, "-", "_", "." or ":"; namespace, 1 to 80, same start, then
alphanumeric, "-" or "_" (NO dots); model and tag, 1 to 80, same start, then
alphanumeric, "-", "_" or "." (no colons).

THE ModelName API (readonly struct, IEquatable<ModelName>)

    const   MissingPart "!MISSING!", DefaultHost "registry.ollama.ai",
            DefaultNamespace "library", DefaultTag "latest",
            DefaultProtocolScheme "https"
    props   Host, Namespace, Model, Tag, ProtocolScheme -- each null when the
            part is absent
    ctor    ModelName(host, @namespace, model, tag, protocolScheme)
    static  Default, CreateDefaults(host, @namespace, tag, protocolScheme),
            Parse(string), Parse(string, ModelName defaults),
            TryParse(string, out ModelName), ParseBare(string),
            ParseFromRelativePath(string), Merge(a, b), IsValidNamespace(s)
    tests   IsValid (the same test as IsFullyQualified), IsFullyQualified
    render  ToRelativePath(), ToString(), DisplayShortest(),
            DisplayNamespaceModel(), BaseUrl()
    equals  EqualsIgnoreCase(other), Equals(other), == , != , GetHashCode()

PARSING NEVER THROWS. Parse and ParseBare always return a value, which may be
nonsense -- that is Ollama's behaviour, preserved here. Check IsValid, or use
`if (!ModelName.TryParse(input, out ModelName name))`. ParseBare fills in
nothing; Parse fills in from Default; the second Parse overload fills in from
defaults you supply. Only two members throw: ToRelativePath
(InvalidOperationException when the name is not fully qualified) and BaseUrl
(UriFormatException when the scheme and host do not form an absolute address).

DisplayShortest is what DisplayName carries throughout the store: it drops the
default host, and with it the default namespace, comparing case-insensitively.
"registry.ollama.ai/library/model:latest" becomes "model:latest";
"registry.ollama.ai/ns/model:tag" becomes "ns/model:tag"; a name on another
host keeps all three parts.

OTHER REGISTRIES. Nothing about Hugging Face is special-cased:
"hf.co/user/repo:Q8_0" parses to host hf.co, namespace user, model repo, tag
Q8_0, the pull goes to https://hf.co/v2/user/repo/manifests/Q8_0, and the
manifest is stored at manifests/hf.co/user/repo/Q8_0. Any registry answering
Ollama's shapes works the same way, a test double included.

PLAIN HTTP is refused unless asked for: a name whose scheme is "http" raises
RegistryException, "insecure protocol http", BEFORE any request goes out unless
ModelStoreOptions.AllowInsecureHttp is true. HTTPS is always allowed, and there
is no option to relax certificate validation -- supply your own
HttpMessageHandler if a lab setup needs that.


CONFIGURING THE STORE (ModelStoreOptions)
=========================================
Every property defaults to Ollama's behaviour, so `new ModelStore()` operates
on the store a local Ollama install would use.

    string   StoreDirectory       = null       null -> OLLAMA_MODELS, else
                                               ~/.ollama/models (absolute)
    string   DefaultRegistryHost  = "registry.ollama.ai"
    string   DefaultNamespace     = "library"
    string   DefaultTag           = "latest"
    bool     AllowInsecureHttp    = false      refuses an http:// name before
                                               any request
    string   BearerToken          = null       offered only after a 401
    int      MaxConcurrentParts   = 16         byte ranges of ONE blob in
                                               flight; 0 or less means 1
    long     MinPartSize, MaxPartSize  = 100 MB and 1000 MB -- the clamp on
                                               the range size, total/concurrency
    TimeSpan StallTimeout         = 30 s       a range with no data for this
                                               long is reissued; a stall does
                                               NOT consume a retry
    int      MaxRetries           = 6          per range, exponential back-off
                                               of 1s, 2s, 4s ... capped at 60s
    TimeSpan ProgressInterval     = 100 ms     how often a running download
                                               reports; one final report is
                                               always made
    HttpMessageHandler HttpMessageHandler = null
    string   UserAgent            = null       null -> the library's own name
                                               and assembly version
    PythonOptions Python          = new()      where a CPython is, for the
                                               features that need one. NEVER
                                               null, and nothing reads it until
                                               such a feature runs - see PYTHON
    static string ResolveDefaultStoreDirectory()

HttpMessageHandler AND TESTING. Nothing in this library reaches the network by
any other route, so a handler here gives you a complete in-memory registry --
which is exactly how the library's own offline test suite works, pairing a fake
handler with a temporary StoreDirectory and a DefaultRegistryHost the fake
answers for.

Two rules about that handler. It is used EXACTLY as you configured it -- when
the library creates its own it is a SocketsHttpHandler with AllowAutoRedirect
off, no automatic decompression and a five-minute pooled connection lifetime,
and yours is not adjusted; redirects are then followed by the library itself,
by hand, up to ten of them, which keeps the Authorization header away from a
redirect target. And the store does NOT dispose a handler you supplied, only
one it created. HttpClient.Timeout is infinite: nothing is abandoned on a clock
except through StallTimeout or your CancellationToken.

BEARER TOKEN SEMANTICS. The token is offered only after a challenge, never
before one. A request goes out with no Authorization header; if the registry
answers 401, the WWW-Authenticate header is parsed and, when a BearerToken is
configured and has not already been used on this client, the same request is
repeated once with "Authorization: Bearer <token>". A second 401, or a 401 with
no token configured, raises RegistryException with StatusCode Unauthorized. The
header only ever goes to the host the request started at: a blob request
redirected to a content-delivery host is refetched WITHOUT it, because such a
URL carries its own signature. Public models need no token, and there is no
token exchange with the challenge realm and no ed25519 request signing.


THE IModelStore OPERATIONS
==========================
ModelStore is the only implementation; the interface is there so you can
substitute your own in tests.

    string StoreDirectory { get; }
    IAsyncEnumerable<PullProgress> PullAsync(string name, CancellationToken)
    IAsyncEnumerable<PullProgress> PullAsync(string name, PullOptions options,
                                             CancellationToken)
    Task ImportBundleAsync(string name, string directory,
                           ImportOptions options = null, CancellationToken)
    Task<IReadOnlyList<string>> MaterializeAsync(string name,
                                                 string targetDirectory,
                                                 MaterializeOptions options
                                                     = null,
                                                 CancellationToken)
    Task<IReadOnlyList<ModelSummary>> ListAsync(CancellationToken)
    Task<bool>          ExistsAsync(string name, CancellationToken)
    Task<ModelInfo>     ShowAsync(string name, CancellationToken)
    Task<ResolvedModel> ResolveAsync(string name, CancellationToken)
    Task CopyAsync(string sourceName, string destinationName,
                   CancellationToken)
    Task DeleteAsync(string name, CancellationToken)
    Task CreateAsync(string name, Modelfile modelfile,
                     CreateOptions options = null, CancellationToken)
    Task<IReadOnlyList<string>> PruneAsync(TimeSpan gracePeriod,
                                           CancellationToken)
    Task<ExportResult> ExportToOnnxAsync(string name,
                                         ExportOptions options = null,
                                         IProgress<PullProgress> progress
                                             = null,
                                         CancellationToken)
    Task<ReduceResult> ReduceOnnxAsync(string name,
                                       ReduceOptions options = null,
                                       IProgress<PullProgress> progress = null,
                                       CancellationToken)

Every CancellationToken parameter is last and defaulted. Every name argument
must parse to a fully qualified name: a null or blank name is an
ArgumentException and a name that does not parse is an
InvalidModelNameException, both thrown before anything else happens.

PullAsync
---------
Downloads a model from its registry into the store, reporting progress as it
goes. The enumerable is LAZY: nothing is requested until you iterate it.

THE PROGRESS STREAM, IN ORDER

    "pulling manifest"          once, before the manifest is fetched
    "pulling <12 hex>"          one stream of reports per layer, in manifest
                                order, with the config layer last
    "verifying sha256 digest"   once, before downloaded blobs are re-hashed
    "writing manifest"          once, before the manifest is written
    "removing unused layers"    only when the PREVIOUS manifest for this name
                                referenced layers the new one does not
    "success"                   always the last report of a successful pull

"<12 hex>" is the first twelve hexadecimal characters of the layer digest, the
short form Ollama prints. A layer report carries Digest, TotalBytes,
CompletedBytes and the computed Percent, and arrives at most once per
ProgressInterval; a status-only report has Digest null and both counts zero.

WHAT HAPPENS UNDERNEATH
  - SAFETENSORS ARE REFUSED FIRST: a manifest with any
    application/vnd.ollama.image.tensor layer raises ModelManagerException
    naming the model, before a single blob request goes out.
  - CACHE HITS. A layer whose blob file already exists is not downloaded; it
    emits one report with CompletedBytes equal to TotalBytes and is skipped in
    the verification pass. (A digest shared by the config layer and a content
    layer is still verified when either pass downloaded it.)
  - RESUME. A blob is split into byte ranges -- total / MaxConcurrentParts,
    clamped to MinPartSize and MaxPartSize, the last one shortened -- fetched
    concurrently into one partial file, with progress recorded in a JSON
    sidecar. A sidecar left by an earlier attempt is resumed when it matches
    this blob and the partial file; anything that does not add up starts over.
    A range that stalls for StallTimeout is reissued without consuming a retry,
    and any other failure is retried up to MaxRetries times.
  - DIGEST VERIFICATION, TWICE. The finished partial file is hashed before it
    is moved into place, and the "verifying sha256 digest" pass re-hashes
    every blob this pull downloaded. Either mismatch deletes the bad file and
    its sidecar and raises DigestMismatchException, so the bad bytes are gone
    from the store by the time you see it.
  - ATOMIC MANIFEST WRITE. The manifest is written LAST, through a temporary
    file in the same directory that is then moved into place, so a concurrent
    reader sees either the old manifest or the new one and a model is either
    fully present or absent. The bytes written are the exact bytes served.
  - PRUNING. Layers the previous manifest for this same name referenced and
    the new one does not are deleted -- but only after every manifest in the
    store has been checked, so a blob another model shares survives.
  - CANCELLATION throws OperationCanceledException from the enumerator, writes
    no manifest, and keeps the partial file and sidecar for a later pull.

ERRORS: ModelNotFoundException (404 for the manifest or a blob),
RegistryException (any other error status, a 401, a transport failure that
survived every retry, an insecure http:// name, an answer that is not a
manifest), DigestMismatchException, ModelManagerException (safetensors). A pull
of a model that is already complete is cheap but not free -- the manifest is
fetched again, every blob confirmed present and the manifest rewritten -- so
guard the call with ExistsAsync when you only want to know it is there.

THE SECOND OVERLOAD, PullAsync(name, options), fetches a BUNDLE - a model that
no Ollama-protocol registry serves - and is described under BUNDLES below. It
is never taken by accident: options of null, and PullOptions.ForRegistry(),
both run exactly the pull described here.

ListAsync
---------
One ModelSummary per manifest, most recently modified first (a stable sort, so
models written in the same second keep the order the directory walk produced).
Only files exactly four levels below <store>/manifests are considered, matching
Ollama's */*/*/* glob. A path that does not parse as a name, and a file that
does not hold a valid manifest, are SKIPPED rather than thrown -- one corrupt
manifest cannot hide the rest of the store -- and a model whose config blob is
missing is still listed, with an empty ModelConfig. Only the manifest and the
small config blob are read; the GGUF files are never opened.

ExistsAsync
-----------
True when the manifest FILE exists. It neither reads the manifest nor checks
that the blobs it names are present; it is the cheapest question to ask.

ShowAsync
---------
Everything that can be said about one model. ModelNotFoundException when the
store has no such manifest. ModelInfo carries:

    ModelName Name                 the fully qualified name
    string    DisplayName          Name.DisplayShortest()
    string    Digest               sha256 of the MANIFEST FILE, lowercase hex
                                   with NO "sha256:" prefix
    long      Size                 every layer plus the config layer
    DateTimeOffset ModifiedAt      the manifest file's last write time
    ModelManifest Manifest         as read from disk
    ModelConfig   Config           never null; empty when unreadable
    string    Template, System     those layers' text, or null
    ModelParameters Parameters     params layer decoded, or null
    IReadOnlyList<string> Licenses license layers in order; empty if none
    IReadOnlyList<ModelMessage> Messages   messages layer; empty if none
    GgufMetadata Metadata          header of the FIRST model-weights GGUF, or
                                   null when the model has no GGUF weights
    IReadOnlyList<GgufMetadata> ProjectorMetadata   one per projector, in
                                   manifest order; empty if none
    IReadOnlyList<ModelCapability> Capabilities
    string    ModelfileText        the model rendered back to Modelfile text
                                   the way `ollama show --modelfile` prints it,
                                   with blob PATHS in the FROM lines, so it can
                                   be parsed and fed back to CreateAsync

ShowAsync OPENS THE GGUF FILES to read their headers (with default
GgufReadOptions, so arrays longer than 1024 elements -- token vocabularies --
are skipped), which makes it far more expensive than ListAsync; do not call it
in a loop over a large store when ListAsync answers the question.

CAPABILITY INFERENCE, in the order capabilities are added, without duplicates:

  1. From the CONFIG: any name in ModelConfig.Capabilities that parses as a
     ModelCapability, ignoring case. Local models rarely carry these.
  2. From the WEIGHTS GGUF, when there is one:
       tokenizer.chat_template contains "tools" or "tool_call"  -> Tools
       that chat template contains "<think>" or "thinking"      -> Thinking
       <arch>.pooling_type present -> Embedding, otherwise      -> Completion
       <arch>.vision.block_count present                        -> Vision
       <arch>.audio.block_count present                         -> Audio
  3. From the PROJECTORS: any projector layer at all            -> Vision
       a projector with a true has_audio_encoder key (exact, or ending in
       ".has_audio_encoder")                                    -> Audio
  4. From the model's own GO TEMPLATE layer: ".Tools" -> Tools, ".Suffix" ->
     Insert, a mention of thinking -> Thinking

The Thinking rule is deliberately looser than upstream's: Ollama parses the Go
template into a syntax tree to find the text around a .Thinking field, while
here a template that mentions a think tag or the word thinking is taken to
support it. Every other rule matches upstream.

ResolveAsync
------------
A name in, file paths out -- the operation a runner cares about.
ModelNotFoundException when the store has no such manifest. ResolvedModel:

    ModelName Name                 the fully qualified name
    string    ManifestPath         absolute path of the manifest file
    string    ModelPath            the FIRST model-weights blob, or null when
                                   the model has none
    IReadOnlyList<string> ModelShardPaths   further model-weights blobs of a
                                   split model, in manifest order; else empty
    IReadOnlyList<string> ProjectorPaths, AdapterPaths   projector and LoRA
                                   adapter blobs, in manifest order
    string    DraftPath            the draft-model blob, or null
    Template, System, Parameters, Licenses, Messages and Config are exactly as
    described for ModelInfo above.

No weights file is opened; the only blobs read are the small text and JSON
layers and the config. Paths are computed from digests, so a path comes back
whether or not the file is there -- a manifest whose blob was deleted by hand
resolves to a path that does not exist, so check File.Exists, or pull again, if
you cannot rule that out.

    ResolvedModel model = await store.ResolveAsync("smollm:135m");
    string weights  = model.ModelPath;   // give this to a runner
    string template = model.Template;    // may be null
    int contextSize = model.Parameters?.NumCtx ?? 4096;

CopyAsync
---------
Gives an existing model a second name. Only the manifest is copied, byte for
byte; every blob is shared, so a copy costs a few hundred bytes. A source and
destination resolving to the same relative path make the call a no-op, a
missing source is ModelNotFoundException, and an existing DESTINATION is
OVERWRITTEN -- there is no "already exists" check, so ask ExistsAsync first.

DeleteAsync
-----------
Removes the manifest, then every blob no remaining manifest references.
ModelNotFoundException when there is no such model. Blobs shared with another
model (after a copy, or a create that inherited from it) are kept; deleting the
last name that references a blob is what removes it. Empty directories under
manifests/ are removed upwards, but never the manifests directory itself, and a
directory that is a symbolic link is left alone. A blob whose file was already
gone counts as deleted.

PruneAsync
----------
Garbage-collects the store: every blob that no manifest references is removed,
and so is every download sidecar this library left behind (the
".codebrix-partial" data file and ".codebrix-parts.json" state file of an
abandoned pull). Only files OLDER than the grace period are eligible - a
younger file may belong to a pull that is still running in another process -
and Ollama's own value for that is one hour:

    IReadOnlyList<string> removed =
        await store.PruneAsync(TimeSpan.FromHours(1));

The returned list holds the digests of the blobs that were removed. Files in
blobs/ that this library does not recognise - a co-located Ollama server's own
"-partial" files, for instance - are never touched, which is deliberately
narrower than Ollama's prune. Manifests are never removed by this operation.
Nothing calls it for you: pulls and creates prune only the layers of the name
they replaced, so a store that has seen interrupted pulls or manual blob
copies is tidied only when you call PruneAsync.

CreateAsync
-----------
Builds a new model from a parsed Modelfile. NOTHING IS DOWNLOADED: FROM names
either a model already in the store or a GGUF file on disk.

    Modelfile modelfile = Modelfile.Parse(
        "FROM ./mistral-7b-instruct.Q4_K_M.gguf\n" +
        "TEMPLATE \"\"\"{{ .System }}\n\n{{ .Prompt }}\"\"\"\n" +
        "SYSTEM You are a terse assistant.\n" +
        "PARAMETER temperature 0.2\n");
    await store.CreateAsync("my-assistant:v1", modelfile,
        new CreateOptions { BaseDirectory = "/models/downloads" });

FROM, FIRST ARGUMENT -- file or model. The argument is resolved against the
base directory (CreateOptions.BaseDirectory, or Environment.CurrentDirectory
when it is null or empty) with Path.GetFullPath; "~" is NOT expanded, because
the argument is a path, not a shell word.

  - If a FILE exists there it is read as GGUF, hashed and imported as a blob,
    and becomes the model layer. The config's model_format ("gguf"),
    model_family (the architecture), model_type (the parameter count as a
    human string) and file_type (the quantization name) are filled in from the
    header where they are not already set, and the architecture is appended to
    model_families.
  - Otherwise the argument is parsed as a MODEL NAME, with the store's own
    defaults, and looked up locally. Every layer of that model is inherited,
    sharing its blobs, with each layer's From field set to the source model's
    short display name, and its config becomes the base of the new one.
  - Neither: ModelNotFoundException, naming the argument.

FROM, FURTHER ARGUMENTS are projectors and MUST be files; a missing one is
ModelNotFoundException. An imported GGUF becomes a PROJECTOR layer when its
general.type is "projector" or "mmproj", or it counts vision blocks but no
model blocks, or its architecture is "clip" with no model blocks and a vision
or audio encoder flag; otherwise it is another model layer. A GGUF whose
general.type is "adapter" cannot be a FROM argument at all:
GgufFormatException, pointing you at ADAPTER.

ADAPTER files must exist (ModelManagerException "file ... does not exist") and
must have general.type "adapter" (GgufFormatException, "not a LoRA adapter").

THE REPLACE / MERGE / APPEND RULES, applied after the layers are gathered:

    TEMPLATE   REPLACES any inherited template layer, and any legacy prompt
               layer, entirely
    SYSTEM     REPLACES any inherited system layer
    MESSAGE    the whole MESSAGE list REPLACES any inherited messages layer
    PARAMETER  MERGES over the inherited parameters: an inherited value
               survives unless the Modelfile sets the same name, and a name it
               sets replaces the inherited value WHOLE (a "stop" list replaces
               the inherited list, it does not extend it)
    LICENSE    APPENDS; inherited licenses are kept and the new ones follow
    RENDERER / PARSER / REQUIRES  set the matching ModelConfig fields

NOT SUPPORTED BY CreateAsync: a DRAFT line (ModelManagerException before any
work -- the PARSER accepts DRAFT and exposes Modelfile.Drafts, it is the STORE
that refuses it); a Modelfile with no FROM (ModelManagerException "no FROM
line", reachable only when you build one from commands by hand); safetensors,
with no conversion step; and globbing -- the argument is a path, not a pattern.

When it finishes, the config layer is written, then the manifest, last and
atomically. If the name already existed, layers the old manifest referenced and
nothing else references are pruned, so re-creating a model repeatedly does not
fill the store with orphaned blobs.


MODELFILES
==========
A faithful port: anything Ollama's parser accepts, this accepts, with matching
error text and line numbers.

    static Modelfile Parse(string text | TextReader reader)
    static Task<Modelfile> ReadFileAsync(string path, CancellationToken)
    Modelfile(IEnumerable<ModelfileCommand> commands)

Parse throws ArgumentNullException for null and ModelfileParseException for bad
text. ReadFileAsync assumes UTF-8 and honours a UTF-8, UTF-16 little-endian or
UTF-16 big-endian byte order mark, as Ollama does. The constructor accepts any
command list -- including one with no FROM, which only Parse insists on -- and
rejects a null command with ArgumentException.

COMMANDS ACCEPTED, keyword matched case-insensitively: FROM, LICENSE, TEMPLATE,
SYSTEM, ADAPTER, DRAFT, RENDERER, PARSER, PARAMETER, MESSAGE, REQUIRES.
Anything else is ModelfileParseException, "command must be one of ...", with
the line number. "#" starts a comment that runs to the end of the line, and a
MESSAGE role must be "system", "user" or "assistant".

THE TYPED VIEWS over the parsed commands:

    IReadOnlyList<ModelfileCommand> Commands   every command, in file order
    string From                    first FROM argument, or null
    IReadOnlyList<string> ModelArgs            every FROM argument, in order
    string Template, System, Renderer, Parser, Requires
                                   the LAST such command's argument, or null
    IReadOnlyList<string> Adapters, Drafts, Licenses    every argument
    IReadOnlyList<ModelMessage> Messages       role and content per MESSAGE
    IReadOnlyList<KeyValuePair<string, string>> ParameterLines   every
                                   PARAMETER as name and RAW value, in order,
                                   including repeats and deprecated names
    IReadOnlyList<string> DeprecatedParameters the names GetParameters drops
    override string ToString()     every command's line, each plus "\n"

ModelfileCommand carries Ollama's INTERNAL spelling, not the keyword as
written: "FROM x" is Name "model", Args "x"; "TEMPLATE x" is Name "template";
"PARAMETER temp 0.5" is Name "temp", Args "0.5"; "MESSAGE user hi" is Name
"message", Args "user: hi". Its ToString renders the line back, re-quoting
where Ollama would. A MESSAGE argument with no ": " separator keeps the whole
argument as the role and an empty content.

QUOTING. Modelfile.Quote wraps text holding a newline, or starting or ending
with a space: in three double quotes when the text also holds a double quote,
in one otherwise, and anything else is unchanged. Modelfile.TryUnquote removes
that quoting and returns false when the quoting is never closed, which is how
the parser knows a value continues on the next line. Single quotes are not
quoting characters, and unclosed quoting at end of file is
ModelfileParseException with LineNumber 0 and the message "unexpected EOF".

GetParameters() applies Ollama's typing to ParameterLines:

    int      num_keep  seed  num_predict  top_k  repeat_last_n  num_ctx
             num_batch  num_gpu  main_gpu  num_thread  draft_num_predict
    float    top_p  min_p  typical_p  temperature  repeat_penalty
             presence_penalty  frequency_penalty
    bool     use_mmap        string[]  stop

Repeated "stop" values are COLLECTED in order; every other repeated name keeps
its LAST value. Integers are read as 64-bit and then range-checked to 32 bits
(a value that does not fit throws), floats use the invariant culture ("0.5",
never "0,5"), and booleans accept Go's spellings: 1, t, T, TRUE, true, True, 0,
f, F, FALSE, false, False.

DEPRECATED parameters are DROPPED silently -- absent from the result and not
reported as unknown -- and listed in DeprecatedParameters. The nine are:
penalize_newline, low_vram, f16_kv, logits_all, vocab_only, use_mlock,
mirostat, mirostat_tau, mirostat_eta. Any other name is
ModelfileParseException, "unknown parameter '<name>'", with LineNumber 0. Note
the asymmetry: PARSING a file with an unknown parameter succeeds and the line
lands in ParameterLines; only GetParameters -- which CreateAsync calls --
rejects it.

ModelfileParseException.LineNumber is the 1-based line, or 0 when the problem
applies to the whole file (a missing FROM, an unexpected end of file, any
parameter problem), and the message is prefixed "(line N): " when non-zero.


GGUF METADATA
=============
GgufMetadata reads the HEADER of a GGUF file -- the magic, the version, every
key-value and every tensor descriptor. IT NEVER READS TENSOR DATA, so reading a
multi-gigabyte model costs a few tens of kilobytes of sequential input.

    static Task<GgufMetadata> ReadAsync(string path | Stream stream,
                            GgufReadOptions options = null, CancellationToken)

The stream overload reads forward only from the current position, which must be
the first byte; a seekable stream also supplies the file size that tensor
ranges are checked against, and that check is skipped when it is unknown.

GgufReadOptions
    int  MaxArraySize       = 1024   How many elements of an array key-value to
         retain. A LONGER array is read past instead: its GgufValue has
         IsOmittedArray true and RawValue null, ArrayLength is still correct,
         and its key is listed in GgufMetadata.OmittedKeys. A NEGATIVE value
         retains every array. The default keeps a token vocabulary out of
         memory while keeping every ordinary key.
    bool ValidateTensorData = true   Check that every tensor's offset and byte
         count fall inside the file; skipped when the length is unknown.

GgufMetadata EXPOSES uint Version; bool IsBigEndian (the magic reads FUGG);
    long Alignment (general.alignment, or 32); long TensorDataOffset; long
    FileSize (-1 when unknown); ulong ParameterCount and TensorDataSize (the
    sums of element and byte counts); IReadOnlyDictionary<string, GgufValue>
    KeyValues, IReadOnlyList<string> Keys and IReadOnlyList<GgufTensorInfo>
    Tensors, all in file order; and IReadOnlyList<string> OmittedKeys.

CONVENIENCE PROPERTIES, each reading one well-known key: string Architecture
    (general.architecture, or "unknown"); string Kind (general.type, or
    "unknown"); GgufFileType FileType (general.file_type, or
    GgufFileType.Unknown) and string FileTypeName (its name, e.g. "Q4_K_M");
    ulong BlockCount, EmbeddingLength and ContextLength (<arch>.block_count,
    <arch>.embedding_length and <arch>.context_length, each 0 when absent);
    string ChatTemplate (tokenizer.chat_template, or null); ulong HeadCountMax
    and HeadCountKvMin (the largest <arch>.attention.head_count and the
    smallest <arch>.attention.head_count_kv, taking the maximum or minimum of a
    per-layer array, and 1 when the key is absent).

LOOKUPS: Has(key), GetValue(key), GetExactValue(key), GetString(key, default),
GetUInt64(key, default), GetBool(key, default), GetTensor(name),
GetTensors(prefix).

KEY QUALIFICATION is the rule to remember. GetValue, and everything built on
it, qualifies a key by the file's architecture first: "general.*" and
"tokenizer.*" are used as written, "split.*" is tried as written and then
qualified, and everything else is looked up as "<architecture>.<key>". So on a
llama file GetUInt64("block_count") reads llama.block_count while
GetExactValue("block_count") reads nothing -- use the latter for a full key.

GgufValue carries GgufValueType Type (Array for an array), bool IsArray,
    GgufValueType ArrayElementType, bool IsOmittedArray, long ArrayLength
    (correct even when omitted) and object RawValue (a boxed scalar, a string,
    or a typed array such as int[] or string[]; null for an omitted array).
    AsString() gives an empty string when the value is not a string;
    AsInt64(), AsUInt64() and AsDouble() give 0 when it is not a signed
    integer, an unsigned integer or a float respectively; AsBoolean() gives
    false when it is not a boolean; TryGetInt64, TryGetUInt64 and TryGetDouble
    report which family it belongs to. AsStringArray() and AsBooleanArray()
    return null unless the value is exactly string[] or bool[];
    AsInt64Array() widens sbyte[], short[], int[] and long[], AsUInt64Array()
    widens byte[], ushort[], uint[] and ulong[], AsDoubleArray() widens
    float[] and double[], and each returns null for anything else. ToString()
    prints the scalar in the invariant culture, or "[n items]".

CONVERSION HAPPENS ONLY WITHIN A FAMILY, as in Ollama's reader: a value of the
wrong family reads back as the zero of the requested type rather than throwing.
GgufMetadata.GetUInt64 is the one bridge, taking a non-negative signed value.

TENSORS
    GgufTensorInfo: string Name; ulong Offset (from the start of the tensor
    data); IReadOnlyList<ulong> Shape (fastest moving first, at most four);
    GgufTensorType Type; long ElementCount (-1 when the shape overflows); long
    ByteCount (-1 when the type has no defined size, the first dimension is not
    a whole number of blocks, or the count overflows); bool IsValid (a name and
    a usable byte count); string TypeName (lowercase ggml name).

    GgufTensorTypes (static): GetName, GetBlockSize (values per block; unknown
    ids fall into the 256-value k-quantization group), GetTypeSize (bytes per
    block, or 0), GetBytesPerElement. GgufTensorType is the ggml type enum,
    F32 = 0 through Q1_0 = 41; GgufFileType is the general.file_type enum,
    F32 = 0 through Q1_0 = 40 plus Unknown = 1024, and GgufFileTypes.GetName
    turns it into the name llama.cpp prints ("Q4_K_M", "BF16"), or "unknown".

LIMITS AND FAILURES. Strings are capped at 16 MB, arrays at 64 M items, tensors
at four dimensions, and the version must be at least 1. Anything the reader
cannot interpret -- a wrong magic, a truncated header, an unknown value type, a
zero or non-integer alignment, a tensor range outside the file, an arithmetic
overflow -- is a GgufFormatException saying which, and a bad file never yields
a partially filled GgufMetadata.


THE DATA TYPES
==============
Their JSON shapes are exactly Ollama's, which is what makes a store directory
interchangeable.

ModelManifest   int SchemaVersion (always 2); string MediaType (always
    MediaTypes.Manifest); ModelLayer Config; List<ModelLayer> Layers in
    manifest order; long GetTotalSize() over the layers plus the config.

ModelLayer      string MediaType (a MediaTypes constant); string Digest
    ("sha256:<64 hex>"); long Size; string From (the source model or imported
    file the layer came from, else null); string Name (a safetensors tensor
    name, else null). Constructors: (), (mediaType, digest, size).

MediaTypes (string constants). Manifest is
    application/vnd.docker.distribution.manifest.v2+json and Config is
    application/vnd.docker.container.image.v1+json; the rest are
    application/vnd.ollama.image.<name>: Model (GGUF weights), Projector
    (vision or audio encoder), Adapter (LoRA), Draft (speculative decoding),
    Template (prompt template text), Prompt (the legacy template name -- READ
    but never written), System, Params (JSON parameters), Messages (JSON
    message array), License, Tensor (safetensors; refused), Json (safetensors
    config) and Embed (deprecated; read and ignored).

ModelConfig (Ollama's ConfigV2)   string ModelFormat ("gguf" for everything
    this library creates), ModelFamily, ModelType (parameter count as a human
    string), FileType (quantization), Renderer, Parser, Requires;
    List<string> ModelFamilies and Capabilities (the latter usually null); int
    ContextLength and EmbeddingLength (0 when not declared here); and
    Dictionary<string, JsonElement> AdditionalProperties -- everything this
    library does not model, preserved verbatim so a config round-trips.

ModelParameters (the params layer, typed)
    int?   NumKeep Seed NumPredict TopK RepeatLastN NumCtx NumBatch NumGpu
           MainGpu NumThread DraftNumPredict
    float? TopP MinP TypicalP Temperature RepeatPenalty PresencePenalty
           FrequencyPenalty
    bool?  UseMmap;  List<string> Stop
    Dictionary<string, JsonElement> AdditionalParameters -- every parameter
           this library does not model, preserved verbatim; this is where
           deprecated names such as mirostat land.
    An unset parameter is null and is omitted from the JSON. Which parameters a
    runner honours is the runner's business; the store only carries them.

ModelMessage    string Role ("system", "user" or "assistant"); string Content.
    Constructors: (), (role, content).

PullProgress    string Status; string Digest (null for a status-only report);
    long TotalBytes, CompletedBytes; double Percent (0 to 100, or 0 when the
    total is unknown); ToString() gives the status, plus "completed/total" for
    a layer report. Constructors: (status) and (status, digest, totalBytes,
    completedBytes).

ModelSummary    ModelName Name; string DisplayName; string Digest (of the
    MANIFEST FILE, hex, no prefix); long Size; DateTimeOffset ModifiedAt;
    ModelConfig Config.

CreateOptions   string BaseDirectory -- the directory relative FROM and ADAPTER
    paths resolve against; null means Environment.CurrentDirectory.

ModelCapability (enum)  Completion, Tools, Insert, Vision, Audio, Embedding,
    Thinking.


BUNDLES
=======
A BUNDLE is a model that is not a GGUF file on an Ollama-protocol registry: a
Hugging Face file repository, the objects under a public storage bucket prefix,
a folder of files that arrived some other way. The registry protocol cannot
serve any of it - Hugging Face answers "Repository is not GGUF or is not
compatible with llama.cpp" for such a repository - so the files are fetched
over plain HTTPS instead and stored in the SAME content-addressed layout as
every GGUF model: one blob per file, one layer per file carrying the
publisher's own relative path, one config layer recording where the files came
from. ListAsync, ExistsAsync, ShowAsync, ResolveAsync, CopyAsync, DeleteAsync
and PruneAsync work on a bundle unchanged, and MaterializeAsync writes its
files back out as the tree its publisher wrote.

A BUNDLE PULL IS ALWAYS ASKED FOR. PullAsync(name) - the overload with no
options - means exactly what it has always meant, the registry protocol and
nothing else, and a name that protocol cannot serve fails there as it always
has. There is no sniffing and no fallback. The explicit overload is:

    IAsyncEnumerable<PullProgress> PullAsync(string name, PullOptions options,
                                             CancellationToken)

Options of null, and PullOptions.ForRegistry(), both run the registry pull
described above, down to the same code path.

NAMES ARE THE EXISTING GRAMMAR; nothing new parses:

    hf.co/<namespace>/<repository>             tag latest, revision "main"
    hf.co/<namespace>/<repository>:<revision>  tag = a branch, tag or commit
    <host>/<namespace>/<model>:<tag>           any host, for a list of
                                               addresses - the host is a label
                                               here, nothing is requested from
                                               it unless a file's address
                                               names it
    local/<namespace>/<model>:<tag>            the convention for an imported
                                               folder

PullOptions

    PullSource Source                Registry (the default) | HuggingFaceFiles
                                     | FileList
    string Repository                <namespace>/<repository>; null takes it
                                     from the name, whose namespace and model
                                     part spell the same thing
    string Revision                  a branch, tag or commit; null takes the
                                     name's tag, and the tag "latest" means
                                     DefaultRevision
    IReadOnlyList<BundleFile> Files  the files of a FileList pull; never null,
                                     an unset list reads as empty
    FileFilter Filter                never null; unset reads as
                                     FileFilter.Default
    bool RequireHashes               false; true refuses a file the source
                                     states no hash for
    const  DefaultRevision "main"
    static ForRegistry(), ForHuggingFace(repository, revision, filter),
           ForFileList(files, filter)

PullSource is Registry, HuggingFaceFiles or FileList. It chooses the listing
step and the wire protocol and nothing else: every source ends in the same
blobs and the same manifest layout.

THE PROGRESS STREAM OF A BUNDLE PULL, IN ORDER

    "listing <repository>"      once, before anything is fetched; a list of
                                addresses reports "listing file list"
    "pulling <path>"            one stream of reports per file, in the order
                                the source listed them, carrying the
                                PUBLISHER'S RELATIVE PATH rather than the
                                twelve hexadecimal characters a registry layer
                                report carries. A file whose source stated a
                                sha256 carries that digest from the first
                                report; a file whose source stated none
                                carries its digest only in the final report,
                                because until the bytes are all there nothing
                                knows what it is
    "verifying sha256 digest"   once, when every blob the new manifest will
                                name is confirmed present at the recorded size
    "writing manifest"          once, before the manifest is written
    "success"                   always the last report of a successful pull

WHAT THE MANIFEST AND THE CONFIG RECORD. Every file becomes a layer whose
media type is MediaTypes.BundleFile ("application/vnd.codebrix.model.file")
and whose Name is the publisher's relative path. The config layer carries:

    model_format    what the file names say the weights are: "onnx",
                    "pytorch", "tensorflow-checkpoint", or "mixed" when more
                    than one kind is present. When no file decides, the source
                    does: "huggingface", "files", "imported", or "derived" for
                    a bundle this library produced itself
    model_family    the model part of the name
    source          "hf.co", "url" or "local"
    repository      <namespace>/<repository>, the folder that was imported, or
                    null
    revision        the COMMIT a Hugging Face listing resolved to - never the
                    branch or tag that was asked for - or null
    licenseId       the identifier the source states, or null
    licenseSource   the address that statement was read from, or null
    pulledAt        when the pull ran, ISO-8601 in UTC

A property the source said nothing about is written as a JSON null rather than
left out, so a reader can tell "the source stated nothing" from "this config
was written by something that did not know about the property".

RE-PULLS AND REVISIONS. A Hugging Face listing resolves the branch or tag once
and pins every address to the commit it resolved to, so a pull cannot take half
of one revision and half of the next. Pulling the same name again re-lists,
re-verifies and rewrites the manifest: when the revision has not moved every
file whose source states a sha256 is already in the store and costs no request
at all, and when it has moved the new manifest replaces the old one and the
blobs only the old one referenced are removed. Compare the config's revision
before and after to tell "unchanged" from "moved".


THE SOURCES
===========
A source says what a bundle holds before anything is downloaded. Each one
implements IBundleSource:

    Task<BundleListing> ListAsync(CancellationToken)

and each is IDisposable, because each holds an HTTP client of its own. You do
not need a source to pull - PullAsync builds the one the options name - but
listing first is how you report the size of a pull before starting it.

BundleListing   string RepositoryId (what was listed, or null for a plain
    list); string ResolvedRevision (the commit, or null for a source with no
    commits); LicenseRecord License, with the shortcuts LicenseId and
    LicenseSource; IReadOnlyList<BundleFile> Files, ALREADY FILTERED; long
    TotalBytes, the sum of the stated sizes, which is a floor rather than a
    promise when a file states none.

HuggingFaceHubSource
--------------------
    new HuggingFaceHubSource(repository, revision, filter, options)

reads the Hub's own HTTP API - no Python, no Hub client library. The repository
document gives the commit to pin and the licence the publisher states; the
recursive tree document gives every file with its size, its git object
identifier and, for a file kept in large-file storage, the SHA-256 of its
content. A tree that arrives in pages is followed to the end.

    const   HubHost "huggingface.co", ShortHubHost "hf.co",
            DefaultRevision "main"
    props   Repository, Revision, Filter
    static  IsHubHost(host), NormalizeRevision(revision),
            BuildFileUrl(repository, revision, path)
    method  ListAsync(CancellationToken), Dispose()

  - THE COMMIT IS PINNED. NormalizeRevision turns null, empty and the store's
    default tag "latest" into "main"; the listing then resolves that to a
    commit and names the commit in every address it builds.
  - HASHES: the sha256 of a large-file entry is the hash of the CONTENT and is
    what a download is held to. The git object identifier is recorded as
    BundleFile.GitSha1 and NEVER verified against, because it hashes the
    object as git stores it, not the bytes that arrive. A small file that the
    Hub keeps in git itself therefore states no verifiable hash at all.
  - GATED OR PRIVATE repositories are refused with a message that says what to
    do, unless ModelStoreOptions.BearerToken holds an access token. A token
    that exists travels with the FIRST request here, because the Hub answers
    an anonymous request for a private repository with a plain 404 rather than
    a challenge; it is offered to the Hub host and to no other host, ever, and
    never follows the redirect to the content delivery host that serves the
    bytes.
  - URL ENCODING: every path segment of the repository, the revision and the
    file path is escaped, so a space or a character outside the ASCII range in
    a publisher's file name survives the trip.

HttpFileListSource and GoogleCloudStorageListing
-----------------------------------------------
    new HttpFileListSource(files, filter)
    new HttpFileListSource(files, filter, options)

lists a bundle that is nothing but a list of addresses: you already know what
the files are, and this fills in what you do not. A file whose size you stated
costs no request. A file whose size is unknown is asked for with a HEAD and,
when the server refuses a HEAD, with a request for its first byte, whose
Content-Range states the whole length; the MD5 a storage bucket sends in a
header is taken while the server is answering. A server that will say neither
is a RegistryException.

    static Task<IReadOnlyList<BundleFile>> GoogleCloudStorageListing.ListAsync(
        bucket, prefix, handler, CancellationToken)
    static string GoogleCloudStorageListing.BuildObjectUrl(bucket, key)
    const  GoogleCloudStorageListing.StorageHost "storage.googleapis.com"

turns a public bucket prefix into that list. A bucket speaks no model protocol
at all: it answers an XML listing of the objects under a prefix, paged, and
each object answers a HEAD with the hashes the bucket holds for it. The prefix
is stripped off the front of every key, so listing "models/example-model/"
yields "checkpoints/<name>" rather than the whole key, and a key that ends in a
slash is the placeholder a console makes for a folder and is left out. The
handler argument is the HttpMessageHandler the requests go through, null for a
default one.

BundleFile
----------
One file of a bundle, in the one shape every source describes a file in:

    ctor    (path, url), (path, url, size),
            (path, url, size, sha256, md5, gitSha1)
    props   Path, Uri Url, Size, Sha256, Md5, GitSha1, FileName, HasSize,
            HasVerifiableHash
    const   UnknownSize (-1, because 0 is a valid size)
    methods WithSize(size), WithMd5(md5), ToString()

Path is always relative and always spelled with forward slashes: it is the path
the publisher uses and the path MaterializeAsync lays out on disk. A backslash
is read as a separator and rewritten, a leading "./" is dropped, and a path
that is rooted, that names a drive or that walks up with ".." is an
ArgumentException - a bundle must never be able to write outside the directory
it is materialized into. Sizes and hashes are what the SOURCE STATED, not what
was measured: Size is UnknownSize when the source did not say, and each hash is
null when the source did not state that hash. A hash that is not hexadecimal of
the length its algorithm calls for is refused; the case is normalized to lower
case.

FileFilter
----------
Which files are pulled: a set of include patterns and a set of exclude patterns
over the publisher's relative path. A path is kept when it matches at least one
include pattern, or there are no include patterns at all, AND matches no
exclude pattern.

    ctor    (), (includeGlobs, excludeGlobs)
    static  Default, ExcludeTrainingArtifacts
    props   IncludeGlobs, ExcludeGlobs, KeepsEverything
    methods WithIncludes(params string[]), WithExcludes(params string[]),
            ShouldInclude(string path), ShouldInclude(BundleFile file),
            Apply(IEnumerable<BundleFile>)

THE GLOB RULES: patterns match the WHOLE relative path, case-sensitively, with
forward slashes as separators. "*" is any run of characters within one path
segment, "**" is any run including separators, "?" is one character within a
segment, and a leading "**/" also matches no directory at all, so "**/*.pt"
matches "model.pt" as well as "weights/model.pt".

ONE RULE STANDS ABOVE THE PATTERNS: a file whose own name is LICENSE,
LICENSE.<anything>, README or README.<anything> is ALWAYS KEPT, whatever the
patterns say and whatever the source, because the licence and the readme are
what a later ShowAsync reports the publisher's terms from. That comparison
ignores case.

FileFilter.Default keeps everything. FileFilter.ExcludeTrainingArtifacts is
opt-in and leaves out what a training run wrote and nothing else needs: the
"logs" tree, every TensorBoard event file wherever it sits, and optimizer state
("optimizer.pt" and any "optimizer*.pt" below the root). Weights,
configuration, tokenizer files and any ONNX files the publisher shipped are all
kept. The filter a source uses when nothing is said is Default, so a pull takes
the publisher's repository as it stands unless you ask for less.

WHAT IS VERIFIED, FILE BY FILE
------------------------------
    stated sha256   the download is held to it, and the blob is named by it
                    before a byte arrives - which is what lets a file already
                    in the store cost no request at all
    stated md5      verified as the bytes stream past, when that is the only
                    hash the source states
    nothing stated  downloaded, and the store computes the SHA-256 itself,
                    names the blob by it and records it - so a second pull
                    verifies against the first

PullOptions.RequireHashes turns "nothing stated" into a ModelManagerException
naming the file, before that file is fetched. A mismatch against either stated
hash is a DigestMismatchException, the bad bytes are already gone, and no
manifest is written.


DESCRIBING A MODEL
==================
BundleDefinition is the vocabulary you write your own models down in - name,
source, what to fetch, what the publisher states about the licence - so that a
catalogue of models is data rather than code. THE LIBRARY SHIPS NO DEFINITIONS
AND NAMES NO PARTICULAR MODEL ANYWHERE.

    static  ForHuggingFace(name, repository, revision, filter, license, notes)
            ForFileList(name, files, filter, license, notes)
            ForRegistry(name, license, notes)
    ctor    (name, source, repository, revision, files, filter, license, notes)
    props   Name, Source, Repository, Revision, Files, Filter, License, Notes
    method  ToPullOptions()

A definition is checked when it is built, so a definition that exists is one a
pull can be started from: the name parses in the store's own grammar (an
InvalidModelNameException if not), a Hugging Face definition names a repository
as <namespace>/<repository>, and a file-list definition carries at least one
file. A ForRegistry definition exists so that one catalogue can hold every kind
of model, GGUF models included.

ToPullOptions() carries the source, the repository, the revision, the files and
the filter into a PullOptions. THE LICENCE AND THE NOTES ARE NOT CARRIED: they
describe the bundle, and a pull reports what the SOURCE states rather than what
a definition claims. That is what makes a definition's licence field worth
asserting against in a test - when a publisher changes a tag, the two disagree
and you find out.

LicenseRecord is what a source states about a licence, and nothing more:

    ctor    (licenseId), (licenseId, licenseSource, note)
    static  None
    props   LicenseId, LicenseSource, Note, IsStated
    method  ToString()

LicenseId is the identifier as the source spells it, for example "apache-2.0"
or "mit"; LicenseSource is the address it was read from - a repository page, a
licence file; Note is anything a human should know that the identifier does
not say. None states nothing, which is what a source with no licence
information reports and what every model pulled from an Ollama-protocol
registry reports. THE LIBRARY APPLIES NO RULE OF ITS OWN: it never refuses a
pull over a licence or the absence of one, and whether a set of model files may
be used, shipped or published is your decision to make from what is reported.


MATERIALIZING
=============
The store keeps a bundle's files content addressed, under names that say what
they are and not what they are called. MaterializeAsync puts the publisher's
names back:

    Task<IReadOnlyList<string>> MaterializeAsync(string name,
        string targetDirectory, MaterializeOptions options = null,
        CancellationToken cancellationToken = default)

It returns the absolute paths it wrote, in manifest order. The target directory
is created if it is missing, and so is every directory a publisher's path
implies.

MaterializeOptions   MaterializeLink Link (default Hardlink); bool Overwrite
    (default false).

MaterializeLink

    Hardlink   a second directory entry for bytes already on disk: no disk
               space, no copying time. A link the platform or the file system
               will not make - across a volume, on a file system without hard
               links - BECOMES A COPY BY ITSELF, so this is always usable
    Copy       the bytes, which costs the space and the time and leaves a file
               that is independent of the store: deleting the model afterwards
               leaves it standing
    Symlink    a symbolic link to the blob. It is never chosen by itself and
               NEVER falls back to anything: a caller that asks for symbolic
               links wants the store's own file to be what a reader ends up
               at, and quietly writing a copy instead would hide that it did
               not happen. A link that cannot be made is a
               ModelManagerException saying so

OVERWRITE is false by default: a file already at one of the target paths fails
the call and nothing further is written, so a directory that holds work of its
own is never overwritten by accident. A DIRECTORY already at a target path
fails the call whatever Overwrite says.

A GGUF MODEL IS REFUSED: a model with no publisher file tree - which is every
model pulled from an Ollama-protocol registry - is an InvalidOperationException
saying so. ResolveAsync names the files of such a model instead.

PATH SAFETY. Every layer name is checked against the target directory before
anything is written: a name that is rooted, that names a drive, that walks up
with ".." or that otherwise lands outside the target directory is a
ModelManagerException rather than a file written where it was told to. A layer
name comes from a manifest on disk, which a bundle pull wrote but which nothing
stops a third party from editing.


IMPORTING A FOLDER
==================
    Task ImportBundleAsync(string name, string directory,
        ImportOptions options = null,
        CancellationToken cancellationToken = default)

walks a folder to its full depth, hashes every file the filter keeps, stores it
as a blob and records it as a layer carrying its path relative to the folder.
This is the route for anything a publisher keeps behind a sign-in - fetch it
however it has to be fetched, then hand the folder over - and for any set of
files you produced yourself. The paths are sorted, so two imports of one folder
write the same manifest.

ImportOptions   FileFilter Filter (never null; unset keeps every file the walk
    finds, and a licence or a readme is kept whatever it says); bool Link
    (default false); LicenseRecord License (null means: if the folder holds a
    LICENSE file, record that file as the place the terms are stated and state
    no identifier, since what a licence file says is for a human to read).

LINK hard-links each file into the blobs directory instead of copying it. That
costs no space and no time and is right for a folder about to be thrown away,
and it falls back to a copy by itself when the file system will not link. What
it does mean is that the file and the blob are then the SAME BYTES ON DISK:
editing the file in place afterwards edits the blob, which is why copying is
what an unset option does.

THE HOST "local" IS THE CONVENTION for an imported bundle -
local/<namespace>/<model>:<tag> - because the name grammar accepts it like any
other host and no registry can ever be confused with it. Nothing enforces it;
any name that parses is accepted.

A directory that does not exist, and a directory that holds no file the filter
keeps, are both a ModelManagerException.


RESOLVING A BUNDLE
==================
ResolveAsync answers for a bundle exactly as it does for a GGUF model, with two
members that carry the difference:

    string Format                        the config layer's model format:
                                         "gguf" for a model pulled from an
                                         Ollama-protocol registry, and one of
                                         "huggingface", "pytorch", "onnx",
                                         "tensorflow-checkpoint", "mixed",
                                         "files", "imported" or "derived" for a
                                         bundle.
                                         null when the config states none and
                                         no GGUF weights layer says otherwise
    IReadOnlyList<ResolvedFile> Files    the files of a bundle, in manifest
                                         order. EMPTY, never null, for a GGUF
                                         model, whose files are named by
                                         ModelPath and the lists beside it

ResolvedFile   string Name (the publisher's relative path, with forward
    slashes); string BlobPath (the absolute path of the blob the content lives
    in); long Size; string Digest ("sha256:<hex>"). ToString() gives the path
    and the size.

FOR A BUNDLE, ModelPath IS NULL and ModelShardPaths, ProjectorPaths,
AdapterPaths and DraftPath are empty: there is no GGUF weights layer to name.
Read the files through Files, or materialize them. As for any resolve, paths
are computed from digests and no file is opened, so check File.Exists if you
cannot rule out a blob deleted by hand.


DERIVED BUNDLES
---------------
A DERIVED BUNDLE is a bundle THIS LIBRARY produced, rather than one it
obtained: the result of exporting a model to ONNX, or of reducing one. It is an
ordinary bundle in every respect - it lists, it shows, it resolves, it
materializes, it copies and it deletes - with four things in its config that an
obtained bundle has none of, which ShowAsync reports as properties of their
own:

    string DerivedFrom      the model it was made from, spelled exactly as
                            that model is stored, or null when the bundle was
                            not derived
    string Tool             what did the work: an export records
                            "onnxruntime-genai", "optimum", or "publisher" when
                            the files are the publisher's own and this library
                            only registered them; a reduction records
                            "onnxruntime" when ONNX Runtime's own tools ran and
                            "CodeBrix.Ollama.ModelManager" when the managed
                            engine did
    string ToolVersion      what that tool reports about itself - this
                            library's own assembly version for the managed
                            engine - or null for a pass-through, which ran
                            nothing
    IReadOnlyDictionary<string, string> Settings
                            the options that work was given - an export records
                            the route, the precision and whether remote code
                            was allowed; a reduction records the mode, the
                            engine asked for and the engine used, whether the
                            graph was prepared, the block-wise settings and the
                            files that were reduced - or null when the bundle
                            was not derived

The same values, and the timestamps beside them, are in the config layer under
the names on ModelConfigKeys, which is a public class of string constants for
exactly this purpose:

    ModelConfigKeys.Source / Repository / Revision / LicenseId / LicenseSource
    / PulledAt                      written by every bundle pull and import
    ModelConfigKeys.DerivedFrom / Tool / ToolVersion / Settings / DerivedAt
                                    written only by a derived bundle

    ModelInfo info = await store.ShowAsync("hf.co/skytnt/midi-model:onnx");
    if (info.DerivedFrom != null)
    {
        Console.WriteLine($"{info.Tool} {info.ToolVersion} made this from " +
                          info.DerivedFrom);
        Console.WriteLine(info.Config
            .AdditionalProperties[ModelConfigKeys.DerivedAt].GetString());
    }

THE LICENCE IS CARRIED OVER from the source, unchanged, because deriving a file
from a model does not change whose terms it is under. As everywhere else in
this library a licence is REPORTED, never enforced.

A DERIVED BUNDLE IS A SNAPSHOT. Nothing links it to its source afterwards: the
source can be deleted, replaced or re-pulled at another revision and the
derived bundle stays exactly as it was, still naming what it came from. Export
or reduce again when the source changes.

DELETING EITHER ONE LEAVES THE OTHER ALONE. Blobs are shared by digest, and a
blob is removed only once no manifest in the store names it.

SHOWING AND LISTING
===================
ShowAsync adds two members to what it reports for any model, and fills a third
one differently:

    LicenseRecord License   what the SOURCE stated, as the config layer
                            recorded it when the model was pulled or imported:
                            the identifier and the address it was read from.
                            LicenseRecord.None when nothing is stated, which is
                            what every model pulled from an Ollama-protocol
                            registry reports. NEVER null
    string Format           the config layer's model format, exactly as
                            described for ResolvedModel.Format above
    IReadOnlyList<string> Licenses
                            every licence LAYER's text, as before, followed by
                            THE TEXT OF EVERY LICENSE FILE THE BUNDLE SHIPS -
                            which is why a licence file is always pulled,
                            whatever the filter says

ModelInfo.Metadata is null for a bundle and ProjectorMetadata is empty: nothing
tries to read a GGUF header that is not there, so ShowAsync on a bundle opens no
weights file at all - the only blobs it reads are the small text ones, the
licence files among them.

ListAsync shows bundles beside GGUF models, newest first, and
ModelSummary.Config.ModelFormat is what tells them apart - "gguf" against one
of the bundle formats. Only the manifest and the small config blob are read, as
always.

COPY, DELETE, EXISTS AND PRUNE ARE THE SAME CODE PATHS for a bundle as for
anything else: CopyAsync gives it a second name and shares every blob,
DeleteAsync removes the manifest and then every blob no remaining manifest
references, and PruneAsync sweeps what nothing references at all.

INTERCHANGE WITH OLLAMA SURVIVES A BUNDLE. Its manifest sits in the same
manifests tree and its blobs in the same blobs directory, and its file layers
carry a media type of this library's own. Ollama's layer walk carries a media
type it does not know and ignores it, so an Ollama install sharing the
directory lists the model, reports its size and removes it, and refuses to run
it - which is the right answer, since there is nothing there for it to load.


PYTHON
======
ONE CORNER of this library needs CPython, and it is not the corner most
consumers use. Obtaining a model, listing, showing, resolving, materializing,
importing a folder, parsing a Modelfile, reading GGUF metadata, passing a
publisher's own ONNX graphs through and reducing a graph with the managed
engine are pure managed code that will never touch Python, on a machine that
has none. CONVERTING A CHECKPOINT to ONNX, and preparing a graph for a
quantizer, are the work that only exists in Python.

The package declares exactly ONE NuGet dependency, CodeBrix.Python, and it is
INERT. Every type of it this library names lives in a single internal class, so
the runtime has no reason to load that assembly until a Python feature actually
runs. A consumer whose work never reaches one never loads it -- there are tests
in this repository that run a whole import, list, resolve and materialize cycle,
a whole pass-through export, and a whole reduction through the managed engine,
each in a process of its own, and assert that no assembly of that name is loaded
afterwards.

FEATURE BY FEATURE, AND EXACTLY. The left column is what you call; the right is
what the machine must have for that call, and "nothing" means no interpreter is
looked for, started or loaded. The reduction rows assume the default
ReduceEngine.Auto except where they say otherwise; ReduceEngine.Managed needs
nothing whatever the mode, and refuses what it does not cover rather than
reaching for Python:

  PullAsync, ImportBundleAsync,       NOTHING
  ListAsync, ExistsAsync, ShowAsync,
  ResolveAsync, MaterializeAsync,
  CopyAsync, DeleteAsync, PruneAsync,
  CreateAsync, Modelfile.Parse,
  GgufMetadata.ReadAsync

  ExportToOnnxAsync                   NOTHING - a pass-through converts
    Route = PublisherOnnx               nothing and starts nothing
  ExportToOnnxAsync                   CPython + onnxruntime_genai, torch,
    Route = GenAiBuilder                transformers, onnx
  ExportToOnnxAsync                   CPython + optimum, onnx, torch,
    Route = Optimum                     transformers
  ExportToOnnxAsync                   whatever the route it picks needs, and
    Route = Auto                        it picks PublisherOnnx - which needs
                                        nothing - for a source that already
                                        ships .onnx files

  ReduceOnnxAsync                     NOTHING - the managed engine, and Auto
    WeightOnlyInt4, WeightOnlyInt8      never even asks whether Python is there
  ReduceOnnxAsync                     NOTHING - the managed engine, because a
    DynamicInt8, on a graph that        prepared graph carries the shapes that
    PreprocessOnly has prepared         mode reads
  ReduceOnnxAsync                     CPython + onnx, onnxruntime - the
    DynamicInt8, on a graph nobody      preparation pass runs there. With
    has prepared                        neither the call fails and says to
                                        prepare the model or pick a
                                        weight-only mode
  ReduceOnnxAsync                     CPython + onnx, onnxruntime - preparing
    PreprocessOnly                      a graph is that runtime's own work and
                                        is not ported
  ReduceOnnxAsync                     CPython + onnx, onnxruntime, whatever
    Engine = Python                     the mode: you asked for that engine

  PythonSupport.Check                 nothing to CALL it - it NEVER throws and
                                      reports a machine with no CPython as
                                      exactly that. With modules named it
                                      starts an interpreter to import them
  PythonSupport.Require               a CPython with the modules named, or it
                                      throws the exception that says which
                                      part is missing
  PythonSupport.Shutdown              nothing; on a machine that never started
                                      an interpreter it does nothing

What Python IS for in this library is the tooling that only exists in Python:
converting a model to another format, and preparing a graph for a quantizer.
Each feature names the modules it imports and checks for them BEFORE it does
anything, so a machine that cannot run one is told so in a second; finding that
out in advance is what PythonSupport.Check is for. MAKING AN EXISTING GRAPH
SMALLER IS NOT ONE OF THEM: the weight-only modes, and dynamic quantization of
a prepared graph, are this library's own managed code.

WHAT THIS PACKAGE DOES NOT DO ABOUT PYTHON: it does not ship, install or
download CPython, and it does not install pip modules. A CPython SHARED LIBRARY
must already be on the machine -- libpython3.XX.so, python3XX.dll or
libpython3.XX.dylib -- and the modules a feature needs must already be
installed in the interpreter that will run it.

SAYING WHERE CPYTHON IS
-----------------------
    public sealed class PythonOptions
        string VirtualEnvironment      the venv folder - the one holding
                                       pyvenv.cfg, bin/python and
                                       site-packages. null by default.
        string LibraryPath             the full path of the CPython shared
                                       library. null by default.
        const string VirtualEnvironmentVariable   "CODEBRIX_OLLAMA_PYTHON_VENV"
        const string LibraryPathVariable          "PYTHONNET_PYDLL"

ModelStoreOptions carries one, and it is never null:

    var options = new ModelStoreOptions();
    options.Python.VirtualEnvironment = "/home/me/venvs/models";
    using var store = new ModelStore(options);

Every PythonSupport call takes a PythonOptions of its own, and null means "all
defaults", so a consumer who sets the environment variables need pass nothing:

    PythonSupportReport report = PythonSupport.Check(null, "onnx");

THE RESOLUTION ORDER, and CODE ALWAYS BEATS AN ENVIRONMENT VARIABLE:

    the virtual environment      1. PythonOptions.VirtualEnvironment
                                 2. CODEBRIX_OLLAMA_PYTHON_VENV
                                 3. PYTHONNET_VENV or VIRTUAL_ENV -- the
                                    environment the PROCESS was launched in
                                 4. none: the interpreter runs out of its own
                                    installation
    the shared library           1. PythonOptions.LibraryPath
                                 2. PYTHONNET_PYDLL
                                 3. the base interpreter the virtual
                                    environment's pyvenv.cfg records - its
                                    home and version keys name the
                                    installation the library is looked for in
                                 4. none: nothing named one

A report says which of the four answered, in LibrarySource (Code,
EnvironmentVariable, VirtualEnvironment, NotFound) and VirtualEnvironmentSource
(Code, EnvironmentVariable, Inherited, None). There is a fifth LibrarySource,
Host: a host application that started the interpreter before this library
looked and named its own shared library in code. Nothing here resolved that
path, so the report carries the path the RUNNING interpreter states about
itself and says Host; Owner says Host as well.

A VIRTUAL ENVIRONMENT IS THE WAY TO DO THIS. Naming one is what puts its
site-packages on the interpreter's path, and it is where a distribution that
refuses a system-wide pip install expects a module to go. No path manipulation
is needed or wanted: name the folder and the interpreter reads its pyvenv.cfg
for itself.

ASKING WHETHER IT WILL WORK
---------------------------
    PythonSupportReport PythonSupport.Check(PythonOptions options,
                                            params string[] requiredModules)

NEVER THROWS. A machine with no CPython at all produces a report that says so.

    PythonSupportReport
        string LibraryPath                 the library that was resolved, or
                                           null; once an interpreter is
                                           running, the one it really loaded
        PythonLibrarySource LibrarySource  where that path came from
        bool LibraryLoads                  the operating system loaded it (and
                                           freed it again; no interpreter was
                                           started to find out)
        Version Version                    the CPython version, or null
        bool IsSupportedVersion            inside the range the embedding layer
                                           supports
        string VirtualEnvironment          the environment in effect, or null
        PythonVirtualEnvironmentSource VirtualEnvironmentSource
        bool IsInitialized                 an interpreter is running
        PythonEngineOwner Owner            None | ModelManager | Host
        IReadOnlyList<PythonModuleReport> Modules
                                           Name, IsInstalled, Error - one entry
                                           per module asked about, in order
        IReadOnlyList<string> Problems     human sentences, empty when nothing
                                           is wrong
        bool IsUsable                      the library loads, the version is
                                           supported, and every module asked
                                           about imports

TWO KINDS OF CHECK, and the difference matters:

  - Check WITH NO MODULES touches no interpreter and starts none. It is a file
    check, a load-and-free of the shared library and a version comparison, and
    it is cheap enough to call on a settings page.
  - Check WITH MODULES STARTS AN INTERPRETER, under the ownership rules below,
    because importing a module is the only way to know whether it is there.
    That is what a real feature would do anyway. It is not cheap the first
    time, and the interpreter it starts lasts for the life of the process.

    PythonSupportReport report =
        PythonSupport.Check(options, "onnx", "onnxruntime");
    if (!report.IsUsable)
    {
        foreach (string problem in report.Problems)
        {
            Console.WriteLine(problem);
        }
    }

DEMANDING THAT IT WORK
----------------------
    void PythonSupport.Require(PythonOptions options, string feature,
                               params string[] requiredModules)

This is what a feature calls before it runs Python, and what a consumer can
call to fail early with a message worth showing. `feature` is a PHRASE, and it
ends every message: "exporting to ONNX", "reducing an ONNX model".

    PythonNotAvailableException        no CPython shared library was found, the
                                       one that was found will not load, or its
                                       version is outside the supported range.
                                       string Feature.
    PythonModuleNotInstalledException  the interpreter is fine; a module is not
                                       installed in it. string Feature, string
                                       ModuleName, and the message carries the
                                       COMMAND that installs it in THAT
                                       interpreter - the virtual environment's
                                       own pip when there is one, a plain
                                       pip install when there is not.
                                       PythonModuleNotInstalledException
                                       .InstallCommand(module, venv) builds the
                                       same line for your own messages.
    PythonScriptException              a script this package ships failed for
                                       some other reason. string Feature,
                                       string ScriptName, string PythonMessage.

All three derive from ModelManagerException.

    try
    {
        PythonSupport.Require(
            options, "exporting to ONNX", "onnx", "onnxruntime");
    }
    catch (PythonModuleNotInstalledException missing)
    {
        //"The Python module 'onnxruntime' is not installed in the CPython at
        // /usr/lib/x86_64-linux-gnu/libpython3.13.so (virtual environment
        // /home/me/venvs/models). Install it there with:
        // /home/me/venvs/models/bin/pip install onnxruntime It is needed for
        // exporting to ONNX."
        Console.WriteLine(missing.Message);
    }

WHO OWNS THE INTERPRETER
------------------------
There is ONE CPython interpreter per process, it cannot be restarted once it
has been shut down, and it belongs to whoever started it. This library decides
ownership the first time it looks, and the contract runs BOTH WAYS.

  THIS LIBRARY OWNS IT -- nothing had started an interpreter when the first
  Python call was made. It applies PythonOptions, starts the interpreter, hands
  the interpreter lock back so that a run may happen on any thread, and asks
  the embedding layer to BOUND its own process-exit shutdown, which is the
  safety net for an application that never disposes anything: a process that
  simply ends still ends. PythonSupport.Owner reports ModelManager, and
  PythonSupport.Shutdown() is what ends it.

  THE HOST OWNS IT -- your application had already started an interpreter. This
  library configures NOTHING (no virtual environment, no library path, no
  process-exit mode), adopts what is running, and NEVER SHUTS IT DOWN.
  PythonSupport.Owner reports Host, and PythonSupport.Shutdown() is a no-op.
  Your own lifetime rules are the only ones in force.

SHUTTING DOWN
-------------
    void PythonSupport.Shutdown()

Call it ONCE, when your application is finished with every Python feature. It
is idempotent, it does nothing at all when the host owns the interpreter, and
after it every Python entry point throws InvalidOperationException("Python has
been shut down for this process; it cannot be initialized again."). There is no
way to start a fresh interpreter afterwards, and nothing in this library tries.

DISPOSING A ModelStore DOES NOT SHUT PYTHON DOWN. Several stores may share a
process, and a store that ended the process's interpreter would break the rest
of the application.

    using (var store = new ModelStore(options))
    {
        // ... work, some of which may use Python ...
    }
    PythonSupport.Shutdown();   // once, at the end of the application

WHEN A VIRTUAL ENVIRONMENT STOPS WORKING. A virtual environment records the
base interpreter it was made from. When that interpreter moves to a new MINOR
version - 3.13 to 3.14 - the environment has to be recreated and any explicit
LibraryPath repointed; point releases inside a minor version flow through
without help. The symptom is a report whose Problems name a library that is not
there, or an interpreter that will not start.

THREADS. After the interpreter starts, the interpreter lock is handed back, so
a Python run may happen on any thread. One run happens at a time, the lock is
held only for the duration of a run, and nothing is awaited while it is held.


EXPORTING TO ONNX
=================
    Task<ExportResult> ExportToOnnxAsync(string name,
        ExportOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)

turns a bundle into ONNX and stores the result as a DERIVED BUNDLE. The model
must be a bundle: a GGUF model pulled from an Ollama-protocol registry has no
publisher file tree and is refused with InvalidOperationException.

THE FOUR ROUTES, ExportOptions.Route:

    Auto            the default. Reads the source and decides, by the rules
                    below
    PublisherOnnx   a PASS-THROUGH of the .onnx files the publisher already
                    shipped. NO PYTHON IS NEEDED OR STARTED, nothing is
                    converted, and not one byte is copied: the store is content
                    addressed, so the derived bundle names the blobs the source
                    already holds. It takes every file of the source except the
                    checkpoints the graph replaces - .safetensors, .bin, .pt,
                    .pth, .h5, .msgpack and TensorFlow .ckpt files - so the
                    configuration, the tokenizer files and any external-data
                    file beside the graph all come with it
    GenAiBuilder    the ONNX Runtime GenAI model builder, which writes a
                    model.onnx, its external data and the genai_config.json
                    beside them for the transformer architectures it supports
    Optimum         Hugging Face Optimum's ONNX exporter, which traces the
                    model's own Python code. It reaches architectures the
                    builder does not write and is the fallback for them

WHAT Auto DECIDES:

    the source ships at least one .onnx file          -> PublisherOnnx
    its config.json names an architecture the GenAI
      builder writes (LlamaForCausalLM, Qwen2/3,
      Phi, Gemma, Mistral, Granite, Whisper and the
      rest of that list)                              -> GenAiBuilder
    anything else                                     -> Optimum

Naming a route outright overrides all of it, which is how a model the automatic
rules do not know is still exported: name GenAiBuilder and let the builder
answer for itself.

WHICH ROUTES NEED PYTHON, AND WHICH MODULES

    PublisherOnnx   none. Nothing of the embedding layer is loaded
    GenAiBuilder    onnxruntime_genai, torch, transformers, onnx
    Optimum         optimum, onnx, torch, transformers

A Python route calls PythonSupport.Require BEFORE it lays anything out, so a
machine without the modules says so in a second rather than after copying a
checkpoint into a temporary folder. The feature is named "exporting to ONNX" in
every message.

ExportOptions

    ExportRoute Route        Auto (the default) | PublisherOnnx | GenAiBuilder
                             | Optimum
    string Precision         "fp32" (the default), "fp16", "bf16", "int8",
                             "int4" - whatever the tool accepts; it is passed
                             verbatim and the TOOL decides. Never null: an
                             unset value reads as
                             ExportOptions.DefaultPrecision.
                             THE Optimum ROUTE IGNORES IT and exports at the
                             checkpoint's own precision; what was asked for is
                             still recorded in the provenance, so a bundle
                             never claims a precision that was not applied
    string OutputName        null takes the source name with its tag replaced
                             by "onnx"
    bool Overwrite           false; a name already in the store fails the
                             export and names this option
    bool AllowRemoteCode     false. See below

ExportResult   string Name (ready to show, resolve or materialize);
    IReadOnlyList<string> Files; string Tool; string ToolVersion;
    ExportRoute RouteUsed (the route that RAN, so never Auto).

ALLOWING THE PUBLISHER'S OWN PYTHON. Some checkpoints define their tokenizer or
their configuration class in .py files inside the repository, and the tooling
refuses to load them unless it is told to import those files.
ExportOptions.AllowRemoteCode = true is that instruction: it reaches the GenAI
builder as hf_remote and Optimum as trust_remote_code, and it RUNS THE
PUBLISHER'S OWN PYTHON in your process, with everything that implies. It is
false by default. Set it only for a model whose files you have looked at, and
never for one you did not choose yourself. The PublisherOnnx route ignores it,
because it runs no Python at all.

THE PROGRESS STREAM, IN ORDER

    "materializing source"   the source bundle is written into a temporary
                             folder for the tool to read (Python routes only)
    "exporting"              the tool is running. It prints its own progress to
                             standard output, which is the tool's habit, not
                             this library's
    "collecting"             what the tool wrote is being taken into the store
    "writing manifest"       the derived bundle's manifest
    "success"                the last report

THE TEMPORARY FOLDER is under the system temporary directory, is removed
however the export ends, and holds a whole copy of the source plus whatever the
tool writes. ON A MACHINE WHOSE TEMPORARY DIRECTORY IS IN MEMORY - which is
most Linux desktops - POINT TMPDIR AT A REAL FILE SYSTEM before exporting a
checkpoint of any size. The pass-through route writes no temporary folder at
all.

    // A publisher who ships ONNX: instant, and no Python anywhere.
    ExportResult passed = await store.ExportToOnnxAsync(
        "hf.co/skytnt/midi-model-tv2o-medium:onnx-only");
    Console.WriteLine(passed.Name);        // ...:onnx
    Console.WriteLine(passed.RouteUsed);   // PublisherOnnx

    // A checkpoint, converted by the builder at int4, under a name of its own.
    var options = new ExportOptions
    {
        Route = ExportRoute.GenAiBuilder,
        Precision = "int4",
        OutputName = "hf.co/m-a-p/MuPT-v1-8192-190M:onnx-int4",
        AllowRemoteCode = true,     // this checkpoint ships its own tokenizer
        Overwrite = true
    };
    var progress = new Progress<PullProgress>(p => Console.WriteLine(p.Status));
    ExportResult built = await store.ExportToOnnxAsync(
        "hf.co/m-a-p/MuPT-v1-8192-190M", options, progress);
    foreach (string file in built.Files)
    {
        Console.WriteLine(file);
    }

    // Then use it like any other bundle.
    await store.MaterializeAsync(built.Name, "/somewhere/on/disk");

WHAT CAN GO WRONG: ModelNotFoundException (no such model),
InvalidOperationException (not a bundle, or PublisherOnnx named for a bundle
with no .onnx in it), PythonNotAvailableException and
PythonModuleNotInstalledException (a Python route on a machine that cannot run
it), PythonScriptException (the tool refused the model or failed - its own
message is in PythonMessage), and ModelManagerException (the output name is
taken and Overwrite is not set, or the tool wrote nothing).


REDUCING AN ONNX MODEL
======================
    Task<ReduceResult> ReduceOnnxAsync(string name,
        ReduceOptions options = null,
        IProgress<PullProgress> progress = null,
        CancellationToken cancellationToken = default)

makes the exported graphs a bundle holds SMALLER by quantizing their weights,
and stores the result as another DERIVED BUNDLE. The source must be a bundle
holding at least one .onnx file - one a publisher shipped, one this library
exported, or one it reduced before - and anything else is refused with a
message saying to export it first.

A REDUCED MODEL IS AN APPROXIMATION OF THE ONE IT CAME FROM. Quantizing
replaces floating-point weights with smaller integers and a scale, and that
cannot be undone: the file is a fraction of the size and the numbers the model
produces are CLOSE TO, not the same as, what it produced before. How close
depends on the model, on the mode and on what you ask it. Measure it on your
own inputs before you ship it.

THE FOUR MODES, ReduceOptions.Mode:

    DynamicInt8       the default. Every constant weight of a MatMul becomes
                      eight-bit and the activations are quantized while the
                      model runs. The graph gains MatMulInteger nodes and the
                      scales that go with them. A Gemm that can become a MatMul
                      is rewritten into one first, so it is covered as well
    WeightOnlyInt8    the constant weight of every MatMul is split into blocks
                      along its rows, and each block is stored as eight-bit
                      values with a scale of its own in a MatMulNBits node.
                      The activations stay in floating point
    WeightOnlyInt4    the same with four bits per value: the smallest of these
                      modes, and the one that changes the numbers most
    PreprocessOnly    shape inference and the basic graph optimizations a
                      quantizer wants to see, stored as a bundle of its own and
                      quantized by nobody. It is how the same prepared graph is
                      given to more than one quantizer, so that what they
                      produce can be compared

WHAT TO EXPECT OF THE SIZE. Only the MatMul weights shrink, so the ratio is
decided by how much of the file they are - which is most of it for a
transformer and less of it for a model with large embedding tables, since
embeddings are read by Gather and no mode here touches Gather. For a graph that
is almost all MatMul weights, expect roughly:

    WeightOnlyInt4    about six to seven times smaller
    WeightOnlyInt8    about three and a half times smaller
    DynamicInt8       about three and a half to four times smaller
    PreprocessOnly    the same size, give or take the shapes it writes in

Work the number out for your own model before you are surprised by it: a block
of 128 single-precision values is 512 bytes and becomes 64 bytes plus a 4-byte
scale plus a packed zero point at four bits, or 128 bytes plus the same at
eight; a dynamically quantized weight becomes a quarter of what it was. Then
add everything in the file that is not a MatMul weight, which does not change.

THE TWO ENGINES
---------------
Two implementations do this work, and which one runs decides what the machine
has to have on it.

    Managed     THIS LIBRARY'S OWN CODE, and it needs NOTHING INSTALLED: no
                Python, no native library, no NuGet package. It reads and
                writes the ONNX format itself and quantizes in memory. It
                covers WeightOnlyInt4 and WeightOnlyInt8 always, and
                DynamicInt8 on a graph that has already been prepared
    Python      ONNX Runtime's own quantization tools, run in a CPython this
                machine has, with onnx and onnxruntime importable there. It is
                the reference implementation, and the only engine for
                PreprocessOnly - preparing a graph is shape inference and that
                runtime's own graph optimizer, neither of which is ported - and
                for dynamic quantization of a graph nobody has prepared

REDUCING AN EXISTING .onnx TO FOUR- OR EIGHT-BIT WEIGHTS NEEDS NOTHING
INSTALLED. That is the plain form of it: obtain a model that already ships ONNX
(or export one on a machine that does have Python), and every machine afterwards
can make it smaller with nothing but this package.

The two engines write THE SAME FILE. Not a similar file: the same bytes, node
for node and weight for weight, for every mode the managed engine covers. There
are tests in this repository that reduce real published models with both engines
and compare what they wrote byte for byte.

CHOOSING AN ENGINE, ReduceOptions.Engine:

    Auto        the default, and the rules are these:
                  WeightOnlyInt4, WeightOnlyInt8 -> Managed, always. Nothing is
                    installed, nothing is looked for, no interpreter is started
                  DynamicInt8 -> Managed when the graph records having been
                    through shape inference, which is what PreprocessOnly
                    leaves behind; otherwise Python, because that pass runs
                    there. With neither - an unprepared graph and no usable
                    CPython - the call fails with a ModelManagerException that
                    says to prepare the model first or to choose a weight-only
                    mode
                  PreprocessOnly -> Python
    Python      ONNX Runtime's own tools, whatever the mode
    Managed     this library's own code. PreprocessOnly is refused with a
                NotSupportedException saying why; a graph holding something the
                port does not implement is refused the same way, naming it

ReduceResult.EngineUsed always names the engine that actually ran, and is never
Auto. The Python engine calls PythonSupport.Require BEFORE anything is laid
out, so a machine without the modules says so in a second rather than after a
bundle has been linked into a temporary folder; the feature is named "reducing
an ONNX model" in every message. The managed engine requires nothing and asks
nothing of the machine.

WHAT THE MANAGED ENGINE DOES NOT COVER, and refuses rather than approximating:
preparing a graph; the QDQ form of weight-only quantization; quantizing a
Gather into GatherBlockQuantized; a weight of any rank but two, or in bfloat16;
a node carrying a sub-graph; and a Gemm with a transposed B side that is not a
constant. The Python engine covers all of it. Every OTHER operator - the
Gathers an embedding table is read by, the Transposes, everything a transformer
is made of besides its matrix multiplies - is carried through untouched, which
is exactly what ONNX Runtime's own tools do with the operator types this
library asks them for.

ReduceOptions

    ReduceMode Mode          DynamicInt8 (the default) | WeightOnlyInt8 |
                             WeightOnlyInt4 | PreprocessOnly
    int BlockSize            128, the tooling's own default. How many weight
                             values share one scale in the weight-only modes;
                             smaller blocks cost more scales and keep more
                             accuracy. Runtimes implement a fixed set of block
                             sizes - 16, 32, 64, 128, 256 - so a graph
                             quantized with anything else is written but may
                             not load. DynamicInt8 ignores it. A value that is
                             not greater than zero is refused
    bool IsSymmetric         false, the tooling's own default: each block keeps
                             a zero point. true stores a scale alone
    int? AccuracyLevel       null, the tooling's own default: the runtime
                             chooses what to compute the block-wise matrix
                             multiply at. It is written into the graph as an
                             attribute and means nothing to the file's size
    bool Preprocess          true. It applies to DynamicInt8, which reads the
                             shapes that preprocessing infers and warns about a
                             graph nobody prepared. THE WEIGHT-ONLY MODES NEVER
                             PREPROCESS, whatever this says: they read the
                             weights themselves, so preparing a graph for them
                             costs time and changes the graph for no gain.
                             PreprocessOnly is the way to prepare one on purpose
    string OutputName        null takes the default name below
    bool Overwrite           false; a name already in the store fails the
                             reduction and names this option
    ReduceEngine Engine      Auto (the default) | Python | Managed
    IReadOnlyList<string>
        Files                null - the default - reduces every .onnx file the
                             bundle holds. Naming files reduces those and
                             CARRIES THE REST OF THE BUNDLE THROUGH UNCHANGED,
                             which is how a bundle that ships several graphs
                             has one of them reduced and stays complete. A name
                             the bundle does not hold is refused, and the
                             message lists the ones it does

ReduceResult   string Name (ready to show, resolve or materialize);
    IReadOnlyList<string> Files; ReduceEngine EngineUsed (the engine that RAN,
    so never Auto); ReduceMode Mode; long SourceBytes; long ReducedBytes;
    string Tool; string ToolVersion. SourceBytes and ReducedBytes are about the
    GRAPHS THAT WERE REDUCED and the files of weights beside them, not about
    the whole bundle: what is carried through counts towards neither.

THE DEFAULT NAME adds the mode's tag to the tag the source carries:

    DynamicInt8       int8
    WeightOnlyInt8    int8-weights
    WeightOnlyInt4    int4-weights
    PreprocessOnly    onnx-preprocessed

A source with no tag of its own, or only the default one, takes the mode's tag
alone (hf.co/x/y becomes hf.co/x/y:int4-weights); anything else is appended to
with a hyphen (hf.co/x/y:onnx becomes hf.co/x/y:onnx-int4-weights). A tag is
never repeated: a mode's tag that already begins with the source's own IS the
whole tag, which is what makes the prepared form of :onnx read
:onnx-preprocessed rather than saying onnx twice.

WHAT THE BUNDLE HOLDS AFTERWARDS is the source with the graphs replaced: every
reduced graph, any file of weights the tool wrote beside one, and every other
file of the source - the configuration, the tokenizer files, the model card, a
graph nobody asked to reduce. The checkpoints an export already replaces
(.safetensors, .bin, .pt and the rest) are left behind, as they are by an
export. Provenance is recorded the way an export records it: derivedFrom is the
source, and settings carry the mode, the engine asked for and the engine used,
whether the graph was prepared, the block-wise settings and the files that were
reduced. The tool is the engine that ran - "onnxruntime" with the version that
package reports, or "CodeBrix.Ollama.ModelManager" with this library's own
version - so a bundle says which of the two made it and which build of it.

EXTERNAL DATA. A graph too large for one protocol buffer message keeps its
weights in a file beside it. Either engine reads such a graph and either can
write one: this library measures the graph and its weights together and asks
for them to be written apart when what is about to be written comes near the
two-gibibyte message limit. Both engines ask the same question of the same
graph, so both write the same shape of answer - one file, or a graph and a file
of weights named after it. The file of weights that belonged to a graph being
reduced is replaced with it, not carried through.

THE ONE LIMIT WORTH KNOWING: the preparation pass writes its result as a single
message unless it was asked for external data, and such a message cannot exceed
two gibibytes. A graph that runs into it fails with a ModelManagerException
naming the graph and the limit, and saying what to do instead - one of the
weight-only modes, which need no preparation, or Preprocess = false.

THE PROGRESS STREAM, IN ORDER

    "materializing source"    the source bundle is written into a temporary
                              folder for the tools to read
    "reducing <file>"         one report per graph, naming it as the bundle
                              spells it. The tools print their own progress to
                              standard output, which is their habit, not this
                              library's
    "collecting"              the files that were not reduced are put beside
                              the ones that were
    "writing manifest"        the derived bundle's manifest
    "success"                 the last report

THE TEMPORARY FOLDER is under the system temporary directory, is removed
however the reduction ends, and holds the source, the prepared copy of one
graph at a time and everything the tools write. ON A MACHINE WHOSE TEMPORARY
DIRECTORY IS IN MEMORY - which is most Linux desktops - POINT TMPDIR AT A REAL
FILE SYSTEM before reducing anything of size.

    // Smallest first: four-bit block-wise weights, every graph in the bundle.
    ReduceResult small = await store.ReduceOnnxAsync(
        "hf.co/skytnt/midi-model-tv2o-medium:onnx",
        new ReduceOptions { Mode = ReduceMode.WeightOnlyInt4 });
    Console.WriteLine(small.Name);            // ...:onnx-int4-weights
    Console.WriteLine(small.SourceBytes);     // what the graphs were
    Console.WriteLine(small.ReducedBytes);    // what they are now

    // Eight-bit dynamic, one graph of several, under a name of its own.
    var options = new ReduceOptions
    {
        Mode = ReduceMode.DynamicInt8,
        Files = new[] { "onnx/model_token.onnx" },
        OutputName = "hf.co/skytnt/midi-model-tv2o-medium:token-int8",
        Overwrite = true
    };
    var progress = new Progress<PullProgress>(p => Console.WriteLine(p.Status));
    ReduceResult one = await store.ReduceOnnxAsync(
        "hf.co/skytnt/midi-model-tv2o-medium:onnx", options, progress);

    // Prepare once, then quantize the prepared bundle without preparing it
    // again - which is how two quantizers are given the same starting graph,
    // and how dynamic quantization reaches the managed engine: the prepared
    // graph records the shape inference the managed engine does not run.
    ReduceResult prepared = await store.ReduceOnnxAsync(
        "hf.co/skytnt/midi-model-tv2o-medium:onnx",
        new ReduceOptions { Mode = ReduceMode.PreprocessOnly });   // Python
    ReduceResult dynamic = await store.ReduceOnnxAsync(
        prepared.Name,
        new ReduceOptions { Mode = ReduceMode.DynamicInt8, Preprocess = false });
    Console.WriteLine(dynamic.EngineUsed);    // Managed

    // Four-bit weights on a machine with no Python at all. Nothing is
    // installed, nothing is looked for, and EngineUsed says so.
    ReduceResult managed = await store.ReduceOnnxAsync(
        "hf.co/skytnt/midi-model-tv2o-medium:onnx",
        new ReduceOptions
        {
            Mode = ReduceMode.WeightOnlyInt4,
            OutputName = "hf.co/skytnt/midi-model-tv2o-medium:managed-int4",
            Overwrite = true
        });
    Console.WriteLine(managed.EngineUsed);    // Managed
    Console.WriteLine(managed.Tool);          // CodeBrix.Ollama.ModelManager

    // Then use it like any other bundle.
    await store.MaterializeAsync(small.Name, "/somewhere/on/disk");

WHAT CAN GO WRONG: ModelNotFoundException (no such model),
InvalidOperationException (not a bundle), NotSupportedException (the managed
engine was asked for something it does not cover),
PythonNotAvailableException and
PythonModuleNotInstalledException (a machine that cannot run the tools),
PythonScriptException (a tool refused a graph or failed - its own message is in
PythonMessage), and ModelManagerException (the bundle holds no .onnx file,
Files names one it does not hold, a graph is too large to prepare, the output
name is taken and Overwrite is not set, or dynamic quantization was asked for
on an unprepared graph with no CPython to prepare it in).

WHAT RUNS A REDUCED MODEL. A four-bit graph needs a runtime that implements
MatMulNBits, and a dynamically quantized one needs MatMulInteger; ONNX Runtime
implements both, and something else may implement neither. Neither this library
nor CodeBrix.Ollama.ModelRunner runs an ONNX model at all - ModelRunner runs
GGUF - so whatever you reduce here is for a runtime you bring yourself.


THE ERROR MODEL
===============
Everything this library raises derives from ModelManagerException, itself an
Exception. Catch the base type to catch all of it.

    ModelManagerException          the base, also thrown on its own for a
                                   safetensors manifest, a DRAFT line, a
                                   Modelfile with no FROM, a manifest naming a
                                   blob the store does not have, a layer whose
                                   JSON will not decode, and a source model
                                   missing its config during a create
      DigestMismatchException      ExpectedDigest and ActualDigest, both
                                   "sha256:<hex>"; the bad file is already gone
      GgufFormatException          not a GGUF file, or a header this reader
                                   cannot interpret; also the two LoRA adapter
                                   mix-ups on create
      InvalidModelNameException    ModelName holds the rejected string
      ModelfileParseException      LineNumber, 1-based, or 0 for a whole-file
                                   problem
      ModelNotFoundException       no manifest, locally or on the registry;
                                   ModelName holds the name as you wrote it
      PythonModuleNotInstalledException
                                   a Python feature was asked for and a module
                                   it needs is not installed in the interpreter
                                   that would run it; Feature, ModuleName, and
                                   the install command in the message
      PythonNotAvailableException  a Python feature was asked for and there is
                                   no usable CPython shared library; Feature
      PythonScriptException        a Python script that ships in this package
                                   failed; Feature, ScriptName, PythonMessage
      RegistryException            an error status, a 401, a transport failure
                                   that survived every retry, an insecure
                                   http:// name, or an answer that is not a
                                   manifest. StatusCode is null when no
                                   response arrived; ResponseBody holds the
                                   body when one was read, else null

WHAT THE BUNDLE PATHS ADD, exception by exception:

    ArgumentException           a Hugging Face pull under a name whose host is
                                not the Hub with PullOptions.Repository unset;
                                a file-list pull with no files; a PullSource a
                                bundle cannot be pulled from; a BundleFile
                                whose path is rooted, names a drive or walks
                                up with "..", whose address is missing,
                                relative or neither http nor https, or whose
                                stated hash is not hexadecimal of the length
                                its algorithm calls for; a BundleDefinition
                                with no repository or no files; a missing
                                directory argument
    InvalidModelNameException   a BundleDefinition whose name does not parse
    ModelManagerException       RequireHashes is set and the source states no
                                hash for a file; the source listed nothing to
                                pull; the folder to import does not exist or
                                holds no file the filter keeps; a file is
                                already at a materialize target path and
                                Overwrite is not set; a layer names a blob the
                                store does not have, or a path that would land
                                outside the target directory; a symbolic link
                                was asked for and could not be made
    InvalidOperationException   MaterializeAsync on a model with no publisher
                                file tree - which is every model pulled from
                                an Ollama-protocol registry
    RegistryException           a gated or private repository and no token; a
                                repository, revision or object that is not
                                there; an answer that is not a Hub document, a
                                file tree or a bucket listing; a server that
                                will say neither how large a file is nor serve
                                a byte of it
    DigestMismatchException     the bytes do not match the sha256 or the md5
                                the source stated; the message says which, and
                                the bad file is already gone

Framework exceptions you will also see: ArgumentException (a null or blank
model name or store directory), ArgumentNullException (a null Modelfile, a null
GGUF path or stream), ArgumentOutOfRangeException (a ReduceOptions.BlockSize
that is not greater than zero), ObjectDisposedException (any operation after
Dispose), OperationCanceledException, InvalidOperationException
(ToRelativePath on a name that is not fully qualified; exporting or
materializing a model with no publisher file tree; any Python entry point after
PythonSupport.Shutdown), NotSupportedException (the managed reduction engine
asked for something it does not cover), UriFormatException (BaseUrl), and
IOException and UnauthorizedAccessException from the file system, unwrapped. A
RegistryException whose StatusCode is HttpStatusCode.Unauthorized is the one to
filter on when you want to prompt for credentials.


THREAD SAFETY AND CONCURRENCY
=============================
What the code guarantees, and nothing more:

  - ONE STORE, MANY THREADS, FOR READING. ListAsync, ExistsAsync, ShowAsync and
    ResolveAsync keep no per-call state and may be called concurrently. The
    store's only mutable state is the lazily created registry client, guarded
    by a lock, and the HttpClient underneath it is safe for concurrent use.
  - WITHIN ONE PULL the byte ranges of a blob are fetched concurrently, up to
    MaxConcurrentParts of them, and the layers one after another. The progress
    channel is single-reader and single-writer: enumerate it once.
  - TWO PULLS OF THE SAME MODEL ARE NOT COORDINATED -- not between two threads,
    not between two processes, and not between this library and a real Ollama
    install. There is no lock file, no advisory lock and no registry of
    downloads in flight; two such pulls would write to the same partial data
    file and sidecar, and the result is undefined. Serialize them yourself.
  - WHAT IS GUARANTEED INSTEAD IS ATOMIC PUBLICATION, the same guarantee Ollama
    relies on: a blob is downloaded to a sidecar, hashed and only then MOVED to
    its blob path, and a manifest is written to a temporary file in its own
    directory and only then MOVED into place, so a concurrent reader sees the
    old state or the new one and never a half-written file.
  - DIFFERENT MODELS IN PARALLEL IS FINE. They share no partial files, and
    pruning deletes a blob only after every manifest has been checked, so a
    blob the other pull just published is safe.
  - ONE PYTHON RUN AT A TIME, whatever thread asks for it. There is one
    interpreter per process and this library serializes its own use of it; the
    interpreter lock is held only for the duration of a run and nothing is
    awaited while it is held. The managed reduction engine takes no such lock,
    but an export or a reduction of the SAME MODEL from two threads at once is
    two calls writing the same derived name: serialize those yourself, as with
    two pulls.


COMPLETE EXAMPLES
=================
Every name below exists in the package exactly as described above. Each
example is a complete method body: put it in an async Main (the MINIMUM
VIABLE PROJECT TEMPLATE shows one) with these usings:

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelManager;

EXAMPLE 1 - PULL, RESOLVE, SHOW, LIST, DELETE
---------------------------------------------
    // Defaults: OLLAMA_MODELS, else ~/.ollama/models; registry.ollama.ai
    using var store = new ModelStore();

    // 1. PULL. Nothing is requested until the enumerator is iterated.
    await foreach (PullProgress progress in store.PullAsync("smollm:135m"))
    {
        Console.WriteLine(progress.Digest == null
            ? progress.Status
            : $"{progress.Status} {progress.Percent:F1}%");
    }
    // pulling manifest / pulling 4d2b8b0d1b2a 0.0% ... 100.0% /
    // verifying sha256 digest / writing manifest / success

    // 2. RESOLVE. This is what an in-process runner opens.
    ResolvedModel resolved = await store.ResolveAsync("smollm:135m");
    Console.WriteLine(resolved.ModelPath);
    // /Users/me/.ollama/models/blobs/sha256-4d2b8b0d1b2a...

    // 3. SHOW. Manifest, decoded layers, GGUF header, capabilities.
    ModelInfo info = await store.ShowAsync("smollm:135m");
    Console.WriteLine($"{info.DisplayName}  {info.Size:N0} bytes");
    Console.WriteLine($"{info.Config.ModelFamily} {info.Config.FileType} " +
                      $"ctx {info.Metadata.ContextLength}");
    Console.WriteLine(string.Join(", ", info.Capabilities));

    // 4. LIST and DELETE.
    var models = await store.ListAsync();   // IReadOnlyList<ModelSummary>
    await store.DeleteAsync("smollm:135m");

EXAMPLE 2 - CREATE A DERIVED MODEL FROM A MODELFILE
---------------------------------------------------
    using var store = new ModelStore();

    // FROM names a model already in the store (pulled in Example 1) or a GGUF
    // file on disk. NOTHING is downloaded by CreateAsync.
    Modelfile modelfile = Modelfile.Parse(
        "FROM smollm:135m\n" +
        "SYSTEM You are a terse assistant.\n" +
        "PARAMETER temperature 0.2\n" +
        "PARAMETER stop <|im_end|>\n");

    await store.CreateAsync("my-smollm:v1", modelfile);

    ModelInfo derived = await store.ShowAsync("my-smollm:v1");
    Console.WriteLine(derived.System);                  // You are a terse assistant.
    Console.WriteLine(derived.Parameters.Temperature);  // 0.2
    Console.WriteLine(derived.ModelfileText);           // FROM <blob path> ...

    // From a GGUF file on disk instead. A relative path resolves against
    // CreateOptions.BaseDirectory, not the process's current directory.
    Modelfile fromFile = Modelfile.Parse("FROM ./mistral-7b-instruct.Q4_K_M.gguf\n");
    await store.CreateAsync("mistral-local:q4", fromFile,
        new CreateOptions { BaseDirectory = "/models/downloads" });

EXAMPLE 3 - A PRIVATE STORE, A HUGGING FACE PULL, AND A GGUF HEADER
-------------------------------------------------------------------
    using var store = new ModelStore(new ModelStoreOptions
    {
        StoreDirectory = "/data/models",   // created on first write
        MaxConcurrentParts = 8,            // byte ranges of one blob in flight
    });

    string name = "hf.co/HuggingFaceTB/smollm-360M-instruct-v0.2-Q8_0-GGUF";
    if (!await store.ExistsAsync(name))
    {
        await foreach (PullProgress progress in store.PullAsync(name))
        {
            if (progress.Digest != null)
                Console.Write($"\r{progress.Status} {progress.Percent,5:F1}%   ");
            else
                Console.WriteLine(progress.Status);
        }
    }

    // Read the weights file's header directly: key-values and tensor
    // descriptors only, never tensor data, so this is cheap at any file size.
    ResolvedModel resolved = await store.ResolveAsync(name);
    GgufMetadata header = await GgufMetadata.ReadAsync(resolved.ModelPath,
        new GgufReadOptions { MaxArraySize = -1 });   // keep the vocabulary too
    Console.WriteLine($"{header.Architecture} {header.FileTypeName} " +
                      $"ctx {header.ContextLength} tensors {header.Tensors.Count}");
    string[] vocabulary = header.GetValue("tokenizer.ggml.tokens")?.AsStringArray();
    Console.WriteLine($"{vocabulary?.Length ?? 0} tokens");

EXAMPLE 4 - HANDLING THE ERRORS A PULL CAN RAISE
------------------------------------------------
    using var store = new ModelStore(new ModelStoreOptions
    {
        BearerToken = Environment.GetEnvironmentVariable("MY_REGISTRY_TOKEN"),
    });

    try
    {
        await foreach (PullProgress _ in store.PullAsync("registry.example.com/team/model:v1")) { }
    }
    catch (ModelNotFoundException ex)
    {
        Console.Error.WriteLine($"No such model on the registry: {ex.ModelName}");
    }
    catch (RegistryException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
    {
        Console.Error.WriteLine("The registry wants credentials it did not get.");
    }
    catch (DigestMismatchException ex)
    {
        // The bad file is already deleted; pulling again re-downloads that layer.
        Console.Error.WriteLine($"Corrupt download: expected {ex.ExpectedDigest}, got {ex.ActualDigest}");
    }
    catch (ModelManagerException ex)
    {
        // Everything else the library raises: a safetensors manifest, a
        // transport failure that survived every retry, an http:// name ...
        Console.Error.WriteLine(ex.Message);
    }

EXAMPLE 5 - PULL A HUGGING FACE FILE REPOSITORY, MATERIALIZE IT, DELETE IT
--------------------------------------------------------------------------
    using var store = new ModelStore(new ModelStoreOptions
    {
        StoreDirectory = "/data/models",
    });

    // Substitute the repository you want; any public one works the same way.
    const string name = "hf.co/example-org/example-model";

    // 1. PULL. A bundle pull is ASKED FOR: PullAsync(name) on its own still
    //    means the registry protocol, which cannot serve such a repository.
    PullOptions options = PullOptions.ForHuggingFace(
        null,                      // repository: taken from the name
        null,                      // revision: the name's tag, "latest" -> "main"
        FileFilter.ExcludeTrainingArtifacts);   // no logs, no optimizer state

    await foreach (PullProgress progress in store.PullAsync(name, options))
    {
        Console.WriteLine(progress.Digest == null
            ? progress.Status
            : $"{progress.Status} {progress.Percent:F1}%");
    }
    // listing example-org/example-model / pulling config.json 100.0% /
    // pulling model.safetensors 12.4% ... / verifying sha256 digest /
    // writing manifest / success

    // 2. WHAT THE STORE NOW KNOWS. The licence is REPORTED, never applied.
    ModelInfo info = await store.ShowAsync(name);
    Console.WriteLine(info.Format);            // pytorch, onnx, mixed ...
    Console.WriteLine(info.License.LicenseId);      // stated, or null
    Console.WriteLine(info.License.LicenseSource);  // where it was read
    foreach (string text in info.Licenses)          // the LICENSE files
        Console.WriteLine(text.Length);

    // 3. RESOLVE, then MATERIALIZE the publisher's own file tree.
    ResolvedModel resolved = await store.ResolveAsync(name);
    Console.WriteLine(resolved.ModelPath);     // null: no GGUF weights here
    foreach (ResolvedFile file in resolved.Files)
        Console.WriteLine($"{file.Name}  {file.Size:N0}  {file.BlobPath}");

    IReadOnlyList<string> written = await store.MaterializeAsync(
        name,
        "/work/example-model",
        new MaterializeOptions { Link = MaterializeLink.Hardlink });
    Console.WriteLine(written[0]);   // /work/example-model/config.json

    // 4. DELETE. The manifest goes, then every blob nothing else references.
    //    What was materialized with hard links is still readable afterwards.
    await store.DeleteAsync(name);

EXAMPLE 6 - PULL A LIST OF ADDRESSES FROM A PUBLIC BUCKET
---------------------------------------------------------
    // A storage bucket speaks no model protocol at all: list it, then pull
    // the list. Each object's md5 comes back with it and is verified as the
    // bytes stream past; the store computes the sha256 in any case.
    IReadOnlyList<BundleFile> files = await GoogleCloudStorageListing.ListAsync(
        "example-bucket",              // the bucket
        "models/example-model/",   // the prefix, stripped off each key
        null);                     // HttpMessageHandler, null for a default

    long total = 0;
    foreach (BundleFile file in files)
    {
        Console.WriteLine($"{file.Path}  {file.Size:N0}  md5 {file.Md5}");
        total += file.HasSize ? file.Size : 0;
    }
    Console.WriteLine($"{files.Count} files, {total:N0} bytes to fetch");

    using var store = new ModelStore();

    // The host of the name is a label here - the addresses in the list are
    // what is actually fetched.
    const string name =
        "storage.googleapis.com/example-bucket/example-model:v1";

    await foreach (PullProgress progress in store.PullAsync(
        name, PullOptions.ForFileList(files, null)))
    {
        Console.WriteLine(progress.Status);
    }

    ModelInfo info = await store.ShowAsync(name);
    Console.WriteLine(info.Format);   // tensorflow-checkpoint, pytorch ...

    // Any other host works the same way: build the BundleFile list yourself.
    var byHand = new[]
    {
        new BundleFile("weights/model.safetensors",
            "https://files.example.org/example-model/model.safetensors",
            467_701_064L,
            "82ac8b2217f8f66f79737e444fe60c686d3cbfee54b0c8ef717f701213bbbb83",
            null,
            null),
        new BundleFile("config.json",
            "https://files.example.org/example-model/config.json"),
    };
    await foreach (PullProgress _ in store.PullAsync(
        "files.example.org/example-org/example-model:v1",
        PullOptions.ForFileList(byHand, null))) { }

EXAMPLE 7 - IMPORT A FOLDER YOU DOWNLOADED BY HAND
--------------------------------------------------
    // The route for anything behind an interactive sign-in: fetch it however
    // it has to be fetched, then hand the folder over. Nothing is requested.
    using var store = new ModelStore();

    const string name = "local/example-org/example-model:v1";

    await store.ImportBundleAsync(name, "/downloads/example-model",
        new ImportOptions
        {
            Filter = FileFilter.ExcludeTrainingArtifacts,
            Link = false,   // copy: the folder stays independent of the store
            License = new LicenseRecord(
                "mit", "https://example.org/example-model/terms", null),
        });

    ModelInfo info = await store.ShowAsync(name);
    Console.WriteLine(info.Config.ModelFormat);   // pytorch, onnx, imported
    Console.WriteLine(info.License.LicenseId);    // mit
    foreach (ModelLayer layer in info.Manifest.Layers)
        Console.WriteLine($"{layer.Name}  {layer.Size:N0}  {layer.Digest}");

    // It lists, copies, materializes and deletes like any other bundle.
    foreach (ModelSummary summary in await store.ListAsync())
        Console.WriteLine(
            $"{summary.DisplayName}  {summary.Config.ModelFormat}");

    await store.MaterializeAsync(name, "/work/example-model",
        new MaterializeOptions
        {
            Link = MaterializeLink.Copy,
            Overwrite = true,
        });


MINIMUM VIABLE PROJECT TEMPLATE
===============================
A complete, working console application. Both files as shown compile and run:
the first run pulls a small model, later runs find it in the store.

MyModelTool.csproj

    <Project Sdk="Microsoft.NET.Sdk">

      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
        <ImplicitUsings>disable</ImplicitUsings>
      </PropertyGroup>

      <ItemGroup>
        <PackageReference Include="CodeBrix.Ollama.ModelManager.MitLicenseForever" />
      </ItemGroup>

    </Project>

Program.cs

    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelManager;

    namespace MyModelTool;

    internal static class Program
    {
        private static async Task<int> Main(string[] args)
        {
            string name = args.Length > 0 ? args[0] : "smollm:135m";

            using var store = new ModelStore();

            if (!await store.ExistsAsync(name))
            {
                await foreach (PullProgress progress in store.PullAsync(name))
                {
                    Console.WriteLine(progress.Digest == null
                        ? progress.Status
                        : $"{progress.Status} {progress.Percent:F0}%");
                }
            }

            ResolvedModel model = await store.ResolveAsync(name);
            Console.WriteLine($"Weights:  {model.ModelPath}");
            Console.WriteLine($"Template: {(model.Template == null ? "(none)" : "present")}");
            Console.WriteLine($"Context:  {model.Parameters?.NumCtx ?? 0}");
            return 0;
        }
    }


PERFORMANCE TIPS
================
  - CREATE ONE ModelStore AND KEEP IT. The registry client and its pooled
    HTTP connections are created lazily on the first pull and live until
    Dispose; a store per call throws that away every time. Every read
    operation is safe to call concurrently on the one instance.
  - ASK ExistsAsync BEFORE PullAsync when you only need the model present. A
    pull of a complete model is cheap but not free: the manifest is fetched
    again, every blob is confirmed and the manifest is rewritten.
  - PREFER ListAsync TO ShowAsync FOR A WHOLE STORE. ListAsync reads only the
    manifest and the small config blob; ShowAsync opens the weights GGUF and
    every projector to read their headers. ResolveAsync opens no weights file
    at all and is the cheap way to get paths.
  - TUNE THE DOWNLOAD TO THE LINK, NOT THE CPU. MaxConcurrentParts is the
    number of byte ranges of ONE blob in flight; MinPartSize and MaxPartSize
    clamp the range size. On a fast, stable link fewer, larger ranges cost
    less per byte; on a flaky one more, smaller ranges lose less per stall.
    A stalled range is reissued after StallTimeout without consuming a retry.
  - PULL DIFFERENT MODELS IN PARALLEL, NEVER THE SAME MODEL TWICE. Distinct
    models share no partial files; two pulls of one model share everything
    and are undefined.
  - LEAVE GgufReadOptions.MaxArraySize AT ITS DEFAULT unless you need the
    token vocabulary: the default keeps every ordinary key and skips only the
    arrays that hold a whole vocabulary. ReadAsync never reads tensor data,
    whatever you set.
  - KEEP ProgressInterval AT ITS DEFAULT OR LONGER. Every layer report is a
    channel write and a consumer wake-up; a display does not need more than
    ten a second.
  - CopyAsync MOVES NO BLOB BYTES: only the manifest is copied and every blob
    is shared, so giving a model a second name costs a few hundred bytes.
  - FILTER BEFORE YOU FETCH. A repository often ships the same weights twice -
    a .bin beside a .safetensors, or an ONNX export beside both - and the
    training logs on top. A FileFilter that excludes what you will not open is
    the difference between a few hundred megabytes and several gigabytes, and
    FileFilter.ExcludeTrainingArtifacts is the ready-made one for logs and
    optimizer state. Listing a source first reports the size before you commit
    to it.
  - STATED HASHES MAKE A RE-PULL FREE. A file whose source states a sha256 is
    written straight to the blob that digest names, so a second pull of an
    unmoved revision costs one listing and no file requests at all. A file
    whose source states nothing is fetched again every time, because until the
    bytes are there nothing can know it is the file already in the store.
  - MATERIALIZE WITH HARD LINKS, WHICH IS THE DEFAULT. A hard link is a second
    directory entry for bytes already on disk: no space, no copying time, and
    a fallback to a copy only where the platform or the file system will not
    link. Laying a 10 GB bundle out twice costs nothing twice over.


COMMON PITFALLS TO AVOID
========================
 1. DO NOT confuse the package id with the namespace. Package:
    CodeBrix.Ollama.ModelManager.MitLicenseForever; namespace:
    CodeBrix.Ollama.ModelManager, one namespace for every public type, so
    `using CodeBrix.Ollama.ModelManager.Store;` is a CS0246 error.

 2. DO NOT expect this to reach a running Ollama. It does not start one, look
    for one or call one. "Ask a local Ollama a question" is the wrong job for
    this package; "have the model file on disk without installing Ollama" is
    the right one.

 3. DO NOT drop the result of PullAsync: it returns IAsyncEnumerable and
    nothing is requested until you iterate it.

 4. DO NOT assume ResolveAsync downloads anything. It reads the local store and
    throws ModelNotFoundException when the model is absent; pull first, or
    guard with ExistsAsync.

 5. DO NOT treat ModelName.Parse as validation. It never throws and will
    cheerfully return a name containing MissingPart, so use TryParse or
    IsValid. A name with an @digest suffix is invalid; use a tag.

 6. DO NOT assume the strings coming back are non-null: the library is compiled
    with nullable reference types off, so your compiler will not warn you --
    Template, System, Parameters, Metadata, DraftPath and ModelPath can all be
    null.

 7. DO NOT use a relative FROM or ADAPTER path without setting
    CreateOptions.BaseDirectory: relative arguments resolve against
    Environment.CurrentDirectory, rarely where the .gguf files are, and "~" is
    never expanded.

 8. DO NOT expect Modelfile.GetParameters to tolerate an unknown parameter
    name. Parsing accepts it; GetParameters -- and therefore CreateAsync --
    throws. Inspect ParameterLines yourself if users supply Modelfiles.

 9. DO NOT expect a PARAMETER in a derived model to extend an inherited list.
    Parameters merge by name and a name you set replaces the inherited value
    whole, so one `PARAMETER stop <|end|>` discards the base model's stop list.

10. DO NOT dispose an HttpMessageHandler you supplied while the store is alive,
    and do not expect the store to dispose it -- it disposes only its own.

11. DO NOT call ShowAsync in a loop over a whole store: it opens and parses the
    GGUF header of the weights and of every projector, while ListAsync reads
    only the manifest and the small config blob.

12. DO NOT treat ModelSummary.Digest as a blob digest: it is the sha256 of the
    MANIFEST FILE, hex with NO "sha256:" prefix, while every layer digest is
    "sha256:<hex>". Likewise GgufValue.AsUInt64 will not read a signed value,
    nor AsInt64 an unsigned one; GgufMetadata.GetUInt64 bridges the two.

13. DO NOT run two pulls of the same model at once, in one process or two:
    nothing coordinates them and they share a partial file. Different models in
    parallel are fine.

14. DO NOT expect PullAsync(name) to fetch a bundle. The plain overload is the
    registry protocol and nothing else, and a Hugging Face repository without
    GGUF fails there exactly as it always has. Ask for the bundle:
    PullAsync(name, PullOptions.ForHuggingFace(...)) or ForFileList(...).

15. DO NOT read "latest" as a Hugging Face revision. The store's default tag
    "latest" maps to the branch "main" for an hf.co bundle, and the manifest
    then records the COMMIT that branch resolved to, not the branch. A name
    with no tag is therefore pinned to whatever "main" pointed at that day;
    pull again to find out whether it moved.

16. DO NOT expect a file with no stated hash to be free on a re-pull. The Hub
    states a sha256 only for files in large-file storage, so small files - a
    config, a tokenizer, a readme - are fetched again every time. They are
    also the cheap ones, which is why this is a note and not a warning.

17. DO NOT call MaterializeAsync on a model pulled from a registry: it has no
    publisher file tree and the call is an InvalidOperationException.
    ResolveAsync names the files of a GGUF model. And materializing refuses to
    replace anything by default - set MaterializeOptions.Overwrite when you
    mean it.

18. DO NOT assume materializing always costs nothing. A hard link cannot cross
    a volume or work on a file system without links, and the call quietly
    copies instead, which costs the space. MaterializeLink.Symlink is the one
    that never falls back: it fails loudly instead, and on Windows it needs
    developer mode or the privilege to create links.

19. DO NOT set PullOptions.RequireHashes against a repository of small files.
    It refuses any file the source states neither a sha256 nor an md5 for, and
    for a Hugging Face repository that is every file the Hub keeps in git
    itself. Use it when only content a publisher vouched for is acceptable,
    and expect to pair it with a filter.

20. DO NOT read a reported licence as permission. The library reports the
    identifier a source states and the address it read it from, and hands you
    the LICENSE text a bundle ships; it applies no rule, refuses no pull, and
    has no opinion. What may be used, shipped or published is yours to decide
    from what is reported, and a source that states nothing reports
    LicenseRecord.None rather than a guess.

21. DO NOT call PythonSupport.Shutdown() more than once, in the middle of an
    application, or in the hope of starting a fresh interpreter. CPython cannot
    be restarted in a process: after the call every Python entry point throws
    InvalidOperationException, and no interpreter is ever started again. Call
    it once, at the end, or not at all - the bounded process-exit mode means a
    process that never calls it still exits.

22. DO NOT expect PythonSupport.Shutdown() to end an interpreter your own
    application started. It is a no-op when PythonSupport.Owner is Host, on
    purpose: this library never shuts down an interpreter it did not start, and
    a library that did would break the rest of your application. When the host
    owns it, this library also configures nothing about it - not the virtual
    environment, not the library path, not the process-exit mode.

23. DO NOT assume PythonSupport.Check is cheap. With NO modules it is cheap and
    starts nothing. With MODULES it starts an interpreter, because importing is
    the only way to know whether a module is there, and that interpreter lasts
    for the life of the process. Check once and keep the report, or check with
    no modules when all you want to know is whether CPython is there at all.

24. DO NOT expect a virtual environment to survive a minor-version move of the
    interpreter it was made from. 3.13 to 3.14 means recreating the environment
    and repointing any explicit PythonOptions.LibraryPath; point releases inside
    a minor version need nothing.

25. DO NOT treat a derived bundle as a live view of the model it came from. It
    is a SNAPSHOT: the source can be deleted, re-pulled at another revision or
    replaced and the derived bundle stays exactly as it was. Export again when
    the source changes, and read DerivedFrom to know what it was made from.

26. DO NOT set ExportOptions.AllowRemoteCode for a model you did not choose
    yourself. It tells the export tooling to import the .py files the
    CHECKPOINT ships - a custom tokenizer or configuration class - which runs
    the publisher's own Python in your process. It is false by default, and
    some checkpoints simply cannot be exported without it.

27. DO NOT export a checkpoint with the system temporary directory in memory. A
    Python route writes the whole source bundle into a temporary folder and the
    tool writes its output beside it, so a machine whose /tmp is a RAM-backed
    file system - most Linux desktops - needs TMPDIR pointed at a real one
    first. The PublisherOnnx route writes no temporary folder at all.

28. DO NOT expect ExportOptions.Precision to reach the Optimum route. It is
    passed verbatim to the GenAI builder, which decides what it accepts, and
    the Optimum exporter ignores it and exports at the checkpoint's own
    precision. What was asked for is recorded in the provenance either way.

29. DO NOT SHIP A REDUCED MODEL YOU HAVE NOT MEASURED. Quantizing is an
    approximation, and how good an approximation depends on the model, the
    mode and the input. Run the model you reduced beside the one you reduced it
    from, on inputs you care about, and look at the difference before anyone
    else does.

30. DO NOT expect every runtime to load what a reduction wrote. Four-bit
    weight-only quantization emits MatMulNBits and dynamic quantization emits
    MatMulInteger; ONNX Runtime implements both, and something else may
    implement neither. Neither this library nor CodeBrix.Ollama.ModelRunner
    runs an ONNX model at all.

31. DO NOT expect ReduceOptions.Preprocess to do anything for the weight-only
    modes. They read the weights themselves and never preprocess, whatever it
    says; only the dynamic mode reads inferred shapes. ReduceMode.PreprocessOnly
    is the way to prepare a graph on purpose, and what it writes is an ordinary
    bundle that can be reduced afterwards.

32. DO NOT read ReduceResult.SourceBytes and ReducedBytes as the size of the
    BUNDLE. They are the size of the graphs that were reduced and of the files
    of weights beside them; everything carried through counts towards neither,
    so a bundle of mostly tokenizer files shrinks less than the numbers suggest.

33. DO NOT reduce a bundle with the system temporary directory in memory,
    for the reason an export must not be run that way: the source is laid out
    in a temporary folder and the tools write their output beside it.

34. DO NOT expect DYNAMIC eight-bit quantization to run without Python on a
    graph nobody has prepared. That mode reads the shapes a preparation pass
    infers, and the managed engine infers none; it takes a graph that records
    having been through one, which is what ReduceMode.PreprocessOnly leaves
    behind - and preparation is the Python engine's work. Prepare the model
    once on a machine that has CPython with onnx and onnxruntime, store the
    prepared bundle, and every machine afterwards can quantize it with nothing
    installed. The weight-only modes never ask: they are managed wherever they
    run.

35. DO NOT read ReduceResult.Tool as the name of a Python package. It names
    the engine that ran - "onnxruntime" for the Python engine, with the
    version that package reports, and "CodeBrix.Ollama.ModelManager" for the
    managed one, with this library's own version. A bundle reduced by the
    managed engine on a machine with no Python records no Python anything.


WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this package to:

  - SHIP, INSTALL or DOWNLOAD CPython, or install a pip module. A CPython
    shared library must already be on the machine, and the modules a Python
    feature imports must already be installed in the interpreter that runs it;
    what this package does is find them, report what is missing and say which
    command would install it.
  - PUSH a model. No upload, no manifest PUT, no blob POST; everything here is
    read-only against the registry.
  - Authenticate to a PRIVATE Ollama registry. Ollama signs its requests with
    an ed25519 key pair and exchanges the challenge for a token at the realm
    the registry names; none of that is implemented, and the only credential
    available is a bearer token you supply, offered only after a 401.
  - Handle SAFETENSORS. Refused by PullAsync, skipped when a manifest is read,
    impossible to import on create, and never converted to GGUF.
  - Create a model with a DRAFT line. The parser keeps DRAFT commands and
    Modelfile.Drafts exposes them, but CreateAsync refuses them. A draft layer
    in a PULLED manifest is fine and surfaces as ResolvedModel.DraftPath.
  - RENDER a prompt template. Template is the template TEXT; there is no Go
    text/template engine, no variable binding and no chat formatting. Nor is a
    template auto-detected on create: Ollama looks up a built-in one by model
    family when a Modelfile gives none, and those are not shipped here, so a
    created model has a template only when TEMPLATE says so or it inherited one.
  - Group a SPLIT GGUF. Extra model-weights layers come back in ModelShardPaths
    in manifest order; the split.* keys are readable through GgufMetadata but
    are not used to order, group or validate the shards.
  - Run a model, tokenize, embed or constrain output with a grammar. That is
    the separate CodeBrix.Ollama.ModelRunner package. No native library and
    no GPU anywhere in this one; it hands you file paths.
  - Listen on a port or talk to a daemon. It is not a server -- no listener,
    no endpoint -- and it does not need Ollama installed: no binary is looked
    for, no process started, no configuration file of Ollama's read.
  - Talk to `ollama serve`. No client for Ollama's HTTP API, no /api/tags, no
    model unloading, no keep-alive. No history, no key pair, no settings and no
    OLLAMA_HOST either: OLLAMA_MODELS is the one environment variable read.
  - Garbage-collect a store on its own. Blobs are removed by DeleteAsync, by
    the pruning PullAsync and CreateAsync do for the name they just wrote, and
    by PruneAsync when you call it; nothing runs in the background.
  - RUN the files of a bundle, or CONVERT a checkpoint without the publisher's
    own tooling. They are fetched, verified, stored and laid out again as the
    publisher's tree, and the one conversion this package offers - exporting to
    ONNX - is this library driving the ONNX Runtime GenAI model builder or
    Hugging Face Optimum in an interpreter YOU already have, never a
    re-implementation of either. Nothing here loads a checkpoint itself or runs
    a model of any kind. Quantizing an ONNX graph IS done here - that is what
    the managed reduction engine is - but it is the only arithmetic this
    package does on a model's weights. What you do with the files is yours.
  - DECIDE ANYTHING ABOUT A LICENCE. It reports what a source states - an
    identifier, the address it was read from, the LICENSE text a bundle ships
    - and applies no rule of its own: no pull is refused over a licence or the
    absence of one, nothing is interpreted, and there is no policy to
    configure.
  - Reach a source that needs an INTERACTIVE SIGN-IN. A share link only a
    browser session can follow is out of reach whatever token you hold. Fetch
    it by hand and hand the folder to ImportBundleAsync.
  - Offer synchronous APIs, or run on .NET below 10.0.

This package IS for: keeping a local, Ollama-compatible model store; pulling
models into it with resumable, verified downloads, from an Ollama-protocol
registry or from the Hugging Face repository, the list of addresses or the
folder a publisher keeps a model in; listing, describing, copying, deleting and
deriving models; exporting a bundle to ONNX and reducing the graphs it holds to
smaller ones, keeping each result in the same store with its provenance; laying
a bundle out as its publisher's own file tree; parsing and writing Modelfiles;
reading GGUF headers; and turning a name into the paths a runner opens.


WORKING EXAMPLES ON GITHUB
==========================

The test suite is the largest body of compiling, working usage of this
package. It runs OFFLINE: an in-memory registry double (an HttpMessageHandler
supplied through ModelStoreOptions.HttpMessageHandler) and a temporary store
directory stand in for the network and the disk, so every test is a worked
example you can run without a connection:

    https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelManager.Tests

Feature-to-test-file map:

  Model names: the grammar, defaults, validation, display forms and relative
  paths, against upstream's own case tables
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Names/ModelNameTests.cs

  Pulling: the status stream in order, manifest bytes kept verbatim, cache
  hits, layer replacement and pruning, safetensors refusal, digest mismatch,
  cancellation, Hugging Face names
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStorePullTests.cs

  The registry client: manifest and blob addresses, the user agent, 404 and
  401 handling, bearer-token retry, the http:// refusal
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Registry/RegistryClientTests.cs

  Blob downloads: byte-range splitting, resume from the sidecar, stalled
  parts, redirects that keep the token off another host, digest failures,
  monotonic progress, servers that ignore ranges
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Registry/BlobDownloadTests.cs

  WWW-Authenticate challenge parsing
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Registry/RegistryChallengeTests.cs

  Resolving a name to files, shards, projectors and drafts
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreResolveTests.cs

  Listing and showing, decoded layers, GGUF metadata, rendered Modelfile
  text, the argument and name errors
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreListShowTests.cs

  Capability inference from config, weights, projectors and templates
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelCapabilitiesTests.cs

  Copy and delete, and which shared blobs survive
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreCopyDeleteTests.cs

  Creating from a Modelfile: FROM a file or a model, template override and
  parameter merge, shared blobs, pruning on replace, adapter and DRAFT
  refusals
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreCreateTests.cs

  Layers: blobs from streams, bytes, text, files and existing blobs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/LayerFactoryTests.cs

  Pruning: unreferenced blobs, the grace period, this library's own sidecars
  and nothing else
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStorePruneTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/LayerPrunerTests.cs

  The store layout, manifest files and digests
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStorePathsTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ManifestFilesTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/Sha256DigestTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/HumanFormatTests.cs

  Modelfile parsing against upstream's cases: commands, quoting, line
  numbers, the typed parameters and their errors, Go boolean spellings
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Modelfile/ModelfileTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Modelfile/ModelfileCommandTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Modelfile/ModelfileParametersTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Modelfile/GoBoolTests.cs

  GGUF headers: every scalar and array type, key qualification, omitted
  arrays, the convenience properties, tensor arithmetic and the type tables
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Gguf/GgufMetadataTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Gguf/GgufValueTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Gguf/GgufTensorInfoTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Gguf/GgufTensorTypesTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Gguf/GgufFileTypesTests.cs

  Pulling a bundle: the progress vocabulary, one layer per file, the config
  that records the source, the revision and the licence, the filter, the
  licence and readme that are pulled whatever the filter says, the second pull
  that costs nothing, the revision that moved, and the plain overload that
  still means the registry
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreBundlePullTests.cs

  Resolving and materializing a bundle: Format and Files, hard links, copies
  and symbolic links, what is already there, and the two things that are
  refused
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreBundleResolveTests.cs

  Importing a folder: the layers and config it writes, linking against
  copying, where the licence comes from, and the folders that are refused
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreBundleImportTests.cs

  A bundle through the operations it was meant to leave alone: listed beside a
  GGUF model and told apart by its format, shown, copied, deleted and pruned
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreBundleListShowDeletePruneTests.cs

  The sources: the Hugging Face listing and its commit pinning, hashes,
  filters, licence reporting and credential refusals; a plain list of
  addresses and the servers that will not answer a HEAD; a storage bucket's
  XML listing, its paging and its hash headers
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Sources/HuggingFaceHubSourceTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Sources/HttpFileListSourceTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Sources/GoogleCloudStorageListingTests.cs

  Downloading by address: ranged parts, the sidecar a second run resumes from,
  the redirect that the token never follows, verification against a stated
  sha256 or md5, and the sha256 computed when nothing is stated
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Registry/FileDownloadTests.cs

  The bundle vocabulary: what a definition insists on and fills in, what a
  file accepts and refuses, the glob rules and the always-kept licence and
  readme, and the options a pull starts from
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Bundles/BundleDefinitionTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Bundles/BundleFileTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Bundles/FileFilterTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Bundles/PullOptionsTests.cs

  Exporting to ONNX: what each route needs, how the automatic one decides,
  the pass-through that copies nothing, the derived bundle's provenance, and
  the names and refusals an export can produce
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreExportTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Export/OnnxExportTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreDerivedBundleTests.cs

  Reducing an ONNX model: the four modes, the engine the automatic choice
  reaches, the derived names, the files carried through, and whole reductions
  run by the managed engine with no interpreter in the process at all
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreReduceTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Reduce/OnnxReduceTests.cs

  Asking for Python and being told what is missing: the report a machine with
  no CPython produces, the resolution order, the two exceptions, and the test
  that proves a whole import, list, resolve and materialize cycle never loads
  the dependency at all
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Python/PythonSupportTests.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Python/PythonInertFenceTests.cs

  The tests that need a real CPython, in a second suite of their own, skipped
  by default: exporting and reducing real published models, and the comparison
  that requires the managed engine and ONNX Runtime's own tools to write the
  same bytes
    https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelManager.Python.Tests

  The live tests, skipped by default, that pull real repositories and real
  bucket objects from their publishers, check every file against what was
  stated, materialize the tree and delete everything again
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/MusicModelLiveTests.cs

  The one live test, skipped by default, that pulls a real model from
  registry.ollama.ai and then resolves, lists and deletes it
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Store/ModelStoreLiveTests.cs

  Test infrastructure worth copying into your own tests: the registry double,
  an in-memory Hugging Face Hub, an in-memory storage bucket, the byte-range
  and failure-injection base they share, a GGUF file builder, a fake model
  builder and a temporary store directory
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/FakeRegistryHandler.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/FakeHubHandler.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/FakeBucketHandler.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/FakeHttpHandlerBase.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/GgufTestFileBuilder.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/FakeModelBuilder.cs
    https://github.com/ellisnet/CodeBrix.Ollama/blob/main/tests/CodeBrix.Ollama.ModelManager.Tests/Infrastructure/TempStoreDirectory.cs


QUICK REFERENCE CARD
====================

INSTALL     dotnet add package CodeBrix.Ollama.ModelManager.MitLicenseForever
USING       using CodeBrix.Ollama.ModelManager;
TARGET      .NET 10 or later   LICENSE  MIT
DEPENDENCIES  one, CodeBrix.Python, inert until a Python feature is used
            no native library ships in the package
NULLABLE    off in the library -- read the per-member notes
STORE       using var store = new ModelStore();
DEFAULT DIR OLLAMA_MODELS, else ~/.ollama/models
LAYOUT      <store>/blobs/sha256-<hex>, plus the in-progress sidecars
            <blob>.codebrix-partial and <blob>.codebrix-parts.json
            <store>/manifests/<host>/<namespace>/<model>/<tag>
NAMES       [scheme://][host/][namespace/]model[:tag]; defaults
            registry.ollama.ai / library / latest / https;
            hf.co/<user>/<repo>:<quant> reaches Hugging Face;
            ModelName.TryParse(s, out name) -- parsing never throws
OPERATIONS  PullAsync (lazy, resumable), ListAsync (newest first),
            ExistsAsync, ShowAsync (opens GGUF headers), ResolveAsync (paths
            only), CopyAsync, DeleteAsync, CreateAsync, PruneAsync(grace)
PULL STATUS pulling manifest -> pulling <12 hex> (per layer) -> verifying
            sha256 digest -> writing manifest -> [removing unused] -> success
BUNDLES     a model that is not GGUF on a registry - a Hugging Face file
            repository, a list of HTTPS addresses, a folder on disk - stored
            in the same blobs and manifests. ASK FOR IT:
            PullAsync(name, PullOptions.ForHuggingFace(repo, rev, filter)) |
            PullAsync(name, PullOptions.ForFileList(files, filter));
            PullOptions.RequireHashes, PullOptions.DefaultRevision "main"
SOURCES     HuggingFaceHubSource(repository, revision, filter, options),
            HttpFileListSource(files, filter[, options]) -> ListAsync ->
            BundleListing (RepositoryId, ResolvedRevision, License, Files,
            TotalBytes); GoogleCloudStorageListing.ListAsync(bucket, prefix,
            handler) -> IReadOnlyList<BundleFile>
FILTERS     new FileFilter(includes, excludes) | FileFilter.Default |
            FileFilter.ExcludeTrainingArtifacts; globs over the whole path,
            ** crosses separators; LICENSE and README are always kept
DESCRIBE    BundleDefinition.ForHuggingFace / ForFileList / ForRegistry ->
            ToPullOptions(); LicenseRecord(licenseId, licenseSource, note) |
            LicenseRecord.None - REPORTED, never applied
BUNDLE OUT  MaterializeAsync(name, directory, MaterializeOptions { Link =
            Hardlink | Copy | Symlink, Overwrite }) -> the paths written;
            ImportBundleAsync(name, directory, ImportOptions { Filter, Link,
            License }) for a folder, by convention under host "local"
BUNDLE IN   ResolvedModel.Files (ResolvedFile: Name, BlobPath, Size, Digest)
            and .Format; ModelInfo.License, .Format and .Licenses;
            ModelSummary.Config.ModelFormat tells a bundle from a GGUF model
BUNDLE STATUS   listing <repository> -> pulling <path> (per file) -> verifying
            sha256 digest -> writing manifest -> success
EXPORT      ExportToOnnxAsync(name, ExportOptions { Route, Precision,
            OutputName, Overwrite, AllowRemoteCode }, IProgress<PullProgress>,
            ct) -> ExportResult { Name, Files, Tool, ToolVersion, RouteUsed;
            ToString() }. ExportOptions.DefaultPrecision is what an unset
            Precision reads as ("fp32").
            Routes (ExportRoute): Auto (default) | PublisherOnnx (NO PYTHON,
            no bytes copied) | GenAiBuilder (onnxruntime_genai, torch,
            transformers, onnx) | Optimum (optimum, onnx, torch, transformers).
            Auto: ships .onnx -> PublisherOnnx; config.json names an
            architecture the builder writes -> GenAiBuilder; else Optimum.
            AllowRemoteCode runs the PUBLISHER'S OWN Python; false by default.
            TMPDIR must be on a real file system for a converting route
EXPORT STATUS   materializing source -> exporting -> collecting -> writing
            manifest -> success
REDUCE      ReduceOnnxAsync(name, ReduceOptions { Mode, BlockSize, IsSymmetric,
            AccuracyLevel, Preprocess, OutputName, Overwrite, Engine, Files },
            IProgress<PullProgress>, ct) -> ReduceResult { Name, Files,
            EngineUsed, Mode, SourceBytes, ReducedBytes, Tool, ToolVersion;
            ToString() }. ReduceOptions.DefaultBlockSize is the block size an
            untouched ReduceOptions carries (128).
            Modes (ReduceMode): DynamicInt8 (default, MatMulInteger, ~3.5x) |
            WeightOnlyInt8 (MatMulNBits, about 3.5x) | WeightOnlyInt4
            (MatMulNBits, about 6x) | PreprocessOnly (prepare, quantize
            nothing). Defaults: block 128, asymmetric, no accuracy level - ONNX
            Runtime's own. Engine (ReduceEngine): Auto (default) | Python
            (onnx, onnxruntime) | Managed (this library's own code; NEEDS
            NOTHING INSTALLED). Auto: weight-only -> Managed always;
            DynamicInt8 -> Managed on a prepared graph, else Python;
            PreprocessOnly -> Python. Both engines write the same bytes.
            Files: null = every .onnx; naming some carries the rest through
            unchanged. Only MatMul weights shrink, so a graph of embeddings
            shrinks less. TMPDIR must be on a real file system, whichever
            engine runs. A REDUCED MODEL IS AN APPROXIMATION
REDUCE NAME the source's tag plus the mode's: int8 | int8-weights |
            int4-weights | onnx-preprocessed. No tag of its own (or "latest")
            takes the mode's tag alone; a tag is never repeated, so :onnx
            prepared reads :onnx-preprocessed
REDUCE STATUS   materializing source -> reducing <file> (one per graph) ->
            collecting -> writing manifest -> success
DERIVED     a bundle this library produced. ModelInfo.DerivedFrom, .Tool,
            .ToolVersion, .Settings (all null for a bundle that was obtained);
            the config keys are on ModelConfigKeys (DerivedFrom, Tool,
            ToolVersion, Settings, DerivedAt, beside the Source, Repository,
            Revision, LicenseId, LicenseSource and PulledAt an obtained bundle
            writes). Default name: the source with its tag replaced by "onnx"
            for an export, and with the mode's tag added for a reduction.
            It is a SNAPSHOT; deleting it leaves the source alone
MODELFILE   Modelfile.Parse(text) | Parse(reader) | ReadFileAsync(path); FROM
            LICENSE TEMPLATE SYSTEM ADAPTER DRAFT RENDERER PARSER PARAMETER
            MESSAGE REQUIRES;  GetParameters() -> ModelParameters
GGUF        GgufMetadata.ReadAsync(path | stream, options, ct); header only,
            never tensor data; MaxArraySize 1024 (negative keeps everything)
PYTHON      needed by the converting export routes and by the Python
            reduction engine, and by nothing else - obtaining a model and
            making an existing graph smaller are pure managed code.
            PythonSupport.Check(options, modules) -> PythonSupportReport
            (never throws); PythonSupport.Require(options, feature, modules)
            (throws); PythonSupport.Shutdown() once, at the end;
            PythonSupport.IsInitialized, PythonSupport.Owner
PYTHON WHERE  PythonOptions { VirtualEnvironment, LibraryPath }, on
            ModelStoreOptions.Python and on every PythonSupport call. venv:
            code, then CODEBRIX_OLLAMA_PYTHON_VENV, then PYTHONNET_VENV /
            VIRTUAL_ENV. library: code, then PYTHONNET_PYDLL, then the venv's
            base interpreter. Code always beats an environment variable. The
            two variable names are the consts
            PythonOptions.VirtualEnvironmentVariable and .LibraryPathVariable
PYTHON REPORT  PythonSupportReport { LibraryPath, LibrarySource, LibraryLoads,
            Version, IsSupportedVersion, VirtualEnvironment,
            VirtualEnvironmentSource, IsInitialized, Owner, Modules, Problems,
            IsUsable }; Modules is PythonModuleReport { Name, IsInstalled,
            Error } in the order asked for; Problems is empty when nothing is
            wrong. PythonLibrarySource: NotFound | Code | EnvironmentVariable
            | VirtualEnvironment | Host. PythonVirtualEnvironmentSource: None |
            Code | EnvironmentVariable | Inherited. PythonEngineOwner: None |
            ModelManager | Host
PYTHON OWNER  ModelManager when this library started the interpreter (it
            configures it, bounds the process-exit shutdown and ends it on
            Shutdown); Host when your application started one first (it
            configures nothing and never ends it). ModelStore.Dispose does NOT
            shut Python down.
ERRORS      ModelManagerException: DigestMismatchException, GgufFormatException,
            InvalidModelNameException, ModelfileParseException,
            ModelNotFoundException, PythonModuleNotInstalledException,
            PythonNotAvailableException, PythonScriptException,
            RegistryException. The Python three carry Feature; the module one
            also ModuleName and the static
            PythonModuleNotInstalledException.InstallCommand(module, venv),
            which builds the same pip line the message carries; the script one
            also ScriptName and PythonMessage
RUNNER      ResolvedModel.ModelPath is what an in-process GGUF runner loads.
            CodeBrix.Ollama.ModelRunner is a separate package with its own
            AGENT-README.


================================================================================
END OF AGENT-README
