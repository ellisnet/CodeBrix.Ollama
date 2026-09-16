/* =============================================================================================
 * smoke-test.c - proves a freshly built codebrix_llama library actually loads and runs
 * =============================================================================================
 *
 * WHAT IT CHECKS, AND WHY IT IS WORTH HAVING
 *   Checking that a shared library exports the right symbol names only proves it linked. This
 *   program loads it THE WAY .NET DOES - dlopen/LoadLibrary by path plus per-symbol lookup, with
 *   no link-time dependency - and then drives the real entry points:
 *
 *     1. load the library by path                    (the P/Invoke resolver's job, done by hand)
 *     2. resolve every entry point in REQUIRED_SYMBOLS. Each one is REQUIRED: a missing symbol
 *        is a run-time crash in the field, not a build error here. The list is the core of what
 *        the managed binding declares plus the two CodeBrix-authored functions; the platform
 *        scripts ALSO check the complete LLAMA_API surface from include/llama.h against nm /
 *        dumpbin, so this list can stay short and readable.
 *     3. codebrix_llama_build_info() / codebrix_llama_rid()
 *                                                     must return non-empty strings, and the RID
 *                                                     must equal the one passed on the command
 *                                                     line - this is the check that catches the
 *                                                     wrong file adopted under the wrong RID
 *     4. llama_backend_init()                         initialises ggml and registers backends
 *     5. ggml_backend_dev_count() >= 1                at least the CPU device must exist; each
 *                                                     device's name and description are printed
 *     6. llama_print_system_info()                    the CPU-feature line (AVX2 / NEON / Metal /
 *                                                     ACCELERATE ...) - printed so the build log
 *                                                     records what the library was compiled with
 *     7. llama_backend_free()
 *
 *   No model is loaded here; that is the conformance checker's job (test-vectors/
 *   conformance-check.c). This program deliberately includes NO llama.cpp header, so it tests
 *   the shipped BINARY rather than the headers it was built from - the same position the managed
 *   binding is in.
 *
 * USAGE
 *   Linux:    cc -O2 -o smoke-test smoke-test.c -ldl
 *   macOS:    cc -O2 -o smoke-test smoke-test.c
 *   Windows:  cl /nologo /O2 smoke-test.c
 *   (the wrapper CMake project also builds it as the target "smoke-test")
 *
 *   smoke-test <path-to-library> <expected-rid>
 *
 * Exit code 0 = every check passed. Any failure prints what broke and exits non-zero.
 * One file, three platforms, no build system needed.
 * ============================================================================================= */

#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
    #include <windows.h>
    #define LIB_HANDLE          HMODULE
    #define LIB_OPEN(path)      LoadLibraryA(path)
    #define LIB_SYM(lib, name)  ((void*)GetProcAddress((lib), (name)))
    #define LIB_CLOSE(lib)      FreeLibrary(lib)
    #define LIB_ERROR()         "LoadLibrary failed (see GetLastError)"
#else
    #include <dlfcn.h>
    #define LIB_HANDLE          void*
    #define LIB_OPEN(path)      dlopen((path), RTLD_NOW | RTLD_LOCAL)
    #define LIB_SYM(lib, name)  dlsym((lib), (name))
    #define LIB_CLOSE(lib)      dlclose(lib)
    #define LIB_ERROR()         dlerror()
#endif

/* llama.cpp's public ABI is plain cdecl on every platform, which is the default here. */
typedef const char * (*fn_cstr_void)(void);
typedef void         (*fn_void_void)(void);
typedef size_t       (*fn_size_void)(void);
typedef void *       (*fn_ptr_size)(size_t);
typedef const char * (*fn_cstr_ptr)(void *);
typedef int          (*fn_int_ptr)(void *);

/* Every entry point below must resolve. Grouped by what the managed binding needs them for. */
static const char *const REQUIRED_SYMBOLS[] = {
    /* CodeBrix-authored identity */
    "codebrix_llama_build_info",
    "codebrix_llama_rid",
    /* backend lifetime and device enumeration */
    "llama_backend_init",
    "llama_backend_free",
    "llama_print_system_info",
    "ggml_backend_dev_count",
    "ggml_backend_dev_get",
    "ggml_backend_dev_name",
    "ggml_backend_dev_description",
    "ggml_backend_dev_type",
    "ggml_backend_dev_memory",
    /* model and context lifetime */
    "llama_model_default_params",
    "llama_context_default_params",
    "llama_model_load_from_file",
    "llama_model_free",
    "llama_init_from_model",
    "llama_free",
    "llama_model_get_vocab",
    "llama_model_n_embd",
    "llama_model_n_ctx_train",
    "llama_model_desc",
    "llama_model_meta_val_str",
    "llama_model_chat_template",
    "llama_n_ctx",
    /* vocabulary and tokenization */
    "llama_vocab_n_tokens",
    "llama_vocab_get_text",
    "llama_vocab_bos",
    "llama_vocab_eos",
    "llama_vocab_is_eog",
    "llama_tokenize",
    "llama_detokenize",
    "llama_token_to_piece",
    /* inference */
    "llama_batch_init",
    "llama_batch_free",
    "llama_batch_get_one",
    "llama_decode",
    "llama_encode",
    "llama_get_logits",
    "llama_get_logits_ith",
    "llama_get_embeddings",
    "llama_get_embeddings_ith",
    "llama_get_embeddings_seq",
    "llama_set_n_threads",
    "llama_synchronize",
    /* sampling */
    "llama_sampler_chain_default_params",
    "llama_sampler_chain_init",
    "llama_sampler_chain_add",
    "llama_sampler_init_greedy",
    "llama_sampler_init_dist",
    "llama_sampler_init_top_k",
    "llama_sampler_init_top_p",
    "llama_sampler_init_min_p",
    "llama_sampler_init_temp",
    "llama_sampler_init_penalties",
    "llama_sampler_init_grammar",
    "llama_sampler_sample",
    "llama_sampler_accept",
    "llama_sampler_reset",
    "llama_sampler_free",
    /* memory (KV cache) */
    "llama_get_memory",
    "llama_memory_clear",
    "llama_memory_seq_rm",
    /* chat templates and adapters */
    "llama_chat_apply_template",
    "llama_chat_builtin_templates",
    "llama_adapter_lora_init",
    "llama_adapter_lora_free",
    "llama_set_adapters_lora",
    /* GGUF metadata access */
    "gguf_init_from_file",
    "gguf_free",
    "gguf_get_n_kv",
    "gguf_get_key",
    "gguf_get_val_str",
    "gguf_get_n_tensors",
    /* logging */
    "llama_log_set",
    "ggml_log_set",
};
#define REQUIRED_SYMBOL_COUNT ((int)(sizeof(REQUIRED_SYMBOLS) / sizeof(REQUIRED_SYMBOLS[0])))

static int failures = 0;

static void check(int ok, const char *what)
{
    printf("  [%s] %s\n", ok ? "ok" : "FAIL", what);
    if (!ok) {
        failures++;
    }
}

int main(int argc, char **argv)
{
    LIB_HANDLE lib;
    void *sym;
    int i;
    char detail[1024];

    fn_cstr_void p_build_info;
    fn_cstr_void p_rid;
    fn_void_void p_backend_init;
    fn_void_void p_backend_free;
    fn_cstr_void p_system_info;
    fn_size_void p_dev_count;
    fn_ptr_size  p_dev_get;
    fn_cstr_ptr  p_dev_name;
    fn_cstr_ptr  p_dev_description;
    fn_int_ptr   p_dev_type;

    const char *build_info;
    const char *rid;
    const char *system_info;
    size_t dev_count;
    size_t d;

    if (argc != 3) {
        fprintf(stderr, "usage: %s <path-to-codebrix_llama-shared-library> <expected-rid>\n", argv[0]);
        return 2;
    }

    printf("codebrix_llama smoke test\n");
    printf("  library      : %s\n", argv[1]);
    printf("  expected rid : %s\n", argv[2]);

    lib = LIB_OPEN(argv[1]);
    if (!lib) {
        fprintf(stderr, "  [FAIL] could not load the library: %s\n", LIB_ERROR());
        return 1;
    }
    check(1, "loaded the library");

    /* --- every required entry point must resolve ------------------------------------------- */
    for (i = 0; i < REQUIRED_SYMBOL_COUNT; i++) {
        sym = LIB_SYM(lib, REQUIRED_SYMBOLS[i]);
        if (!sym) {
            snprintf(detail, sizeof(detail), "missing export: %s", REQUIRED_SYMBOLS[i]);
            check(0, detail);
        }
    }
    if (failures == 0) {
        snprintf(detail, sizeof(detail), "all %d required exports resolve", REQUIRED_SYMBOL_COUNT);
        check(1, detail);
    }

    p_build_info      = (fn_cstr_void) LIB_SYM(lib, "codebrix_llama_build_info");
    p_rid             = (fn_cstr_void) LIB_SYM(lib, "codebrix_llama_rid");
    p_backend_init    = (fn_void_void) LIB_SYM(lib, "llama_backend_init");
    p_backend_free    = (fn_void_void) LIB_SYM(lib, "llama_backend_free");
    p_system_info     = (fn_cstr_void) LIB_SYM(lib, "llama_print_system_info");
    p_dev_count       = (fn_size_void) LIB_SYM(lib, "ggml_backend_dev_count");
    p_dev_get         = (fn_ptr_size)  LIB_SYM(lib, "ggml_backend_dev_get");
    p_dev_name        = (fn_cstr_ptr)  LIB_SYM(lib, "ggml_backend_dev_name");
    p_dev_description = (fn_cstr_ptr)  LIB_SYM(lib, "ggml_backend_dev_description");
    p_dev_type        = (fn_int_ptr)   LIB_SYM(lib, "ggml_backend_dev_type");

    if (!p_build_info || !p_rid || !p_backend_init || !p_backend_free || !p_system_info ||
        !p_dev_count || !p_dev_get || !p_dev_name || !p_dev_description || !p_dev_type) {
        fprintf(stderr, "  [FAIL] the entry points needed to run the rest of the test are missing.\n");
        LIB_CLOSE(lib);
        return 1;
    }

    /* --- identity -------------------------------------------------------------------------- */
    build_info = p_build_info();
    check(build_info != NULL && build_info[0] != '\0', "codebrix_llama_build_info() returns a string");
    printf("      build info : %s\n", build_info ? build_info : "(null)");

    rid = p_rid();
    snprintf(detail, sizeof(detail), "codebrix_llama_rid() = \"%s\" matches expected \"%s\"",
             rid ? rid : "(null)", argv[2]);
    check(rid != NULL && strcmp(rid, argv[2]) == 0, detail);

    /* --- backend init, devices, system info ------------------------------------------------ */
    p_backend_init();
    check(1, "llama_backend_init() returned");

    dev_count = p_dev_count();
    snprintf(detail, sizeof(detail), "ggml_backend_dev_count() = %u (must be >= 1)", (unsigned) dev_count);
    check(dev_count >= 1, detail);
    for (d = 0; d < dev_count; d++) {
        void *dev = p_dev_get(d);
        int type = dev ? p_dev_type(dev) : -1;
        const char *tname = type == 0 ? "CPU" : type == 1 ? "ACCEL" : type == 2 ? "GPU" : type == 3 ? "IGPU" : "?";
        printf("      device %u   : %s  (%s, %s)\n", (unsigned) d,
               dev ? p_dev_name(dev) : "(null)", tname, dev ? p_dev_description(dev) : "(null)");
    }

    system_info = p_system_info();
    check(system_info != NULL && system_info[0] != '\0', "llama_print_system_info() returns a string");
    printf("      system info: %s\n", system_info ? system_info : "(null)");

    p_backend_free();
    check(1, "llama_backend_free() returned");

    LIB_CLOSE(lib);

    printf("\n");
    if (failures) {
        printf("  %d check(s) FAILED\n", failures);
        return 1;
    }
    printf("  all checks passed\n");
    return 0;
}
