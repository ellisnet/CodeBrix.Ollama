================================================================================
llama-native-tools/test-vectors - the conformance assets
================================================================================

WHAT THESE ARE
--------------------------------------------------------------------------------
A tiny synthetic GGUF model, the reference output it must produce, and the two
C programs that generate the one and check the other. Every native build in this
repository loads the model through the library it just built, runs a fixed
prompt through it, and compares every logit with the reference; a mismatch fails
the build. That check is what turns "the library compiled and loads" into "the
compute kernels are correct on this architecture" - and it is the check that
would catch a broken NEON, RVV or Metal path on hardware nobody has to hand.

They are read straight out of the repository. Nothing is downloaded, at build
time or ever, which is the rule this whole folder exists for.

  codebrix-conformance-tiny.gguf   the model: 101,056 bytes, 21 F32 tensors
                                   sha256 66c90c32aea69570911279c49ca1252e60408a2fe2218000cff54444cdb6e7d9
                                   (also pinned in ../linux/pins.env)
  EXPECTED.txt                     the reference: 12 positions x 64 logits, plus
                                   the greedy argmax token at each position
  generate-test-model.c            writes the model (deterministic, seeded)
  conformance-check.c              runs the model and compares / generates

PROVENANCE: THIS IS NOT THIRD-PARTY CONTENT
--------------------------------------------------------------------------------
The model is random weights from a fixed seed, generated here on 2026-09-15 by
generate-test-model.c through the library's own gguf writer. There are no
trained weights, nothing derived from anyone's model, no tokenizer vocabulary
(the vocabulary type is "no_vocab" - the gate feeds token IDS), and nothing
downloaded. It is original content of this repository, covered by the
repository's own licence, and it is deliberately NOT listed in
THIRD-PARTY-NOTICES.txt, because listing it there would be a false statement
about its origin.

The reference logits were produced by the CPU path of the osx-x64 build on
2026-09-15 (Intel i7-8700B, AVX2). See "HOW THE REFERENCE WAS ESTABLISHED".

THE MODEL
--------------------------------------------------------------------------------
"llama" architecture, so it exercises the same graph a real Llama-family model
does - RMS norm, Q/K/V/O projections, RoPE, causal attention, the gated SiLU
FFN, the output projection:

    vocabulary   64          embedding      32        layers      2
    heads        4           kv heads       4         head dim    8
    ffn          64          context        64        all tensors F32

Every kind of kernel a real model touches runs at least once; a wrong SIMD
implementation of any of them changes the logits far beyond the tolerance.
F32 throughout keeps the cross-architecture differences down to rounding.

The prompt is the 12 token ids hard-coded in conformance-check.c
(3 17 42 8 8 61 0 25 33 12 50 7). The model has no tokenizer, so there is no
tokenizer to disagree about.

WHY A TOLERANCE, NOT A HASH
--------------------------------------------------------------------------------
dav1d's decodes are integer-exact on every architecture, so its gate can md5
them. Inference is floating point: AVX2, NEON, scalar RISC-V and Metal sum in
different orders and round differently, so logits agree to about 1e-6 but not
bit for bit. Every logit is therefore compared within an ABSOLUTE tolerance
(../linux/pins.env CONFORMANCE_TOLERANCE, 0.001 - three orders of magnitude
above the spread observed between the CPU builds so far, and far below what a
wrong kernel produces), AND the greedy argmax at every position must match
exactly. A NaN or infinite logit is an immediate failure: on 2026-09-15 the
first Metal run on an Intel UHD 630 returned NaN for every logit, and the
checker's original comparison would have PASSED it (every comparison with NaN
is false). That is fixed and is the reason the non-finite check exists.

THE GENERATOR RUNS ON EVERY BUILD - AND MUST REPRODUCE THE COMMITTED FILE
--------------------------------------------------------------------------------
The model file is committed, not regenerated, because the gate's value is that
every RID is compared with ONE recorded reference. But every build ALSO runs
generate-test-model with the library it just built, and the gate requires the
output to be BYTE-IDENTICAL to the committed file (sha256 in pins.env). That
proves two things at once: the gguf writer in this build is sound, and the
committed asset really is what the committed source produces. All randomness is
a seeded xorshift64* and the floats are built from integer bit patterns, so the
bytes should match on every platform and compiler. If they ever do not, that is
a finding to chase - not a hash to update.

HOW THE REFERENCE WAS ESTABLISHED (2026-09-15)
--------------------------------------------------------------------------------
  1. The osx-x64 library was built (Intel Mac mini, AVX2 CPU path) and
     conformance-check --generate wrote EXPECTED.txt.
  2. The same file was re-checked against a SECOND, separately configured
     build on the same machine (Metal ON, CPU path): max |diff| 4.98e-08.
  3. A Metal-path cross-check on that machine's Intel UHD 630 returned NaN for
     every logit - i.e. ggml-metal does not work on Intel integrated GPUs at
     this commit. That is why the osx-x64 slice is built with Metal OFF, and
     why the first GENUINE second implementation of the reference will be the
     osx-arm64 gate's Metal pass on Apple Silicon, followed by NEON on
     linux-arm64 / win-arm64 and scalar on linux-riscv64. Every one of those
     runs must land within tolerance of this AVX2-generated reference; if one
     does not, the reference stays and the disagreement is investigated.

REGENERATING THE REFERENCE
--------------------------------------------------------------------------------
Only ever needed if generate-test-model.c or the prompt in conformance-check.c
is changed on purpose - and then EVERY RID must be rebuilt and re-gated.

  1. Build any RID with its platform script; it will FAIL at the regeneration
     or conformance step (expected: the committed asset no longer matches).
  2. From that build directory:
         ./generate-test-model codebrix-conformance-tiny.gguf
         ./conformance-check codebrix-conformance-tiny.gguf --generate > EXPECTED.txt
     Copy both into this folder. Put the new size and sha256 into pins.env
     (TEST_VECTOR_MODEL_SIZE / TEST_VECTOR_MODEL_SHA256) and update the header
     comment of EXPECTED.txt and the numbers at the top of this file.
  3. Rebuild every RID. All must pass, and their max |diff| values should stay
     in the 1e-7 range on the CPU paths. Record the new establishment date and
     machine here.

USING THE SAME ASSETS FROM THE MANAGED TESTS
--------------------------------------------------------------------------------
CodeBrix.Ollama.ModelRunner.Tests links this folder's model and EXPECTED.txt in
(as CodeBrix.VideoPlayback.Dav1d.Tests links its .ivf vectors) rather than
keeping a second copy, so the managed binding and the native gate can never
drift on to different assets. The managed test loads the model through the
binding, runs the same 12 token ids, and applies the same tolerance.
================================================================================
