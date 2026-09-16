# ==============================================================================================
# llama-native-tools/wrapper/CMakeLists.txt - the codebrix_llama shared library
# ==============================================================================================
#
# WHAT THIS IS
#   Our own CMake project (never part of the vendored tree) that builds the vendored llama.cpp
#   as STATIC archives and links every one of them, whole-archive, into ONE shared library:
#
#       libcodebrix_llama.so / libcodebrix_llama.dylib / codebrix_llama.dll
#
#   plus the three small C programs this folder's verification gate uses (smoke-test,
#   generate-test-model, conformance-check). CodeBrix.Audio does the same thing with a wrapper
#   project around vendored miniaudio; the point is that the vendored tree in ../llama.cpp/
#   is NEVER edited - everything CodeBrix-specific lives here.
#
# WHY ONE LIBRARY
#   Built the upstream way (BUILD_SHARED_LIBS=ON) llama.cpp produces six shared libraries
#   (libllama, libggml, libggml-base, libggml-cpu, libggml-blas, libggml-metal) chained by
#   @rpath / RPATH. Six files means six export lists, six smoke loads and a resolver that
#   loads them in order. One file means the package, the gate and the managed resolver look
#   exactly like the family's other native packages (dav1d, pdfium, miniaudio). Proven on
#   2026-09-15 on the Intel Mac mini: static build + whole-archive link + dlopen probe.
#
# HOW IT IS DRIVEN
#   The platform scripts configure it like this (values from ../linux/pins.env):
#
#       cmake -S <this folder> -B <build dir> -G Ninja \
#             -DLLAMA_SOURCE_DIR=<scratch copy of ../llama.cpp with patches applied> \
#             -DCODEBRIX_LLAMA_RID=<rid> -DCODEBRIX_LLAMA_BUILD_INFO="<text>" \
#             $LLAMA_CMAKE_OPTIONS $LLAMA_CMAKE_OPTIONS_<ARCH>
#       cmake --build <build dir>
#
#   A hand-run configure works too (LLAMA_SOURCE_DIR may point straight at ../llama.cpp for an
#   experiment), but the settings that the gate depends on are FORCED below so they cannot be
#   forgotten: static archives, PIC, no OpenMP, no KleidiAI download, no HTTP client.
# ==============================================================================================

cmake_minimum_required(VERSION 3.24)   # $<LINK_LIBRARY:WHOLE_ARCHIVE,...> arrived in 3.24

project(codebrix_llama LANGUAGES C CXX)

# ----------------------------------------------------------------------------------------------
# Inputs
# ----------------------------------------------------------------------------------------------
set(LLAMA_SOURCE_DIR "" CACHE PATH
    "The llama.cpp tree to build: normally the scratch copy of ../llama.cpp that the platform script made.")
set(CODEBRIX_LLAMA_RID "" CACHE STRING
    "The .NET runtime identifier this build is for (win-x64, osx-arm64, linux-riscv64, ...). Baked into codebrix_llama_build_info().")
set(CODEBRIX_LLAMA_BUILD_INFO "" CACHE STRING
    "Free text baked into codebrix_llama_build_info(): upstream commit/tag, RID, build date. Set by the platform scripts from pins.env.")

if (NOT LLAMA_SOURCE_DIR OR NOT EXISTS "${LLAMA_SOURCE_DIR}/CMakeLists.txt" OR NOT EXISTS "${LLAMA_SOURCE_DIR}/ggml/CMakeLists.txt")
    message(FATAL_ERROR
        "LLAMA_SOURCE_DIR must point at a llama.cpp source tree (it is '${LLAMA_SOURCE_DIR}').\n"
        "The platform scripts set it to a scratch copy of llama-native-tools/llama.cpp/; for a "
        "hand-run configure pass -DLLAMA_SOURCE_DIR=<path to llama-native-tools/llama.cpp>.")
endif()
if (NOT CODEBRIX_LLAMA_RID)
    message(WARNING "CODEBRIX_LLAMA_RID is not set; codebrix_llama_build_info() will say 'unknown-rid'. The platform scripts always set it.")
    set(CODEBRIX_LLAMA_RID "unknown-rid")
endif()
if (NOT CODEBRIX_LLAMA_BUILD_INFO)
    set(CODEBRIX_LLAMA_BUILD_INFO "hand-run build, no build info recorded")
endif()

# ----------------------------------------------------------------------------------------------
# Settings the gate depends on, FORCED so they survive a hand-run configure. Everything else
# (baseline flags, Metal, BLAS, build type) comes from pins.env on the command line.
# ----------------------------------------------------------------------------------------------
set(BUILD_SHARED_LIBS OFF CACHE BOOL "codebrix_llama: llama and ggml are built as static archives and folded into one shared library" FORCE)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)
set(GGML_OPENMP OFF CACHE BOOL "codebrix_llama: no OpenMP - it would add a libgomp/vcomp dependency" FORCE)
set(GGML_CPU_KLEIDIAI OFF CACHE BOOL "codebrix_llama: ON would make CMake download KleidiAI from GitHub" FORCE)
set(GGML_BACKEND_DL OFF CACHE BOOL "codebrix_llama: backends are compiled in" FORCE)
set(LLAMA_OPENSSL OFF CACHE BOOL "codebrix_llama: no HTTPS client" FORCE)
set(LLAMA_CURL OFF CACHE BOOL "codebrix_llama: no HTTPS client" FORCE)
set(LLAMA_BUILD_COMMON OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_APP OFF CACHE BOOL "" FORCE)
set(LLAMA_BUILD_SERVER OFF CACHE BOOL "" FORCE)
set(GGML_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(GGML_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(LLAMA_ALL_WARNINGS OFF CACHE BOOL "" FORCE)
set(GGML_ALL_WARNINGS OFF CACHE BOOL "" FORCE)
if (NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "" FORCE)
endif()

# ----------------------------------------------------------------------------------------------
# Debug information in a RELEASE build. Release optimisation (-O3 / /O2) is unchanged; this only
# adds the symbol tables and line tables. The platform scripts STRIP the shipped library, so the
# package carries none of it - but the pre-strip twin kept under ../unstripped/ (and the .dSYM
# on macOS, the .pdb on Windows) can then symbolise a crash dump from the field. Without this
# the twins would be as useless as the stripped file (found on the first osx-x64 run:
# "dsymutil: no debug symbols in executable"). Code generation is not affected.
# ----------------------------------------------------------------------------------------------
if (MSVC)
    add_compile_options("$<$<CONFIG:Release>:/Zi>")
    add_link_options("$<$<CONFIG:Release>:/DEBUG>")
else()
    add_compile_options("$<$<CONFIG:Release>:-g>")
endif()

# ----------------------------------------------------------------------------------------------
# Windows: the public API macros (LLAMA_API / GGML_API) expand to __declspec(dllexport) only when
# both the *_SHARED and *_BUILD symbols are defined. Upstream defines them per target for a
# shared build; for our static-then-whole-archive build nothing would define them, and the DLL
# would export NOTHING. Defining all four for EVERY translation unit (the vendored ones and our
# own) makes every public symbol dllexport in its object file, and whole-archive linking carries
# those directives into codebrix_llama.dll. Nothing is ever dllimport, so there is no mismatch.
# On ELF and Mach-O the export surface is controlled by the version script / symbol list below
# instead, and these macros are not needed.
# ----------------------------------------------------------------------------------------------
if (WIN32)
    add_compile_definitions(GGML_SHARED GGML_BUILD LLAMA_SHARED LLAMA_BUILD)
endif()

# ----------------------------------------------------------------------------------------------
# The vendored tree, as a subdirectory. EXCLUDE_FROM_ALL so only what codebrix_llama pulls in
# gets built. Its targets are the static archives: llama, ggml, ggml-base, ggml-cpu, and the
# optional ggml-blas / ggml-metal on macOS.
# ----------------------------------------------------------------------------------------------
add_subdirectory("${LLAMA_SOURCE_DIR}" "${CMAKE_BINARY_DIR}/llama.cpp" EXCLUDE_FROM_ALL)

set(_codebrix_llama_archives "")
foreach (_t IN ITEMS llama ggml ggml-base ggml-cpu ggml-blas ggml-metal)
    if (TARGET ${_t})
        list(APPEND _codebrix_llama_archives ${_t})
    endif()
endforeach()
message(STATUS "codebrix_llama: folding these static archives whole-archive into the shared library: ${_codebrix_llama_archives}")

# ----------------------------------------------------------------------------------------------
# The shared library
# ----------------------------------------------------------------------------------------------
add_library(codebrix_llama SHARED codebrix_llama.c)

target_compile_definitions(codebrix_llama PRIVATE
    "CODEBRIX_LLAMA_RID=\"${CODEBRIX_LLAMA_RID}\""
    "CODEBRIX_LLAMA_BUILD_INFO=\"${CODEBRIX_LLAMA_BUILD_INFO}\"")

# Whole-archive: every object of every archive goes in, including the backend registrations
# that nothing references by name. $<LINK_LIBRARY:WHOLE_ARCHIVE,...> becomes -force_load on
# Apple ld, --whole-archive/--no-whole-archive on GNU ld / lld, and /WHOLEARCHIVE: on MSVC.
# The archives' own usage requirements (Accelerate, Metal, Threads, ...) propagate as usual.
target_link_libraries(codebrix_llama PRIVATE "$<LINK_LIBRARY:WHOLE_ARCHIVE,${_codebrix_llama_archives}>")

# Unversioned, package-unique file names - the managed LibraryImport("codebrix_llama") probes
# exactly libcodebrix_llama.so / libcodebrix_llama.dylib / codebrix_llama.dll and follows no soname.
set_target_properties(codebrix_llama PROPERTIES
    OUTPUT_NAME "codebrix_llama"
    C_VISIBILITY_PRESET hidden
    CXX_VISIBILITY_PRESET hidden
    VISIBILITY_INLINES_HIDDEN ON)

if (APPLE)
    # Install name @rpath/libcodebrix_llama.dylib - .NET's resolver loads by absolute path, but a
    # dylib whose install name is an absolute build-machine path is wrong in every other context.
    set_target_properties(codebrix_llama PROPERTIES
        MACOSX_RPATH ON
        INSTALL_NAME_DIR "@rpath"
        BUILD_WITH_INSTALL_NAME_DIR ON)
    # Export ONLY the public C API. The vendored objects were not compiled with hidden
    # visibility (they are upstream's own flags), so without this every internal C++ symbol of
    # llama and ggml would be exported too - ~thousands of them.
    target_link_options(codebrix_llama PRIVATE
        "-Wl,-exported_symbols_list,${CMAKE_CURRENT_SOURCE_DIR}/exports-macos.txt")
elseif (UNIX)
    # Same export restriction, expressed as a GNU ld / lld version script; plus:
    #   -static-libstdc++ -static-libgcc   the C++ runtime is INSIDE the .so, so the only
    #                                      dynamic dependencies are glibc's own (the allowed list
    #                                      in pins.env) - a user's distro libstdc++ is never a
    #                                      compatibility question
    #   -Wl,-z,defs                        an undefined symbol fails the link here, not at dlopen
    #   -Wl,--exclude-libs,ALL             belt and braces with the version script
    target_link_options(codebrix_llama PRIVATE
        "-Wl,--version-script=${CMAKE_CURRENT_SOURCE_DIR}/exports-linux.map"
        -static-libstdc++ -static-libgcc
        -Wl,-z,defs
        -Wl,--exclude-libs,ALL)
    target_link_libraries(codebrix_llama PRIVATE m pthread dl)
elseif (WIN32)
    # Exports come from the dllexport directives (see the add_compile_definitions above); the
    # static CRT keeps the DLL free of any Visual C++ Redistributable dependency, so KERNEL32
    # (and the other in-box system DLLs) are all `dumpbin /dependents` may list.
    set_property(TARGET codebrix_llama PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    foreach (_t IN LISTS _codebrix_llama_archives)
        set_property(TARGET ${_t} PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    endforeach()
endif()

# ----------------------------------------------------------------------------------------------
# The gate's own programs. They are NOT shipped. They link against the vendored headers only
# where they must (the model generator and the conformance checker use gguf.h / llama.h); the
# smoke test deliberately uses NO llama.cpp header - it loads the finished library the way .NET
# does and resolves every entry point by name.
# ----------------------------------------------------------------------------------------------
set(_tools_dir "${CMAKE_CURRENT_SOURCE_DIR}/..")

add_executable(smoke-test "${_tools_dir}/smoke-test.c")
if (UNIX AND NOT APPLE)
    target_link_libraries(smoke-test PRIVATE dl)
endif()

add_executable(generate-test-model "${_tools_dir}/test-vectors/generate-test-model.c")
target_include_directories(generate-test-model PRIVATE "${LLAMA_SOURCE_DIR}/ggml/include")
target_link_libraries(generate-test-model PRIVATE codebrix_llama)
if (UNIX)
    target_link_libraries(generate-test-model PRIVATE m)
endif()

add_executable(conformance-check "${_tools_dir}/test-vectors/conformance-check.c")
target_include_directories(conformance-check PRIVATE "${LLAMA_SOURCE_DIR}/include" "${LLAMA_SOURCE_DIR}/ggml/include")
target_link_libraries(conformance-check PRIVATE codebrix_llama)
if (UNIX)
    target_link_libraries(conformance-check PRIVATE m)
endif()

# On Windows the two tools link against the import library and need the DLL beside them at
# run time; the platform script copies it. On Unix they carry an rpath to the build tree.
if (APPLE)
    set_target_properties(generate-test-model conformance-check PROPERTIES
        BUILD_RPATH "@loader_path")
elseif (UNIX)
    set_target_properties(generate-test-model conformance-check PROPERTIES
        BUILD_RPATH "$ORIGIN")
endif()
