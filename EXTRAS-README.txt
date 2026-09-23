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


MUSECOCO FIXTURES AND LOCAL-CHECKPOINT TEST
==========================================
MUSECOCO-README.txt is the staging/consumer guide. The ordinary Runner suite
contains tiny synthetic music/BERT bundles under Drivers/MuseCoco/Fixtures.
Its generator is a maintainer tool requiring Python, onnx and transformers;
the test suite only reads the checked-in files and does not execute Python.
The fixture README records provenance and regeneration arguments. Standard
Elu/Erf/Tanh/LayerNormalization oracle fixtures are produced by the existing
Onnx/Fixtures/generate_fixtures.py tool using ONNX Runtime.

MuseCocoStreamingTests runs the synthetic recurrent graph through both output
APIs, checks early event delivery before token generation completes, token-limit
cleanup, fixed-seed repetition, stream lifetime, cancellation and early disposal.
Remigen2StreamDecoderTests checks cutoff parity including incomplete chords,
stable channels, percussion, timestamp ordering, long notes, tempo/signature
changes and the 15-channel melodic limit. These tests need no external models.
MuseCocoGrammarTests checks filtering before top-k/top-p, invalid note ordering,
bar metadata, prompt-token exclusion, EOS gating and incomplete final notes.
MuseCocoContinuationTests checks the experimental rolling prompt through the
synthetic graph: advancing inference positions, bounded retained bars, inherited
metadata, no replayed MIDI, stable channels/timeline, capacity and interruption.

MuseCocoLiveTests in CodeBrix.Ollama.ModelRunner.Tests is a separate, local-bundle
regression for integration issue U1: both APIs must finish 512 generated tokens
with seed 20260921, top-k 15, top-p 1, temperature 1 and piano/moderate attributes.
It compares the complete and streamed music, checks early ordered delivery and
reads the resulting MIDI. It neither stages models nor requires Python. Set
CODEBRIX_OLLAMA_RUN_LIVE_TESTS=1, CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS=1, and
CODEBRIX_OLLAMA_MUSECOCO_MUSIC_BUNDLE to the existing music-int4 bundle. After a
Release build, select only that test (so other live models are not downloaded):
  dotnet tests/CodeBrix.Ollama.ModelRunner.Tests/bin/Release/net10.0/CodeBrix.Ollama.ModelRunner.Tests.dll -class CodeBrix.Ollama.ModelRunner.Tests.MuseCocoLiveTests -showLiveOutput
That class also exercises two experimental continuation sections using the same
local INT4 bundle with a four-bar context and natural EOS allowed. It checks
inference and event continuity; it is not a listening test of musical quality.

MuseCocoLiveTests in CodeBrix.Ollama.EndToEnd.Tests exercises the actual local
checkpoints: stages both FP32 bundles, quantizes each independently to INT8 and
INT4, runs text predictions and MIDI generation at each precision, combines
INT8 BERT with INT4 music, checks repeatability, and writes MIDI artifacts.
It downloads nothing. Set all three gates to 1:
  CODEBRIX_OLLAMA_RUN_LIVE_TESTS
  CODEBRIX_OLLAMA_RUN_PYTHON_TESTS
  CODEBRIX_OLLAMA_RUN_MUSECOCO_TESTS
Also set these directory variables:
  CODEBRIX_OLLAMA_PYTHON_VENV                 torch/numpy/onnx environment
  CODEBRIX_OLLAMA_MUSECOCO_MUSIC_SOURCE       folder with one music .pt
  CODEBRIX_OLLAMA_MUSECOCO_TEXT_SOURCE        custom BERT checkpoint folder
  CODEBRIX_OLLAMA_MUSECOCO_TEST_DIRECTORY     persistent test output/store folder

Allow at least 30 GiB free disk space and sufficient RAM for FP32 staging and
inference. The source checkpoints are large; ImportOptions.Link=true shares
source data on the same filesystem. The test keeps generated bundles for
inspection and reuse. Set OMP_NUM_THREADS=4 and MKL_NUM_THREADS=4 for the recorded
staging measurements. The Runner test uses four inference threads.

Build, then invoke the xUnit executable directly to select only this class:
  dotnet build tests/CodeBrix.Ollama.EndToEnd.Tests/CodeBrix.Ollama.EndToEnd.Tests.csproj -c Release -p:GeneratePackageOnBuild=false
  dotnet tests/CodeBrix.Ollama.EndToEnd.Tests/bin/Release/net10.0/CodeBrix.Ollama.EndToEnd.Tests.dll -class '*MuseCocoLiveTests' -showLiveOutput

The assembly fixture calls PythonSupport.Shutdown after all tests finish. This
explicit lifetime step is necessary for reliable exit after the torch exporter
in the tested environment. A closed-gate run starts no Python interpreter.
Portable execution can be exercised on Intel by setting COMPlus_EnableHWIntrinsic=0
and running the ordinary Runner executable with -class '*MuseCoco*'. This checks
fallback correctness; it is not an ARM performance measurement.
