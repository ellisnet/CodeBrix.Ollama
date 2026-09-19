#!/usr/bin/env python3
"""Generates the per-operator ONNX oracle fixtures this suite compares the managed interpreter against.

RUN IT BY HAND, ONCE, AND CHECK IN WHAT IT WRITES. No test ever runs it: the suite reads only the files it
left behind, so the offline suite needs nothing installed. It needs onnx and onnxruntime, which live in the
maintainer's virtual environment:

    ~/venvs/codebrix-ollama/bin/python generate_fixtures.py

Every case is one tiny graph, a set of deterministic inputs (seeded, so a regeneration reproduces them), and
whatever onnxruntime computes from them. The graphs and the numbers are ours - no publisher's data is among
them - and they are MIT-licensed like the rest of this repository. See README.txt beside this file.
"""
import json
import os
import shutil

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper, shape_inference
import onnxruntime as ort

HERE = os.path.dirname(os.path.abspath(__file__))
SEED = 20260918
CONTRIB_DOMAIN = "com.microsoft"

DTYPE_NAMES = {
    np.dtype("float32"): "float",
    np.dtype("int64"): "int64",
    np.dtype("int32"): "int32",
    np.dtype("bool"): "bool",
}

ELEMENT_TYPES = {
    "float": TensorProto.FLOAT,
    "int64": TensorProto.INT64,
    "int32": TensorProto.INT32,
    "bool": TensorProto.BOOL,
}

cases = []


def rng(salt):
    return np.random.default_rng(SEED + salt)


def floats(shape, salt, scale=1.0):
    return (rng(salt).standard_normal(size=shape).astype(np.float32) * np.float32(scale)).astype(np.float32)


def positives(shape, salt, low=0.25, high=4.0):
    return rng(salt).uniform(low, high, size=shape).astype(np.float32)


def ints(shape, salt, low=-5, high=6):
    return rng(salt).integers(low, high, size=shape).astype(np.int64)


def bools(shape, salt):
    return (rng(salt).integers(0, 2, size=shape) == 1)


def info(name, array):
    return helper.make_tensor_value_info(
        name, ELEMENT_TYPES[DTYPE_NAMES[array.dtype]], list(array.shape))


def build(name, nodes, inputs, outputs, initializers, opset, contrib=False):
    """Builds one model. A case that uses a CONTRIBUTED operator declares that domain as well.

    A contributed operator is a runtime vendor's own, so the ONNX package knows neither its schema nor its
    shapes: such a graph is declared against a newer operator set and a newer file format - which is what the
    model builders that emit these operators write - and it is NOT put through the ONNX checker, because the
    checker would refuse an operator it has never heard of. onnxruntime is what says whether it is valid, and
    it does so by running it.
    """
    graph = helper.make_graph(
        nodes, name,
        [info(key, value) for key, value in inputs.items()],
        outputs,
        initializer=[numpy_helper.from_array(v, k) for k, v in (initializers or {}).items()])
    imports = [helper.make_opsetid("", opset)]
    if contrib:
        imports.append(helper.make_opsetid(CONTRIB_DOMAIN, 1))
    model = helper.make_model(
        graph, opset_imports=imports, producer_name="codebrix-ollama-fixtures")
    model.ir_version = 10 if contrib else 8
    return model


def resolve(name, nodes, inputs, outputs, initializers, opset, folder, declare, contrib=False,
            check=True):
    """Settles what the graph's outputs really are, using ONNX's OWN shape inference.

    The outputs cannot simply be left undeclared - the checker wants a shape - and they cannot be taken from
    a trial run either: with an output left untyped, onnxruntime hands a SCALAR back as a one-element array,
    so a reduction over every axis would be recorded as [1] when the operator's answer is a rank of nought.
    The specification's own inference is the authority on that, so it is what settles the declarations, and
    the saved graph is then run to produce the numbers.
    """
    draft = build(name, nodes, inputs,
                  [helper.make_empty_tensor_value_info(key) for key, _ in outputs],
                  initializers, opset, contrib)
    inferred = shape_inference.infer_shapes(draft, strict_mode=check, data_prop=True)

    declare = declare or {}
    unknown = [value.name for value in inferred.graph.output
               if value.name not in declare
               and (not value.type.tensor_type.HasField("elem_type")
                    or not value.type.tensor_type.HasField("shape"))]
    if not unknown and not declare:
        return list(inferred.graph.output)

    # A shape that depends on the VALUE of an input - a list of axes, a target shape - cannot be inferred
    # from the graph alone, so it is taken from a trial run. That is only ambiguous for a result of one
    # element, where a rank of nought and a shape of [1] look the same coming back from onnxruntime, so a
    # case like that has to be settled by hand rather than guessed at.
    probe_path = os.path.join(folder, "probe.onnx")
    onnx.save(build(name, nodes, inputs,
                    [helper.make_empty_tensor_value_info(key) for key, _ in outputs],
                    initializers, opset, contrib), probe_path)
    produced = ort.InferenceSession(
        probe_path, providers=["CPUExecutionProvider"]).run(None, {k: v for k, v in inputs.items()})
    os.remove(probe_path)


    declared = []
    for value, (key, kind), computed in zip(inferred.graph.output, outputs, produced):
        if key in declare:
            # Stated by hand: neither ONNX's inference nor a trial run can settle this one.
            declared.append(helper.make_tensor_value_info(key, ELEMENT_TYPES[kind], declare[key]))
            print("%-44s shape stated by hand: %s %s" % (name, key, declare[key]))
            continue

        if value.name not in unknown:
            declared.append(value)
            continue

        if computed.size == 1:
            raise RuntimeError(
                "The case '%s' has an output ('%s') whose shape ONNX cannot infer and whose trial run holds"
                " one element, so a rank of nought cannot be told from a shape of [1]. Give the case a shape"
                " that settles it." % (name, key))

        declared.append(helper.make_tensor_value_info(
            key, ELEMENT_TYPES[DTYPE_NAMES[computed.dtype]], list(computed.shape)))
        print("%-44s shape from a trial run: %s %s" % (name, key, list(computed.shape)))

    return declared


def write_case(folder, name, opset, inputs, outputs, produced, note, extra):
    manifest = {"name": name, "opset": opset, "inputs": [], "outputs": []}
    manifest.update(extra)
    if note is not None:
        manifest["note"] = note

    for key, value in inputs.items():
        path = "in_" + key.replace("/", "_") + ".bin"
        with open(os.path.join(folder, path), "wb") as handle:
            handle.write(np.ascontiguousarray(value).tobytes())
        manifest["inputs"].append(
            {"name": key, "type": DTYPE_NAMES[value.dtype], "shape": list(value.shape), "file": path})

    for (key, _), value in zip(outputs, produced):
        # The shape is read from the array onnxruntime handed back, NOT from a contiguous copy of it:
        # np.ascontiguousarray turns a scalar into a one-element vector, which would record a rank of
        # nought as a shape of [1] and quietly make the oracle wrong about the operator's own answer.
        path = "out_" + key.replace("/", "_") + ".bin"
        with open(os.path.join(folder, path), "wb") as handle:
            handle.write(np.ascontiguousarray(value).tobytes())
        manifest["outputs"].append(
            {"name": key, "type": DTYPE_NAMES[value.dtype], "shape": list(value.shape), "file": path})

    with open(os.path.join(folder, "case.json"), "w") as handle:
        json.dump(manifest, handle, indent=2)
        handle.write("\n")


def emit(name, nodes, inputs, outputs, initializers=None, opset=14, tolerance=None, note=None,
         declare=None, contrib=False, check=None):
    """Builds one graph, runs it through onnxruntime and writes the case out.

    `check` says whether the ONNX checker and its strict shape inference are run over the graph. They are
    skipped for an operator the ONNX package has no schema for - every contributed one, and
    SimplifiedLayerNormalization, which a runtime registers in the DEFAULT domain although no release of the
    specification defines it there. onnxruntime is what says those graphs are valid, and it says so by
    running them.
    """
    folder = os.path.join(HERE, name)
    os.makedirs(folder, exist_ok=True)
    if check is None:
        check = not contrib

    declared = resolve(name, nodes, inputs, outputs, initializers, opset, folder, declare, contrib, check)
    model = build(name, nodes, inputs, declared, initializers, opset, contrib)
    if check:
        onnx.checker.check_model(model)
    onnx.save(model, os.path.join(folder, "model.onnx"))

    produced = ort.InferenceSession(
        os.path.join(folder, "model.onnx"),
        providers=["CPUExecutionProvider"]).run(None, {k: v for k, v in inputs.items()})

    extra = {} if tolerance is None else {"tolerance": tolerance}
    write_case(folder, name, opset, inputs, outputs, produced, note, extra)
    cases.append(name)
    print("%-44s %s" % (name, " ".join(str(list(v.shape)) for _, v in inputs.items())))


def node(op, ins, outs, **attrs):
    return helper.make_node(op, ins, outs, **attrs)


# ---------------------------------------------------------------- element-by-element, two tensors

for index, op in enumerate(["Add", "Sub", "Mul", "Div"]):
    lower = op.lower()
    emit("%s_same_float" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": floats((2, 3, 4), index), "b": positives((2, 3, 4), index + 40)}, [("y", "float")])
    emit("%s_broadcast_tail" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": floats((2, 3, 4), index + 80), "b": positives((4,), index + 120)}, [("y", "float")])
    emit("%s_broadcast_both" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": floats((2, 1, 4), index + 160), "b": positives((1, 3, 1), index + 200)}, [("y", "float")])
    emit("%s_scalar" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": floats((3, 5), index + 240), "b": positives((), index + 280)}, [("y", "float")])
    emit("%s_int64" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": ints((2, 4), index + 320), "b": ints((2, 4), index + 360, 1, 7)}, [("y", "int64")])
    emit("%s_empty" % lower, [node(op, ["a", "b"], ["y"])],
         {"a": floats((2, 0, 4), index + 400), "b": positives((4,), index + 440)}, [("y", "float")])

emit("pow_float", [node("Pow", ["a", "b"], ["y"])],
     {"a": positives((3, 4), 1), "b": floats((3, 4), 2, 0.5)}, [("y", "float")])
emit("pow_square", [node("Pow", ["a", "b"], ["y"])],
     {"a": floats((2, 3, 4), 3), "b": np.array(2.0, dtype=np.float32)}, [("y", "float")])
emit("pow_broadcast", [node("Pow", ["a", "b"], ["y"])],
     {"a": positives((2, 3), 4), "b": floats((3,), 5, 0.5)}, [("y", "float")])
emit("pow_int64", [node("Pow", ["a", "b"], ["y"])],
     {"a": ints((4,), 6, 1, 5), "b": ints((4,), 7, 0, 4)}, [("y", "int64")])

# ---------------------------------------------------------------- element-by-element, one tensor

emit("neg_float", [node("Neg", ["a"], ["y"])], {"a": floats((3, 5), 10)}, [("y", "float")])
emit("neg_int64", [node("Neg", ["a"], ["y"])], {"a": ints((3, 5), 11)}, [("y", "int64")])
emit("neg_empty", [node("Neg", ["a"], ["y"])], {"a": floats((0, 5), 12)}, [("y", "float")])
emit("sqrt_float", [node("Sqrt", ["a"], ["y"])], {"a": positives((4, 7), 13)}, [("y", "float")])
emit("sigmoid_float", [node("Sigmoid", ["a"], ["y"])], {"a": floats((4, 7), 14, 6.0)}, [("y", "float")])
emit("sigmoid_extremes", [node("Sigmoid", ["a"], ["y"])],
     {"a": np.array([-120.0, -40.0, -1.0, 0.0, 1.0, 40.0, 120.0], dtype=np.float32)}, [("y", "float")])
emit("cos_float", [node("Cos", ["a"], ["y"])], {"a": floats((5, 9), 15, 3.0)}, [("y", "float")])
emit("sin_float", [node("Sin", ["a"], ["y"])], {"a": floats((5, 9), 16, 3.0)}, [("y", "float")])

# ---------------------------------------------------------------- comparisons and selection

emit("equal_float", [node("Equal", ["a", "b"], ["y"])],
     {"a": ints((3, 4), 20, 0, 3).astype(np.float32), "b": ints((3, 4), 21, 0, 3).astype(np.float32)},
     [("y", "bool")])
emit("equal_int64_broadcast", [node("Equal", ["a", "b"], ["y"])],
     {"a": ints((2, 3, 4), 22, 0, 3), "b": ints((4,), 23, 0, 3)}, [("y", "bool")])
emit("equal_bool", [node("Equal", ["a", "b"], ["y"])],
     {"a": bools((3, 4), 24), "b": bools((3, 4), 25)}, [("y", "bool")])
emit("greater_float", [node("Greater", ["a", "b"], ["y"])],
     {"a": floats((3, 4), 26), "b": floats((3, 4), 27)}, [("y", "bool")])
emit("greater_broadcast", [node("Greater", ["a", "b"], ["y"])],
     {"a": floats((2, 3, 4), 28), "b": floats((1, 4), 29)}, [("y", "bool")])
emit("where_float", [node("Where", ["c", "a", "b"], ["y"])],
     {"c": bools((2, 3, 4), 30), "a": floats((2, 3, 4), 31), "b": floats((2, 3, 4), 32)}, [("y", "float")])
emit("where_three_way_broadcast", [node("Where", ["c", "a", "b"], ["y"])],
     {"c": bools((1, 1, 4), 33), "a": floats((2, 3, 1), 34), "b": np.array(-1.5, dtype=np.float32)},
     [("y", "float")])
emit("where_int64", [node("Where", ["c", "a", "b"], ["y"])],
     {"c": bools((3, 4), 35), "a": ints((3, 4), 36), "b": ints((3, 4), 37)}, [("y", "int64")])

# ---------------------------------------------------------------- matrix multiplication

emit("matmul_2d", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((3, 5), 40), "b": floats((5, 4), 41)}, [("y", "float")])
emit("matmul_constant_weight", [node("MatMul", ["a", "w"], ["y"])],
     {"a": floats((2, 6, 8), 42)}, [("y", "float")], initializers={"w": floats((8, 7), 43)})
emit("matmul_constant_weight_wide", [node("MatMul", ["a", "w"], ["y"])],
     {"a": floats((1, 3, 32), 44)}, [("y", "float")], initializers={"w": floats((32, 96), 45)})
emit("matmul_batched", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((2, 3, 4, 5), 46), "b": floats((2, 3, 5, 6), 47)}, [("y", "float")])
emit("matmul_batch_broadcast", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((1, 3, 4, 5), 48), "b": floats((2, 1, 5, 6), 49)}, [("y", "float")])
emit("matmul_vector_left", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((5,), 50), "b": floats((5, 3), 51)}, [("y", "float")])
emit("matmul_vector_right", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((4, 5), 52), "b": floats((5,), 53)}, [("y", "float")])
emit("matmul_vector_both", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((6,), 54), "b": floats((6,), 55)}, [("y", "float")])
emit("matmul_empty_rows", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((0, 5), 56), "b": floats((5, 3), 57)}, [("y", "float")])
emit("matmul_empty_reduction", [node("MatMul", ["a", "b"], ["y"])],
     {"a": floats((3, 0), 58), "b": floats((0, 4), 59)}, [("y", "float")])
emit("matmul_vector_constant", [node("MatMul", ["a", "w"], ["y"])],
     {"a": floats((64,), 60)}, [("y", "float")], initializers={"w": floats((64, 48), 61)})

# ---------------------------------------------------------------- softmax and reductions

emit("softmax_last", [node("Softmax", ["a"], ["y"], axis=-1)],
     {"a": floats((2, 3, 9), 70, 4.0)}, [("y", "float")])
emit("softmax_middle", [node("Softmax", ["a"], ["y"], axis=1)],
     {"a": floats((2, 5, 3), 71, 4.0)}, [("y", "float")])
emit("softmax_first", [node("Softmax", ["a"], ["y"], axis=0)],
     {"a": floats((4, 3), 72, 4.0)}, [("y", "float")])
emit("softmax_large_values", [node("Softmax", ["a"], ["y"], axis=-1)],
     {"a": (floats((2, 8), 73) * np.float32(60.0)).astype(np.float32)}, [("y", "float")])
emit("softmax_masked_row", [node("Softmax", ["a"], ["y"], axis=-1)],
     {"a": np.array([[1.0, 2.0, -3.4e38, 0.5]], dtype=np.float32)}, [("y", "float")])

emit("reducemean_last", [node("ReduceMean", ["a"], ["y"], axes=[-1], keepdims=1)],
     {"a": floats((2, 3, 16), 74)}, [("y", "float")])
emit("reducemean_drop", [node("ReduceMean", ["a"], ["y"], axes=[-1], keepdims=0)],
     {"a": floats((2, 3, 16), 75)}, [("y", "float")])
emit("reducemean_two_axes", [node("ReduceMean", ["a"], ["y"], axes=[1, 2], keepdims=1)],
     {"a": floats((2, 3, 4, 5), 76)}, [("y", "float")])
emit("reducemean_middle_axis", [node("ReduceMean", ["a"], ["y"], axes=[1], keepdims=0)],
     {"a": floats((2, 5, 3), 77)}, [("y", "float")])
emit("reducemean_all", [node("ReduceMean", ["a"], ["y"], keepdims=0)],
     {"a": floats((3, 4), 78)}, [("y", "float")])
emit("reducemean_axes_input", [node("ReduceMean", ["a", "axes"], ["y"], keepdims=1)],
     {"a": floats((2, 3, 8), 79), "axes": np.array([-1], dtype=np.int64)}, [("y", "float")], opset=18)

emit("reducesum_last", [node("ReduceSum", ["a", "axes"], ["y"], keepdims=1)],
     {"a": floats((2, 3, 16), 80), "axes": np.array([-1], dtype=np.int64)}, [("y", "float")])
# Reducing EVERY axis without keeping them gives a scalar, and neither ONNX's inference nor a trial run can
# say so: the axes arrive as an input, and a rank of nought and a shape of [1] look the same coming back.
emit("reducesum_all", [node("ReduceSum", ["a", "axes"], ["y"], keepdims=0)],
     {"a": floats((3, 4), 81), "axes": np.array([], dtype=np.int64)}, [("y", "float")],
     declare={"y": []})
emit("reducesum_noop", [node("ReduceSum", ["a", "axes"], ["y"], keepdims=1, noop_with_empty_axes=1)],
     {"a": floats((3, 4), 82), "axes": np.array([], dtype=np.int64)}, [("y", "float")])
emit("reducesum_two_axes_drop", [node("ReduceSum", ["a", "axes"], ["y"], keepdims=0)],
     {"a": floats((2, 3, 4), 83), "axes": np.array([0, 2], dtype=np.int64)}, [("y", "float")])

# ---------------------------------------------------------------- shape arithmetic and data movement

emit("shape_basic", [node("Shape", ["a"], ["y"])], {"a": floats((2, 3, 4), 90)}, [("y", "int64")])
emit("shape_empty_axis", [node("Shape", ["a"], ["y"])], {"a": floats((2, 0, 4), 91)}, [("y", "int64")])

emit("gather_axis0", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": floats((5, 4), 92), "i": np.array([0, 3, 1, 3], dtype=np.int64)}, [("y", "float")])
emit("gather_axis1", [node("Gather", ["a", "i"], ["y"], axis=1)],
     {"a": floats((3, 5, 2), 93), "i": np.array([4, 0], dtype=np.int64)}, [("y", "float")])
emit("gather_negative_axis", [node("Gather", ["a", "i"], ["y"], axis=-1)],
     {"a": floats((2, 3, 6), 94), "i": np.array([5, 2, 0], dtype=np.int64)}, [("y", "float")])
emit("gather_scalar_index", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": floats((4, 3), 95), "i": np.array(2, dtype=np.int64)}, [("y", "float")])
emit("gather_negative_index", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": floats((4, 3), 96), "i": np.array([-1, -4], dtype=np.int64)}, [("y", "float")])
emit("gather_matrix_indices", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": floats((6, 2), 97), "i": np.array([[0, 1], [5, 2], [3, 3]], dtype=np.int64)}, [("y", "float")])
emit("gather_no_indices", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": floats((4, 3), 98), "i": np.array([], dtype=np.int64)}, [("y", "float")])
emit("gather_int64_data", [node("Gather", ["a", "i"], ["y"], axis=0)],
     {"a": ints((5,), 99), "i": np.array([4, 0, 2], dtype=np.int64)}, [("y", "int64")])

emit("concat_axis0", [node("Concat", ["a", "b"], ["y"], axis=0)],
     {"a": floats((2, 3), 100), "b": floats((4, 3), 101)}, [("y", "float")])
emit("concat_axis1", [node("Concat", ["a", "b", "c"], ["y"], axis=1)],
     {"a": floats((2, 3), 102), "b": floats((2, 1), 103), "c": floats((2, 5), 104)}, [("y", "float")])
emit("concat_negative_axis", [node("Concat", ["a", "b"], ["y"], axis=-2)],
     {"a": floats((2, 3, 4), 105), "b": floats((2, 2, 4), 106)}, [("y", "float")])
emit("concat_with_empty", [node("Concat", ["a", "b"], ["y"], axis=2)],
     {"a": floats((1, 4, 0, 8), 107), "b": floats((1, 4, 3, 8), 108)}, [("y", "float")])
emit("concat_int64", [node("Concat", ["a", "b"], ["y"], axis=0)],
     {"a": ints((2,), 109), "b": ints((3,), 110)}, [("y", "int64")])

emit("unsqueeze_front", [node("Unsqueeze", ["a", "axes"], ["y"])],
     {"a": floats((3, 4), 111), "axes": np.array([0], dtype=np.int64)}, [("y", "float")])
emit("unsqueeze_back", [node("Unsqueeze", ["a", "axes"], ["y"])],
     {"a": floats((3, 4), 112), "axes": np.array([-1], dtype=np.int64)}, [("y", "float")])
emit("unsqueeze_two_axes", [node("Unsqueeze", ["a", "axes"], ["y"])],
     {"a": floats((3, 4), 113), "axes": np.array([1, 3], dtype=np.int64)}, [("y", "float")])
# A scalar unsqueezed at axis 0 is a tensor of rank ONE holding one element, which is the case the
# declaration has to state: a trial run cannot tell that apart from a scalar.
emit("unsqueeze_scalar", [node("Unsqueeze", ["a", "axes"], ["y"])],
     {"a": np.array(2.5, dtype=np.float32), "axes": np.array([0], dtype=np.int64)}, [("y", "float")],
     declare={"y": [1]})

emit("reshape_flat", [node("Reshape", ["a", "s"], ["y"])],
     {"a": floats((2, 3, 4), 114), "s": np.array([24], dtype=np.int64)}, [("y", "float")])
emit("reshape_infer", [node("Reshape", ["a", "s"], ["y"])],
     {"a": floats((2, 3, 4), 115), "s": np.array([4, -1], dtype=np.int64)}, [("y", "float")])
emit("reshape_keep_dimension", [node("Reshape", ["a", "s"], ["y"])],
     {"a": floats((2, 3, 4), 116), "s": np.array([0, 12], dtype=np.int64)}, [("y", "float")])
emit("reshape_empty", [node("Reshape", ["a", "s"], ["y"])],
     {"a": floats((2, 0, 4), 117), "s": np.array([0, -1], dtype=np.int64)}, [("y", "float")])
emit("reshape_allowzero", [node("Reshape", ["a", "s"], ["y"], allowzero=1)],
     {"a": floats((0, 5), 118), "s": np.array([0, 5], dtype=np.int64)}, [("y", "float")])

emit("transpose_default", [node("Transpose", ["a"], ["y"])], {"a": floats((2, 3, 4), 119)}, [("y", "float")])
emit("transpose_heads", [node("Transpose", ["a"], ["y"], perm=[0, 2, 1, 3])],
     {"a": floats((2, 5, 4, 3), 120)}, [("y", "float")])
emit("transpose_keys", [node("Transpose", ["a"], ["y"], perm=[0, 1, 3, 2])],
     {"a": floats((2, 4, 5, 3), 121)}, [("y", "float")])
emit("transpose_three", [node("Transpose", ["a"], ["y"], perm=[0, 2, 1])],
     {"a": floats((2, 3, 4), 122)}, [("y", "float")])
emit("transpose_empty", [node("Transpose", ["a"], ["y"], perm=[1, 0])],
     {"a": floats((0, 4), 123)}, [("y", "float")])

emit("slice_basic", [node("Slice", ["a", "starts", "ends"], ["y"])],
     {"a": floats((6, 5), 124), "starts": np.array([1, 0], dtype=np.int64),
      "ends": np.array([4, 3], dtype=np.int64)}, [("y", "float")])
emit("slice_to_the_end", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((4, 9), 125), "starts": np.array([3], dtype=np.int64),
      "ends": np.array([9223372036854775807], dtype=np.int64),
      "axes": np.array([1], dtype=np.int64)}, [("y", "float")])
emit("slice_negative_bounds", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((7, 3), 126), "starts": np.array([-4], dtype=np.int64),
      "ends": np.array([-1], dtype=np.int64), "axes": np.array([0], dtype=np.int64)}, [("y", "float")])
emit("slice_step_two", [node("Slice", ["a", "starts", "ends", "axes", "steps"], ["y"])],
     {"a": floats((9, 2), 127), "starts": np.array([0], dtype=np.int64),
      "ends": np.array([9], dtype=np.int64), "axes": np.array([0], dtype=np.int64),
      "steps": np.array([2], dtype=np.int64)}, [("y", "float")])
emit("slice_reversed", [node("Slice", ["a", "starts", "ends", "axes", "steps"], ["y"])],
     {"a": floats((6, 2), 128), "starts": np.array([-1], dtype=np.int64),
      "ends": np.array([-9223372036854775807], dtype=np.int64), "axes": np.array([0], dtype=np.int64),
      "steps": np.array([-1], dtype=np.int64)}, [("y", "float")])
emit("slice_two_axes", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((4, 6, 3), 129), "starts": np.array([1, 2], dtype=np.int64),
      "ends": np.array([3, 5], dtype=np.int64), "axes": np.array([0, 1], dtype=np.int64)}, [("y", "float")])
emit("slice_rotary_halves", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((1, 4, 3, 8), 130), "starts": np.array([4], dtype=np.int64),
      "ends": np.array([8], dtype=np.int64), "axes": np.array([-1], dtype=np.int64)}, [("y", "float")])
emit("slice_empty_result", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((5, 3), 131), "starts": np.array([3], dtype=np.int64),
      "ends": np.array([3], dtype=np.int64), "axes": np.array([0], dtype=np.int64)}, [("y", "float")])
emit("slice_of_empty", [node("Slice", ["a", "starts", "ends", "axes"], ["y"])],
     {"a": floats((0, 4), 132), "starts": np.array([0], dtype=np.int64),
      "ends": np.array([9223372036854775807], dtype=np.int64),
      "axes": np.array([1], dtype=np.int64)}, [("y", "float")])

emit("expand_tail", [node("Expand", ["a", "s"], ["y"])],
     {"a": floats((3, 1), 133), "s": np.array([3, 4], dtype=np.int64)}, [("y", "float")])
emit("expand_new_rank", [node("Expand", ["a", "s"], ["y"])],
     {"a": floats((4,), 134), "s": np.array([2, 3, 4], dtype=np.int64)}, [("y", "float")])
emit("expand_bidirectional", [node("Expand", ["a", "s"], ["y"])],
     {"a": floats((2, 1, 5), 135), "s": np.array([3, 1], dtype=np.int64)}, [("y", "float")])

emit("constantofshape_float", [node("ConstantOfShape", ["s"], ["y"],
     value=numpy_helper.from_array(np.array([1.5], dtype=np.float32), "v"))],
     {"s": np.array([2, 3], dtype=np.int64)}, [("y", "float")])
emit("constantofshape_zero_default", [node("ConstantOfShape", ["s"], ["y"])],
     {"s": np.array([4], dtype=np.int64)}, [("y", "float")])
emit("constantofshape_int64", [node("ConstantOfShape", ["s"], ["y"],
     value=numpy_helper.from_array(np.array([7], dtype=np.int64), "v"))],
     {"s": np.array([2, 2], dtype=np.int64)}, [("y", "int64")])
emit("constantofshape_empty", [node("ConstantOfShape", ["s"], ["y"])],
     {"s": np.array([0, 3], dtype=np.int64)}, [("y", "float")])

emit("range_int64", [node("Range", ["start", "limit", "delta"], ["y"])],
     {"start": np.array(2, dtype=np.int64), "limit": np.array(11, dtype=np.int64),
      "delta": np.array(3, dtype=np.int64)}, [("y", "int64")])
emit("range_int64_empty", [node("Range", ["start", "limit", "delta"], ["y"])],
     {"start": np.array(5, dtype=np.int64), "limit": np.array(5, dtype=np.int64),
      "delta": np.array(1, dtype=np.int64)}, [("y", "int64")])
emit("range_float", [node("Range", ["start", "limit", "delta"], ["y"])],
     {"start": np.array(0.5, dtype=np.float32), "limit": np.array(3.0, dtype=np.float32),
      "delta": np.array(0.75, dtype=np.float32)}, [("y", "float")])
emit("range_backwards", [node("Range", ["start", "limit", "delta"], ["y"])],
     {"start": np.array(6, dtype=np.int64), "limit": np.array(1, dtype=np.int64),
      "delta": np.array(-2, dtype=np.int64)}, [("y", "int64")])

emit("trilu_upper", [node("Trilu", ["a"], ["y"], upper=1)], {"a": floats((5, 5), 136)}, [("y", "float")])
emit("trilu_lower", [node("Trilu", ["a"], ["y"], upper=0)], {"a": floats((5, 5), 137)}, [("y", "float")])
emit("trilu_upper_k", [node("Trilu", ["a", "k"], ["y"], upper=1)],
     {"a": floats((6, 6), 138), "k": np.array(1, dtype=np.int64)}, [("y", "float")])
emit("trilu_lower_negative_k", [node("Trilu", ["a", "k"], ["y"], upper=0)],
     {"a": floats((6, 6), 139), "k": np.array(-2, dtype=np.int64)}, [("y", "float")])
emit("trilu_batched", [node("Trilu", ["a", "k"], ["y"], upper=1)],
     {"a": floats((2, 3, 4, 4), 140), "k": np.array(0, dtype=np.int64)}, [("y", "float")])
emit("trilu_rectangular", [node("Trilu", ["a"], ["y"], upper=1)],
     {"a": floats((3, 7), 141)}, [("y", "float")])

emit("cast_float_to_int64", [node("Cast", ["a"], ["y"], to=TensorProto.INT64)],
     {"a": np.array([-2.7, -0.5, 0.0, 0.5, 2.7, 9.99], dtype=np.float32)}, [("y", "int64")])
emit("cast_int64_to_float", [node("Cast", ["a"], ["y"], to=TensorProto.FLOAT)],
     {"a": ints((3, 4), 142, -100, 100)}, [("y", "float")])
emit("cast_bool_to_float", [node("Cast", ["a"], ["y"], to=TensorProto.FLOAT)],
     {"a": bools((2, 5), 143)}, [("y", "float")])
emit("cast_float_to_bool", [node("Cast", ["a"], ["y"], to=TensorProto.BOOL)],
     {"a": np.array([-1.0, 0.0, 0.5], dtype=np.float32)}, [("y", "bool")])
emit("cast_int64_to_int32", [node("Cast", ["a"], ["y"], to=TensorProto.INT32)],
     {"a": ints((4,), 144, -50, 50)}, [("y", "int32")])
emit("cast_int32_to_int64", [node("Cast", ["a"], ["y"], to=TensorProto.INT64)],
     {"a": ints((4,), 145, -50, 50).astype(np.int32)}, [("y", "int64")])

emit("constant_tensor", [node("Constant", [], ["c"],
     value=numpy_helper.from_array(np.array([[1.0, 2.0], [3.0, 4.0]], dtype=np.float32), "v")),
     node("Add", ["a", "c"], ["y"])],
     {"a": floats((2, 2), 146)}, [("y", "float")])
emit("constant_int", [node("Constant", [], ["c"], value_int=5),
     node("Add", ["a", "c"], ["y"])],
     {"a": ints((3,), 147)}, [("y", "int64")])
emit("identity_float", [node("Identity", ["a"], ["y"])], {"a": floats((2, 3), 148)}, [("y", "float")])

# A 16-bit float WEIGHT, which the engine widens to 32 bits as it reads it. Widening is exact, so the two
# engines agree exactly: onnxruntime widens it at the Cast and the engine widened it at the load.
emit("cast_float16_weight",
     [node("Cast", ["w"], ["widened"], to=TensorProto.FLOAT), node("Add", ["a", "widened"], ["y"])],
     {"a": floats((3, 4), 149)}, [("y", "float")],
     initializers={"w": floats((3, 4), 168).astype(np.float16)},
     note="A 16-bit float weight, which the engine widens as it reads it.")

# ---------------------------------------------------------------- sub-graphs shaped like a decoder's

emit("subgraph_rms_norm",
     [node("Pow", ["x", "two"], ["sq"]),
      node("ReduceMean", ["sq"], ["mean"], axes=[-1], keepdims=1),
      node("Add", ["mean", "eps"], ["shifted"]),
      node("Sqrt", ["shifted"], ["rms"]),
      node("Div", ["x", "rms"], ["normalized"]),
      node("Mul", ["normalized", "weight"], ["y"])],
     {"x": floats((1, 7, 64), 150)},
     [("y", "float")],
     initializers={
         "two": np.array(2.0, dtype=np.float32),
         "eps": np.array(1e-6, dtype=np.float32),
         "weight": positives((64,), 151, 0.5, 1.5)},
     note="Root-mean-square normalization, decomposed exactly as the decoder graphs decompose it.")

emit("subgraph_rotary",
     [node("Slice", ["x", "zero", "half", "last"], ["front"]),
      node("Slice", ["x", "half", "big", "last"], ["back"]),
      node("Neg", ["back"], ["negated"]),
      node("Concat", ["negated", "front"], ["rotated"], axis=-1),
      node("Mul", ["x", "cos"], ["straight"]),
      node("Mul", ["rotated", "sin"], ["turned"]),
      node("Add", ["straight", "turned"], ["y"])],
     {"x": floats((1, 4, 5, 16), 152), "cos": floats((1, 1, 5, 16), 153), "sin": floats((1, 1, 5, 16), 154)},
     [("y", "float")],
     initializers={
         "zero": np.array([0], dtype=np.int64),
         "half": np.array([8], dtype=np.int64),
         "big": np.array([9223372036854775807], dtype=np.int64),
         "last": np.array([-1], dtype=np.int64)},
     note="A rotary position embedding, decomposed into slices, a negation and a concatenation.")

emit("subgraph_attention",
     [node("Transpose", ["q"], ["qt"], perm=[0, 2, 1, 3]),
      node("Transpose", ["k"], ["kt"], perm=[0, 2, 3, 1]),
      node("Transpose", ["v"], ["vt"], perm=[0, 2, 1, 3]),
      node("MatMul", ["qt", "kt"], ["scores"]),
      node("Div", ["scores", "scale"], ["scaled"]),
      node("Trilu", ["ones"], ["keep"], upper=1),
      node("Equal", ["keep", "zero"], ["blocked"]),
      node("Where", ["blocked", "minus_infinity", "scaled"], ["masked"]),
      node("Softmax", ["masked"], ["weights"], axis=-1),
      node("MatMul", ["weights", "vt"], ["context"]),
      node("Transpose", ["context"], ["back"], perm=[0, 2, 1, 3]),
      node("Reshape", ["back", "flat"], ["y"])],
     {"q": floats((1, 6, 4, 8), 155), "k": floats((1, 6, 4, 8), 156), "v": floats((1, 6, 4, 8), 157)},
     [("y", "float")],
     initializers={
         "scale": np.array(np.sqrt(8.0), dtype=np.float32),
         "ones": np.ones((6, 6), dtype=np.float32),
         "zero": np.array(0.0, dtype=np.float32),
         "minus_infinity": np.array(-np.inf, dtype=np.float32),
         "flat": np.array([1, 6, 32], dtype=np.int64)},
     note="Causal multi-head attention, decomposed the way the decoder graphs decompose it.")

emit("subgraph_dynamic_shapes",
     [node("Shape", ["x"], ["shape"]),
      node("Gather", ["shape", "one"], ["length"], axis=0),
      node("Range", ["start", "length", "step"], ["positions"]),
      node("Unsqueeze", ["positions", "front"], ["row"]),
      node("Cast", ["row"], ["floats"], to=TensorProto.FLOAT),
      node("Concat", ["shape", "extra"], ["bigger"], axis=0),
      node("ConstantOfShape", ["bigger"], ["filled"]),
      node("Add", ["filled", "floats"], ["y"])],
     {"x": floats((3, 5), 158)},
     [("y", "float")],
     initializers={
         "one": np.array(1, dtype=np.int64),
         "start": np.array(0, dtype=np.int64),
         "step": np.array(1, dtype=np.int64),
         "front": np.array([0], dtype=np.int64),
         "extra": np.array([1], dtype=np.int64)},
     note="Shape arithmetic on int64 tensors, which is how a graph resolves a length it was not told.")

emit("subgraph_feed_forward",
     [node("MatMul", ["x", "up"], ["projected"]),
      node("Sigmoid", ["projected"], ["gate"]),
      node("Mul", ["projected", "gate"], ["activated"]),
      node("MatMul", ["activated", "down"], ["y"])],
     {"x": floats((1, 5, 32), 159)},
     [("y", "float")],
     initializers={"up": floats((32, 64), 160, 0.2), "down": floats((64, 32), 161, 0.2)},
     note="A gated feed-forward block: two constant weights and a sigmoid-weighted gate.")

emit("subgraph_empty_step",
     [node("Concat", ["past", "fresh"], ["present"], axis=2),
      node("Shape", ["present"], ["shape"]),
      node("Gather", ["shape", "two"], ["length"], axis=0),
      node("Unsqueeze", ["length", "front"], ["row"]),
      node("Cast", ["row"], ["asfloat"], to=TensorProto.FLOAT),
      node("ReduceSum", ["present", "axes"], ["total"], keepdims=0),
      node("Mul", ["total", "asfloat"], ["y"])],
     {"past": floats((1, 2, 0, 4), 162), "fresh": floats((1, 2, 3, 4), 163)},
     [("y", "float"), ("present", "float")],
     initializers={
         "two": np.array(2, dtype=np.int64),
         "front": np.array([0], dtype=np.int64),
         "axes": np.array([], dtype=np.int64)},
     note="A cached decode step's shape: an empty past concatenated onto a fresh key.")

# ---------------------------------------------------------------- 8-bit element types through Cast

emit("cast_float_to_uint8",
     [node("Cast", ["a"], ["q"], to=TensorProto.UINT8),
      node("Cast", ["q"], ["y"], to=TensorProto.INT32)],
     {"a": np.array([-1.5, -0.4, 0.0, 0.5, 1.5, 200.0, 254.6], dtype=np.float32)},
     [("y", "int32")],
     note="Cast to an unsigned byte and back, which is how the quantized types are seen from outside.")

emit("cast_float_to_int8",
     [node("Cast", ["a"], ["q"], to=TensorProto.INT8),
      node("Cast", ["q"], ["y"], to=TensorProto.INT32)],
     {"a": np.array([-3.7, -1.0, 0.0, 1.0, 3.7, 100.2], dtype=np.float32)},
     [("y", "int32")])

emit("cast_uint8_weight_to_float",
     [node("Cast", ["w"], ["y"], to=TensorProto.FLOAT), node("Mul", ["y", "a"], ["z"])],
     {"a": floats((6,), 700)},
     [("z", "float")],
     initializers={"w": np.array([0, 1, 7, 128, 200, 255], dtype=np.uint8)},
     note="An unsigned 8-bit WEIGHT read as it lies and widened by a Cast.")

emit("cast_int8_weight_to_float",
     [node("Cast", ["w"], ["y"], to=TensorProto.FLOAT), node("Mul", ["y", "a"], ["z"])],
     {"a": floats((6,), 701)},
     [("z", "float")],
     initializers={"w": np.array([-128, -7, -1, 0, 1, 127], dtype=np.int8)})

# ---------------------------------------------------------------- the dynamic 8-bit path

def dynamic_quantize(name, values, note=None):
    """A DynamicQuantizeLinear whose three outputs are all made visible.

    Two of them are unsigned bytes, which no tensor handed across the engine's own boundary carries, so they
    are widened by a Cast on the way out - which is exactly what the graph would do with them anyway.
    """
    emit(name,
         [node("DynamicQuantizeLinear", ["a"], ["q", "scale", "zero_point"]),
          node("Cast", ["q"], ["y"], to=TensorProto.INT32),
          node("Cast", ["zero_point"], ["zp"], to=TensorProto.INT32)],
         {"a": values},
         [("y", "int32"), ("scale", "float"), ("zp", "int32")],
         note=note)


dynamic_quantize("dynamic_quantize_linear", floats((3, 5), 710, 2.0))
dynamic_quantize("dynamic_quantize_linear_positive", positives((4, 4), 711, 0.5, 6.0),
                 note="Every value positive, so the range is stretched to include nought.")
dynamic_quantize("dynamic_quantize_linear_negative", -positives((4, 4), 712, 0.5, 6.0),
                 note="Every value negative, so the range is stretched the other way.")
dynamic_quantize("dynamic_quantize_linear_zeros", np.zeros((2, 3), dtype=np.float32),
                 note="Nothing to quantize: the range is nought and the scale must not be a division by it.")
dynamic_quantize("dynamic_quantize_linear_ties",
                 np.array([[-1.0, -0.5, 0.0, 0.5, 1.0],
                           [(k + 0.5) * (2.0 / 255.0) - 1.0 for k in range(5)]], dtype=np.float32),
                 note="Values that land halfway between two integers, where rounding to even is the answer.")
dynamic_quantize("dynamic_quantize_linear_single", np.array([2.5], dtype=np.float32),
                 note="One value, so the range runs from nought to it.")


def quantize_int8(values, signed):
    """Quantizes a [k, n] float matrix to bytes with one scale and one zero point, as the tools do."""
    low, high = (-128, 127) if signed else (0, 255)
    smallest = min(float(values.min()), 0.0)
    largest = max(float(values.max()), 0.0)
    scale = (largest - smallest) / (high - low) if largest != smallest else 1.0
    zero_point = int(np.round(np.clip(low - smallest / scale, low, high)))
    quantized = np.clip(np.round(values / scale) + zero_point, low, high)
    return quantized.astype(np.int8 if signed else np.uint8), np.float32(scale), zero_point


def matmul_integer(name, a, weights, signed, per_column=False, zero_points=True, note=None):
    """A DynamicQuantizeLinear feeding a MatMulInteger, which is the shape the dynamic reduction writes.

    The integer product is handed out as it is - a 32-bit integer tensor, which the engine does carry - and
    the float the graph really wants is the same product through the Cast and the two Mul nodes that the
    quantizer emits after it.
    """
    low, high = (-128, 127) if signed else (0, 255)
    k, n = weights.shape
    if per_column:
        quantized = np.zeros((k, n), dtype=np.int8 if signed else np.uint8)
        scales = np.zeros((n,), dtype=np.float32)
        points = np.zeros((n,), dtype=np.int8 if signed else np.uint8)
        for column in range(n):
            column_values, column_scale, column_zero = quantize_int8(weights[:, column], signed)
            quantized[:, column] = column_values
            scales[column] = column_scale
            points[column] = column_zero
    else:
        quantized, scale, zero_point = quantize_int8(weights, signed)
        scales = np.array(scale, dtype=np.float32)
        points = np.array(zero_point, dtype=np.int8 if signed else np.uint8)

    _ = (low, high)
    nodes = [node("DynamicQuantizeLinear", ["a"], ["a_q", "a_scale", "a_zp"])]
    inputs = ["a_q", "w_q"] + (["a_zp", "w_zp"] if zero_points else [])
    nodes.append(node("MatMulInteger", inputs, ["product"]))
    nodes.append(node("Cast", ["product"], ["as_float"], to=TensorProto.FLOAT))
    nodes.append(node("Mul", ["a_scale", "w_scale"], ["both_scales"]))
    nodes.append(node("Mul", ["as_float", "both_scales"], ["y"]))

    initializers = {"w_q": quantized, "w_scale": scales}
    if zero_points:
        initializers["w_zp"] = points

    emit(name, nodes, {"a": a}, [("product", "int32"), ("y", "float")],
         initializers=initializers, note=note)


matmul_integer("matmul_integer_signed_weight", floats((2, 6), 720), floats((6, 4), 721), True,
               note="An unsigned activation against a signed weight, which is what the reduction writes.")
matmul_integer("matmul_integer_unsigned_weight", floats((2, 6), 722), floats((6, 4), 723), False)
matmul_integer("matmul_integer_per_column", floats((3, 8), 724), floats((8, 5), 725), True,
               per_column=True,
               note="One zero point and one scale for each column, which per-channel quantization writes.")
matmul_integer("matmul_integer_no_zero_points", floats((2, 6), 726), floats((6, 3), 727), True,
               zero_points=False,
               note="The zero points left out, where the specification says they are nought.")
matmul_integer("matmul_integer_batched", floats((2, 3, 6), 728), floats((6, 4), 729), True)
matmul_integer("matmul_integer_wide", floats((1, 40), 730), floats((40, 33), 731), True,
               note="A reduction longer than one vector's worth, so the wide path has a tail to finish.")

# ---------------------------------------------------------------- the block-quantized matrix multiply


def pack_block_weights(quantized, bits):
    """Packs [n, k_blocks, block_size] quantized values into the operator's [n, k_blocks, blob_size] bytes."""
    n, blocks, block_size = quantized.shape
    if bits == 8:
        return quantized.astype(np.uint8)

    packed = np.zeros((n, blocks, block_size // 2), dtype=np.uint8)
    packed |= (quantized[:, :, 0::2] & 0x0F).astype(np.uint8)
    packed |= ((quantized[:, :, 1::2] & 0x0F) << 4).astype(np.uint8)
    return packed


def pack_block_zero_points(points, bits):
    """Packs [n, k_blocks] zero points the same way the values are packed."""
    n, blocks = points.shape
    if bits == 8:
        return points.astype(np.uint8)

    width = (blocks + 1) // 2
    packed = np.zeros((n, width), dtype=np.uint8)
    packed[:, : (blocks + 1) // 2] |= (points[:, 0::2] & 0x0F).astype(np.uint8)
    if blocks > 1:
        packed[:, : blocks // 2] |= ((points[:, 1::2] & 0x0F) << 4).astype(np.uint8)
    return packed


def matmul_nbits(name, salt, m, k, n, bits, block_size, zero_points="packed", bias=False,
                 accuracy_level=None, flat=False, note=None, tolerance=None):
    """One MatMulNBits over values and scales made up on the spot.

    The quantized values are RANDOM rather than the result of quantizing something, which is a harder test
    than a real weight would be: nothing about them is smooth, so an unpack that takes a nibble from the wrong
    half of a byte cannot come out nearly right.
    """
    blocks = (k + block_size - 1) // block_size
    blob = block_size * bits // 8
    generator = rng(salt)
    values = generator.integers(0, 1 << bits, size=(n, blocks, block_size)).astype(np.uint8)
    scales = (generator.random((n, blocks)).astype(np.float32) * np.float32(0.2)
              + np.float32(0.01)).astype(np.float32)
    packed = pack_block_weights(values, bits)
    assert packed.shape == (n, blocks, blob)

    initializers = {"b": packed, "scales": scales.reshape(-1) if flat else scales}
    inputs = ["a", "b", "scales"]
    if zero_points == "packed":
        points = generator.integers(0, 1 << bits, size=(n, blocks)).astype(np.uint8)
        initializers["zero_points"] = pack_block_zero_points(points, bits)
        inputs.append("zero_points")
    elif zero_points == "float":
        points = generator.integers(0, 1 << bits, size=(n, blocks)).astype(np.float32)
        initializers["zero_points"] = points
        inputs.append("zero_points")

    if bias:
        while len(inputs) < 5:
            inputs.append("")
        inputs.append("bias")
        initializers["bias"] = floats((n,), salt + 1)

    attributes = {"K": k, "N": n, "bits": bits, "block_size": block_size}
    if accuracy_level is not None:
        attributes["accuracy_level"] = accuracy_level

    emit(name, [node("MatMulNBits", inputs, ["y"], domain=CONTRIB_DOMAIN, **attributes)],
         {"a": floats((m, k), salt + 2)}, [("y", "float")],
         initializers=initializers, opset=21, contrib=True, tolerance=tolerance, note=note)


for block in (16, 32, 64, 128, 256):
    matmul_nbits("matmul_nbits_4bit_block%d" % block, 740 + block, 2, 256, 6, 4, block)

matmul_nbits("matmul_nbits_4bit_symmetric", 760, 2, 64, 5, 4, 32, zero_points=None,
             note="No zero points, where the operator says the middle of the range is meant.")
matmul_nbits("matmul_nbits_4bit_float_zero_points", 761, 2, 64, 5, 4, 32, zero_points="float",
             note="Zero points stated as floats rather than packed into nibbles.")
matmul_nbits("matmul_nbits_4bit_ragged", 762, 2, 70, 5, 4, 32,
             note="A reduction that is not a whole number of blocks, so the last block is short.")
matmul_nbits("matmul_nbits_4bit_bias", 763, 2, 64, 5, 4, 32, bias=True)
matmul_nbits("matmul_nbits_4bit_accuracy_level", 764, 2, 64, 5, 4, 32, accuracy_level=4,
             tolerance=1e-2,
             note="accuracy_level 4 asks a runtime to quantize the ACTIVATIONS to 8-bit integers as well, and"
                  " onnxruntime takes the option while this engine keeps them in floats. The two therefore"
                  " differ by far more than any other case here - the measured figure is about three"
                  " thousandths - and the bar is the plan's own tolerance for a quantized graph. This case"
                  " exists to PIN that difference rather than to hide it.")
matmul_nbits("matmul_nbits_4bit_flat_scales", 765, 2, 64, 5, 4, 32, flat=True,
             note="Scales as one long list rather than a row per column, which older models write.")
matmul_nbits("matmul_nbits_4bit_vector", 766, 1, 128, 9, 4, 32,
             note="One row of input, which is the shape a decode step really runs.")
matmul_nbits("matmul_nbits_4bit_wide", 767, 3, 128, 40, 4, 128)

for block in (16, 32, 128):
    matmul_nbits("matmul_nbits_8bit_block%d" % block, 780 + block, 2, 256, 6, 8, block)

matmul_nbits("matmul_nbits_8bit_symmetric", 800, 2, 64, 5, 8, 32, zero_points=None)
matmul_nbits("matmul_nbits_8bit_ragged", 801, 2, 70, 5, 8, 32)
matmul_nbits("matmul_nbits_8bit_bias", 802, 2, 64, 5, 8, 32, bias=True)
matmul_nbits("matmul_nbits_8bit_vector", 803, 1, 128, 9, 8, 128)

# ---------------------------------------------------------------- the two simplified normalizations

emit("simplified_layer_normalization",
     [node("SimplifiedLayerNormalization", ["x", "gamma"], ["y"], axis=-1, epsilon=1e-5, stash_type=1)],
     {"x": floats((2, 3, 8), 810)},
     [("y", "float")],
     initializers={"gamma": positives((8,), 811, 0.5, 1.5)},
     opset=21, check=False,
     note="A runtime's own operator that lives in the DEFAULT domain, which is where the builders put it.")

emit("simplified_layer_normalization_axis1",
     [node("SimplifiedLayerNormalization", ["x", "gamma"], ["y"], axis=1, epsilon=1e-3, stash_type=1)],
     {"x": floats((2, 4, 3), 812)},
     [("y", "float")],
     initializers={"gamma": positives((4, 3), 813, 0.5, 1.5)},
     opset=21, check=False,
     note="Everything from the axis on is one row, so this normalizes twelve values at a time.")

emit("simplified_layer_normalization_inv_std",
     [node("SimplifiedLayerNormalization", ["x", "gamma"], ["y", "inv_std"],
           axis=-1, epsilon=1e-5, stash_type=1)],
     {"x": floats((2, 6), 814)},
     [("y", "float"), ("inv_std", "float")],
     initializers={"gamma": positives((6,), 815, 0.5, 1.5)},
     opset=21, check=False)


def skip_simplified(name, outputs, bias=False, shape=(2, 3, 8), salt=820, note=None):
    names = ["output", "mean", "inv_std_var", "input_skip_bias_sum"][:outputs]
    wanted = [(names[0], "float")]
    if outputs == 4:
        wanted = [("output", "float"), ("mean", "float"), ("inv_std_var", "float"),
                  ("input_skip_bias_sum", "float")]

    inputs = ["input", "skip", "gamma"] + (["bias"] if bias else [])
    initializers = {"gamma": positives((shape[-1],), salt + 1, 0.5, 1.5)}
    if bias:
        initializers["bias"] = floats((shape[-1],), salt + 2, 0.3)

    emit(name,
         [node("SkipSimplifiedLayerNormalization", inputs, names, domain=CONTRIB_DOMAIN, epsilon=1e-5)],
         {"input": floats(shape, salt), "skip": floats(shape, salt + 3)},
         wanted,
         initializers=initializers, opset=21, contrib=True, note=note)


skip_simplified("skip_simplified_layer_normalization", 1,
                note="Only the normalized tensor is asked for.")
skip_simplified("skip_simplified_layer_normalization_all", 4, salt=830,
                note="All four outputs, of which the fourth - the residual sum - is the one the graphs wire"
                     " into the next block.")
skip_simplified("skip_simplified_layer_normalization_bias", 4, bias=True, salt=840)
skip_simplified("skip_simplified_layer_normalization_two_dimensional", 4, shape=(4, 8), salt=850)

# ---------------------------------------------------------------- grouped-query attention


def gqa(name, salt, batch=1, sequence=1, past=0, heads=2, kv_heads=2, head_size=16, rotary=True,
        interleaved=False, packed=True, scale=None, rotary_width=None, causal=None, note=None):
    """One GroupQueryAttention node with the inputs the model builders wire into it.

    The caches, the lengths and the total are graph INPUTS rather than constants, because that is how a
    decoder drives it: the same loaded graph runs a prompt and then every cached step after it.
    """
    total = sequence if past == 0 else past + sequence
    width = rotary_width if rotary_width is not None else head_size // 2
    positions = 64

    inputs = {}
    if packed:
        inputs["query"] = floats((batch, sequence, (heads + 2 * kv_heads) * head_size), salt)
        names = ["query", "", ""]
    else:
        inputs["query"] = floats((batch, sequence, heads * head_size), salt)
        inputs["key"] = floats((batch, sequence, kv_heads * head_size), salt + 1)
        inputs["value"] = floats((batch, sequence, kv_heads * head_size), salt + 2)
        names = ["query", "key", "value"]

    inputs["past_key"] = floats((batch, kv_heads, past, head_size), salt + 3)
    inputs["past_value"] = floats((batch, kv_heads, past, head_size), salt + 4)
    names += ["past_key", "past_value", "seqlens_k", "total_sequence_length"]
    inputs["seqlens_k"] = np.full((batch,), total - 1, dtype=np.int32)
    inputs["total_sequence_length"] = np.array([total], dtype=np.int32)

    initializers = {}
    if rotary:
        names += ["cos_cache", "sin_cache"]
        angles = np.arange(positions, dtype=np.float32)[:, None] / (
            10000.0 ** (np.arange(width, dtype=np.float32)[None, :] * 2.0 / (width * 2)))
        initializers["cos_cache"] = np.cos(angles).astype(np.float32)
        initializers["sin_cache"] = np.sin(angles).astype(np.float32)

    attributes = {"num_heads": heads, "kv_num_heads": kv_heads,
                  "do_rotary": 1 if rotary else 0, "rotary_interleaved": 1 if interleaved else 0}
    if scale is not None:
        attributes["scale"] = scale
    if causal is not None:
        attributes["causal"] = causal

    emit(name,
         [node("GroupQueryAttention", names, ["output", "present_key", "present_value"],
               domain=CONTRIB_DOMAIN, **attributes)],
         inputs,
         [("output", "float"), ("present_key", "float"), ("present_value", "float")],
         initializers=initializers, opset=21, contrib=True, note=note)


gqa("gqa_prompt_packed", 860, sequence=4, past=0,
    note="A first prompt: nothing cached, the mask starts at nought, and the packed query holds all three.")
gqa("gqa_decode_packed", 862, sequence=1, past=4,
    note="A cached step: one new position against four cached ones.")
gqa("gqa_decode_longer_cache", 864, sequence=1, past=6)
gqa("gqa_prompt_separate", 866, sequence=3, past=0, packed=False,
    note="The query, key and value as three tensors instead of one.")
gqa("gqa_decode_separate", 868, sequence=1, past=3, packed=False)
gqa("gqa_grouped_heads", 870, sequence=1, past=3, heads=4, kv_heads=2,
    note="Four query heads sharing two cached heads, which is what 'grouped' means.")
gqa("gqa_grouped_prompt", 872, sequence=3, past=0, heads=6, kv_heads=2)
gqa("gqa_no_rotary", 874, sequence=2, past=2, rotary=False,
    note="No rotary embedding, so the caches are not there either.")
gqa("gqa_interleaved_rotary", 876, sequence=2, past=2, interleaved=True,
    note="The turned pairs are neighbours rather than halves, which is the other rotary layout.")
gqa("gqa_partial_rotary", 878, sequence=2, past=2, head_size=32, rotary_width=8,
    note="Only the first sixteen of thirty-two values are turned; the rest pass through.")
gqa("gqa_explicit_scale", 880, sequence=2, past=2, scale=0.125)
gqa("gqa_batch_two", 882, batch=2, sequence=3, past=0)
gqa("gqa_not_causal", 884, sequence=3, past=0, causal=0,
    note="Every position sees every other one, which is what causal=0 asks for.")
gqa("gqa_prompt_long", 886, sequence=8, past=0, heads=2, kv_heads=1)


# ---------------------------------------------------------------- weights in a side file

def emit_external(name, nodes, inputs, outputs, initializers, opset=14, note=None):
    """The same as emit(), except that the weights are written to a side file beside the graph.

    A model whose weights will not fit inside a two-gigabyte protocol-buffer message names them in a file of
    its own and carries offsets into it. This is the small version of that, so the loader's side-file path -
    and the (logical name to path) form of loading, where the side file is not where the graph says it is -
    are both exercised offline.
    """
    folder = os.path.join(HERE, name)
    os.makedirs(folder, exist_ok=True)

    declared = resolve(name, nodes, inputs, outputs, initializers, opset, folder, None)
    model = build(name, nodes, inputs, declared, initializers, opset)
    onnx.checker.check_model(model)
    onnx.save(model, os.path.join(folder, "model.onnx"),
              save_as_external_data=True, all_tensors_to_one_file=True,
              location="model.onnx.data", size_threshold=0, convert_attribute=False)

    produced = ort.InferenceSession(
        os.path.join(folder, "model.onnx"),
        providers=["CPUExecutionProvider"]).run(None, {k: v for k, v in inputs.items()})

    write_case(folder, name, opset, inputs, outputs, produced, note, {"external": "model.onnx.data"})
    cases.append(name)
    print("%-44s SIDE FILE" % name)


emit_external("external_weights",
              [node("MatMul", ["x", "w"], ["projected"]), node("Add", ["projected", "bias"], ["y"])],
              {"x": floats((2, 12), 170)},
              [("y", "float")],
              {"w": floats((12, 20), 171), "bias": floats((20,), 172)},
              note="Its weights are in a side file, which is how a model too large for one message stores them.")


# ---------------------------------------------------------------- graphs the engine has to REFUSE

REFUSALS = os.path.join(HERE, "Refusals")
refusals = []


def refuse(name, nodes, inputs, outputs, initializers=None, opset=14, expect=None, truncate=None):
    """Writes a graph the engine is required to refuse when it loads it.

    These are NOT run through onnxruntime and NOT put through the ONNX checker: several of them are invalid
    on purpose, and what is being pinned is that the engine says so clearly rather than that the file is
    well formed.
    """
    os.makedirs(REFUSALS, exist_ok=True)
    model = build(name, nodes, inputs, outputs, initializers, opset)
    payload = model.SerializeToString()
    if truncate is not None:
        payload = payload[:truncate]

    with open(os.path.join(REFUSALS, name + ".onnx"), "wb") as handle:
        handle.write(payload)

    refusals.append({"name": name, "expect": expect})
    print("%-44s REFUSED: %s" % (name, expect))


refuse("unsupported_operator",
       [node("Relu", ["a"], ["y"])],
       {"a": floats((2, 3), 900)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])],
       expect="Relu")

refuse("old_opset",
       [node("Add", ["a", "b"], ["y"])],
       {"a": floats((2, 3), 901), "b": floats((2, 3), 902)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])],
       opset=12, expect="opset")

refuse("unknown_attribute",
       [node("Transpose", ["a"], ["y"], perm=[1, 0], mystery=3)],
       {"a": floats((2, 3), 903)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [3, 2])],
       expect="mystery")

refuse("float16_input",
       [node("Add", ["a", "b"], ["y"])],
       {},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT16, [2, 3])],
       expect="16-bit float")

refuse("double_input",
       [node("Add", ["a", "b"], ["y"])],
       {},
       [helper.make_tensor_value_info("y", TensorProto.DOUBLE, [2, 3])],
       expect="64-bit float")

refuse("missing_producer",
       [node("Add", ["a", "nowhere"], ["y"])],
       {"a": floats((2, 3), 904)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])],
       expect="nowhere")

refuse("out_of_order",
       [node("Add", ["later", "a"], ["y"]), node("Neg", ["a"], ["later"])],
       {"a": floats((2, 3), 905)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])],
       expect="produced later")

refuse("two_producers",
       [node("Neg", ["a"], ["y"]), node("Neg", ["a"], ["y"])],
       {"a": floats((2, 3), 906)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, 3])],
       expect="more than once")

refuse("truncated",
       [node("Add", ["a", "b"], ["y"])],
       {"a": floats((8, 8), 907), "b": floats((8, 8), 908)},
       [helper.make_tensor_value_info("y", TensorProto.FLOAT, [8, 8])],
       expect="could not be read", truncate=40)

# The oddly typed inputs have to be put in by hand: the helper types every input from its numpy array.
for file_name, element_type in (("double_input", TensorProto.DOUBLE), ("float16_input", TensorProto.FLOAT16)):
    typed = onnx.load(os.path.join(REFUSALS, file_name + ".onnx"))
    for name in ("a", "b"):
        typed.graph.input.append(helper.make_tensor_value_info(name, element_type, [2, 3]))
    with open(os.path.join(REFUSALS, file_name + ".onnx"), "wb") as handle:
        handle.write(typed.SerializeToString())

# A CONTRIBUTED operator the engine does not implement. The domain is accepted - several of the graphs above
# use operators from it - so what is refused here is the OPERATOR, and the message has to name the domain as
# well, because a standard operator of the same name would be a different one.
contributed = onnx.load(os.path.join(REFUSALS, "unsupported_operator.onnx"))
contributed.graph.node[0].op_type = "BiasGelu"
contributed.graph.node[0].domain = CONTRIB_DOMAIN
contributed.opset_import.append(helper.make_opsetid(CONTRIB_DOMAIN, 1))
with open(os.path.join(REFUSALS, "contrib_domain.onnx"), "wb") as handle:
    handle.write(contributed.SerializeToString())
refusals.append({"name": "contrib_domain", "expect": "com.microsoft"})
print("%-44s REFUSED: %s" % ("contrib_domain", "com.microsoft"))


def refuse_contrib(name, nodes, inputs, outputs, initializers=None, expect=None):
    """Writes a graph using a CONTRIBUTED operator that the engine is required to refuse."""
    os.makedirs(REFUSALS, exist_ok=True)
    model = build(name, nodes, inputs, outputs, initializers, 21, contrib=True)
    with open(os.path.join(REFUSALS, name + ".onnx"), "wb") as handle:
        handle.write(model.SerializeToString())

    refusals.append({"name": name, "expect": expect})
    print("%-44s REFUSED: %s" % (name, expect))


def nbits_refusal(name, expect, bits=4, block_size=32, extra_inputs=None, **attributes):
    k, n = 64, 4
    blocks = (k + block_size - 1) // block_size
    blob = block_size * bits // 8
    generator = rng(910)
    initializers = {
        "b": generator.integers(0, 256, size=(n, blocks, blob)).astype(np.uint8),
        "scales": generator.random((n, blocks)).astype(np.float32),
    }
    inputs = ["a", "b", "scales"]
    for extra, array in (extra_inputs or {}).items():
        while len(inputs) < {"zero_points": 3, "g_idx": 4, "bias": 5}[extra]:
            inputs.append("")
        inputs.append(extra)
        initializers[extra] = array

    refuse_contrib(name,
                   [node("MatMulNBits", inputs, ["y"], domain=CONTRIB_DOMAIN,
                         K=k, N=n, bits=bits, block_size=block_size, **attributes)],
                   {"a": floats((2, k), 911)},
                   [helper.make_tensor_value_info("y", TensorProto.FLOAT, [2, n])],
                   initializers=initializers, expect=expect)


nbits_refusal("nbits_two_bits", "bits", bits=2, block_size=32)
nbits_refusal("nbits_block_size", "block size", block_size=48)
nbits_refusal("nbits_group_index", "g_idx",
              extra_inputs={"g_idx": np.zeros((64,), dtype=np.int32)})


def gqa_refusal(name, expect, **attributes):
    heads, kv_heads, head_size, sequence = 2, 2, 16, 2
    settings = {"num_heads": heads, "kv_num_heads": kv_heads}
    settings.update(attributes)
    refuse_contrib(name,
                   [node("GroupQueryAttention",
                         ["query", "", "", "past_key", "past_value", "seqlens_k", "total_sequence_length"],
                         ["output", "present_key", "present_value"], domain=CONTRIB_DOMAIN, **settings)],
                   {"query": floats((1, sequence, (heads + 2 * kv_heads) * head_size), 920),
                    "past_key": floats((1, kv_heads, 2, head_size), 921),
                    "past_value": floats((1, kv_heads, 2, head_size), 922),
                    "seqlens_k": np.array([3], dtype=np.int32),
                    "total_sequence_length": np.array([4], dtype=np.int32)},
                   [helper.make_tensor_value_info("output", TensorProto.FLOAT, [1, sequence, heads * head_size]),
                    helper.make_tensor_value_info("present_key", TensorProto.FLOAT, [1, kv_heads, 4, head_size]),
                    helper.make_tensor_value_info("present_value", TensorProto.FLOAT, [1, kv_heads, 4, head_size])],
                   expect=expect)


gqa_refusal("gqa_local_window", "local_window_size", local_window_size=2)
gqa_refusal("gqa_softcap", "softcap", softcap=20.0)
gqa_refusal("gqa_smooth_softmax", "smooth_softmax", smooth_softmax=1)
gqa_refusal("gqa_quantized_cache", "kv_cache_bit_width", kv_cache_bit_width=8)

refuse("uint8_input",
       [node("Cast", ["a"], ["y"], to=TensorProto.INT32)],
       {},
       [helper.make_tensor_value_info("y", TensorProto.INT32, [2, 3])],
       expect="unsigned 8-bit")

typed = onnx.load(os.path.join(REFUSALS, "uint8_input.onnx"))
typed.graph.input.append(helper.make_tensor_value_info("a", TensorProto.UINT8, [2, 3]))
with open(os.path.join(REFUSALS, "uint8_input.onnx"), "wb") as handle:
    handle.write(typed.SerializeToString())

with open(os.path.join(HERE, "cases.json"), "w") as handle:
    json.dump({"cases": sorted(cases),
               "refusals": sorted(refusals, key=lambda entry: entry["name"])}, handle, indent=2)
    handle.write("\n")

print("\n%d cases and %d refusals written to %s" % (len(cases), len(refusals), HERE))
