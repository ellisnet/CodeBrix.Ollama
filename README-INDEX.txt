================================================================================
README-INDEX: CodeBrix.Ollama
Map of the README files in this repository
================================================================================

If you are an AI coding agent: find the NuGet package you are consuming below and
read its AGENT-README file in full. Read MAINTAINER-README.txt only if you are
changing this repository itself.

This repository produces two packages. They are complementary and independent:
neither references the other. ModelManager downloads models and keeps a local
model store; ModelRunner loads a model file and runs it in-process. An
application uses ModelManager to resolve a model name to the files on disk and
hands those paths to ModelRunner. Neither package is, or contains, an HTTP
server. The two are released together and are to be installed at the same
version.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.Ollama.ModelRunner.MitLicenseForever - runs a GGUF model
      in-process over a self-built llama.cpp engine and exposes it through an
      interface contract: completion, streaming chat, embeddings, tool calling,
      grammar-constrained output, tokenization and model metadata, and rewrites
      a model file at a smaller quantization with that same engine. Ships native
      libraries for Windows, macOS and Linux. It ALSO runs ONNX models, on a
      managed interpreter inside the package that needs nothing installed at
      all: a raw tensors-in-tensors-out surface, a driver that generates MIDI
      music and streams the events as it writes them, and a driver that
      generates text through the same interface contract.
  src/CodeBrix.Ollama.ModelManager/AGENT-README.txt
      CodeBrix.Ollama.ModelManager.MitLicenseForever - pulls models from the
      Ollama registry into a local store laid out exactly as Ollama's own,
      obtains the files of a model no such registry serves - a Hugging Face
      file repository, a list of HTTPS addresses, a folder on disk - into the
      same store and lays them out again as the publisher wrote them, converts
      a checkpoint into an ordinary GGUF model with nothing installed, stores a
      quantized copy of a GGUF model that a quantizer the application supplies
      writes, exports a
      model to ONNX and reduces the graphs it holds to smaller ones, keeping
      each result in the same store with its provenance, lists, shows,
      copies, deletes and creates models from
      Modelfiles, reads GGUF metadata, and resolves a model name to the files
      on disk. Pure managed
      code, with one inert NuGet dependency - CodeBrix.Python - that nothing
      loads until a Python feature is used; the PYTHON section of its
      AGENT-README is the whole of that story.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers, including how the native llama.cpp libraries are built by
      llama-native-tools/ and adopted into the ModelRunner package.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository,
      including the probe console application the tests run as a child process
      to observe what a whole process does, the second, gated test executable
      for the tests that need a real CPython, and the gated end-to-end project
      where a model one package made is run by the other.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences.
  README-INDEX.txt
      This file.
