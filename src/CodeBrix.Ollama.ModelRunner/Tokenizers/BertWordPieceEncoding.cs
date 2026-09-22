namespace CodeBrix.Ollama.ModelRunner;

internal sealed class BertWordPieceEncoding
{
    internal BertWordPieceEncoding(long[] ids, bool truncated)
    {
        Ids = ids;
        Truncated = truncated;
    }
    internal long[] Ids { get; }
    internal bool Truncated { get; }
}
