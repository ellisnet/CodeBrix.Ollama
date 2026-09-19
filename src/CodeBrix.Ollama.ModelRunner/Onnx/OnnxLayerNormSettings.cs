namespace CodeBrix.Ollama.ModelRunner;

/// <summary>
/// What a normalization node settled when the model was loaded: where the normalized part of the shape starts
/// and what is added inside the square root.
/// </summary>
internal sealed class OnnxLayerNormSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="axis">Where the normalized part of the shape starts; it may be negative.</param>
    /// <param name="epsilon">What is added to the mean square before the square root.</param>
    internal OnnxLayerNormSettings(long axis, float epsilon)
    {
        Axis = axis;
        Epsilon = epsilon;
    }

    /// <summary>Where the normalized part of the shape starts; it may be negative.</summary>
    internal long Axis { get; }

    /// <summary>What is added to the mean square before the square root.</summary>
    internal float Epsilon { get; }
}
