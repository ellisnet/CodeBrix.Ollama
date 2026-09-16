using System;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp common/sampling.cpp (chain order only);

/// <summary>
/// The sampler chain for one request: the engine's own samplers, added in the order llama.cpp's own front
/// end adds them.
/// </summary>
/// <remarks>
/// <para>
/// Order is not a detail. Penalties act on raw logits and so come first; a grammar has to reject tokens
/// before any truncation decides the candidate set is small enough; the truncations run from the coarsest to
/// the finest; temperature is applied last, immediately before the draw, because every earlier stage
/// compares probabilities and temperature changes them. This is the order in llama.cpp's
/// <c>common/sampling.cpp</c>, and a chain in any other order produces different text for the same seed.
/// </para>
/// <para>
/// A temperature of zero or less means "always the most likely token", and llama.cpp expresses that as a
/// greedy sampler rather than as a temperature of zero, which would divide by zero. The chain owns every
/// sampler added to it and frees them all when it is freed, so nothing added here is ever freed separately.
/// </para>
/// <para>
/// Two things here are deliberately not what llama.cpp's own front end does. The first is that the penalties
/// sampler is added before the grammar and before every truncation, so it scores the whole vocabulary rather
/// than a set something else has already cut down; <c>llama.h</c> warns that a sampler placed after a
/// truncation sees only what survived it, and penalties that see only the top few candidates penalise
/// nothing the model was not going to say anyway. The second is what the penalties see: this engine accepts
/// only the tokens a request generated, never the tokens of its prompt, because the grammar sampler in the
/// same chain must not be advanced over text the model did not write. llama.cpp's <c>main</c> seeds its
/// penalty window from the prompt as well, so a repeat penalty here acts on a shorter history than the same
/// numbers would give there.
/// </para>
/// </remarks>
internal sealed class EngineSamplerChain : IDisposable
{
    private const string GrammarRoot = "root";
    private const uint RandomSeed = 0xFFFFFFFFu;

    private SafeLlamaSamplerHandle chain;

    private EngineSamplerChain(SafeLlamaSamplerHandle chain)
    {
        this.chain = chain;
    }

    /// <summary>The chain, ready for <c>llama_sampler_sample</c>.</summary>
    /// <returns>The native handle.</returns>
    /// <exception cref="ObjectDisposedException">The chain has been freed.</exception>
    public IntPtr Handle()
    {
        if (chain == null) throw new ObjectDisposedException(nameof(EngineSamplerChain));
        return chain.DangerousGetHandle();
    }

    /// <summary>Builds the chain for one request.</summary>
    /// <param name="vocab">The model's vocabulary, which the grammar sampler needs.</param>
    /// <param name="sampling">The sampling parameters, or <see langword="null"/> for the defaults.</param>
    /// <param name="grammar">The GBNF grammar text, or <see langword="null"/> for an unconstrained chain.</param>
    /// <returns>The chain.</returns>
    /// <exception cref="GrammarException">The grammar text does not parse.</exception>
    /// <exception cref="InferenceException">The engine would not create the chain.</exception>
    public static EngineSamplerChain Create(IntPtr vocab, SamplingOptions sampling, string grammar)
    {
        SamplingOptions options = sampling ?? new SamplingOptions();

        IntPtr raw = NativeMethods.llama_sampler_chain_init(NativeDefaults.SamplerChainParams);
        if (raw == IntPtr.Zero)
        {
            throw new InferenceException(EngineLog.Describe("The engine could not create a sampler chain."));
        }

        SafeLlamaSamplerHandle handle = new SafeLlamaSamplerHandle(raw);

        try
        {
            bool penalising = options.RepeatLastN != 0
                && (Math.Abs(options.RepeatPenalty - 1.0f) > float.Epsilon
                    || Math.Abs(options.FrequencyPenalty) > float.Epsilon
                    || Math.Abs(options.PresencePenalty) > float.Epsilon);

            if (penalising)
            {
                Add(raw, NativeMethods.llama_sampler_init_penalties(
                    options.RepeatLastN,
                    options.RepeatPenalty,
                    options.FrequencyPenalty,
                    options.PresencePenalty));
            }

            if (!string.IsNullOrWhiteSpace(grammar))
            {
                IntPtr constraint = NativeMethods.llama_sampler_init_grammar(vocab, grammar, GrammarRoot);
                if (constraint == IntPtr.Zero)
                {
                    throw new GrammarException(EngineLog.Describe(
                        "The engine could not parse this GBNF grammar. It must define a rule named "
                        + $"'{GrammarRoot}'."));
                }

                Add(raw, constraint);
            }

            if (options.Temperature <= 0.0f)
            {
                Add(raw, NativeMethods.llama_sampler_init_greedy());
            }
            else
            {
                if (options.TopK > 0) Add(raw, NativeMethods.llama_sampler_init_top_k(options.TopK));
                if (options.TypicalP < 1.0f) Add(raw, NativeMethods.llama_sampler_init_typical(options.TypicalP, 1));
                if (options.TopP < 1.0f) Add(raw, NativeMethods.llama_sampler_init_top_p(options.TopP, 1));
                if (options.MinP > 0.0f) Add(raw, NativeMethods.llama_sampler_init_min_p(options.MinP, 1));

                Add(raw, NativeMethods.llama_sampler_init_temp(options.Temperature));
                Add(raw, NativeMethods.llama_sampler_init_dist(options.Seed ?? RandomSeed));
            }

            return new EngineSamplerChain(handle);
        }
        catch (Exception)
        {
            handle.Dispose();
            throw;
        }
    }

    /// <summary>Frees the chain and every sampler in it.</summary>
    public void Dispose()
    {
        SafeLlamaSamplerHandle current = chain;
        if (current == null) return;

        chain = null;
        current.Dispose();
    }

    private static void Add(IntPtr chain, IntPtr sampler)
    {
        if (sampler == IntPtr.Zero)
        {
            throw new InferenceException(EngineLog.Describe("The engine would not create one of the samplers."));
        }

        NativeMethods.llama_sampler_chain_add(chain, sampler);
    }
}
