THE TEXT-DRIVER FIXTURES
========================
Everything the text generation driver is tested against WITH NOTHING
INSTALLED: a bundle small enough to check in, and what the published Python
tokenizer made of a corpus.

WHAT IS HERE
    tiny-bundle/genai_config.json   a generation configuration shaped exactly
                                    like the one a model builder writes - a
                                    decoder block naming the graph, the tensor
                                    names, the %d patterns of the per-layer
                                    cache, the layer and head counts, the head
                                    size, the context length and the special
                                    token numbers
    tiny-bundle/model.onnx          160 KB: a two-layer decoder using the SAME
                                    contributed operators a builder's export
                                    uses - GroupQueryAttention with its rotary
                                    embedding and its cache,
                                    SkipSimplifiedLayerNormalization with its
                                    fourth output carrying the residual, and
                                    SimplifiedLayerNormalization in the default
                                    domain - and working out seqlens_k and the
                                    total sequence length from the attention
                                    mask inside the graph, exactly as those
                                    exports do
    tiny-bundle/vocab.json          a 320-token byte-level vocabulary: the four
                                    special tokens, the 256 byte symbols, and
                                    the merges trained on the corpus
    tiny-bundle/merges.txt          those merges, WITH the `#version` header a
                                    builder's export carries
    tiny-bundle/tokenizer_config.json, special_tokens_map.json
                                    what a bundle of this family says about its
                                    tokenizer
    tokenizer-cases.json            a corpus and the token numbers, pieces and
                                    decoded text the PUBLISHED Python tokenizer
                                    produced for every string in it
    generate_causal_lm_fixtures.py  makes all of the above

WHY A MODEL OF OUR OWN
    The published model is most of a gigabyte and cannot be checked in, and a
    test that needed it would be a test that only runs on a machine that has
    downloaded it. The tiny bundle has the SAME CONTRACT - the same
    configuration shape, the same input and output names, the same cache, the
    same mask arithmetic - and arithmetic the driver cannot tell from a real
    model's, so the loop, the cache, the stop conditions, the refusals and the
    tokenizer are all exercised offline. It is not a language model: what it
    writes is not language and is not meant to be.

    It stops by itself. The LENGTH of the sequence so far is added to the
    end-of-sequence token's score and to nothing else, and that length is read
    out of the attention mask inside the graph - so the longer a generation runs
    the more the model wants to finish, and a greedy generation ends after a
    dozen or two tokens however many were asked for. That is also the fence for
    the mask and the cache: a driver that built the mask wrongly, or that lost
    the cache, would never stop.

WHY THE CASES COME FROM PYTHON
    A tokenizer that is nearly right produces a model that runs and writes
    nonsense, so what the tests assert is the token NUMBERS, and they have to
    come from somewhere other than the code under test. They were dumped once,
    by the script beside them, from transformers' own GPT2Tokenizer - which is
    the code every bundle of this family carries a copy of; the copies rename
    the class and change the default token names and change nothing else.

    The corpus deliberately carries the cases a byte-level encoder has rules
    for: contractions, digits, runs of spaces and tabs and newlines, multi-byte
    UTF-8, characters OUTSIDE the basic multilingual plane (which is where a
    .NET regular expression of the published pattern parts company with the
    published engine), emoji, and the special tokens both alone and in the
    middle of ordinary text.

WHO OWNS THEM
    THEY ARE OURS, and they carry this repository's licence (MIT). The corpus is
    our own text, the vocabulary is a byte-level encoder trained on that corpus
    by the script, and the weights are numbers the script makes up from a fixed
    seed. No publisher's model, vocabulary, corpus or file is among them.

HOW THEY WERE MADE
    By hand, once, in a virtual environment holding onnx, numpy, regex and
    transformers:

        <venv>/bin/python generate_causal_lm_fixtures.py

    THE TEST SUITE NEVER RUNS IT: the tests read these files as they are checked
    in, which is the whole point - what they compare against was computed
    somewhere else.
