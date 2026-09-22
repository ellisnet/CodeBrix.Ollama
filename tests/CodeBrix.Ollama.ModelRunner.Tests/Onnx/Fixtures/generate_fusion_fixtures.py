#!/usr/bin/env python3
"""Native ONNX Runtime oracles for managed transpose/scale fusion and scalar broadcasting.

Run this file to regenerate only these cases, or generate_fixtures.py for the full corpus.
No test invokes Python. Graph optimization is disabled so the oracle executes the original operators.
"""
import json
from pathlib import Path

import numpy as np
import onnx
from onnx import TensorProto, helper
import onnxruntime as ort

HERE = Path(__file__).resolve().parent


def emit(name, nodes, inputs, output_names):
    folder = HERE / name
    folder.mkdir(exist_ok=True)
    graph = helper.make_graph(nodes, name,
        [helper.make_tensor_value_info(k, TensorProto.FLOAT, list(v.shape)) for k, v in inputs.items()],
        [helper.make_tensor_value_info(k, TensorProto.FLOAT, None) for k in output_names])
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", 14)],
                              producer_name="codebrix-ollama-fusion-fixtures", ir_version=8)
    model = onnx.shape_inference.infer_shapes(model, strict_mode=True)
    onnx.checker.check_model(model)
    onnx.save(model, folder / "model.onnx")
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.graph_optimization_level = ort.GraphOptimizationLevel.ORT_DISABLE_ALL
    session = ort.InferenceSession(str(folder / "model.onnx"), options, providers=["CPUExecutionProvider"])
    outputs = dict(zip(output_names, session.run(output_names, inputs)))
    manifest = {"name": name, "opset": 14, "inputs": [], "outputs": [],
                "note": "Seed 20260921; ONNX Runtime CPU with graph optimization disabled."}
    for category, prefix, tensors in [("inputs", "in_", inputs), ("outputs", "out_", outputs)]:
        for key, value in tensors.items():
            filename = prefix + key + ".bin"
            (folder / filename).write_bytes(value.tobytes())
            manifest[category].append({"name": key, "type": "float", "shape": list(value.shape), "file": filename})
    (folder / "case.json").write_text(json.dumps(manifest, indent=2) + "\n")
    return name


def generate():
    random = np.random.default_rng(20260921)
    cases = []

    def values(shape):
        return random.uniform(-1, 1, shape).astype(np.float32)

    variants = [
        ("plain", (2, 3, 5, 17), (1, 3, 9, 17), None, False, None),
        ("scalar", (2, 3, 5, 17), (1, 3, 9, 17), (), False, None),
        ("scalar_left", (2, 3, 5, 17), (1, 3, 9, 17), (1, 1), True, None),
        ("broadcast_fallback", (2, 3, 5, 17), (1, 3, 9, 17), (9,), False, None),
        ("rank_fallback", (2, 3, 5, 17), (1, 3, 9, 17), (1, 1, 1, 1, 1), False, None),
        ("zero_reduction", (2, 3, 5, 0), (1, 3, 9, 0), (), False, None),
        ("zero_rows", (2, 3, 0, 17), (1, 3, 9, 17), (), False, None),
        ("zero_columns", (2, 3, 5, 17), (1, 3, 0, 17), (), False, None),
        ("zero_batch", (0, 3, 5, 17), (0, 3, 9, 17), (), False, None),
        ("vector_left", (17,), (9, 17), (), False, None),
        ("shared_transpose", (2, 3, 5, 17), (1, 3, 9, 17), (), False, "t"),
        ("shared_scale", (2, 3, 5, 17), (1, 3, 9, 17), (), False, "s"),
        ("parallel", (2, 3, 5, 65), (1, 3, 127, 65), (), False, None),
    ]
    for suffix, ashape, bshape, scale_shape, scalar_left, shared in variants:
        order = list(range(len(bshape)))
        order[-2:] = order[-2:][::-1]
        nodes = [helper.make_node("Transpose", ["b"], ["t"], perm=order)]
        inputs = {"a": values(ashape), "b": values(bshape)}
        if scale_shape is not None:
            inputs["scale"] = np.full(scale_shape, -0.375, dtype=np.float32) if np.prod(scale_shape) == 1 else values(scale_shape)
            nodes.append(helper.make_node("Mul", ["scale", "t"] if scalar_left else ["t", "scale"], ["s"]))
        nodes.append(helper.make_node("MatMul", ["a", "t" if scale_shape is None else "s"], ["y"]))
        outputs = ["y"]
        if shared:
            nodes.append(helper.make_node("Identity", [shared], ["retained"]))
            outputs += [shared, "retained"]
        cases.append(emit("matmul_fusion_" + suffix, nodes, inputs, outputs))

    for op in ["Add", "Sub", "Mul", "Div"]:
        for left in [False, True]:
            inputs = {"a": values((3, 4097)) + np.float32(2), "scalar": np.array([[0.375]], dtype=np.float32)}
            cases.append(emit("scalar_broadcast_" + op.lower() + ("_left" if left else "_right"),
                [helper.make_node(op, ["scalar", "a"] if left else ["a", "scalar"], ["y"])], inputs, ["y"]))

    # Reassociation upstream of activation quantization can change integer products by a whole count.
    # Keep the original transpose/scale/matmul plan for these graphs, including when y is also returned.
    cases.append(emit("matmul_fusion_dynamic_quantize", [
        helper.make_node("Transpose", ["b"], ["t"], perm=[0, 2, 1]),
        helper.make_node("Mul", ["t", "scale"], ["s"]),
        helper.make_node("MatMul", ["a", "s"], ["y"]),
        helper.make_node("DynamicQuantizeLinear", ["y"], ["q", "q_scale", "q_zero"]),
        helper.make_node("Cast", ["q"], ["q_float"], to=TensorProto.FLOAT),
    ], {"a": values((1, 1, 64)), "b": values((1, 8, 64)),
        "scale": np.array(0.125, dtype=np.float32)}, ["y", "q_float"]))

    return cases


if __name__ == "__main__":
    index_path = HERE / "cases.json"
    index = json.loads(index_path.read_text())
    index["cases"] = sorted(set(index["cases"]) | set(generate()))
    index_path.write_text(json.dumps(index, indent=2) + "\n")
