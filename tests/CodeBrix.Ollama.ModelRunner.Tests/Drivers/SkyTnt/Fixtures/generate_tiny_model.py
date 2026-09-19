#!/usr/bin/env python
"""Writes the TINY MODEL the MIDI generation loop is tested against with nothing installed.

WHAT IT MAKES.  A bundle shaped exactly like the publisher's - a config.json describing the same
tokenizer, and two graphs with the same inputs, outputs and cache contract - but a few kilobytes
instead of a gigabyte, with weights this script makes up from a fixed seed.  It is not a music model
and does not pretend to be one: it is a pair of graphs whose arithmetic the DRIVER cannot tell from a
real one, so the loop, both caches, the sampling masks and the stop conditions can be exercised
without a download and without a publisher's file in the repository.

WHAT IS IN IT, and why each piece is there:

  base graph   x [batch, events, 8] int64, past_key_values.0.{key,value} [batch, 2, past, 3]
               -> hidden [batch, events, 4], present.0.{key,value} [batch, 2, past + events, 3]

      Three of the four numbers of a state are a projection of the event's tokens PLUS the total of
      everything in the cache, so a driver that failed to feed the cache back would compute different
      states.  The fourth is the LENGTH of the cache, which is what makes the ending token's answer
      grow with the piece - so generation stops by itself after a dozen or so events, and a driver
      that lost the cache would never stop.

  token graph  hidden [batch, states, 4], x [batch, tokens] int64,
               past_key_values.0.{key,value} [batch, 1, past, 2]
               -> y [batch, 1, 3406], present.0.{key,value} [batch, 1, past + 1, 2]

      It concatenates its two inputs, one of which is always empty - the state for the first token of
      an event, the last token for every one after that - exactly as the real one does.  Its answer is
      a random projection of that, with the ending token's column reading the cache length.

HOW TO MAKE THEM AGAIN, by hand, once, in a virtual environment holding onnx:

    <venv>/bin/python generate_tiny_model.py

THE TEST SUITE NEVER RUNS IT.  It reads the files as they are checked in.  Everything here is ours
(MIT, this repository's licence): the weights are seeded random numbers and no publisher's data, model
or file is among them.  The tokenizer block of config.json states the same numbers the published
bundle states, which are facts about a vocabulary rather than anything copied.
"""
import json
import os

import numpy as np
from onnx import TensorProto, helper, numpy_helper

HERE = os.path.dirname(os.path.abspath(__file__))
BUNDLE = os.path.join(HERE, "tiny-model")

VOCAB = 3406
ROW = 8
STATE = 4
BASE_HEADS = 2
BASE_WIDTH = 3
TOKEN_HEADS = 1
TOKEN_WIDTH = 2
EOS = 2


def constant(name, array):
    return numpy_helper.from_array(np.ascontiguousarray(array), name)


def build_base(rng):
    """The graph that turns the events so far into one state per event."""
    weight_state = (rng.standard_normal((1, STATE - 1)) * 0.0002).astype(np.float32)
    weight_key = (rng.random((1, BASE_HEADS * BASE_WIDTH)) * 1e-6).astype(np.float32)
    weight_value = (rng.random((1, BASE_HEADS * BASE_WIDTH)) * 1e-6).astype(np.float32)

    initializers = [
        constant("w_state", weight_state),
        constant("w_key", weight_key),
        constant("w_value", weight_value),
        constant("sum_axes", np.array([2], dtype=np.int64)),
        constant("all_axes", np.array([1, 2, 3], dtype=np.int64)),
        constant("key_shape", np.array([0, 0, BASE_HEADS, BASE_WIDTH], dtype=np.int64)),
        constant("total_shape", np.array([-1, 1, 1], dtype=np.int64)),
        constant("one_shape", np.array([1, 1, 1], dtype=np.int64)),
        constant("length_index", np.array([2], dtype=np.int64)),
        constant("zero", np.array([0.0], dtype=np.float32)),
    ]

    nodes = [
        helper.make_node("Cast", ["x"], ["x_float"], to=TensorProto.FLOAT),
        helper.make_node("ReduceSum", ["x_float", "sum_axes"], ["row_sum"], keepdims=1),
    ]

    for side, weight in (("key", "w_key"), ("value", "w_value")):
        nodes += [
            helper.make_node("MatMul", ["row_sum", weight], [f"new_{side}_flat"]),
            helper.make_node("Reshape", [f"new_{side}_flat", "key_shape"], [f"new_{side}_4d"]),
            helper.make_node("Transpose", [f"new_{side}_4d"], [f"new_{side}"], perm=[0, 2, 1, 3]),
            helper.make_node(
                "Concat", [f"past_key_values.0.{side}", f"new_{side}"], [f"present.0.{side}"], axis=2),
        ]

    nodes += [
        # Everything in the cache, totalled - so a state depends on the whole piece so far.
        helper.make_node("ReduceSum", ["present.0.key", "all_axes"], ["cache_total_flat"], keepdims=0),
        helper.make_node("Reshape", ["cache_total_flat", "total_shape"], ["cache_total"]),
        helper.make_node("MatMul", ["row_sum", "w_state"], ["state_body_raw"]),
        helper.make_node("Add", ["state_body_raw", "cache_total"], ["state_body"]),

        # How LONG the cache is, as one more number of the state.
        helper.make_node("Shape", ["present.0.key"], ["cache_shape"]),
        helper.make_node("Gather", ["cache_shape", "length_index"], ["cache_length"], axis=0),
        helper.make_node("Cast", ["cache_length"], ["cache_length_float"], to=TensorProto.FLOAT),
        helper.make_node("Reshape", ["cache_length_float", "one_shape"], ["cache_length_state"]),
        helper.make_node("Mul", ["row_sum", "zero"], ["state_zero"]),
        helper.make_node("Add", ["state_zero", "cache_length_state"], ["length_channel"]),
        helper.make_node("Concat", ["state_body", "length_channel"], ["hidden"], axis=2),
    ]

    inputs = [
        helper.make_tensor_value_info("x", TensorProto.INT64, ["batch", "events", ROW]),
        helper.make_tensor_value_info(
            "past_key_values.0.key", TensorProto.FLOAT, ["batch", BASE_HEADS, "past", BASE_WIDTH]),
        helper.make_tensor_value_info(
            "past_key_values.0.value", TensorProto.FLOAT, ["batch", BASE_HEADS, "past", BASE_WIDTH]),
    ]
    outputs = [
        helper.make_tensor_value_info("hidden", TensorProto.FLOAT, ["batch", "events", STATE]),
        helper.make_tensor_value_info(
            "present.0.key", TensorProto.FLOAT, ["batch", BASE_HEADS, "total", BASE_WIDTH]),
        helper.make_tensor_value_info(
            "present.0.value", TensorProto.FLOAT, ["batch", BASE_HEADS, "total", BASE_WIDTH]),
    ]
    return helper.make_graph(nodes, "tiny_midi_base", inputs, outputs, initializers)


def build_token(rng, ending_weight, ending_bias):
    """The graph that turns one state into an event's tokens, one at a time."""
    # The first three numbers of a state are squashed into (0, 1) before they are read, so every
    # answer but the ending token's is BOUNDED however large the state grows. That is what lets the
    # ending token's own column - which reads the cache LENGTH and nothing else - overtake them after
    # a dozen or so events, whatever the piece turned out to be.
    weight_answer = rng.standard_normal((STATE - 1, VOCAB)).astype(np.float32)
    weight_ending = np.zeros((1, VOCAB), dtype=np.float32)
    weight_ending[0, EOS] = ending_weight
    bias = np.zeros((VOCAB,), dtype=np.float32)
    bias[EOS] = -ending_bias

    weight_token = (rng.standard_normal((1, STATE)) * 0.0004).astype(np.float32)
    weight_token[0, STATE - 1] = 0.0
    weight_key = (rng.standard_normal((STATE, TOKEN_HEADS * TOKEN_WIDTH)) * 0.01).astype(np.float32)
    weight_value = (rng.standard_normal((STATE, TOKEN_HEADS * TOKEN_WIDTH)) * 0.01).astype(np.float32)

    initializers = [
        constant("w_answer", weight_answer),
        constant("w_ending", weight_ending),
        constant("bias", bias),
        constant("w_token", weight_token),
        constant("w_key", weight_key),
        constant("w_value", weight_value),
        constant("token_axes", np.array([2], dtype=np.int64)),
        constant("key_shape", np.array([0, 0, TOKEN_HEADS, TOKEN_WIDTH], dtype=np.int64)),
        constant("body_from", np.array([0], dtype=np.int64)),
        constant("body_to", np.array([STATE - 1], dtype=np.int64)),
        constant("length_to", np.array([STATE], dtype=np.int64)),
        constant("state_axis", np.array([2], dtype=np.int64)),
    ]

    nodes = [
        helper.make_node("Cast", ["x"], ["x_float"], to=TensorProto.FLOAT),
        helper.make_node("Unsqueeze", ["x_float", "token_axes"], ["x_column"]),
        helper.make_node("MatMul", ["x_column", "w_token"], ["x_state"]),
        helper.make_node("Concat", ["hidden", "x_state"], ["sequence"], axis=1),
    ]

    for side, weight in (("key", "w_key"), ("value", "w_value")):
        nodes += [
            helper.make_node("MatMul", ["sequence", weight], [f"new_{side}_flat"]),
            helper.make_node("Reshape", [f"new_{side}_flat", "key_shape"], [f"new_{side}_4d"]),
            helper.make_node("Transpose", [f"new_{side}_4d"], [f"new_{side}"], perm=[0, 2, 1, 3]),
            helper.make_node(
                "Concat", [f"past_key_values.0.{side}", f"new_{side}"], [f"present.0.{side}"], axis=2),
        ]

    nodes += [
        helper.make_node("Slice", ["sequence", "body_from", "body_to", "state_axis"], ["body"]),
        helper.make_node("Slice", ["sequence", "body_to", "length_to", "state_axis"], ["length_channel"]),
        helper.make_node("Sigmoid", ["body"], ["bounded"]),
        helper.make_node("MatMul", ["bounded", "w_answer"], ["answer_body"]),
        helper.make_node("MatMul", ["length_channel", "w_ending"], ["answer_ending"]),
        helper.make_node("Add", ["answer_body", "answer_ending"], ["answer_raw"]),
        helper.make_node("Add", ["answer_raw", "bias"], ["y"]),
    ]

    inputs = [
        helper.make_tensor_value_info("hidden", TensorProto.FLOAT, ["batch", "states", STATE]),
        helper.make_tensor_value_info("x", TensorProto.INT64, ["batch", "tokens"]),
        helper.make_tensor_value_info(
            "past_key_values.0.key", TensorProto.FLOAT, ["batch", TOKEN_HEADS, "past", TOKEN_WIDTH]),
        helper.make_tensor_value_info(
            "past_key_values.0.value", TensorProto.FLOAT, ["batch", TOKEN_HEADS, "past", TOKEN_WIDTH]),
    ]
    outputs = [
        helper.make_tensor_value_info("y", TensorProto.FLOAT, ["batch", "length", VOCAB]),
        helper.make_tensor_value_info(
            "present.0.key", TensorProto.FLOAT, ["batch", TOKEN_HEADS, "total", TOKEN_WIDTH]),
        helper.make_tensor_value_info(
            "present.0.value", TensorProto.FLOAT, ["batch", TOKEN_HEADS, "total", TOKEN_WIDTH]),
    ]
    return helper.make_graph(nodes, "tiny_midi_token", inputs, outputs, initializers)


def save(graph, path):
    model = helper.make_model(
        graph, opset_imports=[helper.make_opsetid("", 14)], producer_name="codebrix-ollama-tiny-midi")
    model.ir_version = 8
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f:
        f.write(model.SerializeToString())
    return os.path.getsize(path)


def configuration():
    """The same description of the tokenizer the published bundle carries."""
    return {
        "architectures": ["MIDIModel"],
        "model_type": "midi_model",
        "n_embd": STATE,
        "tokenizer": {
            "version": "v2",
            "optimise_midi": True,
            "vocab_size": VOCAB,
            "max_token_seq": ROW,
            "pad_id": 0,
            "bos_id": 1,
            "eos_id": EOS,
            "events": {
                "note": ["time1", "time2", "track", "channel", "pitch", "velocity", "duration"],
                "patch_change": ["time1", "time2", "track", "channel", "patch"],
                "control_change": ["time1", "time2", "track", "channel", "controller", "value"],
                "set_tempo": ["time1", "time2", "track", "bpm"],
                "time_signature": ["time1", "time2", "track", "nn", "dd"],
                "key_signature": ["time1", "time2", "track", "sf", "mi"],
            },
            "event_parameters": {
                "time1": 128, "time2": 16, "duration": 2048, "track": 128, "channel": 16, "pitch": 128,
                "velocity": 128, "patch": 128, "controller": 128, "value": 128, "bpm": 384, "nn": 16,
                "dd": 4, "sf": 15, "mi": 2,
            },
        },
    }


def main():
    rng = np.random.default_rng(20260918)
    base = save(build_base(rng), os.path.join(BUNDLE, "onnx", "model_base.onnx"))
    token = save(
        build_token(rng, ending_weight=0.2, ending_bias=1.8),
        os.path.join(BUNDLE, "onnx", "model_token.onnx"))
    with open(os.path.join(BUNDLE, "config.json"), "w") as f:
        json.dump(configuration(), f, indent=2, sort_keys=True)
        f.write("\n")
    print(f"wrote {BUNDLE}: base {base} bytes, token {token} bytes")


if __name__ == "__main__":
    main()
