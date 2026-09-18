# Reduces an exported graph with ONNX Runtime's dynamic INT8 quantization.
#
# Every constant weight of a MatMul becomes eight-bit and the activations are quantized while the model
# runs. Gemm is named alongside MatMul deliberately: the tool has no dynamic quantizer for Gemm, but it
# rewrites a Gemm that can become a MatMul into one before it quantizes anything, so naming it states
# what is covered without changing what happens. Nothing else is quantized - not Conv, not Attention,
# not the embedding tables - because this mode is here to make the FILE smaller, and those operators
# either grow it or change what it computes for no gain.
#
# The caller sets: input_path, output_path and use_external_data. Nothing outside the output path and
# the process temporary directory is written.
import os

import onnxruntime
from onnxruntime.quantization import QuantType
from onnxruntime.quantization import quantize_dynamic

quantize_dynamic(
    input_path,
    output_path,
    op_types_to_quantize=["MatMul", "Gemm"],
    per_channel=False,
    reduce_range=False,
    weight_type=QuantType.QInt8,
    use_external_data_format=use_external_data,
)

files = []
sizes = []
for written in (output_path, output_path + ".data"):
    if os.path.exists(written):
        files.append(written)
        sizes.append(os.path.getsize(written))

result = {
    "tool": "onnxruntime",
    "version": getattr(onnxruntime, "__version__", ""),
    "files": files,
    "sizes": sizes,
}
