# Exports a checkpoint to ONNX with the ONNX Runtime GenAI model builder.
#
# The builder reads a Hugging Face checkpoint folder and writes model.onnx, its external data file and
# the genai_config.json that goes with them. It is called here the way its own command line calls it -
# parse_extra_options first, then create_model - so that this library runs the publisher's tool rather
# than a re-implementation of it.
#
# The caller sets: model_name, input_path, output_path, precision, execution_provider, cache_dir and
# allow_remote_code. Nothing else is read, and nothing outside output_path and cache_dir is written.
import os

import onnxruntime_genai
from onnxruntime_genai.models import builder

extra_options = []
if allow_remote_code:
    # The builder's own name for "import the .py files this checkpoint ships", which is the only way to
    # read a tokenizer or a configuration class the publisher defined themselves.
    extra_options.append("hf_remote=true")

parsed = builder.parse_extra_options(
    model_name, input_path, output_path, precision, execution_provider, cache_dir, extra_options
)
builder.create_model(
    model_name, input_path, output_path, precision, execution_provider, cache_dir, **parsed
)

files = []
sizes = []
for folder, _, names in os.walk(output_path):
    for name in sorted(names):
        full = os.path.join(folder, name)
        files.append(os.path.relpath(full, output_path).replace(os.sep, "/"))
        sizes.append(os.path.getsize(full))

result = {
    "tool": "onnxruntime-genai",
    "version": getattr(onnxruntime_genai, "__version__", ""),
    "files": files,
    "sizes": sizes,
}
