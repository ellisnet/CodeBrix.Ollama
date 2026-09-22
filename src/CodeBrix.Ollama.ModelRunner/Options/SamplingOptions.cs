using System;

namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// The sampling parameters that turn the model's next-token distribution into a chosen token. The defaults are
/// Ollama's.
/// </summary>
public sealed class SamplingOptions
{
    private uint? _seed;

    /// <summary>
    /// Temperature. 0 selects the most likely token every time (greedy); higher values flatten the
    /// distribution. Default 0.8.
    /// </summary>
    public float Temperature { get; set; } = 0.8f;

    /// <summary>Keep only the k most likely tokens. 0 disables. Default 40.</summary>
    public int TopK { get; set; } = 40;

    /// <summary>Keep the smallest set of tokens whose cumulative probability reaches p. 1.0 disables. Default 0.9.</summary>
    public float TopP { get; set; } = 0.9f;

    /// <summary>Drop tokens less likely than p times the most likely token. 0 disables. Default 0.0.</summary>
    public float MinP { get; set; }

    /// <summary>Locally typical sampling parameter. 1.0 disables. Default 1.0.</summary>
    public float TypicalP { get; set; } = 1.0f;

    /// <summary>Penalty applied to tokens that already appeared in the last <see cref="RepeatLastN"/> tokens. 1.0 disables. Default 1.1.</summary>
    public float RepeatPenalty { get; set; } = 1.1f;

    /// <summary>How many recent tokens the repeat penalty considers. 0 disables; -1 means the whole context. Default 64.</summary>
    public int RepeatLastN { get; set; } = 64;

    /// <summary>Presence penalty. 0 disables. Default 0.0.</summary>
    public float PresencePenalty { get; set; }

    /// <summary>Frequency penalty. 0 disables. Default 0.0.</summary>
    public float FrequencyPenalty { get; set; }

    /// <summary>
    /// The random seed. <see langword="null"/> (the default) draws a fresh seed for every request; a fixed
    /// value selects a repeatable random stream. Reproducing output also requires the same model, settings,
    /// build and hardware. For native GGUF generation, call <see cref="IRunningModel.ClearCacheAsync"/>
    /// before each run being compared: cached prompt evaluation can change floating-point rounding.
    /// </summary>
    /// <remarks>
    /// Set this explicitly to <see langword="null"/> to request randomness. The native engine's reserved
    /// value 0xFFFFFFFF, including an unchecked conversion of -1 to <see cref="uint"/>, is rejected.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is 0xFFFFFFFF.</exception>
    public uint? Seed
    {
        get => _seed;
        set
        {
            if (value == uint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(Seed), value,
                    "Seed 0xFFFFFFFF (an unchecked conversion of -1 to uint) is reserved. Use null for a fresh random seed.");
            _seed = value;
        }
    }
}
