================================================================================
Fixtures/Quantize - what the inference engine's own quantizer writes
================================================================================

WHAT THESE ARE
--------------------------------------------------------------------------------
Five quantized copies of llama-native-tools/test-vectors/codebrix-conformance-
tiny.gguf, written by the inference engine's OWN command-line quantizer. They are
the oracle QuantizeGgufTests compares ModelRunner.QuantizeAsync against, byte for
byte, with no options set on either side.

  conformance-tiny-Q8_0.gguf     28,896 bytes
  conformance-tiny-Q4_0.gguf     17,632 bytes
  conformance-tiny-Q4_K_M.gguf   21,600 bytes
  conformance-tiny-Q5_K_M.gguf   22,816 bytes
  conformance-tiny-Q6_K.gguf     28,896 bytes

They are read straight out of this folder. Nothing is downloaded, at build time
or ever, which is the rule the fixture folders exist for.

PROVENANCE: THIS IS NOT THIRD-PARTY CONTENT
--------------------------------------------------------------------------------
The SOURCE is this repository's own conformance model - random weights from a
fixed seed, no trained weights, no tokenizer vocabulary, nothing downloaded; see
llama-native-tools/test-vectors/README.txt, which says the same thing at greater
length. A quantization of that file is still that file, so these five are
original content of this repository, covered by the repository's own licence, and
they are deliberately NOT listed in THIRD-PARTY-NOTICES.txt: listing them there
would be a false statement about their origin.

The PROGRAM that wrote them is the engine's quantizer, which is MIT-licensed and
is already covered by the notice this repository carries for the engine. It is
not vendored here and nothing in this repository builds it: it is built by a
maintainer, out of a checkout, only when these files have to be made again.

HOW THEY WERE MADE (2026-09-18)
--------------------------------------------------------------------------------
The tool is built from a checkout of the engine AT THE VENDORED COMMIT - tag
b10221, the one llama-native-tools/BUILD-PROVENANCE.txt records - with the SAME
cmake options the shipped linux-x64 native was built with, plus the two that turn
the tool itself on. It is a BUILD, outside this repository, and it installs
nothing:

    cmake -S <checkout> -B <build> \
      -DCMAKE_BUILD_TYPE=Release -DBUILD_SHARED_LIBS=OFF \
      -DCMAKE_POSITION_INDEPENDENT_CODE=ON -DGGML_NATIVE=OFF -DGGML_OPENMP=OFF \
      -DGGML_CPU_KLEIDIAI=OFF -DGGML_BACKEND_DL=OFF -DGGML_AVX512=OFF \
      -DLLAMA_OPENSSL=OFF -DLLAMA_CURL=OFF \
      -DLLAMA_BUILD_COMMON=ON -DLLAMA_BUILD_TOOLS=ON \
      -DLLAMA_BUILD_EXAMPLES=OFF -DLLAMA_BUILD_TESTS=OFF \
      -DLLAMA_BUILD_APP=OFF -DLLAMA_BUILD_SERVER=OFF
    make -C <build> -j llama-quantize

LLAMA_BUILD_COMMON and LLAMA_BUILD_TOOLS are OFF in the shipped native's options
because the shipped native is a library and needs neither; the tool is an
executable and needs both. Every option that reaches the COMPILER - and therefore
the arithmetic - is the shipped native's own, which is what makes a byte-for-byte
comparison a comparison of two quantizers rather than of two compilers.

Then, for each of the five types, with no switches and no thread count:

    <build>/bin/llama-quantize \
      llama-native-tools/test-vectors/codebrix-conformance-tiny.gguf \
      tests/.../Fixtures/Quantize/conformance-tiny-<TYPE>.gguf <TYPE>

WHEN TO MAKE THEM AGAIN
--------------------------------------------------------------------------------
Only when the vendored engine commit moves, or when the conformance model itself
is regenerated. Then rebuild the tool at the NEW tag, write all five again, and
record the new tag and date here. If a rebuild at the SAME tag ever produces
different bytes, that is a finding to chase - not a fixture to update.
