#!/usr/bin/env python
# Writes the smallest real ONNX graph the probe can reduce: one MatMul with a constant weight.
#
# THE TESTS RUN THIS THROUGH THE VIRTUAL ENVIRONMENT'S OWN INTERPRETER, never through the library. The
# probe console application needs a genuine graph to hand the quantizer, and a graph is a protocol
# buffer message that only the ONNX package writes; building one in the probe would mean a second
# protobuf writer in this repository for the sake of one test.
#
#     <venv>/bin/python make_probe_model.py <output.onnx>
#
# It prints the size it wrote, and exits non-zero if it wrote nothing.

import os
import sys

import numpy as np
import onnx
from onnx import TensorProto, helper, numpy_helper

OPSET = 17
IR_VERSION = 9
ROWS = 256
COLUMNS = 8


def main():
    if len(sys.argv) < 2:
        print("usage: make_probe_model.py <output.onnx>", file=sys.stderr)
        return 2

    output = sys.argv[1]
    rng = np.random.default_rng(20260917)
    weights = rng.uniform(-1.0, 1.0, size=(ROWS, COLUMNS)).astype(np.float32)

    node = helper.make_node("MatMul", ["input", "weight"], ["output"], name="matmul")
    graph = helper.make_graph(
        [node],
        "probe_matmul",
        [helper.make_tensor_value_info("input", TensorProto.FLOAT, ["m", ROWS])],
        [helper.make_tensor_value_info("output", TensorProto.FLOAT, ["m", COLUMNS])],
        [numpy_helper.from_array(weights, "weight")],
    )
    model = helper.make_model(graph, opset_imports=[helper.make_opsetid("", OPSET)])
    model.ir_version = IR_VERSION
    model.producer_name = "codebrix-probe"
    onnx.checker.check_model(model)
    onnx.save_model(model, output)

    size = os.path.getsize(output)
    print(f"wrote: {output}")
    print(f"bytes: {size}")
    return 0 if size > 0 else 1


if __name__ == "__main__":
    sys.exit(main())
