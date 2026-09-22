using System;
using CodeBrix.Ollama.Core;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>A parseable empty graph padded to an exact size for store-accounting tests.</summary>
internal static class OnnxTestGraph
{
    internal static byte[] Bytes(long size)
    {
        var proto = new OnnxModelProto { IrVersion = 9, Graph = new OnnxGraphProto() };
        int padding = checked((int)size) - 10;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            proto.DocString = new string('x', padding);
            byte[] bytes = OnnxModel.FromProto(proto, null).Serialize();
            if (bytes.Length == size)
            {
                return bytes;
            }
            padding += checked((int)size - bytes.Length);
        }
        throw new InvalidOperationException("Could not pad the test graph.");
    }
}
