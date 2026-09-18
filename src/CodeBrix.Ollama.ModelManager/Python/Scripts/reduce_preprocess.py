# Prepares an exported graph for quantization with ONNX Runtime's own preprocessing pass.
#
# quant_pre_process runs symbolic shape inference, ONNX Runtime's basic graph optimizations and ONNX
# shape inference, and leaves a marker in the graph's metadata saying it has been prepared. Dynamic
# quantization reads the shapes it infers and warns about a graph nobody prepared; the weight-only
# modes read the weights themselves and need none of it.
#
# WHAT IS WRITTEN IS WHAT THE QUANTIZER IS FED. quantize_dynamic does not hand its quantizer the file it
# is given: it runs ONNX shape inference over it first and records that in a second metadata entry,
# onnx.infer. This script does that last step itself, so the prepared graph on disk IS the graph a
# quantizer works on - which is what lets the managed engine, which infers no shapes of its own, take a
# prepared graph and produce the same file the Python tools produce from it.
#
# THE ONE LIMIT WORTH KNOWING: a prepared graph is written as a single protocol buffer message unless
# the caller asks for external data, and such a message cannot exceed 2 GiB. That failure is reported
# back rather than raised, so the caller can say which graph it was and what to do instead.
#
# The caller sets: input_path, output_path and use_external_data. Nothing outside the output path and
# the process temporary directory is written.
import os

import onnx
import onnxruntime
from onnxruntime.quantization.quant_utils import add_infer_metadata
from onnxruntime.quantization.shape_inference import quant_pre_process

data_name = os.path.basename(output_path) + ".data"

error = ""
message = ""

try:
    quant_pre_process(
        input_model=input_path,
        output_model_path=output_path,
        save_as_external_data=use_external_data,
        all_tensors_to_one_file=True,
        external_data_location=data_name,
    )
    # The same pass quantize_dynamic runs on its way in, written into the file rather than left in memory.
    # The inferred copy is written beside the output, because a side file of weights is named relative to
    # the graph that refers to it; its name is deliberately not the graph's, so that nothing could mistake
    # it for such a side file if a failure ever left it behind.
    output_directory = os.path.dirname(output_path)
    inferred_path = os.path.join(output_directory, "quant-pre-inferred.onnx")
    try:
        onnx.shape_inference.infer_shapes_path(output_path, inferred_path)
        prepared = onnx.load(inferred_path)
    finally:
        if os.path.exists(inferred_path):
            os.remove(inferred_path)

    add_infer_metadata(prepared)
    if use_external_data:
        # The weights are all in memory now, and the ONNX package APPENDS to a file of weights that is
        # already there rather than replacing it, so the one just read has to go first.
        side_path = os.path.join(output_directory, data_name)
        if os.path.exists(side_path):
            os.remove(side_path)
        onnx.save_model(
            prepared,
            output_path,
            save_as_external_data=True,
            all_tensors_to_one_file=True,
            location=data_name,
            size_threshold=1024,
            convert_attribute=False,
        )
    else:
        onnx.save_model(prepared, output_path)
    del prepared
except Exception as failure:  # noqa: BLE001 - the message decides whether this is the size limit
    text = str(failure)
    if "2GB" in text or "2 GB" in text or "exceeds maximum protobuf size" in text:
        error = "size-limit"
        message = text
    else:
        raise

files = []
sizes = []
if not error:
    for written in (output_path, os.path.join(os.path.dirname(output_path), data_name)):
        if os.path.exists(written):
            files.append(written)
            sizes.append(os.path.getsize(written))

result = {
    "tool": "onnxruntime",
    "version": getattr(onnxruntime, "__version__", ""),
    "files": files,
    "sizes": sizes,
    "error": error,
    "message": message,
}
