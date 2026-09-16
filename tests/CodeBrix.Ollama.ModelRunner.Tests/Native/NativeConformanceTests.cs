using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// The check that matters: loads the tiny conformance model through this binding, feeds it the same twelve
/// token ids the native gate's conformance-check.c feeds it, and compares every one of the 12 x 64 logits
/// with the reference in EXPECTED.txt.
/// </summary>
/// <remarks>
/// <para>
/// llama-native-tools already runs this in C for every native build. Running it again from managed code
/// proves something else: that the structure layouts, the by-value parameter passing and the batch handling
/// in this binding are right. A layout fault would not merely change a logit, it would produce garbage or a
/// crash - which is exactly why the comparison is over every value and why a non-finite logit is a failure
/// in its own right.
/// </para>
/// <para>
/// The context settings match conformance-check.c exactly, including its n_ctx of 256: the engine pads a
/// context up to its 256-token minimum whatever is asked for, and asking outright keeps a warning out of the
/// log. The prompt is twelve tokens, so the value cannot affect the logits.
/// </para>
/// </remarks>
public sealed unsafe class NativeConformanceTests
{
    private const double Tolerance = 0.001;

    private static readonly int[] Prompt = { 3, 17, 42, 8, 8, 61, 0, 25, 33, 12, 50, 7 };

    /// <summary>The model loads and reports the shape the generator wrote.</summary>
    [Fact]
    public void llama_model_load_from_file_reports_the_models_shape()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        using SafeLlamaModelHandle model = LoadModel();
        IntPtr vocab = NativeMethods.llama_model_get_vocab(model.DangerousGetHandle());

        //Assert
        NativeMethods.llama_vocab_n_tokens(vocab).Should().Be(64);
        NativeMethods.llama_model_n_embd(model.DangerousGetHandle()).Should().Be(32);
        NativeMethods.llama_model_n_layer(model.DangerousGetHandle()).Should().Be(2);
        NativeMethods.llama_model_n_head(model.DangerousGetHandle()).Should().Be(4);
        NativeMethods.llama_model_n_ctx_train(model.DangerousGetHandle()).Should().Be(64);
        NativeMethods.llama_model_ftype(model.DangerousGetHandle()).Should().Be(LlamaFtype.AllF32);
        (NativeMethods.llama_model_n_params(model.DangerousGetHandle()) > 0UL).Should().BeTrue();
        (NativeMethods.llama_model_size(model.DangerousGetHandle()) > 0UL).Should().BeTrue();
        NativeMethods.llama_vocab_type(vocab).Should().Be(LlamaVocabType.None);
    }

    /// <summary>Every logit the engine produces matches the recorded reference within the gate's tolerance.</summary>
    [Fact]
    public void llama_decode_reproduces_the_reference_logits()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();
        IReadOnlyList<ExpectedRecord> expected = ReadExpected(TestVectors.ExpectedPath);
        expected.Should().HaveCount(Prompt.Length);

        using SafeLlamaModelHandle model = LoadModel();
        IntPtr vocab = NativeMethods.llama_model_get_vocab(model.DangerousGetHandle());
        int vocabSize = NativeMethods.llama_vocab_n_tokens(vocab);
        vocabSize.Should().Be(64);

        using SafeLlamaContextHandle context = CreateContext(model);
        IntPtr ctx = context.DangerousGetHandle();

        //Act
        using LlamaBatchBuffer batch = new LlamaBatchBuffer(Prompt.Length, 1);
        for (int i = 0; i < Prompt.Length; i++)
        {
            batch.Add(Prompt[i], i, 0, true);
        }

        int decoded = NativeMethods.llama_decode(ctx, batch.Batch);

        //Assert
        decoded.Should().Be(0);

        double worst = 0.0;
        for (int position = 0; position < Prompt.Length; position++)
        {
            ExpectedRecord record = expected[position];
            record.Position.Should().Be(position);
            record.Token.Should().Be(Prompt[position]);
            record.Logits.Should().HaveCount(vocabSize);

            float* logits = NativeMethods.llama_get_logits_ith(ctx, position);
            ((IntPtr)logits).Should().NotBe(IntPtr.Zero);

            int argmax = 0;
            for (int j = 0; j < vocabSize; j++)
            {
                float value = logits[j];

                // A NaN or an infinity has to fail on its own: every comparison with NaN is false, so a
                // backend computing garbage would otherwise report a difference of zero and pass.
                float.IsFinite(value).Should().BeTrue();

                double difference = Math.Abs((double)value - record.Logits[j]);
                if (difference > worst) worst = difference;

                if (value > logits[argmax]) argmax = j;
            }

            argmax.Should().Be(record.Argmax);
        }

        (worst <= Tolerance).Should().BeTrue();
    }

    /// <summary>The batch buffer fills a batch the engine accepts and refuses to overfill it.</summary>
    [Fact]
    public void LlamaBatchBuffer_fills_and_clears_a_batch()
    {
        //Arrange
        NativeLibraryLoader.EnsureLoaded();

        //Act
        using LlamaBatchBuffer batch = new LlamaBatchBuffer(2, 1);
        batch.Add(3, 0, 0, false);
        batch.Add(17, 1, 0, true);

        //Assert
        batch.Count.Should().Be(2);
        batch.Batch.NTokens.Should().Be(2);
        batch.Batch.Token[0].Should().Be(3);
        batch.Batch.Pos[1].Should().Be(1);
        batch.Batch.Logits[1].Should().Be((sbyte)1);
        Action overfill = () => batch.Add(0, 2, 0, true);
        overfill.Should().Throw<InvalidOperationException>();

        batch.Clear();
        batch.Count.Should().Be(0);
    }

    private static SafeLlamaModelHandle LoadModel()
    {
        LlamaModelParams parameters = NativeDefaults.ModelParams;
        parameters.NGpuLayers = 0;
        parameters.LoadMode = LlamaLoadMode.Mmap;
        parameters.VocabOnly = 0;

        IntPtr model = NativeMethods.llama_model_load_from_file(TestVectors.ConformanceModelPath, parameters);
        model.Should().NotBe(IntPtr.Zero);
        return new SafeLlamaModelHandle(model);
    }

    private static SafeLlamaContextHandle CreateContext(SafeLlamaModelHandle model)
    {
        LlamaContextParams parameters = NativeDefaults.ContextParams;
        parameters.NCtx = 256;
        parameters.NBatch = 64;
        parameters.NUbatch = 64;
        parameters.NThreads = 4;
        parameters.NThreadsBatch = 4;
        parameters.Embeddings = 0;
        parameters.FlashAttnType = LlamaFlashAttnType.Disabled;

        IntPtr context = NativeMethods.llama_init_from_model(model.DangerousGetHandle(), parameters);
        context.Should().NotBe(IntPtr.Zero);
        return new SafeLlamaContextHandle(context);
    }

    /// <summary>Reads EXPECTED.txt: "pos i token id argmax id logits v0 ... vN", with '#' comment lines.</summary>
    private static IReadOnlyList<ExpectedRecord> ReadExpected(string path)
    {
        List<ExpectedRecord> records = new List<ExpectedRecord>();

        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            parts[0].Should().Be("pos");
            parts[2].Should().Be("token");
            parts[4].Should().Be("argmax");
            parts[6].Should().Be("logits");

            double[] logits = new double[parts.Length - 7];
            for (int i = 0; i < logits.Length; i++)
            {
                logits[i] = double.Parse(parts[i + 7], CultureInfo.InvariantCulture);
            }

            records.Add(new ExpectedRecord
            {
                Position = int.Parse(parts[1], CultureInfo.InvariantCulture),
                Token = int.Parse(parts[3], CultureInfo.InvariantCulture),
                Argmax = int.Parse(parts[5], CultureInfo.InvariantCulture),
                Logits = logits,
            });
        }

        return records;
    }

    private sealed class ExpectedRecord
    {
        public int Position { get; init; }

        public int Token { get; init; }

        public int Argmax { get; init; }

        public double[] Logits { get; init; }
    }
}
