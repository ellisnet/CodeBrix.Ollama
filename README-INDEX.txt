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
application uses ModelManager to resolve a model name to a GGUF file on disk and
hands that path to ModelRunner. Neither package is, or contains, an HTTP server.

AGENT-README FILES (consumer documentation, one per NuGet package)
------------------------------------------------------------------
  AGENT-README.txt
      CodeBrix.Ollama.ModelRunner.MitLicenseForever - runs a GGUF model
      in-process over a self-built llama.cpp engine and exposes it through an
      interface contract: completion, streaming chat, embeddings, tool calling,
      grammar-constrained output, tokenization and model metadata. Ships native
      libraries for Windows, macOS and Linux.
  src/CodeBrix.Ollama.ModelManager/AGENT-README.txt
      CodeBrix.Ollama.ModelManager.MitLicenseForever - pulls models from the
      Ollama registry into a local store laid out exactly as Ollama's own,
      lists, shows, copies, deletes and creates models from Modelfiles, reads
      GGUF metadata, and resolves a model name to the files on disk. Pure
      managed code.

MAINTAINER AND EXTRAS
---------------------
  MAINTAINER-README.txt
      Building, testing, packaging, versioning and provenance notes for
      maintainers, including how the native llama.cpp libraries are built by
      llama-native-tools/ and adopted into the ModelRunner package.
  EXTRAS-README.txt
      Samples, tools and other non-package content in this repository.

GENERAL
-------
  README.md
      Human-facing overview shown on GitHub and nuget.org.
  THIRD-PARTY-NOTICES.txt
      What came from where, and under which licences.
  README-INDEX.txt
      This file.
