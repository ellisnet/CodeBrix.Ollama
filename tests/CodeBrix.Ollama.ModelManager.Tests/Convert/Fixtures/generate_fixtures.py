#!/usr/bin/env python
# Fixture generator for the managed "PyTorch checkpoint -> GGUF" conversion
# (CodeBrix.Ollama.ModelManager, Checkpoints/ + Gguf/ + Convert/).
#
# It writes, next to itself:
#   <variant>/                 a tiny synthetic Llama checkpoint - config.json, the tokenizer file set, and the
#                              weights as model.safetensors or pytorch_model.bin
#   <variant>.auto.gguf        THE ORACLE: what the inference engine's own converter produces from that folder
#   tinyllama-123k.f16.gguf    the same folder at --outtype f16
#   tinyllama-123k.f32.gguf    the same folder at --outtype f32
#
# The .gguf files are the oracle: they are checked in, copied to the test output, and compared with what the
# managed port produces - key by key, tensor by tensor, and byte for byte over the whole file. THE TESTS NEVER
# RUN THIS SCRIPT, and the test suite never reaches outside the repository.
#
# Everything here is ours and is MIT licensed with the repository: the weights are pseudo-random numbers from a
# pinned seed, and the tokenizer is trained on the CORPUS constant below, which is text written for this file.
# No third-party model, vocabulary or text is used.
#
# HOW TO REGENERATE (see README.txt beside this file for the full recipe):
#
#     git clone --depth 1 --branch b10221 <the inference engine> ~/Temp/engine-b10221
#     git -C ~/Temp/engine-b10221 apply <this folder>/oracle-converter.patch
#     export TMPDIR=$HOME/Temp/codebrix-ollama-tmp
#     ~/venvs/codebrix-ollama/bin/python generate_fixtures.py --engine ~/Temp/engine-b10221
#
# The virtual environment must hold torch, safetensors, transformers, regex AND sentencepiece; the converter
# imports sentencepiece before it checks whether tokenizer.model exists, so without it every Llama checkpoint
# dies with ModuleNotFoundError. Versions it was run with (2026-09-18): torch 2.14.0+cpu, safetensors 0.8.0,
# transformers 4.57.6, numpy 2.5.3, regex 2026.9.10, sentencepiece 0.2.2.
#
# WHY THE FOLDER NAMES CARRY A SIZE. The converter derives general.name, general.basename and
# general.size_label from the MODEL DIRECTORY'S NAME (gguf-py/gguf/metadata.py), so the names below are part of
# the oracle and must not be changed without regenerating everything.

import argparse
import io
import json
import os
import shutil
import subprocess
import sys
from collections import Counter

import numpy as np
import regex
import sentencepiece as spm
import torch
from safetensors.torch import save_file

HERE = os.path.dirname(os.path.abspath(__file__))

# The GPT-2 pre-tokenizer pattern, and the corpus the byte-level BPE merges are trained on. The corpus is our
# own text and deliberately carries runs of spaces, tabs and newlines, and multi-byte UTF-8, so that the merges
# cover them (the tokenizer-fidelity risk in the plan).
GPT2_PATTERN = r"""'s|'t|'re|'ve|'m|'ll|'d| ?\p{L}+| ?\p{N}+| ?[^\s\p{L}\p{N}]+|\s+(?!\S)|\s+"""

CORPUS = [
    "the tiny model reads the tiny tokens and the tiny tokens read the tiny model",
    "a tiny llama, a tiny tune, a tiny token: the tiny things are the things we test",
    "tokens  with  doubled  spaces   and    longer    runs    of    spaces",
    "tabs\tand\ttabs\t\tand\t\t\ttabs mixed with\nnewlines\n\nand\n\n\nmore newlines",
    "naive coordinates: cafe, café, naïve, Brücke, über, grüße, æther",
    "greek letters alpha beta gamma: αβγ and δεζ and ηθι",
    "a rocket \U0001f680 and a check ✅ and two llamas \U0001f999\U0001f999 in one line",
    "numbers 1 12 123 1234 12345 and 3 33 333 3333 and 7 77 777",
    "the quick brown fox jumps over the lazy dog, then the dog jumps over the fox",
    "token token token tokens tokenised tokenising tokeniser tokenisers",
    "model model models modelled modelling modeller modellers modelling models",
    "read reads reader readers reading readable readability rereading",
    "tiny tiny tiny tinier tiniest tinily tininess tiny things",
    "the the the and and and of of of to to to in in in is is is",
    "a b c d e f g h i j k l m n o p q r s t u v w x y z",
    "A B C D E F G H I J K L M N O P Q R S T U V W X Y Z",
    "punctuation: commas, periods. colons: semicolons; dashes - and (parentheses) and [brackets]",
    "quotes 'single' and \"double\" and `back` and <angle> and {brace}",
]

SPECIAL_TOKENS = ["<pad>", "<unk>", "<bos>", "<eos>"]
MERGE_COUNT = 60

# The SentencePiece variant's own settings. The trainer is given the SAME corpus as the byte-level BPE, and
# asked for the fewest pieces that corpus can be trained on: it carries 357 distinct characters, and a
# vocabulary smaller than that cannot hold them all beside the byte pieces - the trainer refuses rather than
# dropping any. Byte fallback is on, which is what puts the 256 <0xNN> pieces of type BYTE in the vocabulary;
# two control symbols and two user-defined symbols are asked for as well, so that every kind of piece the
# export has to classify is present in the trained model itself.
SENTENCEPIECE_VOCAB_SIZE = 384
SENTENCEPIECE_CONTROL_SYMBOLS = ["<ctrl0>", "<ctrl1>"]
SENTENCEPIECE_USER_SYMBOLS = ["<sep>", "<cls>"]

# The shared architecture. Every variant starts from this and changes one thing.
BASE_CONFIG = {
    "architectures": ["LlamaForCausalLM"],
    "attention_bias": False,
    "attention_dropout": 0.0,
    "bos_token_id": 2,
    "eos_token_id": 3,
    "hidden_act": "silu",
    "hidden_size": 64,
    "initializer_range": 0.02,
    "intermediate_size": 128,
    "max_position_embeddings": 256,
    "model_type": "llama",
    "num_attention_heads": 4,
    "num_hidden_layers": 2,
    "num_key_value_heads": 4,
    "pad_token_id": 0,
    "pretraining_tp": 1,
    "rms_norm_eps": 1e-05,
    "rope_scaling": None,
    "rope_theta": 10000.0,
    "tie_word_embeddings": False,
    "torch_dtype": "bfloat16",
    "tokenizer_class": "GPT2Tokenizer",
    "transformers_version": "4.36.2",
    "use_cache": True,
    "vocab_size": 320,
}

MODEL_CARD = """---
# A model card for a model that does not exist. Everything here is ours, MIT licensed.
license: mit
tags:
  - test
  - fixture
pipeline_tag: text-generation
language: [en, de]
library_name: transformers
---

# Tiny Llama fixture

A two-layer synthetic Llama checkpoint written by `generate_fixtures.py`. It exists so that the managed
converter can be compared with the inference engine's own converter byte for byte. It is not a model anyone
should run.
"""


def bytes_to_unicode():
    """The GPT-2 byte-to-symbol table: 256 printable symbols, one per byte value."""
    bs = (list(range(ord("!"), ord("~") + 1))
          + list(range(ord("¡"), ord("¬") + 1))
          + list(range(ord("®"), ord("ÿ") + 1)))
    cs = bs[:]
    n = 0
    for b in range(256):
        if b not in bs:
            bs.append(b)
            cs.append(256 + n)
            n += 1
    return {b: chr(c) for b, c in zip(bs, cs)}


def train_byte_level_bpe():
    """Trains a byte-level BPE over CORPUS and returns (vocab, merges).

    Ids are laid out the way MuPT's are: the specials first, then the 256 byte symbols in byte order, then one
    id per merge in the order the merges were learned. Ties between equally frequent pairs are broken by the
    pair itself, so the result is the same on every machine.
    """
    byte_symbol = bytes_to_unicode()
    splitter = regex.compile(GPT2_PATTERN)

    counts = Counter()
    for line in CORPUS:
        for piece in splitter.findall(line):
            symbols = "".join(byte_symbol[b] for b in piece.encode("utf-8"))
            counts[symbols] += 1

    words = {word: list(word) for word in counts}
    merges = []
    for _ in range(MERGE_COUNT):
        pair_counts = Counter()
        for word, symbols in words.items():
            weight = counts[word]
            for i in range(len(symbols) - 1):
                pair_counts[(symbols[i], symbols[i + 1])] += weight
        if not pair_counts:
            break
        best = max(sorted(pair_counts), key=lambda pair: pair_counts[pair])
        merges.append(best)
        joined = best[0] + best[1]
        for word, symbols in words.items():
            if len(symbols) < 2:
                continue
            merged = []
            i = 0
            while i < len(symbols):
                if i < len(symbols) - 1 and symbols[i] == best[0] and symbols[i + 1] == best[1]:
                    merged.append(joined)
                    i += 2
                else:
                    merged.append(symbols[i])
                    i += 1
            words[word] = merged

    vocab = {}
    for token in SPECIAL_TOKENS:
        vocab[token] = len(vocab)
    for b in range(256):
        vocab[byte_symbol[b]] = len(vocab)
    for left, right in merges:
        joined = left + right
        if joined not in vocab:
            vocab[joined] = len(vocab)
    return vocab, merges


def write_tokenizer(directory, vocab, merges, extra_config=None, declare_specials=True):
    """Writes the MuPT file set: vocab.json, merges.txt and tokenizer_config.json, and no tokenizer.json.

    merges.txt is written WITHOUT a `#version` header, exactly as MuPT ships it, because that is the case the
    engine's rule - drop the first line only when it starts with `#` - is easiest to get wrong.

    declare_specials=False writes a configuration that names NO special token at all: no added-token table and
    every `<kind>_token` explicitly null, which is what a publisher whose tokenizer class declares them in its
    own Python leaves on disk. The four tokens are still IN the vocabulary and are still named by config.json;
    nothing in any file says they are special.
    """
    with open(os.path.join(directory, "vocab.json"), "w", encoding="utf-8") as f:
        json.dump(vocab, f, ensure_ascii=False)
    with open(os.path.join(directory, "merges.txt"), "w", encoding="utf-8") as f:
        for left, right in merges:
            f.write(left + " " + right + "\n")

    if declare_specials:
        config = {
            "added_tokens_decoder": {
                str(vocab[token]): {
                    "content": token,
                    "lstrip": False,
                    "normalized": False,
                    "rstrip": False,
                    "single_word": False,
                    "special": True,
                }
                for token in SPECIAL_TOKENS
            },
            "bos_token": "<bos>",
            "clean_up_tokenization_spaces": False,
            "eos_token": "<eos>",
            "model_max_length": 256,
            "pad_token": "<pad>",
            "tokenizer_class": "GPT2Tokenizer",
            "unk_token": "<unk>",
        }
    else:
        config = {
            "bos_token": None,
            "clean_up_tokenization_spaces": False,
            "eos_token": None,
            "model_max_length": 256,
            "pad_token": None,
            "tokenizer_class": "GPT2Tokenizer",
            "unk_token": None,
        }
    if extra_config:
        config.update(extra_config)
    with open(os.path.join(directory, "tokenizer_config.json"), "w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False, indent=1)


def train_sentencepiece():
    """Trains a byte-level-fallback BPE SentencePiece model over CORPUS and returns the ModelProto bytes.

    The pieces are laid out the way a published SentencePiece checkpoint's are: the four specials at the
    identifiers config.json names, then the control and user-defined symbols, then the 256 <0xNN> byte pieces,
    then the learned merges in rank order. Nothing is written to disk by the trainer - model_writer keeps the
    protocol-buffer bytes in memory - and num_threads=1 keeps the result the same on every machine.
    """
    model = io.BytesIO()
    spm.SentencePieceTrainer.train(
        sentence_iterator=iter(CORPUS),
        model_writer=model,
        model_type="bpe",
        vocab_size=SENTENCEPIECE_VOCAB_SIZE,
        byte_fallback=True,
        character_coverage=1.0,
        pad_id=0, unk_id=1, bos_id=2, eos_id=3,
        pad_piece=SPECIAL_TOKENS[0], unk_piece=SPECIAL_TOKENS[1],
        bos_piece=SPECIAL_TOKENS[2], eos_piece=SPECIAL_TOKENS[3],
        control_symbols=SENTENCEPIECE_CONTROL_SYMBOLS,
        user_defined_symbols=SENTENCEPIECE_USER_SYMBOLS,
        add_dummy_prefix=True,
        remove_extra_whitespaces=False,
        normalization_rule_name="identity",
        hard_vocab_limit=True,
        num_threads=1,
        minloglevel=1,
    )
    return model.getvalue()


def write_sentencepiece_tokenizer(directory):
    """Writes tokenizer.model, added_tokens.json and tokenizer_config.json for the SentencePiece variant.

    The two added-token files are what make every token TYPE reachable, in both halves of the vocabulary.
    added_tokens.json names two tokens beyond the trained model's last piece, so they replace [PAD384] and
    [PAD385] as user-defined. tokenizer_config.json's added-token table then reaches four more: two that
    REPLACE a trained piece - one whose content carries the SentencePiece space marker, which becomes a real
    space, and one whose content only LOOKS special, which becomes a control token although the entry says it
    is not special - and two in the padded tail, one user-defined and one control.
    """
    with open(os.path.join(directory, "tokenizer.model"), "wb") as f:
        f.write(train_sentencepiece())

    with open(os.path.join(directory, "added_tokens.json"), "w", encoding="utf-8") as f:
        json.dump({"<extra0>": 384, "<extra1>": 385}, f, ensure_ascii=False, indent=1)

    def entry(content, special):
        return {"content": content, "lstrip": False, "normalized": False, "rstrip": False,
                "single_word": False, "special": special}

    config = {
        "added_tokens_decoder": {
            "0": entry(SPECIAL_TOKENS[0], True),
            "2": entry(SPECIAL_TOKENS[2], True),
            "3": entry(SPECIAL_TOKENS[3], True),
            "6": entry(SENTENCEPIECE_USER_SYMBOLS[0], False),
            "322": entry("▁gap", False),
            "323": entry("<|looksspecial|>", False),
            "386": entry("<extra2>", False),
            "387": entry("<ctrlextra>", True),
        },
        "add_bos_token": True,
        "add_eos_token": False,
        "add_prefix_space": True,
        "bos_token": SPECIAL_TOKENS[2],
        "chat_template": "{% for message in messages %}{{ message['role'] }}: {{ message['content'] }}\n{% endfor %}",
        "clean_up_tokenization_spaces": False,
        "eos_token": SPECIAL_TOKENS[3],
        "model_max_length": 256,
        "pad_token": SPECIAL_TOKENS[0],
        "tokenizer_class": "LlamaTokenizer",
        "unk_token": SPECIAL_TOKENS[1],
    }
    with open(os.path.join(directory, "tokenizer_config.json"), "w", encoding="utf-8") as f:
        json.dump(config, f, ensure_ascii=False, indent=1)


def shard_groups(state):
    """Splits a state dictionary into three shards DELIBERATELY out of both useful orders.

    The engine's converter walks the shards in sorted FILE-NAME order and, inside each one, a safetensors
    container in tensor-name order and a zip pickle in the order its state dictionary was written. Putting the
    second block first, the model-level tensors next and the first block last makes that order differ both from
    sorting the whole set by name and from the order the tensors were built in - so a reader that took either
    shortcut would produce a different file and the oracle would say so.
    """
    first = [name for name in state if name.startswith("model.layers.0.")]
    second = [name for name in state if name.startswith("model.layers.1.")]
    rest = [name for name in state if name not in first and name not in second]
    return [second, rest, first]


def write_shards(directory, state, container, groups):
    """Writes one shard per group plus the *.index.json that says which file each tensor is in."""
    suffix = ".safetensors" if container == "safetensors" else ".bin"
    prefix = "model" if container == "safetensors" else "pytorch_model"
    index_name = ("model.safetensors" if container == "safetensors" else "pytorch_model.bin") + ".index.json"
    total = len(groups)

    weight_map = {}
    for number, group in enumerate(groups, start=1):
        file_name = "%s-%05d-of-%05d%s" % (prefix, number, total, suffix)
        part = {name: state[name] for name in group}
        path = os.path.join(directory, file_name)
        if container == "safetensors":
            save_file(part, path)
        else:
            torch.save(part, path)
        os.chmod(path, 0o644)
        for name in group:
            weight_map[name] = file_name

    # The weight map is written in the order the tensors were BUILT, not grouped by shard, because that is what
    # a publisher's own sharding writes and because a reader must not depend on the order of this object.
    ordered = {name: weight_map[name] for name in state}
    total_size = sum(int(np.prod(list(tensor.shape))) * tensor.element_size() for tensor in state.values())
    with open(os.path.join(directory, index_name), "w", encoding="utf-8") as f:
        json.dump({"metadata": {"total_size": total_size}, "weight_map": ordered}, f,
                  ensure_ascii=False, indent=1)
        f.write("\n")


def build_state_dict(config, seed):
    """Builds the weights in the order transformers itself lays a LlamaForCausalLM state dict out."""
    rng = np.random.default_rng(seed)
    hidden = config["hidden_size"]
    inner = config["intermediate_size"]
    heads = config["num_attention_heads"]
    kv_heads = config["num_key_value_heads"]
    head_dim = hidden // heads
    layers = config["num_hidden_layers"]
    rows = config["vocab_size"]
    bias = config["attention_bias"]

    def block(shape):
        return rng.uniform(-1.25, 1.25, size=shape).astype(np.float32)

    state = {}
    embeddings = block((rows, hidden))
    # Pin four values that only show up when the arithmetic is right: one that overflows float16 to infinity,
    # one that underflows it to a subnormal, and the two signed zeros.
    embeddings[0, 0] = 1.0e30
    embeddings[0, 1] = 3.0e-8
    embeddings[0, 2] = 0.0
    embeddings[0, 3] = -0.0
    state["model.embed_tokens.weight"] = embeddings
    for layer in range(layers):
        prefix = "model.layers." + str(layer) + "."
        state[prefix + "self_attn.q_proj.weight"] = block((heads * head_dim, hidden))
        if bias:
            state[prefix + "self_attn.q_proj.bias"] = block((heads * head_dim,))
        state[prefix + "self_attn.k_proj.weight"] = block((kv_heads * head_dim, hidden))
        if bias:
            state[prefix + "self_attn.k_proj.bias"] = block((kv_heads * head_dim,))
        state[prefix + "self_attn.v_proj.weight"] = block((kv_heads * head_dim, hidden))
        if bias:
            state[prefix + "self_attn.v_proj.bias"] = block((kv_heads * head_dim,))
        state[prefix + "self_attn.o_proj.weight"] = block((hidden, heads * head_dim))
        if bias:
            state[prefix + "self_attn.o_proj.bias"] = block((hidden,))
        state[prefix + "mlp.gate_proj.weight"] = block((inner, hidden))
        state[prefix + "mlp.up_proj.weight"] = block((inner, hidden))
        state[prefix + "mlp.down_proj.weight"] = block((hidden, inner))
        state[prefix + "input_layernorm.weight"] = block((hidden,))
        state[prefix + "post_attention_layernorm.weight"] = block((hidden,))
    state["model.norm.weight"] = block((hidden,))
    if not config["tie_word_embeddings"]:
        state["lm_head.weight"] = block((rows, hidden))
    return state


def to_torch(state, dtype):
    return {name: torch.from_numpy(values).to(dtype) for name, values in state.items()}


def write_variant(name, config, seed, container, extra_config=None, model_card=False, config_extra=None,
                  declare_specials=True, tokenizer="gpt2", sharded=False):
    directory = os.path.join(HERE, name)
    if os.path.isdir(directory):
        shutil.rmtree(directory)
    os.makedirs(directory)

    written_config = dict(config)
    if config_extra:
        written_config.update(config_extra)
    with open(os.path.join(directory, "config.json"), "w", encoding="utf-8") as f:
        json.dump(written_config, f, ensure_ascii=False, indent=1)
        f.write("\n")

    if tokenizer == "sentencepiece":
        write_sentencepiece_tokenizer(directory)
    else:
        vocab, merges = train_byte_level_bpe()
        write_tokenizer(directory, vocab, merges, extra_config, declare_specials)

    if model_card:
        with open(os.path.join(directory, "README.md"), "w", encoding="utf-8") as f:
            f.write(MODEL_CARD)

    dtype = {"bf16": torch.bfloat16, "f16": torch.float16, "f32": torch.float32}[container[1]]
    state = to_torch(build_state_dict(config, seed), dtype)
    if sharded:
        write_shards(directory, state, container[0], shard_groups(state))
    elif container[0] == "safetensors":
        weights = os.path.join(directory, "model.safetensors")
        save_file(state, weights)
        # save_file creates the file with restrictive permissions; a checked-in file is readable.
        os.chmod(weights, 0o644)
    else:
        weights = os.path.join(directory, "pytorch_model.bin")
        torch.save(state, weights)
        os.chmod(weights, 0o644)

    total = sum(int(np.prod(list(tensor.shape))) for tensor in state.values())
    print("  " + name + ": " + str(len(state)) + " tensors, " + str(total) + " parameters", flush=True)
    return directory


def convert(engine, directory, out_type):
    """Runs the PATCHED engine converter on one variant folder and writes the oracle beside it."""
    name = os.path.basename(directory)
    out_file = os.path.join(HERE, name + "." + out_type + ".gguf")
    command = [
        sys.executable,
        os.path.join(engine, "convert_hf_to_gguf.py"),
        directory,
        "--outfile", out_file,
        "--outtype", out_type,
    ]
    environment = dict(os.environ)
    environment["HF_HUB_OFFLINE"] = "1"
    environment["PYTHONPATH"] = os.path.join(engine, "gguf-py")
    completed = subprocess.run(command, cwd=engine, env=environment, capture_output=True, text=True)
    if completed.returncode != 0:
        sys.stderr.write(completed.stdout)
        sys.stderr.write(completed.stderr)
        raise SystemExit("the converter refused " + name + " at --outtype " + out_type)
    print("  " + os.path.basename(out_file) + ": " + str(os.path.getsize(out_file)) + " bytes", flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--engine", required=True, help="a checkout of the inference engine at tag b10221, patched")
    parser.add_argument("--checkpoints-only", action="store_true", help="build the folders and stop")
    parser.add_argument("--variant", action="append", default=None,
                        help="write only this variant (repeatable); the default writes every one of them")
    arguments = parser.parse_args()

    wanted = set(arguments.variant or [])

    def include(name):
        return not wanted or name in wanted

    print("checkpoints", flush=True)
    variants = []

    # (a) the MuPT shape, small: bf16, 4 heads / 4 kv heads, untied, safetensors, with a model card.
    if include("tinyllama-123k"):
        variants.append((write_variant(
            "tinyllama-123k", BASE_CONFIG, seed=20260918, container=("safetensors", "bf16"), model_card=True),
            ["auto", "f16", "f32"]))

    # (b) the same weights in the PyTorch zip pickle, so the two readers can be compared tensor for tensor.
    if include("tinyllamabin-123k"):
        variants.append((write_variant(
            "tinyllamabin-123k", BASE_CONFIG, seed=20260918, container=("bin", "bf16")), ["auto"]))

    # (c) grouped-query attention - the n_head_kv branch of the rotary permutation - and the three tokenizer
    # keys MuPT does not carry: an unknown-token id, add_bos_token, and a chat template.
    if include("tinyllamagqa-115k"):
        gqa = dict(BASE_CONFIG, num_key_value_heads=2)
        variants.append((write_variant(
            "tinyllamagqa-115k", gqa, seed=20260919, container=("safetensors", "bf16"),
            extra_config={
                "add_bos_token": False,
                "add_prefix_space": False,
                "chat_template": "{% for message in messages %}{{ message['role'] }}: {{ message['content'] }}\n{% endfor %}",
            },
            config_extra={"unk_token_id": 1}), ["auto"]))

    # (d) tied embeddings: the checkpoint has no lm_head, so the GGUF has no output.weight.
    if include("tinyllamatied-103k"):
        tied = dict(BASE_CONFIG, tie_word_embeddings=True)
        variants.append((write_variant(
            "tinyllamatied-103k", tied, seed=20260920, container=("safetensors", "bf16")), ["auto"]))

    # (e) attention bias: the bias tensors take the same permutation as the weights, and every one-dimensional
    # tensor stays F32 whatever the output type says.
    if include("tinyllamabias-124k"):
        biased = dict(BASE_CONFIG, attention_bias=True)
        variants.append((write_variant(
            "tinyllamabias-124k", biased, seed=20260921, container=("safetensors", "bf16")), ["auto"]))

    # (f) the two other checkpoint dtypes. A float32 checkpoint at --outtype auto is F16, not F32: the
    # heuristic only recognises bfloat16 and float16 and falls back to F16.
    if include("tinyllamaftt-123k"):
        variants.append((write_variant(
            "tinyllamaftt-123k", BASE_CONFIG, seed=20260922, container=("safetensors", "f32")), ["auto"]))
    if include("tinyllamafst-123k"):
        variants.append((write_variant(
            "tinyllamafst-123k", BASE_CONFIG, seed=20260923, container=("safetensors", "f16")), ["auto"]))

    # (g) a vocabulary shorter than vocab_size, so the converter fills the gap with [PAD{i}] tokens of type
    # UNUSED - the padding rule, which no other variant reaches.
    if include("tinyllamapad-124k"):
        padded = dict(BASE_CONFIG, vocab_size=328)
        variants.append((write_variant(
            "tinyllamapad-124k", padded, seed=20260924, container=("safetensors", "bf16")), ["auto"]))

    # (h) a tokenizer configuration that declares NO special token: no added-token table, and every
    # `<kind>_token` null. It is MuPT's shape, where the tokenizer class names its four special tokens inside
    # the publisher's own Python - which a converter that reads files never sees and never runs. The four
    # tokens are in the vocabulary and config.json still gives them identifiers, so the engine writes the
    # identifiers and writes the tokens themselves as ORDINARY tokens. This is the fixture behind
    # ConvertOptions.AddedSpecialTokens: with nothing supplied our output IS this oracle, and supplying the
    # four contents moves exactly those four token types to CONTROL.
    if include("tinyllamabare-123k"):
        variants.append((write_variant(
            "tinyllamabare-123k", BASE_CONFIG, seed=20260918, container=("safetensors", "bf16"),
            model_card=True, declare_specials=False), ["auto"]))

    # (i) a SentencePiece tokenizer instead of a byte-level BPE: tokenizer.model, added_tokens.json and an
    # added-token table, a vocabulary of 320 pieces inside a configured size of 328 so the padding rule fires
    # as well, and every token type the export can write - normal, unknown, control, user-defined, unused and
    # byte. It is also the only fixture that carries token SCORES, which this road writes and the other does
    # not, and one of those scores is negative zero.
    if include("tinyllamasp-132k"):
        sentencepiece = dict(BASE_CONFIG, vocab_size=392, tokenizer_class="LlamaTokenizer")
        variants.append((write_variant(
            "tinyllamasp-132k", sentencepiece, seed=20260925, container=("safetensors", "bf16"),
            tokenizer="sentencepiece", config_extra={"unk_token_id": 1}), ["auto"]))

    # (j) and (k) the same weights as (a) and (b) split over three shards with an index file beside them. The
    # split is deliberately not in tensor-name order and not in state-dictionary order, so the tensor ORDER in
    # the GGUF is evidence that the parts are walked the way the engine walks them.
    if include("tinyllamasplit-123k"):
        variants.append((write_variant(
            "tinyllamasplit-123k", BASE_CONFIG, seed=20260918, container=("safetensors", "bf16"),
            sharded=True), ["auto"]))
    if include("tinyllamabinsplit-123k"):
        variants.append((write_variant(
            "tinyllamabinsplit-123k", BASE_CONFIG, seed=20260918, container=("bin", "bf16"),
            sharded=True), ["auto"]))

    if arguments.checkpoints_only:
        return

    print("oracles", flush=True)
    for directory, out_types in variants:
        for out_type in out_types:
            convert(arguments.engine, directory, out_type)


if __name__ == "__main__":
    main()
