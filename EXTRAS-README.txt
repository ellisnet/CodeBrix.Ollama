================================================================================
EXTRAS-README: CodeBrix.Ollama
Samples, tools and other non-package content in this repository
================================================================================

  llama-native-tools/
      Everything needed to build the native llama.cpp libraries shipped inside
      the ModelRunner package: the vendored llama.cpp source subset, the build
      scripts for every supported RID, the smoke test, the conformance test
      vectors, and the build provenance record. Nothing in this folder is
      compiled by a `dotnet build`; see its README.txt.

  tests/CodeBrix.Ollama.ModelManager.Probe/
      A console application, not a test project and not packed. It makes the
      observations that only a whole process can make: whether a full import,
      list, resolve and materialize cycle ever loads the CodeBrix.Python
      assembly (it must not), whether a whole pass-through export to ONNX
      loads it either (it must not), whether a whole reduction through the
      MANAGED engine loads it either (it must not - that is the fence for
      making a graph smaller with nothing installed), whether a process that
      has quantized a real graph through the Python engine still ends by
      itself (it must), and
      what happens to a process that starts a Python interpreter - one that
      ends it, one whose host already had one, one that never ends it at all,
      and one whose host named its own library in code. Both ModelManager test
      projects run it as a child process; it prints one "key: value" line per
      fact.

  tests/CodeBrix.Ollama.ModelManager.Python.Tests/
      A second test executable, beside the offline one, for the tests that need
      a real CPython: exporting and reducing real published models, and the
      comparison that requires this library's own managed reduction engine and
      ONNX Runtime's own tools to write the same bytes. It is separate because
      an interpreter belongs to a process, is started once and cannot be
      restarted. Every test in it is gated; with the gate shut the run starts
      nothing. MAINTAINER-README.txt has the recipe.

  samples/ModelQueryTool - a sample application, work in progress.
