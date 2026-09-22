# Reduces an exported graph with ONNX Runtime's block-wise weight-only quantizer.
#
# The constant weight of every MatMul is split into blocks along its rows; each block is stored with a
# scale of its own, and a zero point as well unless the caller asked for symmetric blocks. The MatMul
# becomes a MatMulNBits node, which a runtime widens again as it computes. Only MatMul is quantized -
# the tool's own default - so Gather stays as it is and the embedding tables keep their precision.
#
# The caller sets: input_path, output_path, bits, block_size, is_symmetric, accuracy_level (below zero
# for "let the runtime choose"), nodes_to_exclude and use_external_data. Nothing outside the output path is written.
import os

import onnx
import onnxruntime
from onnxruntime.quantization.matmul_nbits_quantizer import DefaultWeightOnlyQuantConfig
from onnxruntime.quantization.matmul_nbits_quantizer import MatMulNBitsQuantizer

config = DefaultWeightOnlyQuantConfig(
    block_size=block_size,
    is_symmetric=is_symmetric,
    accuracy_level=(accuracy_level if accuracy_level >= 0 else None),
    bits=bits,
)

quantizer = MatMulNBitsQuantizer(model=onnx.load(input_path), algo_config=config, nodes_to_exclude=nodes_to_exclude)
quantizer.process()
quantizer.model.save_model_to_file(output_path, use_external_data)

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
