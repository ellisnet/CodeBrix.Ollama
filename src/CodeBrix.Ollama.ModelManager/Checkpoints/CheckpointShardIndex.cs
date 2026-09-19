using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// A checkpoint's shard index - <c>model.safetensors.index.json</c> or <c>pytorch_model.bin.index.json</c> -
/// which says which file every tensor of a split checkpoint is in.
/// </summary>
/// <remarks>
/// <para>
/// The only part of the file this reads is <c>weight_map</c>, an object mapping a tensor's name on to the name
/// of the file that holds it. The <c>metadata</c> object publishers write beside it records the total size and
/// is not used: the files themselves say how big they are.
/// </para>
/// <para>
/// THE SHARD ORDER IS THE INDEX'S, NOT THE FOLDER'S. The inference engine's converter takes the distinct file
/// names out of the weight map and sorts them, so a file sitting in the folder that the index does not name is
/// never opened and never contributes a tensor. That is why this type carries the file names rather than
/// leaving the caller to list the directory.
/// </para>
/// </remarks>
internal sealed class CheckpointShardIndex
{
    private readonly OrderedDictionary<string, string> _weightMap;

    private CheckpointShardIndex(string path, OrderedDictionary<string, string> weightMap,
        IReadOnlyList<string> shardFileNames, IReadOnlyList<string> tensorNames)
    {
        Path = path;
        _weightMap = weightMap;
        ShardFileNames = shardFileNames;
        TensorNames = tensorNames;
    }

    /// <summary>The path of the index file.</summary>
    internal string Path { get; }

    /// <summary>The distinct file names the weight map points at, sorted the way the engine sorts them.</summary>
    internal IReadOnlyList<string> ShardFileNames { get; }

    /// <summary>The tensor names the index declares, in the order the file declares them.</summary>
    internal IReadOnlyList<string> TensorNames { get; }

    /// <summary>Reads a shard index, if the directory holds one under that name.</summary>
    /// <param name="directory">The checkpoint directory.</param>
    /// <param name="fileName">The index file's name.</param>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The index, or <see langword="null"/> when the directory holds no such file.</returns>
    /// <exception cref="CheckpointFormatException">The file is not an index this reader can use.</exception>
    internal static async Task<CheckpointShardIndex> LoadAsync(string directory, string fileName,
        CancellationToken cancellationToken)
    {
        string path = System.IO.Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            return null;
        }

        byte[] content = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
        return Parse(content, path);
    }

    /// <summary>Reads a shard index from the bytes of an index file.</summary>
    /// <param name="content">The file's bytes.</param>
    /// <param name="path">The path, for messages.</param>
    /// <returns>The index.</returns>
    /// <exception cref="CheckpointFormatException">The file is not an index this reader can use.</exception>
    internal static CheckpointShardIndex Parse(byte[] content, string path)
    {
        var weightMap = new OrderedDictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using (JsonDocument document = JsonDocument.Parse(content))
            {
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" is not a JSON object.");
                }

                if (!document.RootElement.TryGetProperty("weight_map", out JsonElement map)
                    || map.ValueKind != JsonValueKind.Object)
                {
                    throw new CheckpointFormatException("The file \"" + path + "\" has no weight_map object, " +
                        "so it does not say which file each tensor is in.");
                }

                foreach (JsonProperty entry in map.EnumerateObject())
                {
                    if (entry.Value.ValueKind != JsonValueKind.String)
                    {
                        throw new CheckpointFormatException("The file \"" + path + "\" maps the tensor \"" +
                            entry.Name + "\" on to something that is not a file name.");
                    }

                    if (weightMap.ContainsKey(entry.Name))
                    {
                        throw new CheckpointFormatException("The file \"" + path + "\" lists the tensor \"" +
                            entry.Name + "\" twice, so it does not say which file holds it.");
                    }

                    string shard = entry.Value.GetString();
                    RequireShardName(path, entry.Name, shard);
                    weightMap[entry.Name] = shard;
                }
            }
        }
        catch (JsonException exception)
        {
            throw new CheckpointFormatException("The file \"" + path + "\" is not valid JSON.", exception);
        }

        if (weightMap.Count == 0)
        {
            throw new CheckpointFormatException(
                "The weight_map of \"" + path + "\" is empty, so the index names no tensors.");
        }

        var tensorNames = new List<string>(weightMap.Count);
        foreach (KeyValuePair<string, string> entry in weightMap)
        {
            tensorNames.Add(entry.Key);
        }

        return new CheckpointShardIndex(path, weightMap, SortedShardNames(weightMap), tensorNames);
    }

    /// <summary>The file the index says holds one tensor.</summary>
    /// <param name="tensorName">The tensor's name.</param>
    /// <param name="fileName">Receives the file name.</param>
    /// <returns><see langword="true"/> when the index lists the tensor.</returns>
    internal bool TryGetShard(string tensorName, out string fileName)
    {
        return _weightMap.TryGetValue(tensorName, out fileName);
    }

    private static void RequireShardName(string path, string tensorName, string shard)
    {
        if (string.IsNullOrEmpty(shard))
        {
            throw new CheckpointFormatException("The file \"" + path + "\" gives the tensor \"" + tensorName +
                "\" an empty file name.");
        }

        // An index names a file beside itself; a path of any other shape would reach outside the checkpoint.
        if (shard.IndexOf('/') >= 0 || shard.IndexOf('\\') >= 0
            || !string.Equals(shard, System.IO.Path.GetFileName(shard), StringComparison.Ordinal))
        {
            throw new CheckpointFormatException("The file \"" + path + "\" puts the tensor \"" + tensorName +
                "\" in \"" + shard + "\", which is not the name of a file beside the index.");
        }
    }

    private static IReadOnlyList<string> SortedShardNames(OrderedDictionary<string, string> weightMap)
    {
        var distinct = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, string> entry in weightMap)
        {
            if (seen.Add(entry.Value))
            {
                distinct.Add(entry.Value);
            }
        }

        distinct.Sort(StringComparer.Ordinal);
        return distinct;
    }
}
