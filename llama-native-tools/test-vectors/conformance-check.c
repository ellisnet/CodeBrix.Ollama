/* =============================================================================================
 * conformance-check.c - runs the tiny model through a freshly built library and compares
 * =============================================================================================
 *
 * WHAT IT CHECKS
 *   Loads codebrix-conformance-tiny.gguf through the public llama.cpp C API of the library that
 *   was just built, feeds it a FIXED sequence of token ids, and reads back the full logit
 *   vector at every position. Then either:
 *
 *     --generate           prints those logits in the EXPECTED.txt format (only used when
 *                          establishing a new reference - see README.txt), or
 *     --check EXPECTED     compares every logit with the recorded reference within an absolute
 *                          tolerance, AND requires the greedy argmax token at every position to
 *                          match exactly. A mismatch fails the gate.
 *
 *   Every RID runs the same file against the same reference. This is the check that says the
 *   compute kernels are RIGHT on this architecture - AVX2 on x64, NEON on arm64, scalar on
 *   riscv64, Metal on Apple Silicon - not merely that the library loads.
 *
 * WHY A TOLERANCE, NOT A HASH
 *   Different SIMD paths sum in different orders and round differently, so logits agree to
 *   about 1e-6 but not bit-for-bit. dav1d's decodes are integer-exact and can be md5'd; this
 *   cannot. The tolerance (pins.env CONFORMANCE_TOLERANCE, 1e-3) is three orders of magnitude
 *   above the observed spread and far below anything a wrong kernel would produce.
 *
 * --gpu-layers N
 *   Offloads N layers (99 = all) so the check runs on the GPU backend where one exists. The
 *   macOS arm64 gate runs the check twice - once on the CPU, once on Metal - and both must
 *   pass. On a CPU-only build the option is accepted and has no effect.
 *
 * USAGE
 *   conformance-check <model.gguf> --generate [--gpu-layers N] > EXPECTED.txt
 *   conformance-check <model.gguf> --check <EXPECTED.txt> [--tolerance T] [--gpu-layers N]
 *
 * EXPECTED.txt FORMAT (text, one record per position, '#' comments allowed):
 *   pos <i> token <id> argmax <id> logits <v0> <v1> ... <v63>
 *
 * Built by ../wrapper/CMakeLists.txt as the target "conformance-check" against the library
 * just built; it includes llama.h from the vendored tree because it drives the real API.
 * ============================================================================================= */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <math.h>

#include "llama.h"

/* The fixed prompt: 12 token ids inside the 64-token vocabulary. Never change these without
 * re-establishing EXPECTED.txt - see README.txt. */
static const llama_token PROMPT[] = { 3, 17, 42, 8, 8, 61, 0, 25, 33, 12, 50, 7 };
#define PROMPT_LEN ((int)(sizeof(PROMPT) / sizeof(PROMPT[0])))

static void quiet_log(enum ggml_log_level level, const char *text, void *user)
{
    (void) level; (void) user;
    /* Keep the gate's log readable: only warnings and errors from the library are shown. */
    if (level >= GGML_LOG_LEVEL_WARN) {
        fputs(text, stderr);
    }
}

static int argmax(const float *v, int n)
{
    int best = 0;
    int i;
    for (i = 1; i < n; i++) {
        if (v[i] > v[best]) {
            best = i;
        }
    }
    return best;
}

int main(int argc, char **argv)
{
    const char *model_path = NULL;
    const char *expected_path = NULL;
    int generate = 0;
    int gpu_layers = 0;
    double tolerance = 0.001;
    int i;

    struct llama_model_params mparams;
    struct llama_context_params cparams;
    struct llama_model *model;
    struct llama_context *ctx;
    const struct llama_vocab *vocab;
    int n_vocab;
    struct llama_batch batch;
    float *all_logits;
    int failures = 0;
    double max_diff = 0.0;

    for (i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--generate") == 0) {
            generate = 1;
        } else if (strcmp(argv[i], "--check") == 0 && i + 1 < argc) {
            expected_path = argv[++i];
        } else if (strcmp(argv[i], "--gpu-layers") == 0 && i + 1 < argc) {
            gpu_layers = atoi(argv[++i]);
        } else if (strcmp(argv[i], "--tolerance") == 0 && i + 1 < argc) {
            tolerance = atof(argv[++i]);
        } else if (argv[i][0] != '-' && model_path == NULL) {
            model_path = argv[i];
        } else {
            fprintf(stderr, "unexpected argument: %s\n", argv[i]);
            return 2;
        }
    }
    if (!model_path || (!generate && !expected_path) || (generate && expected_path)) {
        fprintf(stderr,
            "usage: %s <model.gguf> --generate [--gpu-layers N]\n"
            "       %s <model.gguf> --check <EXPECTED.txt> [--tolerance T] [--gpu-layers N]\n",
            argv[0], argv[0]);
        return 2;
    }

    llama_log_set(quiet_log, NULL);
    llama_backend_init();

    mparams = llama_model_default_params();
    mparams.n_gpu_layers = gpu_layers;
    mparams.vocab_only = false;
    model = llama_model_load_from_file(model_path, mparams);
    if (!model) {
        fprintf(stderr, "  [FAIL] could not load %s\n", model_path);
        llama_backend_free();
        return 1;
    }

    vocab = llama_model_get_vocab(model);
    n_vocab = llama_vocab_n_tokens(vocab);

    cparams = llama_context_default_params();
    /* llama.cpp pads a context up to its 256-token minimum whatever is asked for, and then
     * warns that 256 exceeds the model's 64-token training context. Asking for 256 outright
     * keeps that warning out of the gate log; the prompt is 12 tokens, so the value is
     * immaterial to the logits. */
    cparams.n_ctx = 256;
    cparams.n_batch = 64;
    cparams.n_ubatch = 64;
    cparams.n_threads = 4;
    cparams.n_threads_batch = 4;
    cparams.embeddings = false;
    cparams.flash_attn_type = LLAMA_FLASH_ATTN_TYPE_DISABLED;
    ctx = llama_init_from_model(model, cparams);
    if (!ctx) {
        fprintf(stderr, "  [FAIL] could not create a context\n");
        llama_model_free(model);
        llama_backend_free();
        return 1;
    }

    /* One batch, every position asks for logits. */
    batch = llama_batch_init(PROMPT_LEN, 0, 1);
    for (i = 0; i < PROMPT_LEN; i++) {
        batch.token[i]     = PROMPT[i];
        batch.pos[i]       = i;
        batch.n_seq_id[i]  = 1;
        batch.seq_id[i][0] = 0;
        batch.logits[i]    = 1;
    }
    batch.n_tokens = PROMPT_LEN;

    if (llama_decode(ctx, batch) != 0) {
        fprintf(stderr, "  [FAIL] llama_decode returned non-zero\n");
        llama_batch_free(batch);
        llama_free(ctx);
        llama_model_free(model);
        llama_backend_free();
        return 1;
    }

    all_logits = (float *) malloc(sizeof(float) * (size_t) n_vocab * (size_t) PROMPT_LEN);
    for (i = 0; i < PROMPT_LEN; i++) {
        const float *l = llama_get_logits_ith(ctx, i);
        if (!l) {
            fprintf(stderr, "  [FAIL] no logits at position %d\n", i);
            failures++;
            continue;
        }
        memcpy(all_logits + (size_t) i * n_vocab, l, sizeof(float) * (size_t) n_vocab);
    }

    if (generate) {
        int j;
        printf("# codebrix-conformance-tiny.gguf reference logits - generated by conformance-check --generate\n");
        printf("# n_vocab %d positions %d gpu_layers %d\n", n_vocab, PROMPT_LEN, gpu_layers);
        printf("# format: pos <i> token <id> argmax <id> logits <v0> ... <v%d>\n", n_vocab - 1);
        for (i = 0; i < PROMPT_LEN; i++) {
            const float *l = all_logits + (size_t) i * n_vocab;
            printf("pos %d token %d argmax %d logits", i, (int) PROMPT[i], argmax(l, n_vocab));
            for (j = 0; j < n_vocab; j++) {
                printf(" %.7g", l[j]);
            }
            printf("\n");
        }
    } else {
        FILE *f = fopen(expected_path, "r");
        char line[16384];
        int records = 0;
        if (!f) {
            fprintf(stderr, "  [FAIL] cannot open %s\n", expected_path);
            failures++;
        }
        while (f && fgets(line, sizeof(line), f)) {
            int pos, tok, am, consumed, j;
            const char *p;
            const float *l;
            int got_am;
            int nonfinite = 0;
            double worst = 0.0;
            if (line[0] == '#' || line[0] == '\n' || line[0] == '\r') {
                continue;
            }
            if (sscanf(line, "pos %d token %d argmax %d logits%n", &pos, &tok, &am, &consumed) != 3) {
                fprintf(stderr, "  [FAIL] unparseable line in %s: %s", expected_path, line);
                failures++;
                continue;
            }
            records++;
            if (pos < 0 || pos >= PROMPT_LEN) {
                fprintf(stderr, "  [FAIL] position %d out of range\n", pos);
                failures++;
                continue;
            }
            if (tok != (int) PROMPT[pos]) {
                fprintf(stderr, "  [FAIL] EXPECTED.txt was made with a different prompt (pos %d token %d, this program uses %d)\n",
                        pos, tok, (int) PROMPT[pos]);
                failures++;
                continue;
            }
            l = all_logits + (size_t) pos * n_vocab;
            p = line + consumed;
            for (j = 0; j < n_vocab; j++) {
                double want;
                char *end;
                want = strtod(p, &end);
                if (end == p) {
                    fprintf(stderr, "  [FAIL] position %d: expected %d logits, found %d\n", pos, n_vocab, j);
                    failures++;
                    break;
                }
                p = end;
                {
                    /* A NaN or infinity must FAIL. fabs(NaN - x) is NaN, and every comparison
                     * with NaN is false, so without this test a backend producing garbage would
                     * report a difference of 0 and pass. Seen for real on 2026-09-15 (the first
                     * Metal run on an Intel UHD 630 returned NaN logits). */
                    double diff;
                    if (!isfinite((double) l[j])) {
                        diff = HUGE_VAL;
                        nonfinite++;
                    } else {
                        diff = fabs((double) l[j] - want);
                    }
                    if (diff > worst) worst = diff;
                }
            }
            if (worst > max_diff) max_diff = worst;
            got_am = argmax(l, n_vocab);
            if (nonfinite > 0) {
                printf("  [FAIL] position %d: %d of %d logits are NaN or infinite - this backend computed garbage\n", pos, nonfinite, n_vocab);
                failures++;
            } else if (worst > tolerance) {
                printf("  [FAIL] position %d: max |logit diff| %.3g exceeds tolerance %.3g\n", pos, worst, tolerance);
                failures++;
            } else if (got_am != am) {
                printf("  [FAIL] position %d: argmax %d, expected %d (logits within tolerance but ranking differs)\n", pos, got_am, am);
                failures++;
            } else {
                printf("  [ok]   position %2d token %2d argmax %2d  max |diff| %.3g\n", pos, tok, am, worst);
            }
        }
        if (f) fclose(f);
        if (records != PROMPT_LEN) {
            fprintf(stderr, "  [FAIL] %s holds %d records, expected %d\n", expected_path, records, PROMPT_LEN);
            failures++;
        }
        printf("  conformance: %d positions, %d logits each, gpu_layers %d, max |diff| %.3g, tolerance %.3g -> %s\n",
               records, n_vocab, gpu_layers, max_diff, tolerance, failures ? "FAILED" : "passed");
    }

    free(all_logits);
    llama_batch_free(batch);
    llama_free(ctx);
    llama_model_free(model);
    llama_backend_free();
    return failures ? 1 : 0;
}
