using System;
using System.Runtime.InteropServices;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers the GGUF reader against the conformance model - the same file the native builds gate themselves
/// against - so the binding is checked over a file whose every key and tensor is known from the generator's
/// source.
/// </summary>
public sealed unsafe class NativeGgufTests
{
    /// <summary>The reader opens the conformance model and finds the tensors the generator wrote.</summary>
    [Fact]
    public void gguf_init_from_file_reads_the_conformance_model()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        GgufInitParams parameters = new GgufInitParams { NoAlloc = 1, Ctx = null };

        //Act
        using SafeGgufContextHandle gguf = Open(parameters);

        //Assert
        NativeMethods.gguf_get_n_tensors(gguf.DangerousGetHandle()).Should().Be(21L);
        NativeMethods.gguf_get_version(gguf.DangerousGetHandle()).Should().Be(3u);
    }

    /// <summary>The architecture key reads back as the generator wrote it.</summary>
    [Fact]
    public void gguf_get_val_str_reads_the_architecture()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        using SafeGgufContextHandle gguf = Open(new GgufInitParams { NoAlloc = 1, Ctx = null });

        //Act
        string architecture = ReadString(gguf, "general.architecture");
        string name = ReadString(gguf, "general.name");
        string tokenizer = ReadString(gguf, "tokenizer.ggml.model");

        //Assert
        architecture.Should().Be("llama");
        name.Should().Be("codebrix-conformance-tiny");
        tokenizer.Should().Be("no_vocab");
    }

    /// <summary>The numeric keys read back as the generator wrote them.</summary>
    [Fact]
    public void gguf_get_val_u32_reads_the_model_shape()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        using SafeGgufContextHandle gguf = Open(new GgufInitParams { NoAlloc = 1, Ctx = null });
        IntPtr ctx = gguf.DangerousGetHandle();

        //Act
        uint contextLength = NativeMethods.gguf_get_val_u32(ctx, NativeMethods.gguf_find_key(ctx, "llama.context_length"));
        uint embeddingLength = NativeMethods.gguf_get_val_u32(ctx, NativeMethods.gguf_find_key(ctx, "llama.embedding_length"));
        uint blockCount = NativeMethods.gguf_get_val_u32(ctx, NativeMethods.gguf_find_key(ctx, "llama.block_count"));
        uint vocabSize = NativeMethods.gguf_get_val_u32(ctx, NativeMethods.gguf_find_key(ctx, "llama.vocab_size"));
        uint feedForward = NativeMethods.gguf_get_val_u32(ctx, NativeMethods.gguf_find_key(ctx, "llama.feed_forward_length"));
        float ropeFreqBase = NativeMethods.gguf_get_val_f32(ctx, NativeMethods.gguf_find_key(ctx, "llama.rope.freq_base"));

        //Assert
        contextLength.Should().Be(64u);
        embeddingLength.Should().Be(32u);
        blockCount.Should().Be(2u);
        vocabSize.Should().Be(64u);
        feedForward.Should().Be(64u);
        ropeFreqBase.Should().Be(10000f);
    }

    /// <summary>A key that is not in the file reports -1 rather than throwing or aborting.</summary>
    [Fact]
    public void gguf_find_key_returns_minus_one_for_an_absent_key()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        using SafeGgufContextHandle gguf = Open(new GgufInitParams { NoAlloc = 1, Ctx = null });

        //Act
        long index = NativeMethods.gguf_find_key(gguf.DangerousGetHandle(), "general.not.a.real.key");

        //Assert
        index.Should().Be(-1L);
    }

    /// <summary>Every tensor is named, is 32-bit float, and has four dimension lengths.</summary>
    [Fact]
    public void gguf_get_tensor_reads_every_tensor()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        using SafeGgufContextHandle gguf = Open(new GgufInitParams { NoAlloc = 1, Ctx = null });
        IntPtr ctx = gguf.DangerousGetHandle();
        long count = NativeMethods.gguf_get_n_tensors(ctx);

        //Act
        long f32Tensors = 0;
        string firstName = null;
        for (long i = 0; i < count; i++)
        {
            if (NativeMethods.gguf_get_tensor_type(ctx, i) == GgmlType.F32) f32Tensors++;
            string name = Marshal.PtrToStringUTF8((IntPtr)NativeMethods.gguf_get_tensor_name(ctx, i));
            if (i == 0) firstName = name;
            name.Should().NotBeEmpty();
        }

        //Assert
        f32Tensors.Should().Be(21L);
        firstName.Should().Be("token_embd.weight");
        NativeMethods.gguf_find_tensor(ctx, "output.weight").Should().NotBe(-1L);
    }

    private static SafeGgufContextHandle Open(GgufInitParams parameters)
    {
        IntPtr context = NativeMethods.gguf_init_from_file(TestVectors.ConformanceModelPath, parameters);
        context.Should().NotBe(IntPtr.Zero);
        return new SafeGgufContextHandle(context);
    }

    private static string ReadString(SafeGgufContextHandle gguf, string key)
    {
        IntPtr ctx = gguf.DangerousGetHandle();
        long index = NativeMethods.gguf_find_key(ctx, key);
        index.Should().NotBe(-1L);
        return Marshal.PtrToStringUTF8((IntPtr)NativeMethods.gguf_get_val_str(ctx, index));
    }
}
