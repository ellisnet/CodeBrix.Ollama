---
# A model card for a model that does not exist. Everything here is ours, MIT licensed.
license: mit
tags:
  - test
  - fixture
pipeline_tag: text-generation
language: [en, de]
library_name: transformers
---

# Tiny Llama fixture

A two-layer synthetic Llama checkpoint written by `generate_fixtures.py`. It exists so that the managed
converter can be compared with the inference engine's own converter byte for byte. It is not a model anyone
should run.
