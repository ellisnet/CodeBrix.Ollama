using System;
using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// One generation, settled and checked before any of it runs: how long it may go on, what ends it, how a
/// token is chosen, and which of the request's settings this driver cannot honour.
/// </summary>
/// <remarks>
/// It is built where the CALLER wrote the request rather than at the first step of the loop, so a setting
/// this driver does not implement is refused with the call that asked for it and not halfway through an
/// enumeration. The copies it takes are what make a caller's later edits unable to change a generation that
/// is already running.
/// </remarks>
internal sealed class CausalLmPlan
{
    private CausalLmPlan(
        int? maximumTokens, IReadOnlyList<string> stopSequences, SamplingOptions sampling, ulong seed)
    {
        MaximumTokens = maximumTokens;
        StopSequences = stopSequences;
        Sampling = sampling;
        Seed = seed;
    }

    /// <summary>The largest number of tokens to generate, or <see langword="null"/> for as many as fit.</summary>
    internal int? MaximumTokens { get; }

    /// <summary>The strings that end the generation when the text reaches one of them.</summary>
    internal IReadOnlyList<string> StopSequences { get; }

    /// <summary>The sampling parameters, copied.</summary>
    internal SamplingOptions Sampling { get; }

    /// <summary>The seed the draw starts from, settled here so that the statistics can name it.</summary>
    internal ulong Seed { get; }

    /// <summary>Settles one request's options and refuses by name what this driver does not implement.</summary>
    /// <param name="options">The caller's options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><c>MaxTokens</c> is below one.</exception>
    /// <exception cref="NotSupportedException">
    /// The request asks for grammar-constrained output, which needs a constraint engine this driver does not
    /// carry.
    /// </exception>
    internal static CausalLmPlan For(GenerationOptions options)
    {
        GenerationOptions settings = options ?? new GenerationOptions();

        if (settings.MaxTokens.HasValue && settings.MaxTokens.Value < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), settings.MaxTokens.Value, "A token limit must be at least one.");
        }

        //Grammar-constrained output is a whole constraint engine of its own - the native half of this library
        //gets it from the engine it binds to - and this driver has none. It is refused by the name of the
        //setting that asked for it rather than quietly ignored, because ignoring it would hand back text that
        //does not obey the schema the caller was relying on.
        if (!string.IsNullOrWhiteSpace(settings.Grammar))
        {
            throw new NotSupportedException(
                "GenerationOptions.Grammar is not supported when generating from an ONNX bundle: this driver"
                + " carries no grammar engine, so it cannot constrain what the model writes.");
        }

        if (!string.IsNullOrWhiteSpace(settings.JsonSchema))
        {
            throw new NotSupportedException(
                "GenerationOptions.JsonSchema is not supported when generating from an ONNX bundle: it is"
                + " turned into a grammar, and this driver carries no grammar engine.");
        }

        if (settings.JsonMode)
        {
            throw new NotSupportedException(
                "GenerationOptions.JsonMode is not supported when generating from an ONNX bundle: it is"
                + " turned into a grammar, and this driver carries no grammar engine.");
        }

        List<string> stops = new List<string>();
        foreach (string stop in settings.StopSequences)
        {
            if (!string.IsNullOrEmpty(stop)) stops.Add(stop);
        }

        SamplingOptions sampling = Copy(settings.Sampling ?? new SamplingOptions());

        //Only null requests randomness; SamplingOptions rejects the native engine's numeric sentinel.
        ulong seed = sampling.Seed.HasValue
            ? sampling.Seed.Value
            : Fresh();

        return new CausalLmPlan(settings.MaxTokens, stops, sampling, seed);
    }

    private static SamplingOptions Copy(SamplingOptions sampling) => new SamplingOptions
    {
        Temperature = sampling.Temperature,
        TopK = sampling.TopK,
        TopP = sampling.TopP,
        MinP = sampling.MinP,
        TypicalP = sampling.TypicalP,
        RepeatPenalty = sampling.RepeatPenalty,
        RepeatLastN = sampling.RepeatLastN,
        PresencePenalty = sampling.PresencePenalty,
        FrequencyPenalty = sampling.FrequencyPenalty,
        Seed = sampling.Seed,
    };

    //A fresh stream for an explicitly unset seed, independent of the framework's sampling algorithm.
    private static ulong Fresh()
    {
        Random source = Random.Shared;
        return ((ulong)(uint)source.Next() << 32) | (uint)source.Next();
    }
}
