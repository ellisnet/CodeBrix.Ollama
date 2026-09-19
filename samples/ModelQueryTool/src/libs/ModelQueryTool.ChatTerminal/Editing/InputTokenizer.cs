using System.Collections.Generic;
using System.Text;

namespace ModelQueryTool.ChatTerminal.Editing;

/// <summary>
/// Decodes a stream of VT-encoded terminal input into <see cref="InputToken"/>s.
/// The tokenizer is stateful and chunk-safe: an escape sequence split across
/// two <see cref="Feed"/> calls is held until its final byte arrives, so input
/// may be delivered in arbitrary fragments (per keystroke or per paste).
/// </summary>
public sealed class InputTokenizer
{
    private enum State
    {
        Ground,
        Escape,      //Saw ESC, awaiting '[' (CSI), 'O' (SS3), or anything else
        Csi,         //Inside an ESC [ sequence, accumulating parameter bytes
        Ss3          //Saw ESC O, awaiting the single final byte
    }

    private State _state = State.Ground;
    private readonly StringBuilder _csiParams = new();

    /// <summary>
    /// Decodes the next fragment of input and returns the tokens completed by
    /// it. Unfinished escape sequences carry over to the next call.
    /// </summary>
    /// <param name="data">The raw input fragment, as sent by the terminal view.</param>
    public IReadOnlyList<InputToken> Feed(string data)
    {
        var tokens = new List<InputToken>();
        if (string.IsNullOrEmpty(data)) { return tokens; }

        foreach (var c in data)
        {
            switch (_state)
            {
                case State.Ground:
                    FeedGround(c, tokens);
                    break;

                case State.Escape:
                    if (c == '[')
                    {
                        _state = State.Csi;
                        _csiParams.Clear();
                    }
                    else if (c == 'O')
                    {
                        _state = State.Ss3;
                    }
                    else
                    {
                        //Alt-modified or bare ESC + char: drop the ESC, take the char
                        _state = State.Ground;
                        FeedGround(c, tokens);
                    }
                    break;

                case State.Csi:
                    if (c >= '\x40' && c <= '\x7e')
                    {
                        //Final byte - the sequence is complete
                        var token = DecodeCsi(c, _csiParams.ToString());
                        if (token.HasValue) { tokens.Add(token.Value); }
                        _state = State.Ground;
                    }
                    else
                    {
                        _csiParams.Append(c);
                    }
                    break;

                case State.Ss3:
                    var ss3Token = DecodeSs3(c);
                    if (ss3Token.HasValue) { tokens.Add(ss3Token.Value); }
                    _state = State.Ground;
                    break;
            }
        }

        return tokens;
    }

    private void FeedGround(char c, List<InputToken> tokens)
    {
        if (c == '\x1b')
        {
            _state = State.Escape;
        }
        else if (c < '\x20' || c == '\x7f')
        {
            tokens.Add(InputToken.ForControl(c));
        }
        else
        {
            tokens.Add(InputToken.ForCharacter(c));
        }
    }

    private static InputToken? DecodeCsi(char finalByte, string parameters)
    {
        switch (finalByte)
        {
            case 'A': return InputToken.ForKey(EditKey.Up);
            case 'B': return InputToken.ForKey(EditKey.Down);
            case 'C': return InputToken.ForKey(EditKey.Right);
            case 'D': return InputToken.ForKey(EditKey.Left);
            case 'H': return InputToken.ForKey(EditKey.Home);
            case 'F': return InputToken.ForKey(EditKey.End);
            case '~':
                switch (parameters)
                {
                    case "1":
                    case "7": return InputToken.ForKey(EditKey.Home);
                    case "3": return InputToken.ForKey(EditKey.Delete);
                    case "4":
                    case "8": return InputToken.ForKey(EditKey.End);
                    case "5": return InputToken.ForKey(EditKey.PageUp);
                    case "6": return InputToken.ForKey(EditKey.PageDown);
                    default: return null;
                }
            default:
                return null;
        }
    }

    private static InputToken? DecodeSs3(char finalByte)
    {
        switch (finalByte)
        {
            case 'A': return InputToken.ForKey(EditKey.Up);
            case 'B': return InputToken.ForKey(EditKey.Down);
            case 'C': return InputToken.ForKey(EditKey.Right);
            case 'D': return InputToken.ForKey(EditKey.Left);
            case 'H': return InputToken.ForKey(EditKey.Home);
            case 'F': return InputToken.ForKey(EditKey.End);
            default: return null;
        }
    }
}
