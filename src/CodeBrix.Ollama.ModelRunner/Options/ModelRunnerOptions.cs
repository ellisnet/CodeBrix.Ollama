using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// Everything <see cref="ModelRunner.LoadAsync"/> needs: which model file to load and how to load and run it.
/// Every property except <see cref="ModelPath"/> has a default that lets a small model load and generate.
/// </summary>
/// <remarks>
/// <para>
/// A <see langword="null"/> value on a nullable numeric property means "leave it to the engine", which
/// usually means "read it from the model file". For a large model on a machine with limited memory the
/// properties that matter are <see cref="LoadMode"/> (keep it at <see cref="ModelLoadMode.MemoryMap"/>),
/// <see cref="ContextSize"/>, <see cref="KeyCacheType"/> / <see cref="ValueCacheType"/> and
/// <see cref="Threads"/>. Use <see cref="ModelRunner.ProbeAsync"/> first to learn what a file needs.
/// </para>
/// </remarks>
public sealed class ModelRunnerOptions
{
    /// <summary>The path of the model's GGUF file. Required.</summary>
    public string ModelPath { get; set; }

    /// <summary>LoRA adapters applied at load time. Empty by default.</summary>
    public IList<LoraAdapterOptions> LoraAdapters { get; } = new List<LoraAdapterOptions>();

    /// <summary>How the model file is brought into memory. Default <see cref="ModelLoadMode.MemoryMap"/>.</summary>
    public ModelLoadMode LoadMode { get; set; } = ModelLoadMode.MemoryMap;

    /// <summary>
    /// The number of layers offloaded to an accelerator. <see langword="null"/> (the default) lets the engine
    /// decide, which on Apple Silicon means every layer goes to Metal and on a CPU-only build means none do.
    /// 0 keeps everything on the CPU.
    /// </summary>
    public int? GpuLayers { get; set; }

    /// <summary>
    /// The context size in tokens. <see langword="null"/> (the default) uses the model's training context,
    /// which for a large model can need a great deal of memory; set it explicitly for such models.
    /// </summary>
    public uint? ContextSize { get; set; }

    /// <summary>The largest number of tokens submitted to the engine in one call. Default 2048.</summary>
    public uint BatchSize { get; set; } = 2048;

    /// <summary>The largest number of tokens processed physically at once. Default 512.</summary>
    public uint PhysicalBatchSize { get; set; } = 512;

    /// <summary>
    /// The number of threads used for generation. <see langword="null"/> (the default) uses the number of
    /// physical cores, or the logical count when the physical count is unknown.
    /// </summary>
    public int? Threads { get; set; }

    /// <summary>
    /// The number of threads used for prompt processing. <see langword="null"/> (the default) uses the same
    /// value as <see cref="Threads"/>.
    /// </summary>
    public int? BatchThreads { get; set; }

    /// <summary>Whether the engine uses flash attention. Default <see cref="FlashAttentionMode.Auto"/>.</summary>
    public FlashAttentionMode FlashAttention { get; set; } = FlashAttentionMode.Auto;

    /// <summary>The storage type of the key cache. Default <see cref="KvCacheType.Default"/>.</summary>
    public KvCacheType KeyCacheType { get; set; } = KvCacheType.Default;

    /// <summary>The storage type of the value cache. Default <see cref="KvCacheType.Default"/>.</summary>
    public KvCacheType ValueCacheType { get; set; } = KvCacheType.Default;

    /// <summary>RoPE base frequency. <see langword="null"/> (the default) reads it from the model.</summary>
    public float? RopeFrequencyBase { get; set; }

    /// <summary>RoPE frequency scaling factor. <see langword="null"/> (the default) reads it from the model.</summary>
    public float? RopeFrequencyScale { get; set; }

    /// <summary>
    /// Whether the context is created for embeddings rather than generation. Default <see langword="false"/>.
    /// Embedding-only models (BERT-style) need <see langword="true"/>; generative models can produce
    /// embeddings either way.
    /// </summary>
    public bool EmbeddingsMode { get; set; }

    /// <summary>How token embeddings are pooled. Default <see cref="EmbeddingPooling.Unspecified"/>.</summary>
    public EmbeddingPooling EmbeddingPooling { get; set; } = EmbeddingPooling.Unspecified;

    /// <summary>
    /// The number of distinct sequences the context can hold at once. Default 1. Hybrid and recurrent models
    /// keep one state per sequence.
    /// </summary>
    public uint MaxSequences { get; set; } = 1;

    /// <summary>
    /// The number of recurrent-state snapshots kept per sequence so a hybrid or recurrent model can roll
    /// back. Default 0 (no rollback).
    /// </summary>
    public uint RecurrentStateSnapshots { get; set; }

    /// <summary>
    /// Whether to also load the model's multi-token-prediction layers, when it has them. Default
    /// <see langword="false"/>. Reserved for the speculative decoding path; loading them costs memory and
    /// this version of the library does not yet use them.
    /// </summary>
    public bool LoadMtpLayers { get; set; }

    /// <summary>Whether to validate every tensor's data while loading. Default <see langword="false"/>.</summary>
    public bool CheckTensors { get; set; }

    /// <summary>
    /// Whether the CPU backend may repack weights into its faster layouts. Default <see langword="true"/>.
    /// </summary>
    public bool UseExtraBufferTypes { get; set; } = true;

    /// <summary>Whether the engine collects performance timings. Default <see langword="true"/>.</summary>
    public bool CollectTimings { get; set; } = true;

    /// <summary>
    /// Which chat-template dialect renders chat requests. Default <see cref="ChatTemplateDialect.Auto"/>.
    /// When a dialect is named and its template is not available the load fails.
    /// </summary>
    public ChatTemplateDialect ChatTemplateDialect { get; set; } = ChatTemplateDialect.Auto;

    /// <summary>
    /// The text of an Ollama Go text/template (a Modelfile TEMPLATE, as CodeBrix.Ollama.ModelManager returns it
    /// for a resolved model). <see langword="null"/> (the default) means there is none.
    /// </summary>
    public string OllamaTemplate { get; set; }

    /// <summary>
    /// A Jinja chat template that replaces the one embedded in the model file. <see langword="null"/> (the
    /// default) uses the embedded one.
    /// </summary>
    public string JinjaTemplate { get; set; }

    /// <summary>Receives load progress from 0.0 to 1.0. <see langword="null"/> (the default) reports nothing.</summary>
    public IProgress<float> LoadProgress { get; set; }
}
