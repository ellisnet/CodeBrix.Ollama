using System;
using System.Text;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ollama/ollama thinking/parser.go;

/// <summary>
/// Splits a thinking model's output stream into reasoning text and answer text as it arrives.
/// </summary>
/// <remarks>
/// <para>
/// A thinking model writes its reasoning between a pair of tags - <c>&lt;think&gt;</c> and
/// <c>&lt;/think&gt;</c> for most of them - and then writes the answer. Feed every chunk the model
/// produces to <see cref="AddContent"/>, which hands back the reasoning text and the answer text that can
/// be shown right away and keeps anything still ambiguous. A chunk ending in <c>"&lt;thi"</c> could be the
/// start of a tag or could be literal text, so it is buffered until the next chunk settles the question.
/// </para>
/// <para>
/// Whitespace is handled the way Ollama does it: whitespace before the opening tag is eaten, whitespace
/// between the opening tag and the first reasoning character is eaten, and whitespace between the closing
/// tag and the first content character is eaten. Whitespace in a stream with no thinking tags at all is
/// left exactly as the model wrote it.
/// </para>
/// <para>
/// One parser instance handles one response. It is not thread-safe.
/// </para>
/// </remarks>
public sealed class ThinkingParser
{
    /// <summary>
    /// How much whitespace a parser still waiting for the opening tag will swallow before deciding the
    /// model is writing whitespace rather than leading up to a tag.
    /// </summary>
    private const int MaximumWhitespaceLength = 4 * 1024;

    private readonly StringBuilder _accumulator = new StringBuilder();

    /// <summary>
    /// Initializes a new instance of the <see cref="ThinkingParser"/> class.
    /// </summary>
    /// <param name="openingTag">The tag the model writes before its reasoning. Defaults to <c>&lt;think&gt;</c>.</param>
    /// <param name="closingTag">The tag the model writes after its reasoning. Defaults to <c>&lt;/think&gt;</c>.</param>
    /// <param name="startsInsideThinking">
    /// <see langword="true"/> when the rendered prompt already ended with <paramref name="openingTag"/>, so
    /// the model's first token is reasoning rather than an opening tag. Use
    /// <see cref="ThinkingTags.PromptEndsWithOpeningTag"/> to decide. Ollama gets to the same place by
    /// feeding the opening tag to a fresh parser, and so does this.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="openingTag"/> or <paramref name="closingTag"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="openingTag"/> or <paramref name="closingTag"/> is empty or nothing but whitespace.
    /// A tag like that matches everywhere and nowhere, so the parser would never split the stream.
    /// </exception>
    public ThinkingParser(string openingTag = "<think>", string closingTag = "</think>",
        bool startsInsideThinking = false)
    {
        if (openingTag == null) { throw new ArgumentNullException(nameof(openingTag)); }
        if (closingTag == null) { throw new ArgumentNullException(nameof(closingTag)); }

        if (openingTag.Trim().Length == 0)
        {
            throw new ArgumentException("The opening tag cannot be empty or whitespace.", nameof(openingTag));
        }

        if (closingTag.Trim().Length == 0)
        {
            throw new ArgumentException("The closing tag cannot be empty or whitespace.", nameof(closingTag));
        }

        OpeningTag = openingTag;
        ClosingTag = closingTag;

        if (startsInsideThinking)
        {
            AddContent(openingTag);
        }
    }

    /// <summary>The tag the model writes before its reasoning.</summary>
    public string OpeningTag { get; }

    /// <summary>The tag the model writes after its reasoning.</summary>
    public string ClosingTag { get; }

    /// <summary>Where the parser has got to in the stream.</summary>
    public ThinkingParserState State { get; private set; } = ThinkingParserState.LookingForOpening;

    /// <summary>
    /// <see langword="true"/> when text is being held back because it might turn out to be part of a tag.
    /// </summary>
    public bool HasBufferedText => _accumulator.Length > 0;

    /// <summary>
    /// Takes the next chunk of model output and returns the reasoning text and the content text that can be
    /// emitted now. Either may be empty; anything that is still ambiguous is buffered for the next call.
    /// </summary>
    /// <param name="chunk">The next piece of the model's output. <see langword="null"/> is treated as empty.</param>
    /// <returns>
    /// A tuple of the reasoning text and the content text to emit now, in that order.
    /// </returns>
    public (string thinking, string content) AddContent(string chunk)
    {
        if (!string.IsNullOrEmpty(chunk))
        {
            _accumulator.Append(chunk);
        }

        StringBuilder thinkingBuilder = new StringBuilder();
        StringBuilder contentBuilder = new StringBuilder();

        //A single chunk can carry the parser through several states, and a caller should never have to wait
        //for text that is already unambiguous, so keep eating until the parser asks to stop.
        bool keepLooping = true;
        while (keepLooping)
        {
            string thinking;
            string content;
            keepLooping = Eat(out thinking, out content);
            thinkingBuilder.Append(thinking);
            contentBuilder.Append(content);
        }

        return (thinkingBuilder.ToString(), contentBuilder.ToString());
    }

    /// <summary>
    /// Ends the stream and returns whatever is still buffered, which is text that never resolved into a tag.
    /// </summary>
    /// <remarks>
    /// The returned text belongs to the reasoning when <see cref="State"/> is
    /// <see cref="ThinkingParserState.Thinking"/> and to the content otherwise. Ollama's parser has no
    /// end-of-stream call and simply drops this text; returning it is the one addition this port makes.
    /// </remarks>
    /// <returns>The buffered text, or an empty string when nothing was buffered.</returns>
    public string Flush()
    {
        string remainder = _accumulator.ToString();
        _accumulator.Clear();
        return remainder;
    }

    /// <summary>
    /// Runs one step of the state machine over the buffer.
    /// </summary>
    /// <param name="thinking">Receives the reasoning text produced by this step.</param>
    /// <param name="content">Receives the content text produced by this step.</param>
    /// <returns><see langword="true"/> when another step should run straight away.</returns>
    private bool Eat(out string thinking, out string content)
    {
        thinking = string.Empty;
        content = string.Empty;

        switch (State)
        {
            case ThinkingParserState.LookingForOpening:
            {
                string accumulated = _accumulator.ToString();
                string trimmed = accumulated.TrimStart();
                if (trimmed.StartsWith(OpeningTag, StringComparison.Ordinal))
                {
                    string after = trimmed.Substring(OpeningTag.Length).TrimStart();
                    //`after` may hold more than reasoning, so hand it back to the loop rather than
                    //returning it as reasoning here.
                    _accumulator.Clear();
                    _accumulator.Append(after);
                    State = after.Length == 0
                        ? ThinkingParserState.ThinkingStartedEatingWhitespace
                        : ThinkingParserState.Thinking;
                    return true;
                }

                if (trimmed.Length == 0 && _accumulator.Length > MaximumWhitespaceLength)
                {
                    //Whitespace waiting for an opening tag that never comes would otherwise accumulate
                    //without limit, so past the cap it is handed over as the content it turned out to be.
                    //The parser stays where it is, because a later chunk may still open a thinking block.
                    _accumulator.Clear();
                    content = accumulated;
                    return false;
                }

                if (OpeningTag.StartsWith(trimmed, StringComparison.Ordinal))
                {
                    //A partial opening tag, so keep accumulating.
                    return false;
                }

                if (trimmed.Length == 0)
                {
                    //Whitespace only, so keep accumulating.
                    return false;
                }

                //Real content with no opening tag in front of it, so the model skipped thinking. The
                //untrimmed text is returned because there is no thinking tag whose whitespace to eat.
                State = ThinkingParserState.ThinkingDone;
                _accumulator.Clear();
                content = accumulated;
                return false;
            }

            case ThinkingParserState.ThinkingStartedEatingWhitespace:
            {
                string trimmed = _accumulator.ToString().TrimStart();
                _accumulator.Clear();
                if (trimmed.Length == 0)
                {
                    return false;
                }

                State = ThinkingParserState.Thinking;
                _accumulator.Append(trimmed);
                return true;
            }

            case ThinkingParserState.Thinking:
            {
                string accumulated = _accumulator.ToString();
                int closingIndex = accumulated.IndexOf(ClosingTag, StringComparison.Ordinal);
                if (ClosingTag.Length > 0 && closingIndex >= 0)
                {
                    thinking = accumulated.Substring(0, closingIndex);
                    string remaining = accumulated
                        .Substring(closingIndex + ClosingTag.Length)
                        .TrimStart();
                    _accumulator.Clear();
                    State = remaining.Length == 0
                        ? ThinkingParserState.ThinkingDoneEatingWhitespace
                        : ThinkingParserState.ThinkingDone;
                    content = remaining;
                    return false;
                }

                int overlapLength = Overlap(accumulated, ClosingTag);
                if (overlapLength > 0)
                {
                    thinking = accumulated.Substring(0, accumulated.Length - overlapLength);
                    //Hold on to the candidate closing tag until it is settled one way or the other.
                    string remaining = accumulated.Substring(accumulated.Length - overlapLength);
                    _accumulator.Clear();
                    _accumulator.Append(remaining);
                    return false;
                }

                //Nothing but reasoning, so it can all go out.
                _accumulator.Clear();
                thinking = accumulated;
                return false;
            }

            case ThinkingParserState.ThinkingDoneEatingWhitespace:
            {
                string trimmed = _accumulator.ToString().TrimStart();
                _accumulator.Clear();
                //The first non-whitespace character ends the whitespace eating.
                if (trimmed.Length != 0)
                {
                    State = ThinkingParserState.ThinkingDone;
                }

                content = trimmed;
                return false;
            }

            default:
            {
                content = _accumulator.ToString();
                _accumulator.Clear();
                return false;
            }
        }
    }

    /// <summary>
    /// Returns the length of the longest suffix of <paramref name="text"/> that is also a prefix of
    /// <paramref name="delimiter"/>.
    /// </summary>
    /// <param name="text">The accumulated text.</param>
    /// <param name="delimiter">The tag being looked for.</param>
    /// <returns>The overlap length, or zero when there is none.</returns>
    private static int Overlap(string text, string delimiter)
    {
        int longest = Math.Min(delimiter.Length, text.Length);
        for (int i = longest; i > 0; i--)
        {
            if (text.EndsWith(delimiter.Substring(0, i), StringComparison.Ordinal))
            {
                return i;
            }
        }

        return 0;
    }
}
