/* =============================================================================================
 * codebrix_llama.c - the one CodeBrix-authored translation unit inside libcodebrix_llama
 * =============================================================================================
 *
 * Everything else in the shared library is upstream llama.cpp / ggml, folded in whole-archive
 * from the vendored source (see CMakeLists.txt beside this file). This file adds two entry
 * points that let the managed binding, the smoke test and a person with a hex editor tell WHICH
 * build of the library they are holding:
 *
 *   codebrix_llama_build_info()  a free-text line: upstream commit and tag, the RID, the build
 *                                date and host, baked in by the platform script via pins.env
 *   codebrix_llama_rid()         just the .NET runtime identifier, e.g. "osx-arm64"
 *
 * Both return static, NUL-terminated ASCII strings owned by the library; never free them.
 *
 * The public llama.cpp surface (llama_*, ggml_*, gguf_*) is exported unchanged; nothing here
 * wraps or renames it. The managed binding is written against the vendored include/llama.h.
 * ============================================================================================= */

#if defined(_WIN32)
    #define CODEBRIX_LLAMA_API __declspec(dllexport)
#else
    #define CODEBRIX_LLAMA_API __attribute__((visibility("default")))
#endif

#ifndef CODEBRIX_LLAMA_RID
    #define CODEBRIX_LLAMA_RID "unknown-rid"
#endif
#ifndef CODEBRIX_LLAMA_BUILD_INFO
    #define CODEBRIX_LLAMA_BUILD_INFO "no build info recorded"
#endif

CODEBRIX_LLAMA_API const char * codebrix_llama_build_info(void)
{
    return CODEBRIX_LLAMA_BUILD_INFO;
}

CODEBRIX_LLAMA_API const char * codebrix_llama_rid(void)
{
    return CODEBRIX_LLAMA_RID;
}
