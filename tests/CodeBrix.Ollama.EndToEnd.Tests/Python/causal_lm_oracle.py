#!/usr/bin/env python3
"""Generates text from an ONNX text-generation bundle the way the people who wrote the bundle would.

The test suite spawns this with the maintainer's virtual environment. It is the SECOND OPINION the managed
driver is held to, and nothing here goes through the library under test:

  * TOKENIZING is the publisher's own tokenizer, loaded through transformers with the bundle's own
    tokenization module (these bundles carry one, so trust_remote_code is required). That tokenizer is what
    the checkpoint was trained and validated with, so its token numbers are the ones that count.
  * GENERATING is onnxruntime-genai, which is the runtime the generation configuration in the bundle was
    written FOR: it reads the same genai_config.json, lays out the same inputs, keeps the same cache and runs
    the graph through onnxruntime. Its tokenizer is not used - it wants a tokenizer.json these bundles do not
    carry - so the token numbers are handed to it directly and the ones it answers with are read back
    directly. That keeps the comparison to the thing being compared: the loop and the arithmetic.

It also does TEACHER FORCING on request: given a sequence, it runs the graph once over the whole of it and
reports the most likely token at every position. That is how a quantized variant is compared without letting
one different choice send the two runs down different paths.

    python causal_lm_oracle.py --bundle <dir> --out <file.json> [--max N] [--threads N]
                               [--prompt <text> ...] [--teacher-force <file.json>]

Everything it writes is JSON, so the C# side reads numbers rather than parsing prose.
"""
import argparse
import json
import os
import time


def load_tokenizer(bundle):
    """The publisher's own tokenizer, out of the bundle, with nothing downloaded."""
    os.environ.setdefault("HF_HUB_OFFLINE", "1")
    from transformers import AutoTokenizer

    return AutoTokenizer.from_pretrained(bundle, trust_remote_code=True, use_fast=False)


def generate(model, ids, maximum):
    """Greedy generation through onnxruntime-genai, token numbers in and token numbers out."""
    import onnxruntime_genai as og

    params = og.GeneratorParams(model)
    params.set_search_options(do_sample=False, max_length=len(ids) + maximum)

    generator = og.Generator(model, params)
    started = time.perf_counter()
    generator.append_tokens(ids)
    prefill = time.perf_counter() - started

    produced = []
    while not generator.is_done() and len(produced) < maximum:
        generator.generate_next_token()
        produced.append(int(generator.get_next_tokens()[0]))

    return produced, prefill, time.perf_counter() - started


def teacher_force(bundle, sequences, threads):
    """The most likely token at every position of a sequence, from one run over the whole of it.

    This is deliberately NOT onnxruntime-genai: it is onnxruntime driven directly, because what is wanted is
    every position's answer at once rather than a generation. The inputs are the ones the bundle's own
    configuration names.
    """
    import numpy as np
    import onnxruntime as ort

    with open(os.path.join(bundle, "genai_config.json"), "r", encoding="utf-8") as handle:
        config = json.load(handle)

    decoder = config["model"]["decoder"]
    layers = int(decoder["num_hidden_layers"])
    kv_heads = int(decoder.get("num_key_value_heads", decoder["num_attention_heads"]))
    head_size = int(decoder["head_size"])
    past_key = decoder["inputs"]["past_key_names"]
    past_value = decoder["inputs"]["past_value_names"]

    options = ort.SessionOptions()
    if threads:
        options.intra_op_num_threads = threads
    session = ort.InferenceSession(
        os.path.join(bundle, decoder["filename"]), options, providers=["CPUExecutionProvider"])

    answers = []
    for sequence in sequences:
        feeds = {
            decoder["inputs"]["input_ids"]: np.array([sequence], dtype=np.int64),
            decoder["inputs"]["attention_mask"]: np.ones((1, len(sequence)), dtype=np.int64),
        }
        empty = np.zeros((1, kv_heads, 0, head_size), dtype=np.float32)
        for layer in range(layers):
            feeds[past_key.replace("%d", str(layer))] = empty
            feeds[past_value.replace("%d", str(layer))] = empty

        logits = session.run([decoder["outputs"]["logits"]], feeds)[0]
        answers.append([int(x) for x in np.argmax(logits[0], axis=-1)])

    return answers


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--bundle", required=True)
    parser.add_argument("--out", required=True)
    parser.add_argument("--max", type=int, default=32)
    parser.add_argument("--threads", type=int, default=0)
    parser.add_argument("--prompt", action="append", default=[])
    parser.add_argument("--teacher-force")
    arguments = parser.parse_args()

    tokenizer = load_tokenizer(arguments.bundle)
    report = {"prompts": [], "teacher_forced": []}

    try:
        import onnxruntime_genai as og

        report["engine"] = "onnxruntime-genai " + og.__version__
    except ImportError:
        og = None
        report["engine"] = "(onnxruntime-genai is not installed)"

    if arguments.prompt and og is not None:
        if arguments.threads:
            os.environ["OMP_NUM_THREADS"] = str(arguments.threads)
        model = og.Model(arguments.bundle)
        for prompt in arguments.prompt:
            ids = [int(x) for x in tokenizer.encode(prompt)]
            produced, prefill, elapsed = generate(model, ids, arguments.max)
            report["prompts"].append({
                "text": prompt,
                "ids": ids,
                "decoded": tokenizer.decode(ids),
                "generated": produced,
                "generated_text": tokenizer.decode(produced),
                "prefill_seconds": prefill,
                "seconds": elapsed,
            })

    if arguments.teacher_force:
        with open(arguments.teacher_force, "r", encoding="utf-8") as handle:
            sequences = json.load(handle)
        report["teacher_forced"] = teacher_force(arguments.bundle, sequences, arguments.threads)

    with open(arguments.out, "w", encoding="utf-8") as handle:
        json.dump(report, handle)

    print("CAUSAL LM ORACLE OK")


if __name__ == "__main__":
    main()
