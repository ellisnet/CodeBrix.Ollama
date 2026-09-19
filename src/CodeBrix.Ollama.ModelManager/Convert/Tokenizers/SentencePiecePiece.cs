namespace CodeBrix.Ollama.ModelManager; //was previously: conversion/base.py@b10221;

/// <summary>
/// One entry of a SentencePiece vocabulary: its text, the score the trainer gave it and what kind of piece it
/// is. Its position in <see cref="SentencePieceModel.Pieces"/> is its identifier.
/// </summary>
internal sealed class SentencePiecePiece
{
    /// <summary>Creates a piece.</summary>
    /// <param name="text">The piece's text.</param>
    /// <param name="score">The score the trainer gave it.</param>
    /// <param name="type">What kind of piece it is.</param>
    internal SentencePiecePiece(string text, float score, SentencePieceTokenType type)
    {
        Text = text;
        Score = score;
        Type = type;
    }

    /// <summary>The piece's text, with the space marker still in it.</summary>
    internal string Text { get; }

    /// <summary>The score the trainer gave the piece.</summary>
    internal float Score { get; }

    /// <summary>What kind of piece it is.</summary>
    internal SentencePieceTokenType Type { get; }

    /// <summary>Returns the piece's text.</summary>
    /// <returns>The value of <see cref="Text"/>.</returns>
    public override string ToString()
    {
        return Text ?? string.Empty;
    }
}
