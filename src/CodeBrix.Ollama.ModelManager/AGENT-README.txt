================================================================================
AGENT-README: CodeBrix.Ollama.ModelManager
A Guide for AI Coding Agents -- CONSUMING the
CodeBrix.Ollama.ModelManager.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Ollama.ModelManager is a cross-platform, zero-dependency .NET 10
library that maintains a local store of large language models in exactly
Ollama's on-disk layout, pulls models into it from any registry that speaks
Ollama's manifest-and-blob protocol, and resolves a model name to the GGUF
files on disk that an in-process runner loads.

It does three things, and they build on one another:

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
  3. THE FILE FORMATS -- a Modelfile parser that accepts what Ollama's parser
     accepts, with the same messages and line numbers, and a GGUF header
     reader that returns every key-value and tensor descriptor without ever
     touching tensor data.

Everything that touches the disk or the network is async and takes a
CancellationToken last, with a default; name parsing, Modelfile parsing and the
tensor-type arithmetic are ordinary synchronous methods, and there are no
synchronous wrappers over the async work.

Target framework: .NET 10 or later; no netstandard and no .NET Framework
target. Source: https://github.com/ellisnet/CodeBrix.Ollama

WHAT THIS PACKAGE IS NOT
------------------------
  - NOT an Ollama client. It never talks to a running `ollama serve`, never
    calls /api/generate or /api/chat, never starts or looks for a daemon. It
    talks to a model REGISTRY over HTTPS and to the FILE SYSTEM, nothing else.
    It is NOT a server either: no listener, no port, no endpoint.
  - It does NOT run models: no inference, no tokenizer, no embeddings, no
    native code, no GPU. It hands you file paths. It does NOT need Ollama
    installed either -- no binary is looked for, no process started, no
    configuration file of Ollama's read.
  - It does NOT convert safetensors: a manifest carrying safetensors layers is
    refused by the pull, and a create can only import GGUF files. It does NOT
    render templates either -- a prompt template is returned as stored text,
    and evaluating Go text/template syntax is somebody else's job.

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

HOW THIS RELATES TO CodeBrix.Ollama.ModelRunner
-----------------------------------------------
The same repository produces a second, SEPARATE package,
CodeBrix.Ollama.ModelRunner.MitLicenseForever, which loads a GGUF file and runs
it in-process. It is still being written and is NOT published yet; do not add a
PackageReference to it on the strength of this document. The two packages are
independent -- neither references the other, and neither is or contains an HTTP
server. The seam between them is ResolveAsync: the paths in the ResolvedModel
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
CodeBrix.Ollama.ModelManager. NuGet dependencies: NONE -- the dependency group
is empty, JSON goes through the in-box System.Text.Json, hashing through
System.Security.Cryptography, and there is no native component. License: MIT,
with license acceptance required.

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

That is the whole story. The library declares ONE namespace, and all
thirty-four public types live in it. The repository folders (Common/, Names/,
Gguf/, Modelfile/, Store/, Registry/) are FILE ORGANIZATION ONLY, not
namespaces: `using CodeBrix.Ollama.ModelManager.Store;` is a CS0246 error.
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


QUICK START
===========
    using System;
    using System.Threading.Tasks;
    using CodeBrix.Ollama.ModelManager;

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

ModelStore is IDisposable: disposing releases the registry client and its
pooled connections and leaves the store directory alone. Create one and keep it
-- every operation after Dispose throws ObjectDisposedException.


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
      RegistryException            an error status, a 401, a transport failure
                                   that survived every retry, an insecure
                                   http:// name, or an answer that is not a
                                   manifest. StatusCode is null when no
                                   response arrived; ResponseBody holds the
                                   body when one was read, else null

Framework exceptions you will also see: ArgumentException (a null or blank
model name or store directory), ArgumentNullException (a null Modelfile, a null
GGUF path or stream), ObjectDisposedException (any operation after Dispose),
OperationCanceledException, InvalidOperationException (ToRelativePath on a name
that is not fully qualified), UriFormatException (BaseUrl), and IOException and
UnauthorizedAccessException from the file system, unwrapped. A
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


WHAT THIS PACKAGE DOES NOT DO
=============================
Do NOT reach for this package to:

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
    the separate CodeBrix.Ollama.ModelRunner package, still in progress.
  - Talk to `ollama serve`. No client for Ollama's HTTP API, no /api/tags, no
    model unloading, no keep-alive. No history, no key pair, no settings and no
    OLLAMA_HOST either: OLLAMA_MODELS is the one environment variable read.
  - Garbage-collect a store on its own. Blobs are removed by DeleteAsync, by
    the pruning PullAsync and CreateAsync do for the name they just wrote, and
    by PruneAsync when you call it; nothing runs in the background.
  - Offer synchronous APIs, or run on .NET below 10.0.

This package IS for: keeping a local, Ollama-compatible model store; pulling
models into it with resumable, verified downloads; listing, describing,
copying, deleting and deriving models; parsing and writing Modelfiles; reading
GGUF headers; and turning a name into the paths a runner opens.


COMMON MISTAKES
===============
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


QUICK REFERENCE CARD
====================

INSTALL     dotnet add package CodeBrix.Ollama.ModelManager.MitLicenseForever
USING       using CodeBrix.Ollama.ModelManager;
TARGET      .NET 10 or later   DEPENDENCIES  none   LICENSE  MIT
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
MODELFILE   Modelfile.Parse(text) | Parse(reader) | ReadFileAsync(path); FROM
            LICENSE TEMPLATE SYSTEM ADAPTER DRAFT RENDERER PARSER PARAMETER
            MESSAGE REQUIRES;  GetParameters() -> ModelParameters
GGUF        GgufMetadata.ReadAsync(path | stream, options, ct); header only,
            never tensor data; MaxArraySize 1024 (negative keeps everything)
ERRORS      ModelManagerException: DigestMismatchException, GgufFormatException,
            InvalidModelNameException, ModelfileParseException,
            ModelNotFoundException, RegistryException
RUNNER      ResolvedModel.ModelPath is what an in-process GGUF runner loads.
            CodeBrix.Ollama.ModelRunner is a separate package, in progress.


================================================================================
