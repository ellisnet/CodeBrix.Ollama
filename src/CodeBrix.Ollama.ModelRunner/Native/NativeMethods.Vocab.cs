using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;

/// <summary>
/// The native entry points for the vocabulary, tokenization and the engine's built-in chat templates.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>The tokenizer family the vocabulary uses.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaVocabType llama_vocab_type(IntPtr vocab);

    /// <summary>How many tokens the vocabulary has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_n_tokens(IntPtr vocab);

    /// <summary>A token's raw text, owned by the vocabulary.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* llama_vocab_get_text(IntPtr vocab, int token);

    /// <summary>A token's score.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float llama_vocab_get_score(IntPtr vocab, int token);

    /// <summary>A token's attribute bits.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial LlamaTokenAttr llama_vocab_get_attr(IntPtr vocab, int token);

    /// <summary>Whether a token ends generation - end of sentence, end of turn and the rest.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_vocab_is_eog(IntPtr vocab, int token);

    /// <summary>Whether a token is a control token rather than something to render.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_vocab_is_control(IntPtr vocab, int token);

    /// <summary>The beginning-of-sentence token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_bos(IntPtr vocab);

    /// <summary>The end-of-sentence token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_eos(IntPtr vocab);

    /// <summary>The end-of-turn token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_eot(IntPtr vocab);

    /// <summary>The sentence-separator token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_sep(IntPtr vocab);

    /// <summary>The next-line token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_nl(IntPtr vocab);

    /// <summary>The padding token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_pad(IntPtr vocab);

    /// <summary>The mask token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_mask(IntPtr vocab);

    /// <summary>Whether the model asks for a beginning-of-sentence token to be added.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_vocab_get_add_bos(IntPtr vocab);

    /// <summary>Whether the model asks for an end-of-sentence token to be added.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_vocab_get_add_eos(IntPtr vocab);

    /// <summary>Whether the model asks for a separator token to be added.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool llama_vocab_get_add_sep(IntPtr vocab);

    /// <summary>The model's suppressed tokens, owned by the vocabulary, with the count written back.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int* llama_vocab_get_suppress_tokens(IntPtr vocab, int* nSuppressTokens);

    /// <summary>The fill-in-the-middle prefix token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_pre(IntPtr vocab);

    /// <summary>The fill-in-the-middle suffix token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_suf(IntPtr vocab);

    /// <summary>The fill-in-the-middle middle token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_mid(IntPtr vocab);

    /// <summary>The fill-in-the-middle padding token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_pad(IntPtr vocab);

    /// <summary>The fill-in-the-middle repository token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_rep(IntPtr vocab);

    /// <summary>The fill-in-the-middle separator token, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_vocab_fim_sep(IntPtr vocab);

    /// <summary>Tokenizes UTF-8 text; returns the token count, or the negated count needed when the buffer is too small.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_tokenize(IntPtr vocab, byte* text, int textLen, int* tokens, int nTokensMax, [MarshalAs(UnmanagedType.U1)] bool addSpecial, [MarshalAs(UnmanagedType.U1)] bool parseSpecial);

    /// <summary>Writes one token's text, without a terminator; returns the byte count, or the negated count needed.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_token_to_piece(IntPtr vocab, int token, byte* buf, int length, int lstrip, [MarshalAs(UnmanagedType.U1)] bool special);

    /// <summary>Turns tokens back into UTF-8 text; returns the byte count, or the negated count needed.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_detokenize(IntPtr vocab, int* tokens, int nTokens, byte* text, int textLenMax, [MarshalAs(UnmanagedType.U1)] bool removeSpecial, [MarshalAs(UnmanagedType.U1)] bool unparseSpecial);

    /// <summary>Renders a chat with one of the engine's built-in templates; returns the byte count the result needs.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int llama_chat_apply_template(byte* tmpl, LlamaChatMessage* chat, nuint nMsg, [MarshalAs(UnmanagedType.U1)] bool addAss, byte* buf, int length);
}
