#!/usr/bin/env python
# Fixture generator for the managed ONNX codec and quantizer (CodeBrix.Ollama.ModelManager, Onnx/).
#
# The .onnx files this script writes next to itself ARE THE ORACLE: they are checked in, copied to the test
# output, and compared byte for byte against what the managed port produces. The tests never run this script.
#
# Run it with the reference virtual environment's interpreter, from this folder:
#
#     ~/venvs/codebrix-ollama/bin/python generate_fixtures.py
#
# Versions it was run with (2026-09-17): onnx 1.22.0, onnxruntime 1.30.0, numpy 2.5.3, onnx_ir 1.0.0.
#
# Naming: "<model>.onnx" is an input, "<model>.<mode>.onnx" is the expected output for that mode.
#   nbits_b<bits>_bs<block_size>_<sym|asym>[_acc<level>]   weight-only block-wise -> MatMulNBits
#   dyn_<i8|u8>[_pc][_rr]                                  quantize_dynamic, weight type / per-channel /
#                                                          reduce_range
#   dyn_i8_mm                                              quantize_dynamic restricted to MatMul and Gemm,
#                                                          which is what the library itself asks for
#
# Dynamic inputs are stored AFTER onnx.shape_inference (what quantize_dynamic feeds its quantizer), because
# the managed engine does no shape inference of its own; see the "_inferred" inputs below.

import os

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper
from onnxruntime.quantization import quantize_dynamic
from onnxruntime.quantization.quant_utils import QuantType, load_model_with_shape_infer
from onnxruntime.quantization.matmul_nbits_quantizer import (
    DefaultWeightOnlyQuantConfig,
    MatMulNBitsQuantizer,
)

HERE = os.path.dirname(os.path.abspath(__file__))
OPSET = 17
IR_VERSION = 9


def deterministic_weights(rows, cols, dtype, seed):
    """A small, repeatable spread of values that crosses zero and is not symmetric about it."""
    rng = np.random.default_rng(seed)
    values = rng.uniform(-0.75, 1.25, size=(rows, cols)).astype(np.float32)
    # Pin a few extremes so the min/max search, the zero point and the clamping are all exercised.
    values[0, 0] = -1.0
    values[-1, -1] = 2.0
    if rows > 2:
        values[1, :] = 0.0
    return values.astype(dtype)


def make_model(graph):
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", OPSET)])
    model.ir_version = IR_VERSION
    model.producer_name = "codebrix-fixtures"
    onnx.checker.check_model(model)
    return model


def matmul_model(k, n, dtype, seed, name):
    elem = TensorProto.FLOAT16 if dtype == np.float16 else TensorProto.FLOAT
    weights = deterministic_weights(k, n, dtype, seed)
    initializer = numpy_helper.from_array(weights, "weight")
    node = helper.make_node("MatMul", ["input", "weight"], ["output"], name="matmul")
    graph = helper.make_graph(
        [node],
        name,
        [helper.make_tensor_value_info("input", elem, ["m", k])],
        [helper.make_tensor_value_info("output", elem, ["m", n])],
        [initializer],
    )
    return make_model(graph)


def two_matmul_model():
    first = numpy_helper.from_array(deterministic_weights(64, 8, np.float32, 11), "weight_one")
    second = numpy_helper.from_array(deterministic_weights(8, 6, np.float32, 12), "weight_two")
    nodes = [
        helper.make_node("MatMul", ["input", "weight_one"], ["hidden"], name="matmul_one"),
        helper.make_node("MatMul", ["hidden", "weight_two"], ["output"], name="matmul_two"),
    ]
    graph = helper.make_graph(
        nodes,
        "two_matmuls",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", 64])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", 6])],
        [first, second],
    )
    return make_model(graph)


def gemm_model(with_bias, trans_b, name):
    k, n = 48, 6
    weights = deterministic_weights(k, n, np.float32, 21)
    if trans_b:
        weights = np.ascontiguousarray(weights.T)
    initializers = [numpy_helper.from_array(weights, "weight")]
    inputs = ["input", "weight"]
    if with_bias:
        bias = np.linspace(-0.5, 0.5, n, dtype=np.float32)
        initializers.append(numpy_helper.from_array(bias, "bias"))
        inputs.append("bias")
    attributes = {"transB": 1} if trans_b else {}
    node = helper.make_node("Gemm", inputs, ["output"], name="gemm", **attributes)
    graph = helper.make_graph(
        [node],
        name,
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", k])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", n])],
        initializers,
    )
    return make_model(graph)


def gather_transpose_model():
    """An embedding table read by a Gather, a MatMul, and a Transpose: the shape of every real transformer.

    Two things here that no other fixture has. The Gather and the Transpose are operators ONNX Runtime's integer
    registry WOULD rewrite if it were asked for its default set of operator types, and this library asks for MatMul
    and Gemm only, so both must be carried through untouched. And the table is large enough (8,192 bytes) that the
    save-and-reload the dynamic quantizer performs on its way in moves it to a side file and reads it back, which
    leaves an explicit default data location on it in the quantized model.
    """
    table = deterministic_weights(64, 32, np.float32, 51)
    weight = deterministic_weights(32, 8, np.float32, 52)
    nodes = [
        helper.make_node("Gather", ["table", "ids"], ["gathered"], name="gather"),
        helper.make_node("MatMul", ["gathered", "weight"], ["product"], name="matmul"),
        helper.make_node("Transpose", ["product"], ["output"], name="transpose", perm=[1, 0]),
    ]
    graph = helper.make_graph(
        nodes,
        "gather_transpose",
        [helper.make_tensor_value_info("ids", TensorProto.INT64, ["m"])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, [8, "m"])],
        [numpy_helper.from_array(table, "table"), numpy_helper.from_array(weight, "weight")],
    )
    return make_model(graph)


def branch_matmuls_model():
    """Two branches that meet, whose nodes are NOT in the order the quantizers' own sort puts them in.

    ONNX Runtime sorts the nodes as it saves a quantized model, walking the graph from the initializers and inputs in
    name order, so a graph with a branch comes out in a different order from the one it went in with. A port that
    quantizes in place and never sorts writes the same nodes in the wrong order.
    """
    first = numpy_helper.from_array(deterministic_weights(32, 8, np.float32, 61), "aaa_first")
    second = numpy_helper.from_array(deterministic_weights(8, 6, np.float32, 62), "bbb_second")
    third = numpy_helper.from_array(deterministic_weights(32, 6, np.float32, 63), "ccc_third")
    nodes = [
        helper.make_node("MatMul", ["input", "aaa_first"], ["hidden"], name="matmul_first"),
        helper.make_node("MatMul", ["hidden", "bbb_second"], ["left"], name="matmul_left"),
        helper.make_node("MatMul", ["input", "ccc_third"], ["right"], name="matmul_right"),
        helper.make_node("Add", ["left", "right"], ["output"], name="add"),
    ]
    graph = helper.make_graph(
        nodes,
        "branch_matmuls",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", 32])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", 6])],
        [first, second, third],
    )
    return make_model(graph)


def unsupported_op_model():
    """No MatMul and no Gemm at all: both managed paths must leave this model alone."""
    scale = numpy_helper.from_array(np.array([2.0, 0.5, -1.5], dtype=np.float32), "scale")
    nodes = [
        helper.make_node("Mul", ["input", "scale"], ["scaled"], name="mul"),
        helper.make_node("Relu", ["scaled"], ["output"], name="relu"),
    ]
    graph = helper.make_graph(
        nodes,
        "unsupported_op",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", 3])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", 3])],
        [scale],
    )
    return make_model(graph)


def non_constant_b_model():
    """MatMul whose B is a graph input, so neither engine may quantize it."""
    nodes = [helper.make_node("MatMul", ["input", "other"], ["output"], name="matmul")]
    graph = helper.make_graph(
        nodes,
        "non_constant_b",
        [
            helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", 16]),
            helper.make_tensor_value_info("other", TensorProto.FLOAT, [16, 4]),
        ],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", 4])],
        [],
    )
    return make_model(graph)


def unknown_fields_model():
    """Carries messages the managed codec does not model: a function, a sparse initializer and training info.

    The codec must round-trip it byte for byte through its unknown-field carrier.
    """
    model = matmul_model(32, 4, np.float32, 31, "unknown_fields")
    function = helper.make_function(
        domain="codebrix.test",
        fname="ScaleByTwo",
        inputs=["x"],
        outputs=["y"],
        nodes=[
            helper.make_node("Constant", [], ["two"], value=numpy_helper.from_array(np.float32(2.0), "two")),
            helper.make_node("Mul", ["x", "two"], ["y"]),
        ],
        opset_imports=[helper.make_opsetid("", OPSET)],
    )
    model.functions.extend([function])
    model.opset_import.extend([helper.make_opsetid("codebrix.test", 1)])

    values = numpy_helper.from_array(np.array([1.5, -2.5], dtype=np.float32), "sparse_values")
    indices = numpy_helper.from_array(np.array([[0, 1], [2, 3]], dtype=np.int64), "sparse_indices")
    sparse = helper.make_sparse_tensor(values, indices, [4, 4])
    sparse.values.name = "sparse_initializer"
    model.graph.sparse_initializer.extend([sparse])

    helper.set_model_props(model, {"codebrix.note": "carried through opaquely"})
    model.graph.doc_string = "a graph the codec only partly models"
    return model


def write(model, filename):
    path = os.path.join(HERE, filename)
    onnx.save_model(model, path)
    return path


def write_external(model, filename):
    """One initializer written to a side file, per ONNX's external-data convention."""
    path = os.path.join(HERE, filename)
    side = path + ".data"
    if os.path.exists(side):
        os.remove(side)
    onnx.save_model(
        model,
        path,
        save_as_external_data=True,
        all_tensors_to_one_file=True,
        location=filename + ".data",
        size_threshold=0,
        convert_attribute=False,
    )
    return path


def nbits(input_path, bits, block_size, symmetric, accuracy_level, suffix):
    config = DefaultWeightOnlyQuantConfig(
        block_size=block_size,
        is_symmetric=symmetric,
        accuracy_level=accuracy_level,
        bits=bits,
    )
    quantizer = MatMulNBitsQuantizer(model=onnx.load(input_path), algo_config=config)
    quantizer.process()
    output = input_path[: -len(".onnx")] + "." + suffix + ".onnx"
    quantizer.model.save_model_to_file(output, False)
    return output


def dynamic(input_path, weight_type, per_channel, reduce_range, suffix):
    output = input_path[: -len(".onnx")] + "." + suffix + ".onnx"
    quantize_dynamic(
        input_path,
        output,
        per_channel=per_channel,
        reduce_range=reduce_range,
        weight_type=weight_type,
    )
    return output


def dynamic_matmul_only(input_path, suffix="dyn_i8_mm"):
    """quantize_dynamic AS THE LIBRARY CALLS IT: MatMul and Gemm and nothing else.

    The default set of operator types quantizes Gather and Transpose too, which makes a different model out of any
    real transformer - its embedding table becomes integers. reduce_dynamic.py names the two operator types, so the
    oracle for the managed engine has to name them as well.
    """
    output = input_path[: -len(".onnx")] + "." + suffix + ".onnx"
    quantize_dynamic(
        input_path,
        output,
        op_types_to_quantize=["MatMul", "Gemm"],
        per_channel=False,
        reduce_range=False,
        weight_type=QuantType.QInt8,
    )
    return output


def inferred_copy(input_path):
    """What quantize_dynamic hands its quantizer: the shape-inferred model with the 'onnx.infer' marker."""
    model = load_model_with_shape_infer(__import__("pathlib").Path(input_path))
    output = input_path[: -len(".onnx")] + ".inferred.onnx"
    onnx.save_model(model, output)
    return output


def main():
    written = []

    plain = {
        "matmul_k64_n8_fp32": matmul_model(64, 8, np.float32, 1, "matmul_k64_n8"),
        "matmul_k70_n5_fp32": matmul_model(70, 5, np.float32, 2, "matmul_k70_n5"),
        "matmul_k33_n3_fp32": matmul_model(33, 3, np.float32, 3, "matmul_k33_n3"),
        "matmul_k64_n4_fp16": matmul_model(64, 4, np.float16, 4, "matmul_k64_n4_fp16"),
        "matmul_k130_n3_fp32": matmul_model(130, 3, np.float32, 5, "matmul_k130_n3"),
        "two_matmuls_fp32": two_matmul_model(),
        "gemm_bias_transb_fp32": gemm_model(True, True, "gemm_bias_transb"),
        "gemm_plain_fp32": gemm_model(False, False, "gemm_plain"),
        "unsupported_op_fp32": unsupported_op_model(),
        "non_constant_b_fp32": non_constant_b_model(),
        "unknown_fields_fp32": unknown_fields_model(),
        "gather_transpose_fp32": gather_transpose_model(),
        "branch_matmuls_fp32": branch_matmuls_model(),
    }
    paths = {}
    for name, model in plain.items():
        paths[name] = write(model, name + ".onnx")
        written.append(paths[name])

    external = matmul_model(64, 6, np.float32, 41, "external_data")
    written.append(write_external(external, "external_data_fp32.onnx"))
    written.append(os.path.join(HERE, "external_data_fp32.onnx.data"))

    # The same graph as gather_transpose_fp32, but with its weights in a file beside it, so that a weight-only
    # reduction can be checked against the oracle for a model whose weights had to be read in first. Loading a side
    # file leaves an explicit default data location on every tensor that came out of it, and the surviving embedding
    # table carries that into the quantized model.
    external_gather = gather_transpose_model()
    paths["external_gather_fp32"] = write_external(external_gather, "external_gather_fp32.onnx")
    written.append(paths["external_gather_fp32"])
    written.append(os.path.join(HERE, "external_gather_fp32.onnx.data"))

    # Weight-only block-wise, the full matrix on the exact-block model and a spread on the others.
    full_matrix = [
        (4, 32, False, None, "nbits_b4_bs32_asym"),
        (4, 32, True, None, "nbits_b4_bs32_sym"),
        (4, 128, False, None, "nbits_b4_bs128_asym"),
        (4, 128, True, None, "nbits_b4_bs128_sym"),
        (8, 32, False, None, "nbits_b8_bs32_asym"),
        (8, 32, True, None, "nbits_b8_bs32_sym"),
        (8, 128, False, None, "nbits_b8_bs128_asym"),
        (8, 128, True, None, "nbits_b8_bs128_sym"),
        (4, 32, False, 4, "nbits_b4_bs32_asym_acc4"),
        (4, 32, True, 4, "nbits_b4_bs32_sym_acc4"),
    ]
    for bits, block_size, symmetric, accuracy_level, suffix in full_matrix:
        written.append(nbits(paths["matmul_k64_n8_fp32"], bits, block_size, symmetric, accuracy_level, suffix))

    spread = [
        (4, 32, False, None, "nbits_b4_bs32_asym"),
        (4, 32, True, None, "nbits_b4_bs32_sym"),
        (8, 32, False, None, "nbits_b8_bs32_asym"),
        (4, 128, False, None, "nbits_b4_bs128_asym"),
    ]
    for name in (
        "matmul_k70_n5_fp32",
        "matmul_k33_n3_fp32",
        "matmul_k64_n4_fp16",
        "matmul_k130_n3_fp32",
        "two_matmuls_fp32",
        "gemm_bias_transb_fp32",
        "unsupported_op_fp32",
        "non_constant_b_fp32",
    ):
        for bits, block_size, symmetric, accuracy_level, suffix in spread:
            written.append(nbits(paths[name], bits, block_size, symmetric, accuracy_level, suffix))

    # The two models that only a REAL graph's shape exposes: an operator this library never asks to quantize, and a
    # branch whose nodes come out of the sort in another order. One weight-only oracle and one dynamic oracle each,
    # the dynamic one restricted to the operator types the library names.
    for name in ("gather_transpose_fp32", "branch_matmuls_fp32", "external_gather_fp32"):
        written.append(nbits(paths[name], 4, 32, False, None, "nbits_b4_bs32_asym"))

    # Feeding an already-quantized model back in: pin whatever ORT does with it.
    requantized = nbits(
        os.path.join(HERE, "matmul_k64_n8_fp32.nbits_b4_bs32_asym.onnx"),
        4,
        32,
        False,
        None,
        "nbits_b4_bs32_asym",
    )
    written.append(requantized)

    # Dynamic INT8. The inputs the managed engine reads are the shape-inferred copies.
    dynamic_sources = [
        "matmul_k64_n8_fp32",
        "matmul_k70_n5_fp32",
        "two_matmuls_fp32",
        "gemm_bias_transb_fp32",
        "gemm_plain_fp32",
        "unsupported_op_fp32",
        "non_constant_b_fp32",
    ]
    dynamic_modes = [
        (QuantType.QInt8, False, False, "dyn_i8"),
        (QuantType.QUInt8, False, False, "dyn_u8"),
        (QuantType.QInt8, True, False, "dyn_i8_pc"),
        (QuantType.QUInt8, True, False, "dyn_u8_pc"),
        (QuantType.QInt8, False, True, "dyn_i8_rr"),
        (QuantType.QUInt8, True, True, "dyn_u8_pc_rr"),
    ]
    for name in dynamic_sources:
        written.append(inferred_copy(paths[name]))
        for weight_type, per_channel, reduce_range, suffix in dynamic_modes:
            written.append(dynamic(paths[name], weight_type, per_channel, reduce_range, suffix))

    for name in ("gather_transpose_fp32", "branch_matmuls_fp32"):
        written.append(inferred_copy(paths[name]))
        written.append(dynamic_matmul_only(paths[name]))

    # Feeding an already dynamically quantized model back in: pin whatever ORT does with it.
    written.append(
        dynamic(
            os.path.join(HERE, "matmul_k64_n8_fp32.dyn_i8.onnx"),
            QuantType.QInt8,
            False,
            False,
            "dyn_i8",
        )
    )

    total = 0
    for path in sorted(set(written)):
        size = os.path.getsize(path)
        total += size
        print(f"{size:9d}  {os.path.basename(path)}")
    print(f"{total:9d}  TOTAL ({len(set(written))} files)")


if __name__ == "__main__":
    main()
