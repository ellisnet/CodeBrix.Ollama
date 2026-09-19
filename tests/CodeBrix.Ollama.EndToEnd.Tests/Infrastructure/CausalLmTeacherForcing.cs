using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Ollama.ModelRunner;

namespace CodeBrix.Ollama.EndToEnd.Tests;

/// <summary>
/// The managed engine's answer at every position of a sequence, from ONE run over the whole of it.
/// </summary>
/// <remarks>
/// <para>
/// WHY TEACHER FORCING. Two engines running a quantized graph can differ by a millionth and still choose a
/// different token once, and from that point on they are writing different text and no comparison of the two
/// means anything. Reading the SAME sequence through both and comparing what each would have said next at
/// every position measures the disagreement itself, over every position rather than up to the first one.
/// </para>
/// <para>
/// It goes through the RAW <see cref="IOnnxModel"/> surface, which is public: the driver is not involved,
/// because what is being compared here is the arithmetic and not the loop.
/// </para>
/// </remarks>
public static class CausalLmTeacherForcing
{
    /// <summary>The most likely token at every position of every sequence.</summary>
    /// <param name="files">The bundle's files: its own names against the paths they are really at.</param>
    /// <param name="sequences">The sequences.</param>
    /// <param name="threads">How many threads the engine is given.</param>
    /// <param name="cancellationToken">The test's cancellation token.</param>
    /// <returns>One row of answers per sequence.</returns>
    public static async Task<IReadOnlyList<IReadOnlyList<int>>> RunAsync(
        IReadOnlyDictionary<string, string> files,
        IReadOnlyList<IReadOnlyList<int>> sequences,
        int threads,
        CancellationToken cancellationToken)
    {
        Decoder decoder = ReadDecoder(files["genai_config.json"]);

        await using IOnnxModel graph = await OnnxModel.LoadFromFilesAsync(
            files, decoder.FileName, new OnnxRunnerOptions { Threads = threads }, cancellationToken);

        List<IReadOnlyList<int>> answers = new List<IReadOnlyList<int>>();
        foreach (IReadOnlyList<int> sequence in sequences)
        {
            long[] ids = new long[sequence.Count];
            long[] mask = new long[sequence.Count];
            for (int i = 0; i < sequence.Count; i++)
            {
                ids[i] = sequence[i];
                mask[i] = 1;
            }

            Dictionary<string, OnnxTensor> feeds = new Dictionary<string, OnnxTensor>(StringComparer.Ordinal)
            {
                [decoder.InputIds] = OnnxTensor.FromInt64(ids, 1, ids.Length),
                [decoder.AttentionMask] = OnnxTensor.FromInt64(mask, 1, mask.Length),
            };

            OnnxTensor empty = OnnxTensor.FromFloats(
                Array.Empty<float>(), 1, decoder.KeyValueHeads, 0, decoder.HeadSize);
            for (int layer = 0; layer < decoder.Layers; layer++)
            {
                feeds[decoder.PastKeys.Replace("%d", layer.ToString())] = empty;
                feeds[decoder.PastValues.Replace("%d", layer.ToString())] = empty;
            }

            OnnxTensor logits = (await graph.RunAsync(feeds, cancellationToken))[decoder.Logits];

            int vocabulary = (int)logits.Shape[logits.Shape.Count - 1];
            int rows = (int)(logits.Count / vocabulary);
            List<int> row = new List<int>(rows);
            for (int at = 0; at < rows; at++)
            {
                int best = 0;
                float highest = float.NegativeInfinity;
                for (int token = 0; token < vocabulary; token++)
                {
                    float value = logits.Floats[(at * vocabulary) + token];
                    if (value <= highest) continue;

                    highest = value;
                    best = token;
                }

                row.Add(best);
            }

            answers.Add(row);
        }

        return answers;
    }

    /// <summary>How many of two rows of answers differ, position by position.</summary>
    /// <param name="left">One side's answers.</param>
    /// <param name="right">The other side's.</param>
    /// <returns>The number of positions that differ, and the number compared.</returns>
    public static (int Differing, int Compared) Compare(
        IReadOnlyList<IReadOnlyList<int>> left, IReadOnlyList<IReadOnlyList<int>> right)
    {
        int differing = 0;
        int compared = 0;
        for (int sequence = 0; sequence < left.Count && sequence < right.Count; sequence++)
        {
            IReadOnlyList<int> one = left[sequence];
            IReadOnlyList<int> other = right[sequence];
            for (int at = 0; at < one.Count && at < other.Count; at++)
            {
                compared++;
                if (one[at] != other[at]) differing++;
            }
        }

        return (differing, compared);
    }

    private static Decoder ReadDecoder(string configurationPath)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllBytes(configurationPath));
        JsonElement decoder = document.RootElement.GetProperty("model").GetProperty("decoder");
        JsonElement inputs = decoder.GetProperty("inputs");
        JsonElement outputs = decoder.GetProperty("outputs");

        return new Decoder
        {
            FileName = decoder.GetProperty("filename").GetString(),
            InputIds = inputs.GetProperty("input_ids").GetString(),
            AttentionMask = inputs.GetProperty("attention_mask").GetString(),
            PastKeys = inputs.GetProperty("past_key_names").GetString(),
            PastValues = inputs.GetProperty("past_value_names").GetString(),
            Logits = outputs.GetProperty("logits").GetString(),
            Layers = decoder.GetProperty("num_hidden_layers").GetInt32(),
            KeyValueHeads = decoder.GetProperty("num_key_value_heads").GetInt32(),
            HeadSize = decoder.GetProperty("head_size").GetInt32(),
        };
    }

    private sealed class Decoder
    {
        public string FileName { get; set; }

        public string InputIds { get; set; }

        public string AttentionMask { get; set; }

        public string PastKeys { get; set; }

        public string PastValues { get; set; }

        public string Logits { get; set; }

        public int Layers { get; set; }

        public int KeyValueHeads { get; set; }

        public int HeadSize { get; set; }
    }
}
