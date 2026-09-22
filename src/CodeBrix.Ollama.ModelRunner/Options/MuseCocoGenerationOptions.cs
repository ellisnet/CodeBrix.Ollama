using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>Sampling and length settings for MuseCoco attribute-to-MIDI generation.</summary>
public sealed class MuseCocoGenerationOptions
{
    private long? _seed;

    /// <summary>Maximum generated REMIGEN2 tokens, excluding the attribute prefix. Default 2560.</summary>
    public int MaximumTokens { get; set; } = 2560;

    /// <summary>Number of tokens before EOS is allowed. Default 512; must not exceed MaximumTokens.</summary>
    public int MinimumTokens { get; set; } = 512;

    /// <summary>Positive softmax temperature. Default 1.</summary>
    public double Temperature { get; set; } = 1;

    /// <summary>Maximum most likely candidates to retain. Default 15. One gives deterministic greedy generation.</summary>
    public int TopK { get; set; } = 15;

    /// <summary>Nucleus threshold after top-k filtering, in (0, 1]. Default 1 keeps all top-k candidates.</summary>
    public double TopP { get; set; } = 1;

    /// <summary>
    /// Fixed nonnegative seed, or null (default) to deliberately request a fresh random seed.
    /// The reserved native-runner sentinel 0xFFFFFFFF and all negative values are rejected.
    /// The result reports the effective seed. Repeatability also requires the same model precision,
    /// settings, build and processor arithmetic; it does not imply identical output across precisions.
    /// </summary>
    public long? Seed
    {
        get => _seed;
        set
        {
            if (value < 0 || value == uint.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(Seed), value,
                    "Seed must be nonnegative and cannot be 0xFFFFFFFF. Use null for a fresh random seed.");
            }
            _seed = value;
        }
    }

    internal MuseCocoGenerationOptions CopyAndValidate(int maximumTokens)
    {
        var copy = (MuseCocoGenerationOptions)MemberwiseClone();
        if (copy.MaximumTokens < 1 || copy.MaximumTokens > maximumTokens) throw new ArgumentOutOfRangeException(nameof(MaximumTokens));
        if (copy.MinimumTokens < 0 || copy.MinimumTokens > copy.MaximumTokens) throw new ArgumentOutOfRangeException(nameof(MinimumTokens));
        if (!double.IsFinite(copy.Temperature) || copy.Temperature <= 0) throw new ArgumentOutOfRangeException(nameof(Temperature));
        if (copy.TopK < 1) throw new ArgumentOutOfRangeException(nameof(TopK));
        if (!double.IsFinite(copy.TopP) || copy.TopP <= 0 || copy.TopP > 1) throw new ArgumentOutOfRangeException(nameof(TopP));
        return copy;
    }
}
