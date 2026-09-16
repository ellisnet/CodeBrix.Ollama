# CodeBrix.Ollama

Two cross-platform, zero-dependency .NET libraries for **downloading, managing and running
large language models in-process**, with no Ollama installation and no server:

* **CodeBrix.Ollama.ModelManager** - a local model store laid out exactly as Ollama's own
  (`blobs/sha256-<hex>` and `manifests/<host>/<namespace>/<model>/<tag>`), fed from any registry
  that speaks Ollama's manifest-and-blob protocol (`registry.ollama.ai` by default, Hugging Face
  through `hf.co/<user>/<repo>:<quant>` names). It pulls with resumable parallel downloads and
  sha256 verification, lists, shows, copies, deletes and creates models from Modelfiles, reads
  GGUF metadata, and resolves a model name to the GGUF files on disk. Pure managed code.
* **CodeBrix.Ollama.ModelRunner** - runs a GGUF model in-process over a self-built native
  inference engine, bound through hand-written P/Invoke and exposed to the application through
  an interface contract, with one native library per supported platform.

The two packages do not depend on each other. An application asks ModelManager for the path of
a model and hands that path to ModelRunner, or to any other in-process GGUF runner.

Both are provided as .NET 10 libraries and associated `*.MitLicenseForever` NuGet packages, and
support applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Ollama.ModelManager.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain
`CodeBrix.Ollama.ModelManager`:

* NuGet package ID: `CodeBrix.Ollama.ModelManager.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.Ollama.ModelManager` - i.e. `using CodeBrix.Ollama.ModelManager;`

The `.MitLicenseForever` suffix belongs to the package ID only; it never appears in a namespace, a
using directive or a type name. Each library declares exactly one namespace, and every public type
lives in it - the folders you see in the repository are file organization, not namespaces.

XML documentation (IntelliSense) ships alongside the assembly.

The package has no NuGet dependencies: its dependency group is empty and it pulls in nothing but
the .NET runtime. Downloads go through the in-box `HttpClient`, JSON through `System.Text.Json`
and hashing through `System.Security.Cryptography`.

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

## Requirements

* Outbound HTTPS to the registry a model name refers to, for pulls only. Everything else works
  offline against the local store.
* Disk space for the store; a pull writes to the store directory and nowhere else.

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

## Documentation

Each NuGet package includes its own `AGENT-README.txt`, a complete API reference and usage guide
written for AI coding agents - point your agent at that file when it is writing code against the
library.

Additional sample code and usage examples are available in the `CodeBrix.Ollama.ModelManager.Tests`
project:
https://github.com/ellisnet/CodeBrix.Ollama/tree/main/tests/CodeBrix.Ollama.ModelManager.Tests

## License

CodeBrix.Ollama is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Ollama/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
these packages, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Ollama/blob/main/THIRD-PARTY-NOTICES.txt).
