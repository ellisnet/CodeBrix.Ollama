namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp include/llama.h;


/// <summary>
/// The tokenizer family a model's vocabulary uses. Mirrors <c>enum llama_vocab_type</c>.
/// </summary>
internal enum LlamaVocabType : int
{
    /// <summary>No vocabulary; the model takes token ids directly.</summary>
    None = 0,

    /// <summary>The LLaMA tokenizer: byte-level BPE with byte fallback.</summary>
    Spm = 1,

    /// <summary>The GPT-2 tokenizer: byte-level BPE.</summary>
    Bpe = 2,

    /// <summary>The BERT tokenizer: WordPiece.</summary>
    Wpm = 3,

    /// <summary>The T5 tokenizer: Unigram.</summary>
    Ugm = 4,

    /// <summary>The RWKV tokenizer: greedy tokenization.</summary>
    Rwkv = 5,

    /// <summary>The PLaMo-2 tokenizer: Aho-Corasick with dynamic programming.</summary>
    Plamo2 = 6,
}
