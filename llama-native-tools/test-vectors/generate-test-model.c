/* =============================================================================================
 * generate-test-model.c - writes the tiny synthetic GGUF model the conformance gate runs
 * =============================================================================================
 *
 * WHAT IT PRODUCES
 *   codebrix-conformance-tiny.gguf: a complete, loadable "llama"-architecture model with random
 *   weights from a FIXED seed. It is original content of this repository - no trained weights,
 *   nothing derived from anyone's model, nothing downloaded - so it can be committed and it is
 *   deliberately absent from THIRD-PARTY-NOTICES.txt.
 *
 *   Shape (chosen so a forward pass touches every kind of kernel a real model uses - RMS norm,
 *   the Q/K/V/O projections, RoPE, attention, the gated FFN, the output projection - while the
 *   file stays around 100 KB):
 *
 *       vocab 64   embedding 32   layers 2   heads 4   kv heads 4   head dim 8   ffn 64   ctx 64
 *       every tensor F32
 *
 *   The vocabulary is "no_vocab": the gate feeds the model TOKEN IDS directly, so no tokenizer
 *   is involved and none has to be invented.
 *
 * WHY THE FILE IS COMMITTED AND NOT REGENERATED ON EVERY BUILD
 *   The whole point of the conformance gate is comparing every RID's output with ONE recorded
 *   reference. If the generator's output ever changed (a different libm, a compiler that
 *   reorders the random stream), the reference would silently change with it. So the file is
 *   generated once, committed, hashed in README.txt, and the generator is kept so it can be
 *   audited and, if the model ever needs to change, regenerated on purpose - which then means
 *   re-establishing EXPECTED.txt (see README.txt).
 *
 *   The generator is nevertheless run on every build, into the scratch output, and its output
 *   must be BYTE-IDENTICAL to the committed file (the platform scripts check the sha256). That
 *   proves the gguf writer in this build is sound and that the committed asset is what this
 *   source produces.
 *
 * DETERMINISM
 *   All randomness comes from a 64-bit xorshift generator seeded with a constant; the floats
 *   are built from integer bit patterns, so the same bytes come out on every platform and
 *   compiler. The gguf writer lays the file out identically everywhere (little-endian, fixed
 *   alignment).
 *
 * USAGE
 *   generate-test-model <output.gguf>
 *   (built by ../wrapper/CMakeLists.txt as the target "generate-test-model", linked against
 *   the codebrix_llama library that was just built - it uses the library's gguf_* API)
 * ============================================================================================= */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <stdint.h>

#include "ggml.h"
#include "gguf.h"

/* ---- model shape (see README.txt before changing anything - it invalidates EXPECTED.txt) ---- */
#define N_VOCAB   64
#define N_EMBD    32
#define N_LAYER   2
#define N_HEAD    4
#define N_HEAD_KV 4
#define N_FF      64
#define N_CTX     64
#define HEAD_DIM  (N_EMBD / N_HEAD)          /* 8 */
#define N_EMBD_KV (HEAD_DIM * N_HEAD_KV)     /* 32 */

#define SEED      0x434F444542524958ULL     /* the ASCII bytes of "CODEBRIX" */
#define SCALE     0.08f                      /* weight magnitude: small enough for sane logits */

/* ---- deterministic xorshift64* ---- */
static uint64_t rng_state = SEED;

static uint64_t rng_next(void)
{
    uint64_t x = rng_state;
    x ^= x >> 12;
    x ^= x << 25;
    x ^= x >> 27;
    rng_state = x;
    return x * 0x2545F4914F6CDD1DULL;
}

/* uniform in [-1, 1), built from the top 24 bits so every platform gets the same float */
static float rng_uniform(void)
{
    uint32_t bits = (uint32_t)(rng_next() >> 40);        /* 24 bits */
    return ((float) bits / 8388608.0f) - 1.0f;           /* [0, 2) - 1 */
}

static void fill_uniform(struct ggml_tensor *t, float scale)
{
    float *p = (float *) t->data;
    int64_t n = ggml_nelements(t);
    int64_t i;
    for (i = 0; i < n; i++) {
        p[i] = rng_uniform() * scale;
    }
}

static void fill_const(struct ggml_tensor *t, float v)
{
    float *p = (float *) t->data;
    int64_t n = ggml_nelements(t);
    int64_t i;
    for (i = 0; i < n; i++) {
        p[i] = v;
    }
}

static struct ggml_tensor *new_2d(struct ggml_context *ctx, const char *name, int64_t ne0, int64_t ne1)
{
    struct ggml_tensor *t = ggml_new_tensor_2d(ctx, GGML_TYPE_F32, ne0, ne1);
    ggml_set_name(t, name);
    return t;
}

static struct ggml_tensor *new_1d(struct ggml_context *ctx, const char *name, int64_t ne0)
{
    struct ggml_tensor *t = ggml_new_tensor_1d(ctx, GGML_TYPE_F32, ne0);
    ggml_set_name(t, name);
    return t;
}

int main(int argc, char **argv)
{
    struct ggml_init_params ip;
    struct ggml_context *ctx;
    struct gguf_context *gguf;
    struct ggml_tensor *t;
    char name[128];
    int l;

    if (argc != 2) {
        fprintf(stderr, "usage: %s <output.gguf>\n", argv[0]);
        return 2;
    }

    /* Enough room for every tensor's data plus ggml's per-tensor overhead. */
    ip.mem_size   = 4 * 1024 * 1024;
    ip.mem_buffer = NULL;
    ip.no_alloc   = false;
    ctx = ggml_init(ip);
    if (!ctx) {
        fprintf(stderr, "ggml_init failed\n");
        return 1;
    }

    gguf = gguf_init_empty();

    /* ---- metadata ------------------------------------------------------------------------ */
    gguf_set_val_str(gguf, "general.architecture", "llama");
    gguf_set_val_str(gguf, "general.name", "codebrix-conformance-tiny");
    gguf_set_val_str(gguf, "general.description",
        "Synthetic random-weight model generated by llama-native-tools/test-vectors/generate-test-model.c "
        "for the CodeBrix.Ollama native conformance gate. Not a trained model. Original content of the "
        "CodeBrix.Ollama repository (MIT).");
    gguf_set_val_u32(gguf, "general.file_type", 0);                      /* ALL_F32 */
    gguf_set_val_u32(gguf, "llama.context_length", N_CTX);
    gguf_set_val_u32(gguf, "llama.embedding_length", N_EMBD);
    gguf_set_val_u32(gguf, "llama.block_count", N_LAYER);
    gguf_set_val_u32(gguf, "llama.feed_forward_length", N_FF);
    gguf_set_val_u32(gguf, "llama.attention.head_count", N_HEAD);
    gguf_set_val_u32(gguf, "llama.attention.head_count_kv", N_HEAD_KV);
    gguf_set_val_f32(gguf, "llama.attention.layer_norm_rms_epsilon", 1e-5f);
    gguf_set_val_u32(gguf, "llama.rope.dimension_count", HEAD_DIM);
    gguf_set_val_f32(gguf, "llama.rope.freq_base", 10000.0f);
    gguf_set_val_u32(gguf, "llama.vocab_size", N_VOCAB);
    gguf_set_val_str(gguf, "tokenizer.ggml.model", "no_vocab");

    /* ---- tensors, in the order llama.cpp names them ------------------------------------------
     * GGUF shapes list ne[0] first (the fastest-varying dimension). For a weight W applied as
     * ggml_mul_mat(W, x), ne = [n_in, n_out]. */
    t = new_2d(ctx, "token_embd.weight", N_EMBD, N_VOCAB);   fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
    t = new_1d(ctx, "output_norm.weight", N_EMBD);           fill_const(t, 1.0f);    gguf_add_tensor(gguf, t);
    t = new_2d(ctx, "output.weight", N_EMBD, N_VOCAB);       fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);

    for (l = 0; l < N_LAYER; l++) {
        snprintf(name, sizeof(name), "blk.%d.attn_norm.weight", l);
        t = new_1d(ctx, name, N_EMBD);               fill_const(t, 1.0f);    gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.attn_q.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_EMBD);       fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.attn_k.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_EMBD_KV);    fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.attn_v.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_EMBD_KV);    fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.attn_output.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_EMBD);       fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.ffn_norm.weight", l);
        t = new_1d(ctx, name, N_EMBD);               fill_const(t, 1.0f);    gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.ffn_gate.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_FF);         fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.ffn_down.weight", l);
        t = new_2d(ctx, name, N_FF, N_EMBD);         fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
        snprintf(name, sizeof(name), "blk.%d.ffn_up.weight", l);
        t = new_2d(ctx, name, N_EMBD, N_FF);         fill_uniform(t, SCALE); gguf_add_tensor(gguf, t);
    }

    if (!gguf_write_to_file(gguf, argv[1], false)) {
        fprintf(stderr, "gguf_write_to_file failed for %s\n", argv[1]);
        gguf_free(gguf);
        ggml_free(ctx);
        return 1;
    }

    printf("wrote %s: %d tensors, vocab %d, embd %d, layers %d, heads %d/%d, ffn %d, ctx %d, seed 0x%016llx\n",
           argv[1], (int) gguf_get_n_tensors(gguf), N_VOCAB, N_EMBD, N_LAYER, N_HEAD, N_HEAD_KV, N_FF, N_CTX,
           (unsigned long long) SEED);

    gguf_free(gguf);
    ggml_free(ctx);
    return 0;
}
