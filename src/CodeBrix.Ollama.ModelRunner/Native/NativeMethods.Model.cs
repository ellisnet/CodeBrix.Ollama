using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points that load a model file and read what the engine knows about it.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Loads a model from a GGUF file; returns zero on failure.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_model_load_from_file(string pathModel, LlamaModelParams modelParams);

    /// <summary>Loads a model from an already open C <c>FILE *</c>; returns zero on failure.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_model_load_from_file_ptr(IntPtr file, LlamaModelParams modelParams);

    /// <summary>Loads a model from several shard files given in order; returns zero on failure.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_model_load_from_splits(byte** paths, nuint nPaths, LlamaModelParams modelParams);

    /// <summary>Builds a model from GGUF metadata plus a callback that fills each tensor.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_model_init_from_user(IntPtr metadata, delegate* unmanaged[Cdecl]<IntPtr, void*, void> setTensorData, void* setTensorDataUd, LlamaModelParams modelParams);

    /// <summary>Writes a model back out as a GGUF file.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_model_save_to_file(IntPtr model, string pathModel);

    /// <summary>Releases a model and everything loaded against it, including its adapters.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void llama_model_free(IntPtr model);

    /// <summary>The model's vocabulary, owned by the model.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr llama_model_get_vocab(IntPtr model);

    /// <summary>Which rotary position embedding the model uses.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaRopeType llama_model_rope_type(IntPtr model);

    /// <summary>The context size the model was trained with.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_ctx_train(IntPtr model);

    /// <summary>The model's embedding width.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_embd(IntPtr model);

    /// <summary>The width of the model's input embeddings.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_embd_inp(IntPtr model);

    /// <summary>The width of the model's output embeddings.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_embd_out(IntPtr model);

    /// <summary>How many layers the model has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_layer(IntPtr model);

    /// <summary>How many next-token-prediction layers the model has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_layer_nextn(IntPtr model);

    /// <summary>How many attention heads the model has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_head(IntPtr model);

    /// <summary>How many key/value heads the model has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_head_kv(IntPtr model);

    /// <summary>The model's sliding-window-attention width, or zero.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_n_swa(IntPtr model);

    /// <summary>The rotary frequency scale the model was trained with.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float llama_model_rope_freq_scale_train(IntPtr model);

    /// <summary>How many classifier outputs the model has; only meaningful for classifier models.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_model_n_cls_out(IntPtr model);

    /// <summary>The label of a classifier output, or null when it has none.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_model_cls_label(IntPtr model, uint i);

    /// <summary>Copies one metadata value as text; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_meta_val_str(IntPtr model, string key, byte* buf, nuint bufSize);

    /// <summary>How many metadata key/value pairs the model carries.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_meta_count(IntPtr model);

    /// <summary>The GGUF key name behind one of the sampling metadata keys, or null.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_model_meta_key_str(LlamaModelMetaKey key);

    /// <summary>Copies the metadata key at an index; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_meta_key_by_index(IntPtr model, int i, byte* buf, nuint bufSize);

    /// <summary>Copies the metadata value at an index as text; returns its length, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_meta_val_str_by_index(IntPtr model, int i, byte* buf, nuint bufSize);

    /// <summary>Copies a one-line description of the model; returns its length.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_desc(IntPtr model, byte* buf, nuint bufSize);

    /// <summary>The model file's quantization.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaFtype llama_model_ftype(IntPtr model);

    /// <summary>The total size of the model's tensors in bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ulong llama_model_size(IntPtr model);

    /// <summary>The model's embedded chat template, or null. A null name asks for the default one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_model_chat_template(IntPtr model, string name);

    /// <summary>How many parameters the model has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ulong llama_model_n_params(IntPtr model);

    /// <summary>Whether the model has an encoder that needs <c>llama_encode</c>.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_model_has_encoder(IntPtr model);

    /// <summary>Whether the model has a decoder that needs <c>llama_decode</c>.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_model_has_decoder(IntPtr model);

    /// <summary>The token an encoder-decoder model's decoder starts with, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_model_decoder_start_token(IntPtr model);

    /// <summary>Whether the model is recurrent, like Mamba or RWKV.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_model_is_recurrent(IntPtr model);

    /// <summary>Whether the model is hybrid, like Jamba or Granite.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_model_is_hybrid(IntPtr model);

    /// <summary>Whether the model is diffusion-based, like LLaDA or Dream.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_model_is_diffusion(IntPtr model);

    /// <summary>Rewrites a model file at another quantization; returns zero on success.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint llama_model_quantize(string fnameInp, string fnameOut, LlamaModelQuantizeParams* quantizeParams);
}
