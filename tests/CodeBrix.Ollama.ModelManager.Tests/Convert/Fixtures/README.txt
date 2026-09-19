CONVERSION FIXTURES - WHAT THEY ARE AND HOW TO REBUILD THEM
===========================================================

WHAT IS HERE

A folder per variant, each a tiny synthetic Llama checkpoint, and beside each of
them the GGUF file that the inference engine's OWN converter produced from it.
Those GGUF files are the ORACLE: the tests convert the folder with the managed
converter and compare the result with the oracle byte for byte, over the whole
file.

    tinyllama-123k/         bfloat16, 4 heads / 4 key-value heads, untied
                            embeddings, weights as model.safetensors, with a model
                            card - the shape of a published checkpoint, small.
                            Converted three ways: auto, f16 and f32.
    tinyllamabin-123k/      the SAME weights written as pytorch_model.bin, so that
                            the two container readers can be compared tensor for
                            tensor. It is also why the two GGUFs list their tensors
                            in different orders: a safetensors container is read in
                            name order and a zip pickle in the order the state
                            dictionary was written.
    tinyllamagqa-115k/      grouped-query attention - 4 heads, 2 key-value heads -
                            which takes the other branch of the rotary permutation.
                            It also carries the three tokenizer keys the others do
                            not: an unknown-token identifier, add_bos_token, and a
                            chat template.
    tinyllamatied-103k/     tied embeddings, so the checkpoint has no lm_head and
                            the GGUF has no output.weight.
    tinyllamabias-124k/     attention bias, so the query and key BIASES take the
                            permutation as well, and every one-dimensional tensor
                            stays at full precision.
    tinyllamaftt-123k/      a float32 checkpoint. At --outtype auto it is written
                            as F16, not F32: the heuristic recognises only bfloat16
                            and float16 and falls back to F16.
    tinyllamafst-123k/      a float16 checkpoint.
    tinyllamapad-124k/      a vocabulary shorter than the configured vocabulary
                            size, so the converter fills the gap with [PAD<n>]
                            tokens of type unused.
    tinyllamabare-123k/     the same weights as tinyllama-123k with a
                            tokenizer_config.json that declares NO special
                            token: no added-token table, and every
                            <kind>_token null. It is the shape a publisher
                            leaves on disk when the tokenizer class names its
                            special tokens inside its own Python, which a
                            converter that reads files never sees and must never
                            run - so the engine writes those four tokens as
                            ORDINARY tokens, and the oracle here says so. It is
                            the fixture behind ConvertOptions.AddedSpecialTokens,
                            which supplies the declaration the files are missing
                            and moves exactly those four token types to control.
    tinyllamasp-132k/       a SENTENCEPIECE tokenizer instead of a byte-level
                            BPE: a tokenizer.model trained by the script with
                            byte fallback on, an added_tokens.json and an
                            added-token table in tokenizer_config.json. It is the
                            only fixture that carries token SCORES and no merge
                            table, and it is where every token type is reachable
                            - normal, unknown, control, user-defined, unused and
                            byte. Its vocabulary of 384 pieces sits inside a
                            configured size of 392, so the padding rule fires as
                            well, and one of its scores is negative zero.
    tinyllamasplit-123k/    the SAME weights as tinyllama-123k, split over three
                            safetensors shards with a model.safetensors.index.json
                            beside them.
    tinyllamabinsplit-123k/ the same weights again, split over three
                            pytorch_model-*.bin shards with their own index.
                            The two splits put the SECOND block in the first
                            shard, the model-level tensors in the second and the
                            FIRST block in the last, which is neither tensor-name
                            order nor the order the tensors were built in - so
                            the order the oracle lists them in is evidence that
                            the parts are walked the way the engine walks them.

THE FOLDER NAMES ARE PART OF THE ORACLE. The converter derives general.name,
general.basename and general.size_label from the model DIRECTORY'S name, so
renaming a folder changes the bytes of its GGUF. The size in each name is close
enough to the real parameter count that the naming heuristic reads it as a size
label rather than as a context length.

LICENCE

Everything here is ours and is MIT licensed with the repository. The weights are
pseudo-random numbers from a pinned seed; BOTH tokenizers are trained by
generate_fixtures.py on the CORPUS constant inside it, which is text written for
that file - the byte-level BPE by the script's own trainer and the SentencePiece
model by the sentencepiece library, from the same corpus. No third-party model,
vocabulary or text is used, and no third-party model file is checked in.

HOW TO REBUILD

The tests never run generate_fixtures.py. Rebuilding needs a checkout of the
inference engine and a Python environment; neither is needed to run the suite.

    git clone --depth 1 --branch b10221 https://github.com/ggml-org/llama.cpp \
        ~/Temp/engine-b10221
    git -C ~/Temp/engine-b10221 apply <this folder>/oracle-converter.patch
    export TMPDIR=$HOME/Temp/codebrix-ollama-tmp        # /tmp may be RAM-backed
    cd <this folder>
    ~/venvs/codebrix-ollama/bin/python generate_fixtures.py --engine ~/Temp/engine-b10221

--variant <name> writes one folder and its oracles and leaves the others alone,
which is how a variant is ADDED: every other file here stays the one it was
generated as rather than being written again. It is repeatable, and the default
is every variant.

The virtual environment must hold torch, safetensors, transformers, regex AND
SENTENCEPIECE, which the script now needs for two reasons. The converter imports
it before it checks whether a tokenizer.model exists, so without that module every
Llama checkpoint dies with ModuleNotFoundError, which the converter does not
catch; and the script itself TRAINS the SentencePiece variant's tokenizer with it.
Versions the fixtures were built with: torch 2.14.0+cpu, safetensors 0.8.0,
transformers 4.57.6, numpy 2.5.3, regex 2026.9.10, sentencepiece 0.2.2.

oracle-converter.patch is the recorded change that makes the converter usable as
an oracle. Its own header says what each hunk is for; in short it touches tokenizer
LOADING and the pre-tokenizer recognition table, and nothing that writes a tensor,
a key or a vocabulary. A new BYTE-LEVEL vocabulary needs a new entry in its
recognition table: run the converter once, read the refused hash off the
"chkhsh:" line, and add it in the same shape. A SENTENCEPIECE vocabulary needs
NOTHING added: that road never fingerprints a pre-tokenizer - it writes
tokenizer.ggml.pre as the constant "default" - so the SentencePiece variant was
produced by the patch exactly as the eleven byte-level oracles left it.
