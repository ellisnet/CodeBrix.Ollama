namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// What a SentencePiece model says one of its pieces is. The numbers are the ones the
/// <c>ModelProto.SentencePiece.Type</c> enumeration gives them and are what a <c>tokenizer.model</c> carries on
/// the wire, so they are fixed by the file format rather than chosen here.
/// </summary>
internal enum SentencePieceTokenType
{
    /// <summary>An ordinary piece of the vocabulary. This is the value a piece takes when it names none.</summary>
    Normal = 1,

    /// <summary>The piece that stands for text the model cannot represent.</summary>
    Unknown = 2,

    /// <summary>A piece the tokenizer never produces from text, such as a beginning-of-sequence marker.</summary>
    Control = 3,

    /// <summary>A piece the publisher added by hand and the tokenizer matches literally.</summary>
    UserDefined = 4,

    /// <summary>A piece that is present but disabled.</summary>
    Unused = 5,

    /// <summary>One of the 256 pieces that stand for a single byte, written <c>&lt;0xNN&gt;</c>.</summary>
    Byte = 6,
}
