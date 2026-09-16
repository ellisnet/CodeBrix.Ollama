using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: LLamaSharp LLama/Native/SafeLlamaModelHandle.cs (reference only);

/// <summary>
/// The text side of the binding: tokenizing, detokenizing, rendering one token, applying a built-in chat
/// template and reading metadata strings.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these entry points speaks the same protocol, and it is the reason this type exists. The
/// caller passes a buffer and its size; the engine returns how many tokens or bytes it wrote, or the
/// NEGATED count it would have needed when the buffer was too small. So each wrapper tries a stack buffer
/// first and, on a negative answer, allocates exactly what was asked for and calls again. The protocol is
/// the header's; the shape of the retry follows LLamaSharp, which solves the same problem the same way.
/// </para>
/// <para>
/// All text crossing this boundary is UTF-8. Nothing here ever uses the platform's ANSI code page.
/// </para>
/// </remarks>
internal static unsafe class NativeText
{
    private const int StackBufferBytes = 512;

    /// <summary>Turns text into tokens.</summary>
    /// <param name="vocab">The vocabulary.</param>
    /// <param name="text">The text, which may be empty.</param>
    /// <param name="addSpecial">Whether to add the beginning- and end-of-sentence tokens the model asks for.</param>
    /// <param name="parseSpecial">Whether special and control tokens in the text are recognised rather than escaped.</param>
    /// <returns>The tokens.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    /// <exception cref="InferenceException">The engine could not tokenize the text.</exception>
    public static int[] Tokenize(IntPtr vocab, string text, bool addSpecial, bool parseSpecial)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        int byteCount = Encoding.UTF8.GetByteCount(text);
        byte[] utf8 = new byte[byteCount + 1];
        Encoding.UTF8.GetBytes(text, 0, text.Length, utf8, 0);

        // A token is at least one byte of input, so the input length is a safe first guess, and a small
        // floor keeps the special-token-only cases from asking for a zero-length buffer.
        int capacity = byteCount + 8;
        int[] tokens = new int[capacity];

        fixed (byte* utf8Pointer = utf8)
        fixed (int* tokenPointer = tokens)
        {
            int written = NativeMethods.llama_tokenize(
                vocab, utf8Pointer, byteCount, tokenPointer, capacity, addSpecial, parseSpecial);

            if (written >= 0) return Trim(tokens, written);
            if (written == int.MinValue)
            {
                throw new InferenceException(
                    "The engine reported that tokenizing this text would produce more tokens than a 32-bit "
                    + "count can hold. Tokenize it in pieces.");
            }

            capacity = -written;
        }

        tokens = new int[capacity];
        fixed (byte* utf8Pointer = utf8)
        fixed (int* tokenPointer = tokens)
        {
            int written = NativeMethods.llama_tokenize(
                vocab, utf8Pointer, byteCount, tokenPointer, capacity, addSpecial, parseSpecial);

            if (written < 0)
            {
                throw new InferenceException(
                    $"The engine could not tokenize the text: it asked for {capacity} tokens and then "
                    + $"returned {written}.");
            }

            return Trim(tokens, written);
        }
    }

    /// <summary>Renders one token's text.</summary>
    /// <param name="vocab">The vocabulary.</param>
    /// <param name="token">The token.</param>
    /// <param name="lstrip">How many leading spaces the caller has already emitted and wants skipped.</param>
    /// <param name="special">Whether special tokens are rendered rather than left blank.</param>
    /// <returns>The text, which is empty for a token that renders to nothing.</returns>
    /// <remarks>
    /// The bytes of one token are not always a whole UTF-8 sequence - a byte-fallback vocabulary emits one
    /// byte at a time - so a caller that is assembling text should join the BYTES and decode once, using
    /// <see cref="TokenToBytes"/>, rather than concatenating what this returns.
    /// </remarks>
    public static string TokenToPiece(IntPtr vocab, int token, int lstrip, bool special)
    {
        byte[] bytes = TokenToBytes(vocab, token, lstrip, special);
        return bytes.Length == 0 ? string.Empty : Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Renders one token's raw bytes, which may be part of a UTF-8 sequence rather than all of it.</summary>
    /// <param name="vocab">The vocabulary.</param>
    /// <param name="token">The token.</param>
    /// <param name="lstrip">How many leading spaces the caller has already emitted and wants skipped.</param>
    /// <param name="special">Whether special tokens are rendered rather than left blank.</param>
    /// <returns>The bytes.</returns>
    /// <exception cref="InferenceException">The engine could not render the token.</exception>
    public static byte[] TokenToBytes(IntPtr vocab, int token, int lstrip, bool special)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_token_to_piece(vocab, token, stack, StackBufferBytes, lstrip, special);

        if (written >= 0)
        {
            if (written == 0) return Array.Empty<byte>();
            byte[] result = new byte[written];
            Marshal.Copy((IntPtr)stack, result, 0, written);
            return result;
        }

        int needed = -written;
        byte[] buffer = new byte[needed];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_token_to_piece(vocab, token, pointer, needed, lstrip, special);
        }

        if (written < 0)
        {
            throw new InferenceException(
                $"The engine could not render token {token}: it asked for {needed} bytes and then returned "
                + $"{written}.");
        }

        return Trim(buffer, written);
    }

    /// <summary>Turns tokens back into text.</summary>
    /// <param name="vocab">The vocabulary.</param>
    /// <param name="tokens">The tokens.</param>
    /// <param name="removeSpecial">Whether the beginning- and end-of-sentence tokens are dropped.</param>
    /// <param name="unparseSpecial">Whether special tokens are rendered rather than left blank.</param>
    /// <returns>The text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    /// <exception cref="InferenceException">The engine could not detokenize.</exception>
    public static string Detokenize(IntPtr vocab, int[] tokens, bool removeSpecial, bool unparseSpecial)
    {
        if (tokens == null) throw new ArgumentNullException(nameof(tokens));
        if (tokens.Length == 0) return string.Empty;

        int capacity = (tokens.Length * 8) + 16;
        byte[] buffer = new byte[capacity];
        int written;

        fixed (int* tokenPointer = tokens)
        fixed (byte* bufferPointer = buffer)
        {
            written = NativeMethods.llama_detokenize(
                vocab, tokenPointer, tokens.Length, bufferPointer, capacity, removeSpecial, unparseSpecial);
        }

        if (written < 0)
        {
            capacity = -written;
            buffer = new byte[capacity];
            fixed (int* tokenPointer = tokens)
            fixed (byte* bufferPointer = buffer)
            {
                written = NativeMethods.llama_detokenize(
                    vocab, tokenPointer, tokens.Length, bufferPointer, capacity, removeSpecial, unparseSpecial);
            }
        }

        if (written < 0)
        {
            throw new InferenceException(
                $"The engine could not turn {tokens.Length} tokens back into text: it asked for {capacity} "
                + $"bytes and then returned {written}.");
        }

        return written == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Renders a chat with one of the engine's own built-in templates.</summary>
    /// <param name="template">The template, by Jinja source or by the name of a built-in one.</param>
    /// <param name="roles">Each message's role.</param>
    /// <param name="contents">Each message's text, in the same order as <paramref name="roles"/>.</param>
    /// <param name="addAssistant">Whether to end with the tokens that open an assistant turn.</param>
    /// <returns>The rendered prompt.</returns>
    /// <exception cref="ArgumentNullException">An argument is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">The two arrays are not the same length.</exception>
    /// <exception cref="ChatTemplateException">The engine does not recognise the template.</exception>
    /// <remarks>
    /// This is the engine's own limited template support, which matches a fixed list of known templates
    /// rather than running Jinja. It is bound because the header has it; the library's own renderers are
    /// what the chat path uses.
    /// </remarks>
    public static string ApplyChatTemplate(string template, string[] roles, string[] contents, bool addAssistant)
    {
        if (roles == null) throw new ArgumentNullException(nameof(roles));
        if (contents == null) throw new ArgumentNullException(nameof(contents));
        if (roles.Length != contents.Length)
        {
            throw new ArgumentException(
                $"There are {roles.Length} roles and {contents.Length} message bodies; there must be one of "
                + "each per message.", nameof(contents));
        }

        List<byte[]> pinned = new List<byte[]>();
        byte[] templateBytes = template == null ? null : ToUtf8(template);
        LlamaChatMessage[] messages = new LlamaChatMessage[roles.Length == 0 ? 1 : roles.Length];

        for (int i = 0; i < roles.Length; i++)
        {
            pinned.Add(ToUtf8(roles[i] ?? string.Empty));
            pinned.Add(ToUtf8(contents[i] ?? string.Empty));
        }

        GCHandle[] handles = new GCHandle[pinned.Count];
        GCHandle templateHandle = default;
        try
        {
            for (int i = 0; i < pinned.Count; i++)
            {
                handles[i] = GCHandle.Alloc(pinned[i], GCHandleType.Pinned);
            }

            for (int i = 0; i < roles.Length; i++)
            {
                messages[i].Role = (byte*)handles[i * 2].AddrOfPinnedObject();
                messages[i].Content = (byte*)handles[(i * 2) + 1].AddrOfPinnedObject();
            }

            byte* templatePointer = null;
            if (templateBytes != null)
            {
                templateHandle = GCHandle.Alloc(templateBytes, GCHandleType.Pinned);
                templatePointer = (byte*)templateHandle.AddrOfPinnedObject();
            }

            byte[] buffer = new byte[EstimateRenderCapacity(roles, contents)];
            int written = Render(templatePointer, messages, roles.Length, addAssistant, buffer);

            if (written > buffer.Length)
            {
                buffer = new byte[written];
                written = Render(templatePointer, messages, roles.Length, addAssistant, buffer);
            }

            if (written < 0)
            {
                throw new ChatTemplateException(
                    "The engine does not recognise this chat template. Its built-in template support covers a "
                    + "fixed list of templates and does not run Jinja.");
            }

            return written == 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, Math.Min(written, buffer.Length));
        }
        finally
        {
            if (templateHandle.IsAllocated) templateHandle.Free();
            for (int i = 0; i < handles.Length; i++)
            {
                if (handles[i].IsAllocated) handles[i].Free();
            }
        }
    }

    /// <summary>Estimates how many bytes a rendered chat needs, so the ordinary case renders in one pass.</summary>
    /// <param name="roles">Each message's role.</param>
    /// <param name="contents">Each message's text, in the same order as <paramref name="roles"/>.</param>
    /// <returns>The estimate, in bytes.</returns>
    /// <remarks>
    /// Two bytes for every character of every role and every message body, plus a kilobyte for the template's
    /// own markup. The roles count too: they are short, but there is one per message and the built-in
    /// templates write each of them out, so leaving them out made the estimate short for a long conversation
    /// and cost a second render pass. The estimate only has to be close - a buffer that turns out to be too
    /// small is detected and the render is repeated against a big enough one.
    /// </remarks>
    internal static int EstimateRenderCapacity(string[] roles, string[] contents)
    {
        long capacity = 1024;

        if (roles != null)
        {
            for (int i = 0; i < roles.Length; i++)
            {
                capacity += (roles[i] ?? string.Empty).Length * 2L;
            }
        }

        if (contents != null)
        {
            for (int i = 0; i < contents.Length; i++)
            {
                capacity += (contents[i] ?? string.Empty).Length * 2L;
            }
        }

        return capacity > int.MaxValue ? int.MaxValue : (int)capacity;
    }

    private static int Render(byte* template, LlamaChatMessage[] messages, int count, bool addAssistant, byte[] buffer)
    {
        fixed (LlamaChatMessage* messagePointer = messages)
        fixed (byte* bufferPointer = buffer)
        {
            return NativeMethods.llama_chat_apply_template(
                template, messagePointer, (nuint)count, addAssistant, bufferPointer, buffer.Length);
        }
    }

    /// <summary>Reads one of a model's metadata values as text.</summary>
    /// <param name="model">The model.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>The value, or <see langword="null"/> when the model has no such key.</returns>
    public static string ModelMetaValue(IntPtr model, string key)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_model_meta_val_str(model, key, stack, StackBufferBytes);
        if (written < 0) return null;
        if (written < StackBufferBytes) return Encoding.UTF8.GetString(stack, written);

        byte[] buffer = new byte[written + 1];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_model_meta_val_str(model, key, pointer, (nuint)buffer.Length);
        }

        return written < 0 ? null : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Reads the metadata key at an index.</summary>
    /// <param name="model">The model.</param>
    /// <param name="index">The index, below <c>llama_model_meta_count</c>.</param>
    /// <returns>The key, or <see langword="null"/> when the index is out of range.</returns>
    public static string ModelMetaKeyAt(IntPtr model, int index)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_model_meta_key_by_index(model, index, stack, StackBufferBytes);
        if (written < 0) return null;
        if (written < StackBufferBytes) return Encoding.UTF8.GetString(stack, written);

        byte[] buffer = new byte[written + 1];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_model_meta_key_by_index(model, index, pointer, (nuint)buffer.Length);
        }

        return written < 0 ? null : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Reads the metadata value at an index as text.</summary>
    /// <param name="model">The model.</param>
    /// <param name="index">The index, below <c>llama_model_meta_count</c>.</param>
    /// <returns>The value, or <see langword="null"/> when the index is out of range.</returns>
    public static string ModelMetaValueAt(IntPtr model, int index)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_model_meta_val_str_by_index(model, index, stack, StackBufferBytes);
        if (written < 0) return null;
        if (written < StackBufferBytes) return Encoding.UTF8.GetString(stack, written);

        byte[] buffer = new byte[written + 1];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_model_meta_val_str_by_index(model, index, pointer, (nuint)buffer.Length);
        }

        return written < 0 ? null : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Reads the engine's one-line description of a model.</summary>
    /// <param name="model">The model.</param>
    /// <returns>The description, for example "llama 7B Q4_K - Medium".</returns>
    public static string ModelDescription(IntPtr model)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_model_desc(model, stack, StackBufferBytes);
        if (written < 0) return string.Empty;
        if (written < StackBufferBytes) return Encoding.UTF8.GetString(stack, written);

        byte[] buffer = new byte[written + 1];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_model_desc(model, pointer, (nuint)buffer.Length);
        }

        return written < 0 ? string.Empty : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Reads one of a LoRA adapter's metadata values as text.</summary>
    /// <param name="adapter">The adapter.</param>
    /// <param name="key">The metadata key.</param>
    /// <returns>The value, or <see langword="null"/> when the adapter has no such key.</returns>
    public static string AdapterMetaValue(IntPtr adapter, string key)
    {
        byte* stack = stackalloc byte[StackBufferBytes];
        int written = NativeMethods.llama_adapter_meta_val_str(adapter, key, stack, StackBufferBytes);
        if (written < 0) return null;
        if (written < StackBufferBytes) return Encoding.UTF8.GetString(stack, written);

        byte[] buffer = new byte[written + 1];
        fixed (byte* pointer = buffer)
        {
            written = NativeMethods.llama_adapter_meta_val_str(adapter, key, pointer, (nuint)buffer.Length);
        }

        return written < 0 ? null : Encoding.UTF8.GetString(buffer, 0, written);
    }

    /// <summary>Copies a string to a NUL-terminated UTF-8 byte array.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The bytes, terminator included.</returns>
    internal static byte[] ToUtf8(string value)
    {
        if (value == null) return new byte[1];
        int count = Encoding.UTF8.GetByteCount(value);
        byte[] bytes = new byte[count + 1];
        Encoding.UTF8.GetBytes(value, 0, value.Length, bytes, 0);
        return bytes;
    }

    private static int[] Trim(int[] source, int length)
    {
        if (length == source.Length) return source;
        int[] result = new int[length];
        Array.Copy(source, result, length);
        return result;
    }

    private static byte[] Trim(byte[] source, int length)
    {
        if (length == source.Length) return source;
        byte[] result = new byte[length];
        Array.Copy(source, result, length);
        return result;
    }
}
