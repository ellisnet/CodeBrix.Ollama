================================================================================
llama-native-tools/unstripped - the durable home for pre-strip binaries
================================================================================

UNLIKE output/, THIS FOLDER IS COMMITTED. It exists so the unstripped mate of
every binary shipped in the package survives the machine it was built on
(../output/ is git-ignored and disposable). These files are needed for crash
triage: a stripped release binary in a crash dump can only be symbolised from
its unstripped twin. Nothing here is shipped, and nothing here is an input to
any build or pack step.

One folder per runtime identifier, mirroring runtimes/<rid>/native/ in the
package tree:

  <rid>/libcodebrix_llama.so       the pre-strip ELF (Linux)
  <rid>/libcodebrix_llama.dylib    the pre-strip Mach-O (macOS)
  <rid>/libcodebrix_llama.dylib.dSYM/
                                   the macOS debug-symbol bundle
  <rid>/codebrix_llama.pdb         Windows debug symbols, IF the build emitted
                                   any (see the windows/ README; a Release build
                                   may produce none, and then there is nothing
                                   to store)

Verify any file two ways:

  1. sha256 matches the "SHA256 unstripped" line of that RID's entry in
     ../BUILD-PROVENANCE.txt (and SHA256SUMS beside this file).
  2. Linux: the GNU build-id equals the shipped binary's -
        readelf -n <here>/libcodebrix_llama.so
        readelf -n ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/libcodebrix_llama.so
     macOS: the LC_UUID equals the shipped dylib's (dwarfdump --uuid).

STORED SO FAR
--------------------------------------------------------------------------------
  osx-x64    stored 2026-09-15 from the Intel Mac mini's output/ tree - the
             5,226,136-byte unstripped dylib plus its 55 MB .dSYM bundle.
             LC_UUID 5830F46C-EFA7-3F60-840C-45833A3F0831, verified equal on
             the shipped file, the unstripped twin and the dSYM at adoption.
             IMPORTANT: this build is not UUID-reproducible (see
             ../BUILD-PROVENANCE.txt), so a fresh rebuild's twin would carry
             a different UUID, would not match the shipped binary, and must
             never be substituted for the file stored here.
             NOTE: this twin belongs to the 11.0-floor build. osx-x64 must be
             rebuilt at the 13.3 floor (see ../macos/README.txt), and that
             rebuild replaces this twin, the dSYM and the shipped file together.
  osx-arm64  stored 2026-09-15 from the Apple Silicon Mac mini's output/ tree -
             the 5,524,600-byte unstripped dylib plus its 57 MB .dSYM bundle.
             LC_UUID 88DBABCC-377F-3BBB-A943-F3E67AD56808, verified equal on
             the shipped file, the unstripped twin and the dSYM at adoption.
             Same UUID caveat as osx-x64: a rebuild's twin is a different file.
  (the other five RIDs: not yet built)

THE RULE
--------------------------------------------------------------------------------
Whenever a newly built binary is adopted into runtimes/<rid>/native/, its
unstripped mate from the same build lands here in the same commit, and
SHA256SUMS is extended. A binary here that no longer matches the shipped one's
build-id/LC_UUID is stale and must be replaced, never kept alongside.

SIZE NOTE. These twins are several times the size of the shipped library
(the shipped osx-x64 dylib is about 4.2 MB; its unstripped mate plus dSYM is
larger still). Seven RIDs will put on the order of 100 MB into git history.
Jeremy accepted that on 2026-09-15 for the crash-triage value.
================================================================================
