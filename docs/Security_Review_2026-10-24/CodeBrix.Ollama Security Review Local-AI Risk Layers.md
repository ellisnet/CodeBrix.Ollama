# CodeBrix.Ollama Security Review: Local-AI Risk Layers

Oct 24, 2026 · @Jeremy Ellis

## Summary

ModelRunner's design removes two of the video's five risk layers outright: no listener or API (Layer 4) and no tool execution (Layer 5). Its headline exploits do not reproduce against this code either: Llama Drama (template code execution) and Bleeding Llama (false tensor sizes) are closed at the pinned llama.cpp b10221. What remains is narrower but real: template denial of service, special-token injection, no template pinning, an unverified native library, and in-process blast radius. ModelManager carries most of the open work, mainly around pinning and verifying what it downloads. **Update 2026-10-24:** the five most urgent fixes below and the rest of Phase 1 are now in the code; see Mitigation Status.

Reviewed: `ellisnet/codebrix.ollama` at commit `d4674c2` (2026-10-17), all of `src/`, `samples/ModelQueryTool`, `llama-native-tools/` and the relevant parts of vendored llama.cpp. Crafted GGUF files were run against the shipped linux-x64 native to confirm loader behaviour.

| Video layer | ModelRunner | ModelManager | Deciding fact |
| --- | --- | --- | --- |
| 1. Malicious model files and repos | Partly applies | Applies | No loader scripts run on pull; but pins are weak, non-LFS files unhashed, native library not hash-checked |
| 2. Chat templates | Partly applies | Partly applies | Custom sandboxed engines (no code execution); poisoned-template injection, DoS and pinning gaps remain |
| 3. Runner parsing | Mostly mitigated | Mostly mitigated | b10221 bounds-checks tensor data; gaps in LoRA, ONNX single-file route, protobuf depth |
| 4. Network exposure | Does not apply | Outbound only | No listener anywhere; ModelManager's HTTP client has token-scoping and redirect gaps |
| 5. Tool access and backdoors | Does not apply directly | n/a | Tool calls are parsed, never executed; name matching and argument validation are loose |

Most urgent fixes, all verified in source:

1. **Hugging Face commit pinning is silently ignored** (ModelManager): asking for a 40-hex commit downloads the head of `main`.
2. **Bearer token is not host-scoped** (ModelManager): any URL-list host that answers 401 receives it, including over `http://`.
3. **Native library is identity-checked, not integrity-checked** (ModelRunner): a forged `codebrix_llama` passes; OS search-path fallback makes planting possible.
4. **ONNX single-file load follows absolute and `../` external-data paths** (ModelRunner).
5. **Templates have no render budget** (ModelRunner): a hostile template can hang the engine thread or crash the process with a stack overflow.

## Mitigation Status (2026-10-24)

The review's Phase 1 and four low-risk Phase 2 items are now in the codebase. They ship in the next ModelManager and ModelRunner packages. Gate: Debug and Release, 0 warnings, 5,676 offline tests, 0 failed. Opt-in Python live suite: transformers 5.20.2 venv 43 / 44 (the one failure is the Optimum test; optimum is not installed there). On the 4.57.6 venv the Optimum export passes with the opt-in, and the GenAI builder exports are refused by design.

### Fixed

| # | Finding | Package | What changed |
| --- | --- | --- | --- |
| 1 | HF commit pin ignored | ModelManager | Every non-`main` revision, commits included, is read through `/revision/{rev}`. A Hub answer naming a different commit is refused. A test pins a commit other than `main`'s head. |
| 2 | Bearer token not host-scoped | ModelManager | Token sent only over https and only to its own origin (scheme + host + port): the registry the model name points at, or the Hugging Face Hub. "Established" state is tracked per origin. Never offered to a URL-list host, a redirect target, or a 401 from any other origin. Doc comments corrected. |
| 3 | Native library identity-checked only; OS search-path fallback | ModelRunner | The SHA-256 of every shipped native (all 7 RIDs) is embedded from `Native/SHA256SUMS`. Each candidate is hashed before `NativeLibrary.Load` and refused on mismatch; the identity probe still runs. The bare-name OS search is off unless `ModelRunner.AllowSystemNativeLibrary = true`, and a library loaded that way is reported as not hash-checked. `[DefaultDllImportSearchPaths(SafeDirectories)]` added. A test fails if any native drifts from the table. |
| 4 | ONNX single-file route follows absolute / `../` external data | ModelRunner | `ForFile` now confines side files to the graph's folder, as the folder route does. |
| 5 | No template render budget; unguarded statement nesting | ModelRunner | New `ModelRunnerOptions.TemplateLimits`: steps 1M, output 8M chars, string 8M, collection 1M items, nesting 256, timeout 10 s. Covers both the Jinja and the Go dialect; recursion and self-containing values are depth-checked, with a stack-sufficiency backstop. The request's `CancellationToken` reaches both renderers. Jinja and Ollama dialects now render off the engine thread. |
| 6 | No protobuf nesting-depth limit | Core | Sub-graph depth capped at 64 in `OnnxGraphProto.Parse` and in `EnumerateGraphTensors`. |
| 7 | CVE-2026-9856 (`chat_template` names) and no Python version floors | ModelManager | Before any Python runs, `tokenizer_config.json`, `chat_template.json` and `processor_config.json` are read in .NET; template names outside `^[A-Za-z0-9_.-]{1,64}$` (and `.`/`..`) are refused. Version floors: torch 2.6, onnx 1.16, transformers 5.10, reported by `PythonSupport.Check` and enforced by `Require` (`PythonModuleTooOldException`). |
| 8 | No security-rebase policy | Release | "SECURITY REBASE POLICY" section in `llama-native-tools/README.txt`. `UPSTREAM.txt` records "Last checked against advisories: 2026-10-24, for b10221"; all 13 llama.cpp GitHub advisories were fixed at or before b8146. |
| P2 | Non-LFS HF files unhashed | ModelManager | Verified against the tree API's git blob SHA-1 when no sha256 is stated. A git SHA-1 now satisfies `RequireHashes`. |
| P2 | No public at-rest verification | ModelManager | `IModelStore.VerifyAsync(name)` re-hashes every layer and reports `VerifyResult` / `VerifyFailure` without deleting anything. A bundle pull's existence-and-size step now reports "checking stored files" rather than "verifying sha256 digest". |
| P2 | Redirects to any host and scheme; URL lists accept `http://` | ModelManager | https→http refused. Redirects to loopback, private, link-local or unspecified IP literals (and `localhost`) refused unless the request started there. URL-list `http://` entries need `AllowInsecureHttp`. |
| P2 | Path checks miss ADS, reserved names, case collisions; digest `$` | ModelManager | Bundle paths refuse `:`, control characters, Windows device names, trailing dot or space, and case-insensitive duplicates. `Sha256Digest` anchors with `\z`, and upper-case hex is normalized to lower case. |

### Consumer opt-ins added

- **`ExportOptions.AllowUnpatchedTransformers`** (default false): waives only the transformers 5.10 floor, for the GenAI builder and Optimum. It exists because optimum-onnx does not yet install beside transformers 5.x. The .NET `chat_template` check still runs, the torch and onnx floors still apply, and `Check` still reports the shortfall.
- **`ModelRunner.AllowSystemNativeLibrary`** (default false): restores the OS search path as a last resort, without a hash check.

### Behaviour changes callers will notice

- `http://` URL lists and https→http redirects need `AllowInsecureHttp`.
- Private-address redirects are refused.
- A natively built or system `codebrix_llama` needs `AllowSystemNativeLibrary`.
- `OnnxModel.LoadAsync(path)` no longer reads side files outside the graph's folder.
- Templates exceeding a limit throw `ChatTemplateException`. Jinja `range()` is capped at 1M items (was 10M).
- Python export and reduce require torch ≥ 2.6 and onnx ≥ 1.16. Export requires transformers ≥ 5.10 unless the opt-in is set.
- A Hugging Face pull with `RequireHashes = true` now succeeds on small files verified by git SHA-1.
- A listing containing a refused path or duplicate fails with `ArgumentException`.

### Still open

Everything in Phases 2 and 3 not listed above, notably:
- special-token escaping
- template SHA-256 and `TemplatePolicy`
- tool-call name and schema validation
- `CheckTensors` default
- `GgufReader` into Core and pre-validation
- LoRA bounds patch
- `HashPolicy`, `ModelLock`, `VerifyOnResolve`, read-only materialize, `SafeDefault`, `PublisherPolicy`
- size caps, and symlink handling on import
- digest-bearing provenance
- out-of-process hosts

Two residuals from this round:
- Redirect checks do not resolve DNS, so a public name pointing at a private address passes.
- Core's own `OnnxModel` external-data lookup is not confined. Its only caller, ModelManager's reduce, checks every side file against the bundle root first.

Release hygiene (signing, notarization, SourceLink, `SECURITY.md`) is unchanged.

## Layer 1: Model Files, Repositories and Runner Binaries

Nothing CodeBrix.Ollama downloads is ever executed by a pull, so the video's "run this loader script" infostealer has no path in. The video's hash point does apply: every SHA-256 check today proves only that bytes match what the same server said, and the mechanisms that would add authenticity (pins) are missing or broken.

**Does not apply**

- Pull, list, resolve and materialize never execute downloaded content. No auto-updater, no runtime native download; natives ship inside the NuGet package.
- Publisher Python runs only with `ExportOptions.AllowRemoteCode = true` (default false, documented as dangerous). `torch.load` uses `weights_only=True`; the managed converter uses an allow-list `RestrictedUnpickler` that never imports or evaluates.
- Shipped native binaries match the SHA-256s recorded in `BUILD-PROVENANCE.txt` (all 7 recomputed and matched).

**Applies**

| Finding | Package | Severity | Evidence |
| --- | --- | --- | --- |
| HF commit pin ignored: 40-hex revision lists `main` HEAD instead | ModelManager | High | `HuggingFaceHubSource.cs` `ReadRepositoryAsync` skips `/revision/` for commits; `ReadCommit` prefers the document's `sha` |
| No way to pin a registry manifest digest; manifest trusted from same server | ModelManager | Medium | `ModelName` rejects `@digest`; no `PullOptions` field |
| Non-LFS HF files unhashed (`config.json`, `tokenizer_config.json`, `chat_template.jinja`, `*.py`); git SHA-1 recorded but never checked | ModelManager | Medium | `BundleFile.cs:73-77, 119-121` |
| Store contents never re-hashed after pull; no public verify API; store shared with real Ollama by default | ModelManager | Medium | `ResolveAsync`, cache-hit `skipVerify`; `VerifyBlobAsync` is internal |
| Default filter downloads `*.py`, `*.pkl`, `*.pt`, `.sh`, `.exe`; no executable-content warning | ModelManager | Medium | `FileFilter.Default` keeps everything |
| Native library identity-checked, not integrity-checked: forged `codebrix_llama` passes | ModelRunner | High | `NativeLibraryLoader.cs:183-206` compares strings from `codebrix_llama.c:33-41` |
| Native loader falls back to OS search path (`LD_LIBRARY_PATH`, `DYLD_*`, DLL search order) | ModelRunner | Medium | `NativeLibraryLoader.cs:419-425` |
| Windows DLLs unsigned; macOS dylibs ad-hoc signed, not notarized; NuGet packages unsigned | Release | Medium | `BUILD-PROVENANCE.txt` |
| Sample's pinned weights digest is reported and never enforced | ModelQueryTool | Low | `KnownModels.cs:26-27`, `ModelStager.cs:570-590` |

## Layer 2: Chat Templates

Llama Drama-style code execution does not apply: both template engines are CodeBrix's own interpreters with no reflection, no I/O and an attribute allow-list. Pillar Security's poisoned-template attack does apply, as it does to every engine they tested (llama.cpp, Ollama, vLLM, SGLang), because a template that conditionally injects text needs no code execution. ModelRunner currently offers no way to pin, hash or analyse the template it runs.

**Does not apply**

- Jinja engine: statements limited to `if/for/set/macro/call/filter/generation/with/break/continue/do`; no `include`, `import` or `extends`; globals limited to `range`, `namespace`, `dict`, `raise_exception`, `strftime_now` (`JinjaFunctions.cs:14-17`). Attribute access reaches only dictionary keys and named string/list/dict methods (`JinjaValues.cs:824-853`), so no `__class__` or `__globals__` escape.
- Context values are deep-copied into plain data; `ToolDefinition` objects never reach a template as live CLR objects.
- Ollama Go engine: no reflection, no `call` built-in, `{{template}}` resolves only same-text definitions.
- llama.cpp's own minja engine is not compiled in (`LLAMA_BUILD_COMMON=OFF`), so CVE-2026-18581 (minja parser `abort()`) does not apply.
- No network path can rewrite a template (Layer 4).

**Applies**

| Finding | Severity | Evidence |
| --- | --- | --- |
| No render budget: unbounded loops (Go `{{range 1000000000000}}`), string repeat and padding widths, no output cap, no cancellation mid-render; engine thread hangs and `Dispose` gives up after 30 s | High | `JinjaRenderer.cs:152-173`, `JinjaValues.cs:439-451`, `GoFormat.cs:147-199`, `EngineWorker.cs:115` |
| Statement nesting and long flat chains are not depth-guarded; very likely an uncatchable stack overflow that kills the host process (reasoned, not reproduced) | High | `JinjaParser.cs:17` guard covers expressions only; `ParseBody`→`ParseIf`→`ParseBody` unguarded |
| Rendered prompt is tokenized with special-token parsing on, and message content is inserted raw: `<\|im_start\|>system…` in a user message or tool result becomes real control tokens | Medium-High | `RunningModel.cs:631` (`NativeText.Tokenize(…, true)`), `ChatJinjaVariables.cs:92` |
| No template hash, pin or policy; effective template text is internal; probe and load open the file separately | Medium | `ChatTemplateStrategy.cs:35-38`; no SHA-256 anywhere in ModelRunner |
| Ollama-dialect `TEMPLATE` from a Modelfile silently overrides the GGUF's embedded template in Auto mode | Medium | `ChatTemplateStrategy.cs:69-74` |
| Template text decides tool-call tags, thinking tags and the thinking-off switch, so a poisoned template can route answers into the hidden reasoning channel | Low | `ToolCallFormat.cs:94-126`, `ThinkingTags.cs:139-160` |
| ModelManager copies a publisher's chat template verbatim into converted GGUFs, with no hash in provenance and no size cap | Medium | `SpecialVocabulary.cs:120-142`, `TokenizerExport.cs:87-89` |
| `OllamaTemplate.NamedBuiltIn` tolerates up to 99 edits, so it is safe for substitution but not as an integrity check | Info | `OllamaTemplate.cs:79-94` |

## Layer 3: Runner Parsing

Bleeding Llama (CVE-2026-7482) does not apply: it lived in Ollama's own Go quantize path (`/api/create` with `quantize=F32`, then `/api/push`), fixed in Ollama 0.17.1, and the llama.cpp C++ loader that CodeBrix uses was never affected. At the pinned b10221 every tensor's data range is checked against file size, and nine crafted files (truncated, inflated dimensions, bad types, huge offsets) were all refused cleanly by the shipped library, including through `QuantizeAsync`. The weight of this layer shifts to in-process blast radius and a few side doors.

**Does not apply**

- Main model load: `llama-model-loader.h:46-48` rejects `offs + nbytes > file size` and the overflow wrap. `gguf.cpp` bounds `n_dims`, element-count overflow, type range, `blck_size == 0`, string and array lengths, alignment.
- May 15 2026 oss-security gguf.cpp items: V-02, V-04, V-05, V-06 are guarded at b10221; V-01 needs 32-bit `size_t`, and all seven RIDs are 64-bit. CVE-2026-33298 (`ggml_nbytes` overflow) was fixed in b7824, well before b10221.
- `QuantizeAsync` uses llama.cpp's quantizer with `check_tensors=true` and the same bounds check, writes to a temp file beside the output and renames atomically.
- mmap is default and read-only. KV overrides, control vectors and state-file loading are declared in P/Invoke but unreachable from managed code.
- ModelManager and Core use no `ArrayPool`, uninitialized arrays or `SkipLocalsInit`, and every read loops or uses `ReadExactlyAsync`, so the Bleeding Llama bug class (short read into a stale buffer, written out) cannot occur there.
- MIDI reader and protobuf reader check every length against remaining bytes; ONNX tensor payloads must match declared shape before use.

**Applies**

| Finding | Package | Severity | Evidence |
| --- | --- | --- | --- |
| In-process by design: any future native memory disclosure exposes the whole host app's heap (keys, tokens, prompts), and there is no out-of-process option | ModelRunner | Medium (architectural) | No `Process.Start` or IPC host anywhere |
| ONNX single-file load route follows absolute and `../` external-data locations outside the model folder (file-read oracle) | ModelRunner | Medium | `OnnxModelLocation.cs:121-122`: `ForFile` skips `NormalizeRelative` |
| LoRA adapter tensor data not bounds-checked; truncated adapter loads with zero-filled weights | ModelRunner (llama.cpp) | Medium | `llama-adapter.cpp:395-404`; reproduced |
| `CheckTensors` exposed but default off, so garbage or NaN weight blocks are accepted | ModelRunner | Medium | `ModelRunnerOptions.cs:119-120` |
| ONNX arena uses uninitialized arrays: any kernel that misses an output element would leak heap into outputs (none found; invariant unenforced across \~40 kernels) | ModelRunner | Medium | `OnnxArena.cs:119-132` |
| No protobuf nesting-depth limit: deeply nested ONNX subgraphs overflow the stack and kill the process; reachable from a pulled bundle before any opt-in | Core (both) | Medium | `OnnxAttributeProto.cs:113,164` → `OnnxGraphProto.Parse` |
| Shape-driven allocations (`ConstantOfShape`, `Expand`, `Range`) and tokenizer vocab ids have no budget beyond `int.MaxValue` | ModelRunner | Low | `OnnxRangeKernel.cs:92`, `Gpt2TokenizerFiles.cs:80-81` |
| Managed GGUF validator is solid but never runs on pull, resolve, or quantize input/output; alignment not checked as power of two, duplicate keys allowed | ModelManager | Medium | `GgufReader.cs:244-267`; not called from `Registry/` or `ResolveAsync` |
| Unchecked `ElementCount * width` arithmetic in checkpoint readers (crash or self-aliasing, not a leak); compressed zip storage entries accepted | ModelManager | Low | `CheckpointTensor.cs:23`, `PyTorchZipReader.cs:207-215` |
| llama.cpp pin is 12 weeks old (2026-08-01) with no documented security-rebase policy; binding hard-codes one commit, so security updates need a coordinated release | Release | Medium | `UPSTREAM.txt`, `NativeLibraryLoader.cs:44` |

## Layer 4: Network Exposure

This layer does not apply to ModelRunner, and the code confirms the design intent: nothing in `src/` or `samples/` opens a listener, binds a port, or exposes the model or its chat template to anything over IP. The 175,000 exposed servers, `0.0.0.0` binding and DNS-rebinding template rewrite all need an API that does not exist here. ModelManager has only an outbound HTTP client, which has its own gaps.

**Does not apply**

- Repo-wide grep for `HttpListener`, `TcpListener`, `Socket`, `Kestrel`, `WebApplication`, `MapGet`, `0.0.0.0`, `IPAddress.Any`, `localhost`: zero hits outside an outbound `SocketsHttpHandler`.
- Native build forces `LLAMA_BUILD_SERVER`, `LLAMA_CURL`, `LLAMA_OPENSSL` off; `GGML_RPC` and `GGML_BACKEND_DL` off (`wrapper/CMakeLists.txt:72-82`).
- ModelQueryTool's only network use is `/download -y`.

**Residual note:** a consumer that wraps ModelRunner in a server inherits every Layer 4 risk. One paragraph in the README saying "bind to loopback, require auth, never expose template or system-prompt mutation over the wire" would close the loop for downstream users.

**Applies (ModelManager outbound HTTP)**

| Finding | Severity | Evidence |
| --- | --- | --- |
| Redirects followed to any host and any scheme, including https→http and private or loopback addresses (SSRF for unhashed pulls) | Medium | `RegistryClient.cs:530-546`, `BlobDownload.cs:361-366` |
| `AllowInsecureHttp` enforced only for registry names; URL-list `BundleFile`s accept `http://` unconditionally | Medium | `BundleFile.cs:247-253` |
| No size caps on manifest, listing or XML bodies, or on manifest-declared blob size; stall watchdog resets per byte, so a trickle feed never times out | Low | `RegistryClient.cs:164`, `BlobDownload.cs:510-571` |
| Raw response bodies and full URLs (including signed query strings) go into exception messages | Low | `RegistryClient.cs:443`, `BlobDownload.cs:184` |

## Layer 5: Tool Access and Backdoored Weights

ModelRunner never executes a tool, so a backdoored model's trigger phrase can only produce a `ToolCall` object that the consuming application must choose to run. Backdoored weights themselves cannot be detected by any parser; the guards that matter are provenance (Layer 1) and making the tool-call boundary strict, which today is looser than it should be.

**Does not apply**

- No built-in shell, file or HTTP tools; `ChatPipeline` and `RunningModel.cs:664-671` only return `ToolCalls`. The tool stage runs only when the caller offered tools.
- A `ToolCall.Name` is always the exact name of an offered tool; a model cannot invent a tool that reaches the app.
- Tool calls are never parsed out of reasoning text. Parse buffers are capped at 1 MB.
- ModelQueryTool offers no tools at all, so its trigger-phrase exposure is limited to text (but see the terminal-escape item below).

**Applies**

| Finding | Severity | Evidence |
| --- | --- | --- |
| JSON tool-name matching is substring-based: the first offered name found anywhere in output wins, and the JSON `name` member is never compared to it, so `{"name":"exfiltrate","arguments":{"x":"read_file"}}` becomes a `read_file` call | Medium | `ToolCallParser.cs:405-481, 498-585` (ported from Ollama `tools.go`) |
| Arguments are not validated against the tool's JSON schema; string `arguments` passed through unparsed; duplicate keys preserved | Medium | `ChatFunctionCallParser.cs:344-356, 393-396`, `CompactJson.cs:58-75` |
| Special-token injection (Layer 2) is the main path from a tool result back into forged tool calls on the next turn | Medium-High | see Layer 2 |
| Sample terminal writes model output with ESC, C0 and C1 control characters intact: a hostile model can repaint the screen or forge "app" notices | Low-Medium | `StreamingWordWrapper.cs:136-179`, `ChatShell.cs:788, 804` |
| "Obliterated" or anonymous fine-tunes: no publisher policy or trusted-namespace concept exists to steer tool-enabled apps toward official weights | Medium | ModelManager has no `TrustedNamespaces` / publisher policy |

## Findings Outside Video's Five Layers

The review turned up issues the video doesn't name; two are as serious as anything above. The bearer-token leak and the Python export path traversal both reach the user's credentials or file system without any model behaving badly.

| Finding | Package | Severity | Evidence |
| --- | --- | --- | --- |
| `BearerToken` is store-wide, not host-scoped: any URL-list host answering 401 gets it (over `http://` too), and a sticky "established" flag then sends it up front. Contradicts the docs ("offered to the Hub host and to no other host, ever") | ModelManager | High | `RegistryClient.cs:398-408, 593-598`; `HttpFileListSource.cs:132-146` |
| CVE-2026-9856: both Python export routes call transformers `save_pretrained` on the publisher's tokenizer; dict-form `chat_template` keys become file names (path-traversal write). Validated environment is transformers 4.57.6, inside the affected range | ModelManager | High (if installed transformers is unpatched) | onnxruntime-genai `save_processing`, optimum `maybe_save_preprocessors`; no version floor in `PythonHost.Check` |
| `AllowRemoteCode = true` runs publisher Python inside the host .NET process (Python.NET), with full environment and stored HF login token; GenAI route sets no offline flag | ModelManager | Medium (opt-in) | `PythonHost.cs:401-439`, `export_genai.py:16-19` |
| No version floors for torch (CVE-2025-32434 bypasses `weights_only` ≤2.5.1), transformers or onnx (external-data traversal before 1.16) | ModelManager | Medium | `PythonHost.Check` only tests imports |
| Default materialize mode is hard link, so any in-place write to a materialized file silently corrupts the content-addressed blob, and nothing re-verifies it | ModelManager | Medium | `MaterializeOptions`, `ModelStore.cs:422-426` |
| Folder import and derived-output collection follow symlinks (a cloned repo with `tokenizer.json -> ~/.aws/credentials` gets stored); materialize writes through planted links in the target | ModelManager | Low-Medium | `ModelStore.cs:1997`, `BundleMaterializer.cs:138-159` |
| Path checks miss Windows alternate data streams (`config.json:evil`), reserved names, case collisions; digest regex `$` accepts trailing `\n` | ModelManager | Low | `BundleFile.cs:186-227`, `Sha256Digest.cs:31` |
| Provenance records names and tool versions but no source digests or template hash, so it cannot support trust decisions | ModelManager | Low | `DerivedProvenance.cs` |
| No CI, SourceLink, lock files or `SECURITY.md`; packages built on maintainer machines; shipped `AGENT-README.txt` steers consumers' coding agents, so it's a supply-chain asset too | Release | Medium (process) | `.github/` holds only `copilot-instructions.md` |
| `FileFilter` compiles caller globs to regex without timeout or `NonBacktracking` | ModelManager | Low | `FileFilter.cs:282` |

## Guard Plan: ModelRunner

Ten guards, ordered by risk reduction per unit of work. API names are proposals in the existing style (options objects, async-only, refusal by name). One structural move unlocks several of them: CodeBrix.Ollama.Core is already compiled into both packages, so moving ModelManager's managed `GgufReader` into Core gives ModelRunner a pre-load validator without adding a dependency.

1. **Native library integrity** (Layer 1, 3)
   - Generate `runtimes/SHA256SUMS` at adoption time and embed the per-RID hash as a constant at build; verify file bytes in `NativeLibraryLoader.LoadNoLock` before `NativeLibrary.Load`.
   - Remove the bare-name OS-search fallback, or gate it behind `ModelRunner.AllowSystemNativeLibrary = false`. Add `[DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]`.
   - Keep the existing identity probe as a second check; it catches ABI drift, the hash catches forgery.
2. **Template limits** (Layer 2, DoS)
   - `ModelRunnerOptions.TemplateLimits { MaxRenderSteps, MaxOutputChars, MaxStringChars, MaxCollectionItems, MaxNestingDepth, RenderTimeout }` with conservative defaults (e.g. 1M steps, 4M chars, depth 256, 2 s).
   - Count statement nesting in `JinjaParser.ParseBody` and evaluation depth in `JinjaRenderer.Evaluate`; same for the Go executor (lower its 10,000 paren cap). Size-check repeat, pad, `batch`, `range`, printf width.
   - Pass the request's `CancellationToken` into both renderers; render Jinja and Ollama dialects off the engine thread.
   - `EagerTemplateValidation = true` by default: compile at `LoadAsync` so a hostile template fails at load, not at first chat.
3. **Content special-token handling** (Layer 2, 5)
   - `ChatRequest.ContentSpecialTokens = Escape | Strip | Allow`, default `Escape` for user and tool roles.
   - Implementation: render with per-message sentinels, split the prompt, tokenize template-owned spans with `parseSpecial=true` and message content with `parseSpecial=false`. This removes the forged-turn path without touching templates.
4. **Template identity and policy** (Layer 2)
   - `ModelDetails.ChatTemplateSha256` from `ProbeAsync`; `IRunningModel.EffectiveChatTemplate { Dialect, Source, Sha256 }` after load.
   - `ModelRunnerOptions.TemplatePolicy = AllowEmbedded | RequirePinned | PreferBuiltIn | RequireExplicit` plus `ExpectedTemplateSha256`, checked inside `LoadOnWorker` against what was actually loaded (closes the probe/load gap).
   - Auto mode: report when an Ollama `TEMPLATE` overrode the GGUF's embedded one instead of doing it silently.
5. **Template analysis and differential check** (Layer 2, Pillar attack)
   - `JinjaTemplate.Analyze()` / `OllamaTemplate.Analyze()`: walk the syntax tree and report conditionals that test message content against string literals, use of `strftime_now` / `currentDate` (time bombs), URLs in literal text, and the literal-text inventory.
   - `ChatRequest.TemplateIntegrityCheck = Off | Warn | Refuse`: render once with real content and once with each user/tool content replaced by a random nonce; cut the content spans out of both and compare. Any difference means template output depends on content, which is exactly what a trigger does.
6. **Tool-call validation** (Layer 5)
   - `ChatRequest.ToolCallValidation` flags, default `NameExact | RejectDuplicateKeys`: JSON `name` must equal an offered tool exactly; mismatches dropped and reported.
   - Optional `Schema` flag validates arguments against `ParametersJsonSchema` (required, types, `additionalProperties`); `MaxArgumentsBytes` cap.
   - Optional `ConstrainToolCalls`: once the tool-call tag appears, switch to a grammar from the existing `JsonSchemaGrammar` that allows only offered names and their schemas.
   - Let callers pin `ToolCallFormat` and thinking tags so a template cannot redefine them.
7. **GGUF and LoRA pre-validation** (Layer 3)
   - Move `GgufReader` into Core; add `ModelRunnerOptions.PreValidate = true` that runs it on the model and every LoRA adapter before native load (tensor extents, alignment power of two, duplicate keys refused).
   - Default `CheckTensors = true` (cost is a one-time pass at load; document an opt-out for very large models).
   - Carry a `llama-native-tools/patches/` patch adding the `offs + nbytes <= file size` check to `llama-adapter.cpp` `set_tensor`, and offer it upstream.
8. **ONNX hardening** (Layer 3)
   - Run external-data locations through `NormalizeRelative` in the `ForFile` route too, confining side files to the graph's folder.
   - Depth counter (cap 64) through Core's protobuf `Parse` chain and `EnumerateGraphTensors`.
   - `OnnxArena`: zeroed arrays for graph outputs (keep uninitialized only for internal scratch), or a debug-build coverage assertion; `OnnxRunnerOptions.MaxTensorElements` budget for shape-driven allocations; cap tokenizer vocab ids.
9. **Engine update policy** (Layer 3, "update aggressively")
   - Document a security-rebase rule in `llama-native-tools/README.txt`: watch llama.cpp advisories, re-vendor on any gguf/ggml/loader fix, record a "last checked against advisories" date in `UPSTREAM.txt`.
   - Expose `NativeRuntimeInfo.UpstreamDate` so consuming apps can log or warn on engine age.
10. **In-process boundary** (Layer 3, blast radius)
    - Short term: state the in-process trust assumption in README and AGENT-README, plus the "if you wrap this in a server" paragraph from Layer 4.
    - Longer term: optional `ModelRunnerHost.OutOfProcess` that runs the native engine in a child process with a cleared environment and pipe transport, so a future native 0-day sees only model data, not the host's secrets.

## Guard Plan: ModelManager

The goal "always check hashes when downloading" is reachable for every source except plain URL lists with no stated hash, and needs one bug fix plus one new verification (git SHA-1 for small HF files). But the video is right that a hash alone proves only what the server intended; real protection comes from pins (commit, manifest digest, lockfile) and from verifying again at rest. The plan covers both.

1. **Fix HF commit pinning** (bug, High)
   - Always request `/api/models/{repo}/revision/{rev}`, including for 40-hex commits, and throw if the returned `sha` differs from the requested commit.
   - Add a test that pins to a commit other than `main`'s head (the current test passes only because both are the same).
   - Add `PullOptions.RequireCommitPin` to refuse branch names for callers who want immutability.
2. **Hash every downloaded file** (Layer 1)
   - Verify non-LFS HF files against the tree API's git object ID: `SHA1("blob " + length + "\0" + bytes)`. With LFS sha256 already checked, every HF file is then verified.
   - Replace boolean `RequireHashes` with `PullOptions.HashPolicy = RequireSha256 | RequireStrong (sha256 or git SHA-1) | AllowMd5 | TrustOnFirstUse`. Default for HF and registry: `RequireStrong`; for URL lists: `RequireStrong` unless the caller opts down. MD5 from `x-goog-hash` stops counting as verified by default.
3. **Pin for authenticity** (Layer 1, video's hash caveat)
   - Registry: accept `name@sha256:<hex>` and `PullOptions.ExpectedManifestDigest`; hash the raw manifest bytes before parsing.
   - Lockfile: `store.GetLockAsync(name)` returns a `ModelLock` (source, commit or manifest digest, every file path + sha256); `PullOptions.Lock` refuses any pull whose files differ. This is the one guard that survives a compromised publisher account after first review.
4. **Verify at rest** (Layer 1, shared store)
   - Public `IModelStore.VerifyAsync(name)` that re-hashes every layer and reports mismatches.
   - `ResolveOptions.Verify` and `ModelStoreOptions.VerifyOnResolve`, defaulting on when the store is the shared `~/.ollama/models`; cache results keyed on size, mtime and inode so repeat resolves stay cheap.
   - Hash cache hits during pull; rename the existence-and-size check that currently reports "verifying sha256 digest".
   - Materialize: default to copy, or `chmod 0444` blobs before hard-linking, so an in-place edit can't corrupt the store.
5. **Check content, not just bytes** (Layer 2, 3)
   - Run the managed GGUF validator (moved to Core) on every GGUF layer at pull completion, in `ResolveAsync`, and before and after the quantize delegate; close its gaps (alignment power of two and bounded, duplicate keys and tensor names refused, version bound, no silent skip on non-seekable streams).
   - Record `chat_template_sha256` and its source file in model config and `ModelInfo`; add `ModelStoreOptions.TemplateReview` callback that sees new or changed templates; cap templates at about 1 MB on conversion.
   - Protobuf depth cap (Core) before any ONNX pass-through or reduce.
6. **File-type and publisher policy** (Layer 1, 5)
   - `FileFilter.SafeDefault` excluding `*.py`, `*.pkl`, `*.pickle`, `*.sh`, `*.bat`, `*.ps1`, `*.exe`, `*.dll`, `*.so`, `*.dylib`, `*.ipynb`, and `*.bin` / `*.pt` / `*.pth` / `*.ckpt` when safetensors exist.
   - `BundleListing.ContainsExecutableCode` (including `config.json` `auto_map`); `PullOptions.AllowExecutableFiles = false`.
   - `PullOptions.PublisherPolicy` callback (or `TrustedNamespaces`) that sees the listing before any byte downloads, so tool-enabled apps can insist on official or trusted-quantizer weights.
7. **Transport and credentials**
   - Host-scoped credentials: `ModelStoreOptions.Credentials` as `Func<Uri, string>` or host map; send only on exact scheme+host+port over https; per-host "established" state; never answer a 401 from an unlisted host. Fix the doc comments.
   - `AllowedHosts` and redirect allowlist (Ollama registry, Hugging Face and its CDN hosts, `storage.googleapis.com`); refuse https→http and private, loopback or link-local targets; apply `AllowInsecureHttp` to URL lists.
   - `MaxBlobBytes`, `MaxManifestBytes`, `MaxListingBytes`, per-attempt deadline; strip query strings and cap bodies in exception messages.
8. **Paths and file system**
   - `BundleFile.NormalizePath`: also refuse `:` anywhere, control characters, Windows reserved names, trailing dot or space, case-insensitive duplicates. `Sha256Digest`: anchor with `\z`, lowercase only.
   - Import: `ImportOptions.FollowSymlinks = false`, copy then hash the copy. Materialize: refuse symlinks inside the target, `FileMode.CreateNew`.
   - Temp work folders created 0700 (or under the store); prune `sha256-<random>` leftovers.
9. **Python export routes**
   - Before any script runs, read `tokenizer_config.json`, `chat_template.json` and `processor_config.json` in C# and refuse dict-form `chat_template` names that aren't `^[A-Za-z0-9_.-]{1,64}$` (CVE-2026-9856).
   - Version floors in `PythonHost.Check`: transformers with fix `eaaaf84`, torch ≥ 2.6, onnx ≥ 1.16.
   - Set `HF_HUB_OFFLINE=1`, `TRANSFORMERS_OFFLINE=1` and builder `hf_token=false`; prefer safetensors over `.bin`.
   - Longer term: run Python routes in a child process (`python -I -E`, cleared env, temp `HOME`/`HF_HOME`, OS sandbox where available) instead of in-process.
10. **Provenance worth trusting**
    - Record source manifest digest, every source file digest, `chat_template_sha256`, the remote-code flag and `.py` hashes when it was set. Optionally HMAC the record with a key kept outside the store.

ModelQueryTool: enforce its pinned weights digest (refuse, or require `/download -y --accept-new-weights`), pin a commit rather than a tag, and strip ESC/C0/C1 from model and server text before it reaches the terminal.

## Suggested Sequencing

Ship in three releases: bugs and crash paths first, verification second, policy and isolation last. Several phase-2 items change defaults (`CheckTensors`, special-token escaping, exact tool names, `SafeDefault`, `HashPolicy`), so they belong in a release whose notes call out the behaviour change.

| Phase | ModelRunner | ModelManager | Why this phase |
| --- | --- | --- | --- |
| 1: fix what's broken | Native hash check + drop OS-path fallback; ONNX `ForFile` path confinement; template statement-depth guard and render limits; protobuf depth cap (Core) | HF commit pin fix; host-scoped token and https-only send; `chat_template` key check + Python version floors; security-rebase policy note | Each is small, verified in source, and closes a leak, a pin bypass or a process crash |
| 2: verify everything | `CheckTensors` on; `GgufReader` into Core + pre-validation; LoRA bounds patch; special-token `Escape`; `ToolCallValidation` (NameExact, duplicates); template SHA-256 + `TemplatePolicy`; zeroed ONNX outputs | git SHA-1 for non-LFS files + `HashPolicy`; `VerifyAsync` / `VerifyOnResolve`; GGUF validation on pull and resolve; template hash in config; redirect allowlist, size caps; path and symlink fixes; read-only or copied materialize | Delivers "every file hashed, every file re-checked" and the template identity the rest builds on |
| 3: policy and isolation | `Analyze()` + differential render check; schema validation and constrained tool calls; out-of-process host option | `ModelLock` lockfile; `SafeDefault` filter + `PublisherPolicy`; digest-bearing provenance; Python routes in a sandboxed child process | Larger designs; they turn verification into trust decisions and contain future unknown bugs |

Release hygiene runs alongside: Authenticode and Developer ID signing plus notarization for natives, NuGet author signing, SourceLink, CI builds, lock files and a `SECURITY.md`.

## Sources

- [Your Local AI Is Not as Private as You Think](https://youtu.be/3mx84MRj8lY) (Kai, 2026-10-21), via the attached summary
- [Bleeding Llama: When AI Model Files Become Memory Leaks](https://www.msuiche.com/posts/bleeding-llama-when-ai-model-files-become-memory-leaks/) (CVE-2026-7482, Ollama Go quantize path, fixed 0.17.1)
- [Three Ollama CVEs in One Week](https://mondoo.com/blog/three-ollama-cves-bleeding-llama-and-windows-updater-flaws) (Windows updater signature bypass and header-derived path traversal)
- [CVE-2026-33298](https://www.sentinelone.com/vulnerability-database/cve-2026-33298/) (`ggml_nbytes` overflow, fixed b7824)
- [llama.cpp GGUF parser flaws](https://www.techtimes.com/articles/317230/20260526/llamacpp-gguf-parser-flaws-critical-integer-overflow-enables-arbitrary-reads-every-local-ai-stack.htm) (May 15 2026 oss-security V-01 to V-06)
- [CVE-2026-18581](https://www.sentinelone.com/vulnerability-database/cve-2026-18581/) (llama.cpp minja parser abort; minja not built here)
- [CVE-2026-9856](https://www.sentinelone.com/vulnerability-database/cve-2026-9856/) (transformers `save_pretrained` chat-template path traversal)
- [Chat template backdoors across 18 models and 4 engines](https://www.pillar.security/blog/from-discovery-to-large-scale-validation-chat-template-backdoors-across-18-models-and-4-engines) (Pillar Security)
- [onnxruntime-genai builders/base.py](https://raw.githubusercontent.com/microsoft/onnxruntime-genai/main/src/python/py/models/builders/base.py) and [optimum save\_utils.py](https://raw.githubusercontent.com/huggingface/optimum/main/optimum/utils/save_utils.py) (upstream `save_pretrained` calls)
- Repository: [ellisnet/codebrix.ollama](https://github.com/ellisnet/codebrix.ollama) at `d4674c2`
