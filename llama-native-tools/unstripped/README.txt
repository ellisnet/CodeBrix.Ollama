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

  <rid>/libcodebrix_llama.so.xz    the pre-strip ELF (Linux), xz-compressed -
                                   see THE LINUX TWINS ARE COMPRESSED below
  <rid>/libcodebrix_llama.dylib    the pre-strip Mach-O (macOS)
  <rid>/libcodebrix_llama.dylib.dSYM/
                                   the macOS debug-symbol bundle
  <rid>/codebrix_llama.pdb         Windows debug symbols, IF the build emitted
                                   any (see the windows/ README; a Release build
                                   may produce none, and then there is nothing
                                   to store)

Verify any file two ways:

  1. sha256. SHA256SUMS beside this file lists every file AS STORED HERE (so
     `sha256sum -c SHA256SUMS` runs as-is; for the Linux twins that is the .xz
     archive). The "SHA256 unstripped" line of each RID's entry in
     ../BUILD-PROVENANCE.txt is the hash of the UNCOMPRESSED binary; for Linux
     check it with
        xz -dc <rid>/libcodebrix_llama.so.xz | sha256sum
  2. Linux: the GNU build-id equals the shipped binary's -
        xz -dk <rid>/libcodebrix_llama.so.xz      (writes the .so beside it)
        readelf -n <rid>/libcodebrix_llama.so
        readelf -n ../../src/CodeBrix.Ollama.ModelRunner/runtimes/<rid>/native/libcodebrix_llama.so
     (delete the decompressed .so afterwards; only the .xz is committed)
     macOS: the LC_UUID equals the shipped dylib's (dwarfdump --uuid).

THE LINUX TWINS ARE COMPRESSED
--------------------------------------------------------------------------------
An unstripped llama.cpp ELF carries full DWARF and is 80-115 MB - two of the
three were over GitHub's 100 MB per-file limit and could not be pushed at all.
Jeremy decided on 2026-09-15 to store the Linux twins xz-compressed instead
(`xz -T0 -9e -k`, 14-18 MB each; the raw .so is then deleted). This changes
nothing about their purpose: `xz -dk` restores the byte-identical binary, and
the sha256 and build-id checks above are done on that. The macOS twins and
their dSYM bundles are small enough to stay uncompressed; a Windows .pdb, if
one is ever stored, follows whichever rule its size needs.

STORED SO FAR
--------------------------------------------------------------------------------
  osx-x64    stored 2026-09-15 from the Intel Mac mini's output/ tree - the
             5,195,632-byte unstripped dylib plus its 57 MB .dSYM bundle, from
             the REBUILD at the 13.3 floor (the 11.0-floor twin, LC_UUID
             5830F46C-EFA7-3F60-840C-45833A3F0831, was replaced, not kept).
             LC_UUID E98A82F7-4F50-3E3E-955F-96CC1242669A, verified equal on
             the shipped file, the unstripped twin and the dSYM at adoption.
             IMPORTANT: this build is not UUID-reproducible (see
             ../BUILD-PROVENANCE.txt), so a fresh rebuild's twin would carry
             a different UUID, would not match the shipped binary, and must
             never be substituted for the file stored here.
  linux-x64  stored 2026-09-15 from this laptop's output/ tree (container route,
             native x86_64) as libcodebrix_llama.so.xz, 17,829,980 bytes; it
             decompresses to the 114,326,288-byte unstripped ELF, full DWARF,
             sha256 40c32e86... (the BUILD-PROVENANCE "SHA256 unstripped" line).
             Build-id fd7e8b30909507f14bf523c03c402c40e1ae4f98, verified equal
             on the shipped file at adoption and again after the round trip
             through xz.
  linux-arm64 stored 2026-09-15 from this laptop's output/ tree (container
             route under qemu-user emulation) as libcodebrix_llama.so.xz,
             16,999,208 bytes; decompresses to the 112,111,304-byte unstripped
             ELF, sha256 321c65e1.... Build-id
             ed211a6aa0b77d2c5d13bf07eed093d6b448de27, verified equal on the
             shipped file at adoption and after the xz round trip.
  linux-riscv64 stored 2026-09-15 from this laptop's output/ tree (container
             route under qemu-user emulation) as libcodebrix_llama.so.xz,
             14,022,316 bytes; decompresses to the 79,725,640-byte unstripped
             ELF, sha256 2ff7bf71.... Build-id
             a85df2912e3d3a1c44d86cdb550fb6b38348eb8e, verified equal on the
             shipped file at adoption and after the xz round trip.
  osx-arm64  stored 2026-09-15 from the Apple Silicon Mac mini's output/ tree -
             the 5,524,600-byte unstripped dylib plus its 57 MB .dSYM bundle.
             LC_UUID 88DBABCC-377F-3BBB-A943-F3E67AD56808, verified equal on
             the shipped file, the unstripped twin and the dSYM at adoption.
             Same UUID caveat as osx-x64: a rebuild's twin is a different file.
  (win-x64 and win-arm64: not yet built)

THE RULE
--------------------------------------------------------------------------------
Whenever a newly built binary is adopted into runtimes/<rid>/native/, its
unstripped mate from the same build lands here in the same commit (Linux:
xz-compressed, raw .so deleted), and SHA256SUMS is extended with the file as
stored. A binary here that no longer matches the shipped one's
build-id/LC_UUID is stale and must be replaced, never kept alongside.

SIZE NOTE. These twins are several times the size of the shipped library
(the shipped osx-x64 dylib is about 4.2 MB; its unstripped mate plus dSYM is
larger still). Seven RIDs put on the order of 150-200 MB into git history
(the Linux twins counted compressed). Jeremy accepted that on 2026-09-15 for
the crash-triage value; the compression rule above is what keeps every single
file under GitHub's 100 MB limit.
================================================================================
