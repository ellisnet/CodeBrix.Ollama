================================================================================
llama-native-tools/output - where the build scripts put their results
================================================================================

THIS FOLDER'S CONTENTS ARE NOT COMMITTED. The folder's .gitignore ignores
everything here except this file (see the `/output/*` and `!/output/README.txt`
rules in ../.gitignore, and read the comment above them before editing either).
Nothing here is an input to anything: delete the whole folder and the next build
recreates it.

The shipped binaries do not live here. They are COPIED from here into the
package tree at:

    ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/

and it is that copy which is committed. See "ADOPTING A BUILT BINARY INTO THE
PACKAGE" in the platform README that produced the binary.


WHAT LANDS HERE
--------------------------------------------------------------------------------
One folder per runtime identifier, all seven side by side in one tree, whichever
platform produced them:

  <rid>/<library>              the file the package ships. Unversioned:
                               libcodebrix_llama.so, libcodebrix_llama.dylib or
                               codebrix_llama.dll
  <rid>/LICENSE-LlamaCpp.txt   llama.cpp's LICENSE (MIT), verbatim. Ships WITH
                               the binary: the MIT licence requires its notice
                               to accompany copies, and this is how it travels
  <rid>/BUILD-INFO.txt         toolchain, pins, sizes, sha256, deployment target
                               or glibc floor, and the conformance results for
                               that build. This is what ../BUILD-PROVENANCE.txt
                               is filled in from
  <rid>/unstripped/<library>   the pre-strip binary. NOT shipped. This copy is
                               transient, like the rest of this folder; the one
                               that is kept lives in ../unstripped/<rid>/
  <rid>/<library>.dSYM         macOS only - debug symbols. NOT shipped
  <rid>/codebrix_llama.pdb     Windows only - debug symbols, if the toolchain
                               emitted any. NOT shipped
  staging/<rid>/<library>.<ext>
                               a compressed copy, for moving the binary to
                               whichever machine assembles the package. Each
                               platform uses what it has in the box: .xz on
                               Linux, .gz on macOS, .zip on Windows

A build that fails its gate exits non-zero and leaves its <rid>/ folder in place
on purpose, so it can be inspected. A failed build's output must never be
adopted - being present here means it was BUILT, not that it PASSED. The gate
result is recorded in BUILD-INFO.txt and in ../BUILD-PROVENANCE.txt.


DO NOT HAND-EDIT ANYTHING HERE
--------------------------------------------------------------------------------
Every file is generated. If a value looks wrong, fix the script that wrote it
and rebuild - the whole point of this folder is that it can be thrown away and
regenerated from the repository alone.
================================================================================
