================================================================================
EXTRAS-README: CodeBrix.Ollama
Samples, tools and other non-package content in this repository
================================================================================

  src/CodeBrix.Ollama.Core/
      A THIRD LIBRARY PROJECT THAT IS NOT A THIRD NUGET PACKAGE. It holds the
      code both libraries need - the hand-written ONNX codec with its protocol
      buffer reader and writer, and the tokenizer primitives - and is never
      packed on its own: each of the two packable projects references it and
      packs its DLL inside its own package, so neither package gains a
      dependency and neither library depends on the other. Everything in it is
      internal, so it is invisible to a consuming application even though the
      assembly sits beside the one it is using. MAINTAINER-README.txt, "THE
      Core PROJECT", has the whole of the decision, the compatibility guard
      that turns a mixture of package versions into one clear message, and the
      two-package release check.

  tests/CodeBrix.Ollama.Core.Tests/
      The shared project's own suite: the codec's round trip over its checked-in
      fixtures, the merge-table reading, the compatibility guard, and the fence
      that hashes the shared project's whole non-private surface against a
      recorded value so that a change to it cannot ship without the guard's
      revision being bumped. Entirely offline and gated by nothing.

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

  tests/CodeBrix.Ollama.EndToEnd.Tests/
      A third test executable, and THE ONLY PROJECT IN THIS REPOSITORY THAT
      REFERENCES BOTH LIBRARIES. The two packages must not depend on each
      other, so the one place where a model the store made is loaded and run by
      the runner is a test project: it pulls a real checkpoint, converts it to
      GGUF, hands the resolved path to the runner and compares what the model
      generates with what the checkpoint's own framework generates. It is also
      where the store's quantization meets the runner's - the store takes the
      quantizer as a delegate, and the lambda that fills it is written here,
      because that is where a consumer writes it too. Every test
      in it is gated; with the gate shut the whole assembly skips and nothing
      is downloaded, converted or loaded. MAINTAINER-README.txt has the recipe
      and the measurements.

  samples/ModelQueryTool - a sample application, work in progress.
