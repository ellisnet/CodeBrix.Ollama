#!/usr/bin/env python3
"""Runs one step, and then a cached second step, of a real ONNX decoder through onnxruntime.

The test suite spawns this with the maintainer's virtual environment. It is the SECOND OPINION the managed
interpreter is held to: nothing here goes through the library under test, and the numbers it writes are
onnxruntime's own.

It writes, under the work directory:

    step0/inputs.json   + one .bin per tensor - what it fed the graph
    step0/outputs.json  + one .bin per tensor - what onnxruntime made of it
    step1/inputs.json   + .bin - the same, for a step whose past is step 0's present
    step1/outputs.json  + .bin
    oracle.json         - how long each step took, and how many threads it used

The C# side reads the INPUTS back and feeds exactly those to the managed engine, so the two engines are
compared on identical numbers rather than on two runs that drifted apart.

    python onnx_oracle.py --model <path> --work <dir> --kind token|base|causal [--threads N]
                          [--strip-accuracy-level <path>]

--strip-accuracy-level asks for a COPY of the graph with the accuracy_level attribute taken off every
MatMulNBits node, written to the path given, and runs THAT instead of the original. It exists for one
comparison and is described where it is used, in strip_accuracy_level() below.
"""
import argparse
import json
import os
import time

import numpy as np
import onnx
import onnxruntime as ort

SEED = 20260918

DTYPES = {
    1: ("float", np.float32),
    7: ("int64", np.int64),
    9: ("bool", np.bool_),
    6: ("int32", np.int32),
}

# Each subject's symbolic dimensions, for a first step and for a cached second one. The token model's first
# step carries one hidden state and NO token, and every step after it carries no hidden state and one token;
# the base model consumes a window of events; the causal language model prefills a short prompt and then takes
# one token at a time. All of them take their own previous `present` back as `past`.
STEPS = {
    "token": [
        {"batch": 1, "states": 1, "token_seq": 0, "past_seq": 0},
        {"batch": 1, "states": 0, "token_seq": 1},
    ],
    "base": [
        {"batch": 1, "mid_seq": 2, "token_seq": 8, "past_seq": 0},
        {"batch": 1, "mid_seq": 1, "token_seq": 8},
    ],
    "causal": [
        {"batch_size": 1, "sequence_length": 4, "past_sequence_length": 0,
         "total_sequence_length": 4, "kv_cache_dim": 64},
        {"batch_size": 1, "sequence_length": 1, "total_sequence_length": 5,
         "kv_cache_dim": 64},
    ],
}

# Inputs that are a mask rather than data: every position is attended to, which is what a decoder driving this
# graph would supply and what the graph's own arithmetic reads the sequence lengths out of.
MASKS = ("attention_mask",)

# Token identifiers have to be inside the embedding the graph carries, or the gather that reads it is out of
# range. Both graphs' vocabularies are far larger than this; a small ceiling keeps the case honest and safe.
MAX_TOKEN = 300


def dimensions(shape, sizes, name):
    resolved = []
    for dimension in shape:
        if isinstance(dimension, int):
            resolved.append(dimension)
        elif dimension in sizes:
            resolved.append(sizes[dimension])
        else:
            raise SystemExit(
                "The input '%s' has a dimension '%s' that this oracle does not know how to fill."
                % (name, dimension))
    return resolved


def make_inputs(session, sizes, step, previous, salt):
    feeds = {}
    for entry in session.get_inputs():
        name = entry.name
        if name.startswith("past_key_values.") and previous is not None:
            feeds[name] = previous[name.replace("past_key_values.", "present.")]
            continue

        shape = dimensions(entry.shape, sizes, name)
        if name.startswith("past_key_values.") and "past_seq" in sizes:
            shape[2] = sizes["past_seq"]

        if name in MASKS:
            feeds[name] = np.ones(shape, dtype=np.int64 if entry.type == "tensor(int64)" else np.int32)
            continue

        # A stable hash of the name, so a re-run reproduces the same numbers: Python's own hash() is
        # randomized per process and would not.
        generator = np.random.default_rng(SEED + salt + (sum(ord(letter) for letter in name) % 1000))
        if entry.type == "tensor(int64)":
            feeds[name] = generator.integers(0, MAX_TOKEN, size=shape).astype(np.int64)
        elif entry.type == "tensor(float)":
            feeds[name] = generator.standard_normal(size=shape).astype(np.float32)
        else:
            raise SystemExit("The input '%s' is a %s, which this oracle does not build." % (name, entry.type))

    _ = step
    return feeds


def write(folder, tensors, order):
    os.makedirs(folder, exist_ok=True)
    manifest = []
    for name in order:
        value = tensors[name]
        safe = name.replace("/", "_").replace(".", "_")
        path = safe + ".bin"
        with open(os.path.join(folder, path), "wb") as handle:
            handle.write(np.ascontiguousarray(value).tobytes())
        manifest.append({
            "name": name,
            "type": {np.dtype("float32"): "float", np.dtype("int64"): "int64",
                     np.dtype("int32"): "int32", np.dtype("bool"): "bool"}[value.dtype],
            "shape": list(value.shape),
            "file": path,
        })

    with open(os.path.join(folder, "manifest.json"), "w") as handle:
        json.dump({"tensors": manifest}, handle, indent=2)
        handle.write("\n")


def strip_accuracy_level(model_path, destination):
    """Writes a copy of the graph with accuracy_level taken off every MatMulNBits node, and returns its path.

    WHY THIS EXISTS. accuracy_level does not describe the weight: it is the LOWEST precision a runtime may
    compute the ACTIVATIONS at, and 4 means "you may quantize input A to 8-bit integers as well". onnxruntime
    takes that option; the managed engine reads the attribute and keeps its activations in 32-bit floats,
    which is more accurate than anything the attribute permits. Taking the attribute off asks onnxruntime for
    the same precision the managed engine already computes at, so the two can be held to the ordinary
    quantized-weight tolerance instead of to the wide one the attribute forces.

    THE STORED BUNDLE IS NEVER TOUCHED. The copy goes where the caller says, which is the test's own temporary
    folder; the caller puts it BESIDE the model it came from, because these exports keep their weights in a
    side file that the graph names relatively and onnxruntime refuses to follow such a name out of the
    directory the model is in. Only the graph is rewritten - a few tens of kilobytes - and the side file,
    which is the hundreds of megabytes, is neither read nor copied here.
    """
    model = onnx.load(model_path, load_external_data=False)
    stripped = 0
    for node in model.graph.node:
        if node.op_type != "MatMulNBits":
            continue

        kept = [attribute for attribute in node.attribute if attribute.name != "accuracy_level"]
        stripped += len(node.attribute) - len(kept)
        del node.attribute[:]
        node.attribute.extend(kept)

    if stripped == 0:
        raise SystemExit(
            "The graph '%s' carries no accuracy_level attribute, so asking for it to be stripped is a"
            " mistake somewhere: the comparison it is for would be measuring nothing." % model_path)

    onnx.save(model, destination)
    print("stripped accuracy_level from %d nodes into %s" % (stripped, destination))
    return destination


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--work", required=True)
    parser.add_argument("--kind", required=True, choices=sorted(STEPS))
    parser.add_argument("--threads", type=int, default=8)
    parser.add_argument("--strip-accuracy-level", default=None)
    arguments = parser.parse_args()

    model_path = arguments.model
    if arguments.strip_accuracy_level:
        model_path = strip_accuracy_level(model_path, arguments.strip_accuracy_level)

    options = ort.SessionOptions()
    options.intra_op_num_threads = arguments.threads
    options.inter_op_num_threads = 1
    options.execution_mode = ort.ExecutionMode.ORT_SEQUENTIAL
    session = ort.InferenceSession(
        model_path, sess_options=options, providers=["CPUExecutionProvider"])

    names = [entry.name for entry in session.get_outputs()]
    report = {"kind": arguments.kind, "threads": arguments.threads, "steps": []}
    previous = None

    for index, sizes in enumerate(STEPS[arguments.kind]):
        feeds = make_inputs(session, sizes, index, previous, index * 17)
        started = time.perf_counter()
        produced = session.run(None, feeds)
        elapsed = (time.perf_counter() - started) * 1000.0

        tensors = dict(zip(names, produced))
        folder = os.path.join(arguments.work, "step%d" % index)
        write(os.path.join(folder, "inputs"), feeds, [entry.name for entry in session.get_inputs()])
        write(os.path.join(folder, "outputs"), tensors, names)
        report["steps"].append({"step": index, "milliseconds": elapsed})
        previous = tensors
        print("step %d: %.2f ms" % (index, elapsed))

    with open(os.path.join(arguments.work, "oracle.json"), "w") as handle:
        json.dump(report, handle, indent=2)
        handle.write("\n")

    print("ORACLE OK")


if __name__ == "__main__":
    main()
