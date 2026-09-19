#!/usr/bin/env python
"""Writes everything the TEXT generation driver is tested against with nothing installed.

WHAT IT MAKES.

  tiny-bundle/    a bundle shaped exactly like the one a model builder writes - a genai_config.json with a
                  decoder block, one graph using the same contributed operators, and a byte-level byte-pair
                  tokenizer - but a couple of hundred kilobytes instead of most of a gigabyte, with weights
                  this script makes up from a fixed seed.  It is not a language model and does not pretend to
                  be one: it is a graph whose arithmetic the DRIVER cannot tell from a real one, so the loop,
                  the cache, the mask, the stop conditions and the tokenizer can all be exercised without a
                  download and without a publisher's file in the repository.

                  IT STOPS BY ITSELF.  Every score has the LENGTH of the sequence so far added to the
                  end-of-sequence token's column and to nothing else, so the longer the generation runs the
                  more the model wants to finish, and a greedy generation ends on its own.  That is also a
                  fence for the mask: the length is read out of the attention mask inside the graph, exactly
                  as the real bundles read it, so a driver that built the mask wrongly would never stop.

  tokenizer-cases.json   a corpus and what the PUBLISHED Python tokenizer makes of every string in it - the
                  token numbers, and the text they decode back to.  The corpus carries ASCII, contractions,
                  digits, runs of whitespace, multi-byte UTF-8, characters outside the basic multilingual
                  plane (which is where a .NET regular expression of the published pattern would part company
                  with the published one), emoji and the special tokens.

HOW TO MAKE THEM AGAIN, by hand, once, in a virtual environment holding onnx, numpy and transformers:

    <venv>/bin/python generate_causal_lm_fixtures.py

THE TEST SUITE NEVER RUNS IT.  It reads the files as they are checked in.  Everything here is ours (MIT,
this repository's licence): the corpus is our own text, the vocabulary is trained on that corpus, and the
weights are seeded random numbers.  No publisher's model, vocabulary or file is among them.

The tokenizer the cases are dumped from is transformers' own GPT2Tokenizer, which is the code every bundle of
this family carries a copy of - the copies rename the class and change the default token names and change
nothing else.
"""
import json
import os

import numpy as np
from onnx import TensorProto, helper, numpy_helper

HERE = os.path.dirname(os.path.abspath(__file__))
BUNDLE = os.path.join(HERE, "tiny-bundle")

VOCAB_TARGET = 320
HIDDEN = 32
HEADS = 4
KV_HEADS = 2
HEAD_SIZE = 8
LAYERS = 2
INTERMEDIATE = 64
CONTEXT = 64
SEED = 20260918

PAD, UNK, BOS, EOS = "<pad>", "<unk>", "<bos>", "<eos>"

# Our own text. It carries contractions, digits, runs of spaces and tabs and newlines and multi-byte UTF-8 on
# purpose, so that the merges the trainer learns cover the cases the pre-tokenizer has rules for.
CORPUS = [
    "the quick brown fox jumps over the lazy dog",
    "she said it's the dog's dinner and we've all had enough",
    "I'll go, he'd stay, they're late, we're early, you'd think so",
    "numbers 0 1 2 3 4 5 6 7 8 9 and 10 11 12 and 2026 and 1234567890",
    "a line\nand another line\n\nand a third one after a blank line",
    "tabs\there\tand\tthere  and  two  spaces  everywhere   ",
    "Nöldeke and Müller and Jørgensen and Ångström and Łódź",
    "你好世界 and こんにちは and 한글",
    "punctuation: ! ? . , ; : ' \" ( ) [ ] { } < > / \\ | - _ = + * & ^ % $ # @ ~ `",
    "MiXeD CaSe AnD camelCase and snake_case and kebab-case and SCREAMING_CASE",
    "the the the the the and and and and of of of of to to to to in in in in",
    "one two three four five six seven eight nine ten eleven twelve",
]

# The strings the cases are dumped for. Everything in the corpus, plus the edges the corpus does not reach.
CASES = CORPUS + [
    "",
    " ",
    "  ",
    "   a",
    "a   ",
    "\n \n\n \t\t  ",
    "  　nbsp and em space and ideographic space",
    "don't",
    " don't",
    "'s alone and 't alone and 're alone",
    "\U0001f600\U0001f3bc\U0001f9d1‍\U0001f4bb",
    "\U00020000\U00020001\U0002a700 extension B letters",
    "\U0001d400\U0001d401\U0001d7ce mathematical letters and a mathematical digit",
    "\U000104a0\U000104a1 osmanya digits",
    "mixed \U00020000 and \U0001f600 and é together",
    "<bos>hello<eos>",
    "<pad><unk><bos><eos>",
    "a<bos>b",
    "<bos>",
    "text with <bos> in the middle of it",
    "\U0001f600" * 8,
    "é and é look the same and are not",
]


def bytes_to_unicode():
    """The GPT-2 byte-to-symbol table: one printable symbol per byte value."""
    printable = (
        list(range(ord("!"), ord("~") + 1))
        + list(range(ord("¡"), ord("¬") + 1))
        + list(range(ord("®"), ord("ÿ") + 1))
    )
    symbols = printable[:]
    extra = 0
    for value in range(256):
        if value not in printable:
            printable.append(value)
            symbols.append(256 + extra)
            extra += 1
    return dict(zip(printable, [chr(x) for x in symbols]))


def train_byte_level_bpe():
    """Trains a byte-level BPE over CORPUS and returns (vocab, merges).

    Ids are laid out the way a bundle of this family lays them out: the four specials first, then the 256 byte
    symbols in byte order, then one id per merge in the order the merges were learned. Ties between equally
    frequent pairs are broken by the pair itself, so the result is the same on every machine.
    """
    byte_symbol = bytes_to_unicode()
    words = {}
    for line in CORPUS:
        # The published pre-tokenizer's own rule, written out here so the trainer sees the same pieces the
        # tokenizer will.
        import regex as re

        for piece in re.findall(
                r"""'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+""", line):
            symbols = "".join(byte_symbol[b] for b in piece.encode("utf-8"))
            words[symbols] = words.get(symbols, 0) + 1

    merges = []
    word_pieces = {word: list(word) for word in words}
    while len(merges) + 256 + 4 < VOCAB_TARGET:
        counts = {}
        for word, pieces in word_pieces.items():
            weight = words[word]
            for left, right in zip(pieces, pieces[1:]):
                counts[(left, right)] = counts.get((left, right), 0) + weight
        if not counts:
            break
        best = max(sorted(counts), key=lambda pair: counts[pair])
        merges.append(best)
        joined = best[0] + best[1]
        for word, pieces in word_pieces.items():
            merged = []
            at = 0
            while at < len(pieces):
                if at + 1 < len(pieces) and (pieces[at], pieces[at + 1]) == best:
                    merged.append(joined)
                    at += 2
                else:
                    merged.append(pieces[at])
                    at += 1
            word_pieces[word] = merged

    vocab = {}
    for token in (PAD, UNK, BOS, EOS):
        vocab[token] = len(vocab)
    for value in range(256):
        vocab[byte_symbol[value]] = len(vocab)
    for left, right in merges:
        joined = left + right
        if joined not in vocab:
            vocab[joined] = len(vocab)
    return vocab, merges


def write_tokenizer(directory, vocab, merges):
    """Writes vocab.json, merges.txt, tokenizer_config.json and special_tokens_map.json.

    merges.txt carries the `#version: 0.2` header, because that is what a bundle written by a model builder
    carries: the builder saves the tokenizer through the publisher's own save path, and that path writes the
    header. It is also what makes the two readings of the file - the published one, which drops the first line
    whatever it is, and this repository's, which drops it only when it begins with `#` - select the same table.
    """
    os.makedirs(directory, exist_ok=True)
    with open(os.path.join(directory, "vocab.json"), "w", encoding="utf-8") as handle:
        json.dump(vocab, handle, ensure_ascii=False)
    with open(os.path.join(directory, "merges.txt"), "w", encoding="utf-8") as handle:
        handle.write("#version: 0.2\n")
        for left, right in merges:
            handle.write(left + " " + right + "\n")

    def entry(content):
        return {
            "content": content,
            "lstrip": False,
            "normalized": True,
            "rstrip": False,
            "single_word": False,
            "special": True,
        }

    with open(os.path.join(directory, "tokenizer_config.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "add_bos_token": False,
            "add_prefix_space": False,
            "added_tokens_decoder": {
                str(vocab[PAD]): entry(PAD),
                str(vocab[UNK]): entry(UNK),
                str(vocab[BOS]): entry(BOS),
                str(vocab[EOS]): entry(EOS),
            },
            "bos_token": BOS,
            "clean_up_tokenization_spaces": False,
            "eos_token": EOS,
            "errors": "replace",
            "model_max_length": CONTEXT,
            "pad_token": PAD,
            "tokenizer_class": "GPT2Tokenizer",
            "unk_token": UNK,
        }, handle, indent=2)

    with open(os.path.join(directory, "special_tokens_map.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "bos_token": BOS,
            "eos_token": EOS,
            "pad_token": PAD,
            "unk_token": UNK,
        }, handle, indent=2)


def constant(name, array):
    return numpy_helper.from_array(np.ascontiguousarray(array), name)


def build_graph(rng, vocab_size, eos_id):
    """One decoder graph with the same operators, the same cache contract and the same mask arithmetic as a
    builder's export, at a hundredth of the size."""
    qkv_width = (HEADS * HEAD_SIZE) + (2 * KV_HEADS * HEAD_SIZE)

    def weight(name, rows, columns, scale):
        return constant(name, (rng.standard_normal((rows, columns)) * scale).astype(np.float32))

    positions = np.arange(CONTEXT, dtype=np.float32)[:, None]
    inverse = (1.0 / (10000.0 ** (np.arange(HEAD_SIZE // 2, dtype=np.float32) / (HEAD_SIZE // 2))))[None, :]
    angles = positions * inverse

    end_of_sequence = np.zeros((vocab_size,), dtype=np.float32)
    end_of_sequence[eos_id] = 0.15

    initializers = [
        weight("token_embd", vocab_size, HIDDEN, 0.30),
        weight("lm_head", HIDDEN, vocab_size, 0.20),
        constant("norm_in", np.ones((HIDDEN,), dtype=np.float32)),
        constant("cos_cache", np.cos(angles).astype(np.float32)),
        constant("sin_cache", np.sin(angles).astype(np.float32)),
        constant("mask_axes", np.array([1], dtype=np.int64)),
        constant("one_i64", np.array(1, dtype=np.int64)),
        constant("width_index", np.array(1, dtype=np.int64)),
        constant("eos_bias", end_of_sequence),
    ]

    nodes = [
        helper.make_node("Gather", ["token_embd", "input_ids"], ["embedding"], axis=0),

        # seqlens_k is the index of the LAST valid key, which is the total length less one, and
        # total_sequence_length is the mask's own width. Both are worked out inside the graph from the mask,
        # exactly as a builder's export works them out.
        helper.make_node("ReduceSum", ["attention_mask", "mask_axes"], ["mask_total"], keepdims=0),
        helper.make_node("Sub", ["mask_total", "one_i64"], ["mask_last"]),
        helper.make_node("Cast", ["mask_last"], ["seqlens_k"], to=TensorProto.INT32),
        helper.make_node("Shape", ["attention_mask"], ["mask_shape"]),
        helper.make_node("Gather", ["mask_shape", "width_index"], ["mask_width"], axis=0),
        helper.make_node("Cast", ["mask_width"], ["total_sequence_length"], to=TensorProto.INT32),
        helper.make_node("Cast", ["mask_width"], ["length_float"], to=TensorProto.FLOAT),
        helper.make_node("Mul", ["eos_bias", "length_float"], ["length_penalty"]),

        helper.make_node(
            "SimplifiedLayerNormalization", ["embedding", "norm_in"], ["normed_0"],
            axis=-1, epsilon=1e-5, stash_type=1),
    ]

    residual = "embedding"
    normed = "normed_0"

    for layer in range(LAYERS):
        tag = str(layer)
        initializers += [
            weight("w_qkv_" + tag, HIDDEN, qkv_width, 0.25),
            weight("w_out_" + tag, HEADS * HEAD_SIZE, HIDDEN, 0.25),
            weight("w_gate_" + tag, HIDDEN, INTERMEDIATE, 0.25),
            weight("w_up_" + tag, HIDDEN, INTERMEDIATE, 0.25),
            weight("w_down_" + tag, INTERMEDIATE, HIDDEN, 0.25),
            constant("norm_a_" + tag, np.ones((HIDDEN,), dtype=np.float32)),
            constant("norm_m_" + tag, np.ones((HIDDEN,), dtype=np.float32)),
        ]

        nodes += [
            helper.make_node("MatMul", [normed, "w_qkv_" + tag], ["qkv_" + tag]),
            helper.make_node(
                "GroupQueryAttention",
                ["qkv_" + tag, "", "",
                 "past_key_values." + tag + ".key", "past_key_values." + tag + ".value",
                 "seqlens_k", "total_sequence_length", "cos_cache", "sin_cache"],
                ["attn_" + tag, "present." + tag + ".key", "present." + tag + ".value"],
                domain="com.microsoft",
                num_heads=HEADS, kv_num_heads=KV_HEADS, do_rotary=1, rotary_interleaved=0,
                local_window_size=-1, softcap=0.0),
            helper.make_node("MatMul", ["attn_" + tag, "w_out_" + tag], ["proj_" + tag]),
            helper.make_node(
                "SkipSimplifiedLayerNormalization",
                ["proj_" + tag, residual, "norm_a_" + tag],
                ["normed_a_" + tag, "", "", "residual_a_" + tag],
                domain="com.microsoft", epsilon=1e-5),
            helper.make_node("MatMul", ["normed_a_" + tag, "w_gate_" + tag], ["gate_" + tag]),
            helper.make_node("MatMul", ["normed_a_" + tag, "w_up_" + tag], ["up_" + tag]),
            helper.make_node("Sigmoid", ["gate_" + tag], ["gate_sig_" + tag]),
            helper.make_node("Mul", ["gate_" + tag, "gate_sig_" + tag], ["silu_" + tag]),
            helper.make_node("Mul", ["silu_" + tag, "up_" + tag], ["act_" + tag]),
            helper.make_node("MatMul", ["act_" + tag, "w_down_" + tag], ["down_" + tag]),
            helper.make_node(
                "SkipSimplifiedLayerNormalization",
                ["down_" + tag, "residual_a_" + tag, "norm_m_" + tag],
                ["normed_m_" + tag, "", "", "residual_m_" + tag],
                domain="com.microsoft", epsilon=1e-5),
        ]

        residual = "residual_m_" + tag
        normed = "normed_m_" + tag

    nodes += [
        helper.make_node("MatMul", [normed, "lm_head"], ["raw_logits"]),
        helper.make_node("Add", ["raw_logits", "length_penalty"], ["logits"]),
    ]

    inputs = [
        helper.make_tensor_value_info(
            "input_ids", TensorProto.INT64, ["batch_size", "sequence_length"]),
        helper.make_tensor_value_info(
            "attention_mask", TensorProto.INT64, ["batch_size", "total_sequence_length"]),
    ]
    outputs = [
        helper.make_tensor_value_info(
            "logits", TensorProto.FLOAT, ["batch_size", "sequence_length", vocab_size]),
    ]
    for layer in range(LAYERS):
        tag = str(layer)
        for what in ("key", "value"):
            inputs.append(helper.make_tensor_value_info(
                "past_key_values." + tag + "." + what, TensorProto.FLOAT,
                ["batch_size", KV_HEADS, "past_sequence_length", HEAD_SIZE]))
            outputs.append(helper.make_tensor_value_info(
                "present." + tag + "." + what, TensorProto.FLOAT,
                ["batch_size", KV_HEADS, "total_sequence_length", HEAD_SIZE]))

    graph = helper.make_graph(nodes, "tiny_causal_lm", inputs, outputs, initializer=initializers)
    model = helper.make_model(
        graph,
        opset_imports=[helper.make_opsetid("", 21), helper.make_opsetid("com.microsoft", 1)],
        producer_name="codebrix-ollama-fixtures")
    model.ir_version = 10
    return model


def write_configuration(directory, vocab, eos_id):
    with open(os.path.join(directory, "genai_config.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "model": {
                "bos_token_id": vocab[BOS],
                "context_length": CONTEXT,
                "decoder": {
                    "filename": "model.onnx",
                    "head_size": HEAD_SIZE,
                    "hidden_size": HIDDEN,
                    "inputs": {
                        "input_ids": "input_ids",
                        "attention_mask": "attention_mask",
                        "past_key_names": "past_key_values.%d.key",
                        "past_value_names": "past_key_values.%d.value",
                    },
                    "outputs": {
                        "logits": "logits",
                        "present_key_names": "present.%d.key",
                        "present_value_names": "present.%d.value",
                    },
                    "num_attention_heads": HEADS,
                    "num_hidden_layers": LAYERS,
                    "num_key_value_heads": KV_HEADS,
                },
                "eos_token_id": eos_id,
                "pad_token_id": vocab[PAD],
                "type": "tinyllama",
                "vocab_size": len(vocab),
            },
            "search": {
                "do_sample": False,
                "max_length": CONTEXT,
                "past_present_share_buffer": False,
            },
        }, handle, indent=4)


def write_cases(directory):
    """What the published Python tokenizer makes of every string in the corpus."""
    from transformers import GPT2Tokenizer

    tokenizer = GPT2Tokenizer.from_pretrained(directory)
    cases = []
    for text in CASES:
        ids = [int(x) for x in tokenizer.encode(text)]
        cases.append({
            "text": text,
            "ids": ids,
            "decoded": tokenizer.decode(ids),
            "decoded_with_specials": tokenizer.decode(ids, skip_special_tokens=False),
            "decoded_without_specials": tokenizer.decode(ids, skip_special_tokens=True),
            "pieces": tokenizer.tokenize(text),
        })

    with open(os.path.join(HERE, "tokenizer-cases.json"), "w", encoding="utf-8") as handle:
        json.dump({
            "tokenizer": "transformers GPT2Tokenizer (slow), the code every bundle of this family copies",
            "cases": cases,
        }, handle, ensure_ascii=False, indent=1)


def main():
    rng = np.random.default_rng(SEED)
    vocab, merges = train_byte_level_bpe()
    os.makedirs(BUNDLE, exist_ok=True)
    write_tokenizer(BUNDLE, vocab, merges)
    write_configuration(BUNDLE, vocab, vocab[EOS])

    model = build_graph(rng, len(vocab), vocab[EOS])
    with open(os.path.join(BUNDLE, "model.onnx"), "wb") as handle:
        handle.write(model.SerializeToString())

    write_cases(BUNDLE)
    print("vocabulary", len(vocab), "merges", len(merges),
          "graph", os.path.getsize(os.path.join(BUNDLE, "model.onnx")), "bytes")


if __name__ == "__main__":
    main()
