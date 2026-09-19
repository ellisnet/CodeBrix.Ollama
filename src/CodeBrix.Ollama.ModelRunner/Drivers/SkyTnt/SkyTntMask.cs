namespace CodeBrix.Ollama.ModelRunner; //was previously: app_onnx.py@f504d5cb58f769ab0f2909c679238f6621034573

/// <summary>
/// WHICH TOKENS THE MODEL IS ALLOWED TO ANSWER WITH at each position of an event's row - the part of the
/// publisher's generation loop that makes a row of tokens always mean something.
/// </summary>
/// <remarks>
/// <para>
/// THE FIRST TOKEN OF AN EVENT may only say which of the six kinds it is, or that the piece is finished.
/// EVERY TOKEN AFTER IT may only be a value of the one parameter the kind puts at that position: a pitch where
/// a pitch belongs, a channel where a channel belongs. So the model cannot answer "pitch" with a tempo, and
/// there is no such thing as a row that does not decode.
/// </para>
/// <para>
/// THE REFUSALS RIDE ON THE SAME MASKS. Forbidding the model to change an instrument, or to move a controller,
/// takes that kind's token out of the first position; confining it to certain channels takes the others out of
/// the block wherever a channel belongs. And once the model has said the piece is finished, every position
/// after that may only be padding.
/// </para>
/// </remarks>
internal static class SkyTntMask
{
    /// <summary>Works out what may be answered at one position of one event's row.</summary>
    /// <param name="tokenizer">The vocabulary.</param>
    /// <param name="position">Which token of the row is being chosen, counting the kind as nought.</param>
    /// <param name="ended">Whether the model has already said the piece is finished.</param>
    /// <param name="type">
    /// The kind of event being written, or <see langword="null"/> before the first token has said which.
    /// </param>
    /// <param name="disableProgramChange">Whether the model is refused an instrument change.</param>
    /// <param name="disableControlChange">Whether the model is refused a controller change.</param>
    /// <param name="allowedChannels">Which of the sixteen channels it may write to.</param>
    /// <returns>One flag per token: whether it may be chosen.</returns>
    internal static bool[] Allowed(
        SkyTntTokenizer tokenizer,
        int position,
        bool ended,
        SkyTntEventType type,
        bool disableProgramChange,
        bool disableControlChange,
        bool[] allowedChannels)
    {
        bool[] allowed = new bool[tokenizer.VocabularySize];

        if (ended)
        {
            allowed[tokenizer.PadId] = true;
            return allowed;
        }

        if (position == 0)
        {
            foreach (SkyTntEventType kind in tokenizer.EventTypes)
            {
                if (disableProgramChange && kind.Name == "patch_change") continue;
                if (disableControlChange && kind.Name == "control_change") continue;
                allowed[kind.Id] = true;
            }

            allowed[tokenizer.EndId] = true;
            return allowed;
        }

        if (type == null || position > type.Parameters.Count)
        {
            allowed[tokenizer.PadId] = true;
            return allowed;
        }

        SkyTntParameter parameter = type.Parameters[position - 1];
        bool isChannel = parameter.Name == SkyTntTokenizer.ChannelParameter;
        for (int value = 0; value < parameter.Size; value++)
        {
            if (isChannel && allowedChannels != null && !allowedChannels[value]) continue;
            allowed[parameter.TokenFor(value)] = true;
        }

        return allowed;
    }
}
