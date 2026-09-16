================================================================================
llama-native-tools/patches - local changes to the vendored llama.cpp source
================================================================================

AS OF 2026-09-15 THIS FOLDER IS EMPTY. No patch is needed on the platform that
has been built so far (osx-x64); the other six RIDs have not been built yet and
may turn one up - if so it goes here, never into ../llama.cpp/.

WHY THE FOLDER EXISTS ANYWAY
--------------------------------------------------------------------------------
The vendored source in ../llama.cpp/ is an unmodified upstream snapshot and must
stay that way - that is what makes it verifiable against upstream (see
../llama.cpp/UPSTREAM.txt). If a build ever does need a source change, it
belongs here as a patch file, never as an edit in ../llama.cpp/.

Note that most things that LOOK like they need a source change do not: the
whole build configuration is CMake options in ../linux/pins.env, and everything
CodeBrix-specific (the single-library link, the export lists, the identity
functions, the gate tools) lives in ../wrapper/ and ../test-vectors/, outside
the vendored tree. A patch is the last resort, for an actual upstream bug.

HOW A PATCH WOULD BE USED
--------------------------------------------------------------------------------
  1. Name it NNN-short-description.patch (e.g. 001-riscv-scalar-fallback.patch),
     produced with `git diff` or `diff -u` against the vendored tree, with paths
     relative to ../llama.cpp/ (i.e. -p1 applies from inside llama.cpp/).

  2. Head the file with a comment block: what it fixes, which platforms it
     applies to, the upstream issue or pull-request link if there is one, and
     the date it can be dropped (normally: when the vendored snapshot is next
     bumped to a commit that contains the fix).

  3. The build scripts copy ../llama.cpp/ into a scratch directory, apply every
     patch in this folder in filename order with `patch -p1` (Linux, macOS) or
     `git apply -p1` (Windows), and build there. If a patch fails to apply the
     build stops - patches are never applied "best effort".

  4. Record it in ../BUILD-PROVENANCE.txt (the "Patches applied" line of every
     affected RID) and in ../../THIRD-PARTY-NOTICES.txt (the "Modifications"
     line of the llama.cpp entry), because a patched binary is no longer plain
     upstream llama.cpp and the licence requires the change to be stated.
================================================================================
