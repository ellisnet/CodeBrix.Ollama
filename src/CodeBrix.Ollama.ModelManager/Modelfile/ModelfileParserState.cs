// Ported from Ollama (https://github.com/ollama/ollama), MIT License, Copyright (c) Ollama. Source: parser/parser.go at commit a43fad18.
namespace CodeBrix.Ollama.ModelManager;

/// <summary>
/// The states of the Modelfile parser's state machine, one for one with the <c>state</c> constants
/// in Ollama's parser.
/// </summary>
internal enum ModelfileParserState
{
    /// <summary>Between commands: leading whitespace, blank lines and the end of a value.</summary>
    Nil = 0,

    /// <summary>Reading the command keyword, for example FROM or PARAMETER.</summary>
    Name = 1,

    /// <summary>Reading a command's argument text, quoted or not.</summary>
    Value = 2,

    /// <summary>Reading the parameter name that follows the PARAMETER keyword.</summary>
    Parameter = 3,

    /// <summary>Reading the role that follows the MESSAGE keyword.</summary>
    Message = 4,

    /// <summary>Inside a # comment, up to the end of the line.</summary>
    Comment = 5,
}
