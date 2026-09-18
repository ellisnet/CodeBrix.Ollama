#!/usr/bin/env python
# Runs a full-precision graph and a reduced one over the same inputs and reports how far apart they are.
#
# A REDUCED MODEL IS AN APPROXIMATION, and the only honest way to say how good an approximation is to run
# both and look. This script is TEST-SIDE tooling: the tests spawn the virtual environment's own
# interpreter to run it, exactly as a person would, rather than going through the library. The library
# never runs a model; nothing here belongs inside it.
#
#     <venv>/bin/python validate_outputs.py --fp32 <model.onnx> --reduced <model.onnx> \
#         [--dims name=value ...] [--seed 20260917] [--int-max 16] [--inventory] [--label text]
#
# Symbolic dimensions (batch, sequence length, "past_seq" and the like) are resolved from --dims, and any
# that nothing names take 0 if their name mentions the past and 1 otherwise, which is what a first step
# with an empty cache looks like. Integer inputs are token identifiers, so they stay under --int-max -
# except an attention mask, which is all ones, because an attention operator reads the number of tokens
# attended to out of it and would refuse anything else.
#
# Every measurement is printed as one "key: value" line; the last line is "OK" when both models ran.

import argparse
import os
import sys

import numpy as np
import onnxruntime


def parse_arguments():
    parser = argparse.ArgumentParser(description="Compare a reduced ONNX model with the one it came from.")
    parser.add_argument("--fp32", required=True)
    parser.add_argument("--reduced", required=True)
    parser.add_argument("--dims", nargs="*", default=[])
    parser.add_argument("--seed", type=int, default=20260917)
    parser.add_argument("--int-max", type=int, default=16)
    parser.add_argument("--inventory", action="store_true")
    parser.add_argument("--label", default="")
    return parser.parse_args()


def dimension_map(pairs):
    dims = {}
    for pair in pairs:
        if "=" in pair:
            name, value = pair.split("=", 1)
            dims[name.strip()] = int(value)
    return dims


def resolve(dimension, dims):
    if isinstance(dimension, int) and dimension > 0:
        return dimension
    name = str(dimension)
    if name in dims:
        return dims[name]
    return 0 if "past" in name.lower() else 1


def numpy_type(onnx_type):
    if "float16" in onnx_type:
        return np.float16
    if "double" in onnx_type:
        return np.float64
    if "float" in onnx_type:
        return np.float32
    if "int64" in onnx_type:
        return np.int64
    if "int32" in onnx_type:
        return np.int32
    if "bool" in onnx_type:
        return np.bool_
    return np.float32


def make_inputs(session, dims, seed, int_max):
    rng = np.random.default_rng(seed)
    feeds = {}
    for meta in session.get_inputs():
        shape = [resolve(dimension, dims) for dimension in meta.shape]
        dtype = numpy_type(meta.type)
        if dtype in (np.int64, np.int32) and "mask" in meta.name.lower():
            # An attention mask is not an identifier: an attention operator reads the number of tokens
            # attended to out of it, and random values there are out of range rather than merely odd.
            values = np.ones(shape, dtype=dtype)
        elif dtype in (np.int64, np.int32):
            values = rng.integers(0, max(int_max, 1), size=shape).astype(dtype)
        elif dtype is np.bool_:
            values = np.ones(shape, dtype=np.bool_)
        else:
            values = (rng.standard_normal(size=shape) * 0.1).astype(dtype)
        feeds[meta.name] = values
        print(f"input: {meta.name} {tuple(shape)} {np.dtype(dtype).name}")
    return feeds


# Bytes per element of the tensor types an exported graph's weights arrive in. Anything not named here
# is counted as four bytes, which is what a graph of floating-point weights is made of.
ELEMENT_SIZES = {
    1: 4, 2: 1, 3: 1, 4: 2, 5: 2, 6: 4, 7: 8, 9: 1, 10: 2, 11: 8, 12: 4, 13: 8, 16: 2,
}


def inventory(path):
    """How much of a graph is the MatMul weights the quantizers replace, which is what decides the ratio.

    The weights are measured from each tensor's SHAPE AND TYPE rather than from its bytes, so a graph
    whose weights live in a file beside it is measured without reading that file. Reading it is not only
    slower: the ONNX package refuses to read a file of weights that has more than one hard link, and a
    bundle laid out by this library is laid out with hard links.
    """
    import onnx  # noqa: PLC0415 - only the inventory needs it

    model = onnx.load(path, load_external_data=False)
    initializers = {i.name: i for i in model.graph.initializer}
    matmul = set()
    for node in model.graph.node:
        if node.op_type in ("MatMul", "Gemm") and len(node.input) > 1 and node.input[1] in initializers:
            matmul.add(node.input[1])

    total = 0
    weights = 0
    for name, initializer in initializers.items():
        size = ELEMENT_SIZES.get(initializer.data_type, 4)
        for dimension in initializer.dims:
            size *= dimension
        total += size
        if name in matmul:
            weights += size

    on_disk = os.path.getsize(path)
    side = path + ".data"
    if os.path.exists(side):
        on_disk += os.path.getsize(side)

    print(f"inventory-bytes: {on_disk}")
    print(f"inventory-initializer-bytes: {total}")
    print(f"inventory-matmul-weight-bytes: {weights}")
    print(f"inventory-matmul-share: {(weights / on_disk) if on_disk else 0.0:.4f}")
    for bits in (4, 8):
        # A block of 128 values: the values themselves, one 4-byte scale, and a packed zero point.
        per_block = 128 * bits / 8 + 4 + bits / 8
        predicted = on_disk - weights + weights * per_block / (128 * 4)
        print(f"inventory-predicted-weight-only-int{bits}: {predicted:.0f}"
              f" (x{(on_disk / predicted) if predicted else 0.0:.2f})")
    predicted = on_disk - weights + weights / 4.0
    print(f"inventory-predicted-dynamic-int8: {predicted:.0f}"
          f" (x{(on_disk / predicted) if predicted else 0.0:.2f})")


def compare(first, second):
    """The differences between two runs, output by output, worst case first."""
    max_abs = 0.0
    max_rel = 0.0
    finite = True
    shapes_match = len(first) == len(second)

    for index, (a, b) in enumerate(zip(first, second)):
        a = np.asarray(a).astype(np.float64)
        b = np.asarray(b).astype(np.float64)
        if a.shape != b.shape:
            shapes_match = False
            print(f"output-{index}-shape-mismatch: {a.shape} vs {b.shape}")
            continue
        finite = finite and bool(np.all(np.isfinite(b)))
        difference = float(np.max(np.abs(a - b))) if a.size else 0.0
        scale = float(np.max(np.abs(a))) if a.size else 0.0
        relative = difference / scale if scale > 0 else 0.0
        max_abs = max(max_abs, difference)
        max_rel = max(max_rel, relative)
        print(f"output-{index}-shape: {tuple(a.shape)}")
        print(f"output-{index}-max-abs-diff: {difference:.6g}")
        print(f"output-{index}-max-rel-diff: {relative:.6g}")

    agreement = 1.0
    if first and np.asarray(first[0]).ndim >= 2 and np.asarray(first[0]).shape == np.asarray(second[0]).shape:
        a = np.asarray(first[0])
        b = np.asarray(second[0])
        if np.issubdtype(a.dtype, np.floating) and a.shape[-1] > 1:
            same = np.argmax(a, axis=-1) == np.argmax(b, axis=-1)
            agreement = float(np.mean(same))

    return max_abs, max_rel, finite, shapes_match, agreement


def main():
    arguments = parse_arguments()
    dims = dimension_map(arguments.dims)

    print(f"label: {arguments.label}")
    print(f"onnxruntime: {onnxruntime.__version__}")
    print(f"fp32-bytes: {os.path.getsize(arguments.fp32)}")
    print(f"reduced-bytes: {os.path.getsize(arguments.reduced)}")

    if arguments.inventory:
        inventory(arguments.fp32)

    options = onnxruntime.SessionOptions()
    options.log_severity_level = 3
    reference_session = onnxruntime.InferenceSession(
        arguments.fp32, options, providers=["CPUExecutionProvider"]
    )
    feeds = make_inputs(reference_session, dims, arguments.seed, arguments.int_max)

    reference = reference_session.run(None, feeds)
    print(f"fp32-outputs: {len(reference)}")

    reduced_session = onnxruntime.InferenceSession(
        arguments.reduced, options, providers=["CPUExecutionProvider"]
    )
    reduced = reduced_session.run(None, feeds)
    print(f"reduced-outputs: {len(reduced)}")

    max_abs, max_rel, finite, shapes_match, agreement = compare(reference, reduced)

    print(f"max-abs-diff: {max_abs:.6g}")
    print(f"max-rel-diff: {max_rel:.6g}")
    print(f"top1-agreement: {agreement:.4f}")
    print(f"finite: {finite}")
    print(f"shapes-match: {shapes_match}")
    print("OK")
    return 0 if finite and shapes_match else 1


if __name__ == "__main__":
    sys.exit(main())
