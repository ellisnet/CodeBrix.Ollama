# Exports a checkpoint to ONNX with Hugging Face Optimum.
#
# Optimum traces the model's own Python code, so it reaches architectures the GenAI model builder does
# not write, and it decides the task from the checkpoint's configuration when the caller asks for
# "auto". It exports at the checkpoint's own precision: the precision the caller asked for is recorded
# in the bundle's provenance, not applied here.
#
# The caller sets: input_path, output_path, task and allow_remote_code. Nothing outside output_path is
# written, and local_files_only keeps the export off the network.
import os

import optimum.version
from optimum.exporters.onnx import main_export

main_export(
    model_name_or_path=input_path,
    output=output_path,
    task=task,
    trust_remote_code=allow_remote_code,
    local_files_only=True,
)

files = []
sizes = []
for folder, _, names in os.walk(output_path):
    for name in sorted(names):
        full = os.path.join(folder, name)
        files.append(os.path.relpath(full, output_path).replace(os.sep, "/"))
        sizes.append(os.path.getsize(full))

result = {
    "tool": "optimum",
    "version": getattr(optimum.version, "__version__", ""),
    "files": files,
    "sizes": sizes,
}
