# ==============================================================================================
# exports-windows.cmake - generates the module-definition (.def) file for codebrix_llama.dll
# ==============================================================================================
#
# This is the Windows counterpart of exports-linux.map and exports-macos.txt: it decides which
# symbols leave the library. The other two are static pattern lists (llama_* ggml_* gguf_*
# codebrix_llama_*) that the linker applies to whatever the static archives contain. A Windows
# .def file cannot hold patterns, only names - so this script produces the names by reading the
# static archives that were just built, with the same rule:
#
#     every C-linkage symbol named llama_* / ggml_* / gguf_* that is DEFINED in one of the
#     archives is exported; everything else stays inside the DLL.
#
# WHY NOT __declspec(dllexport) THROUGH THE LLAMA_API / GGML_API MACROS
#   That was the first design (define GGML_SHARED GGML_BUILD LLAMA_SHARED LLAMA_BUILD for every
#   translation unit). The first real win-x64 run (2026-09-15) showed that upstream also puts
#   those macros on INTERNAL C++ functions - src/llama-ext.h (llama_graph_reserve,
#   llama_quant_*, llama_get_memory_breakdown, ...) and ggml/src/ggml-impl.h (gguf_type_size,
#   gguf_write_to_buf, ...) - so the DLL exported 22 C++-mangled names on top of the C API. On
#   Linux and macOS those never leak because mangled names do not match the llama_*/ggml_*
#   patterns; on Windows a dllexport cannot be taken back by a .def file (the linker merges
#   both). Reading the archives and listing the C-linkage names gives exactly the Unix surface.
#
# HOW IT IS CALLED (from CMakeLists.txt, as a custom command that runs after the archives
# are built and before the DLL is linked):
#
#     cmake -DDUMPBIN=<path to dumpbin.exe> -DOUTPUT=<path of the .def to write>
#           -DARCHIVE_COUNT=<n> -DARCHIVE_0=<lib> ... -DARCHIVE_<n-1>=<lib>
#           -P exports-windows.cmake
#
# dumpbin /symbols prints one line per COFF symbol, for example
#
#     0DE 00000000 SECT35 notype ()    External     | ggml_vec_dot_q4_0_q8_0     (a function)
#     1A2 00000000 SECT7  notype       External     | ggml_table_gelu_f16        (data)
#     118 00040000 UNDEF  notype       External     | ggml_table_f32_f16         (COMMON data:
#                                                       UNDEF with a non-zero size as its value)
#     118 00000000 UNDEF  notype       External     | ggml_table_f32_f16         (a reference)
#
# Taken: SECTnn External symbols (defined in that object) and UNDEF External symbols with a
# non-zero value (COFF common symbols - how MSVC emits an uninitialised C global such as
# ggml-cpu's 256 KB ggml_table_f32_f16; the linker allocates them at link time). Not taken: UNDEF
# with a zero value, which is a reference to something defined elsewhere - a .def entry with no
# definition behind it fails the link. C++ names are mangled and start with '?', so the
# llama_/ggml_/gguf_ test excludes them. Data symbols are written with the DATA keyword so an
# import library built from this .def describes them correctly. The two codebrix_llama_*
# identity functions carry their own dllexport in codebrix_llama.c and are not part of any
# archive, so they are not listed here.
# ==============================================================================================

cmake_minimum_required(VERSION 3.24)

foreach (_required IN ITEMS DUMPBIN OUTPUT ARCHIVE_COUNT)
    if (NOT DEFINED ${_required} OR "${${_required}}" STREQUAL "")
        message(FATAL_ERROR "exports-windows.cmake: -D${_required}=... is required")
    endif()
endforeach()
if (ARCHIVE_COUNT LESS 1)
    message(FATAL_ERROR "exports-windows.cmake: ARCHIVE_COUNT must be at least 1")
endif()

set(_functions "")
set(_data "")
math(EXPR _last "${ARCHIVE_COUNT} - 1")
foreach (_i RANGE 0 ${_last})
    set(_archive "${ARCHIVE_${_i}}")
    if (NOT EXISTS "${_archive}")
        message(FATAL_ERROR "exports-windows.cmake: archive ${_i} does not exist: ${_archive}")
    endif()

    set(_listing "${OUTPUT}.${_i}.symbols.txt")
    execute_process(
        COMMAND "${DUMPBIN}" /nologo /symbols "${_archive}"
        OUTPUT_FILE "${_listing}"
        RESULT_VARIABLE _rc)
    if (NOT _rc EQUAL 0)
        message(FATAL_ERROR "exports-windows.cmake: '${DUMPBIN} /symbols ${_archive}' failed with exit code ${_rc}")
    endif()

    # Keep only the lines that can possibly be wanted; the llama archive alone has ~480,000.
    file(STRINGS "${_listing}" _lines
         REGEX "^[0-9A-Fa-f]+ [0-9A-Fa-f]+ (SECT[0-9A-Fa-f]+|UNDEF) +notype( +\\(\\))? +External +\\| +(llama_|ggml_|gguf_)[A-Za-z0-9_]+ *$")

    set(_n_fn 0)
    set(_n_data 0)
    foreach (_line IN LISTS _lines)
        if (_line MATCHES "^[0-9A-Fa-f]+ [0-9A-Fa-f]+ SECT[0-9A-Fa-f]+ +notype +\\(\\) +External +\\| +((llama_|ggml_|gguf_)[A-Za-z0-9_]+) *$")
            list(APPEND _functions "${CMAKE_MATCH_1}")
            math(EXPR _n_fn "${_n_fn} + 1")
        elseif (_line MATCHES "^[0-9A-Fa-f]+ [0-9A-Fa-f]+ SECT[0-9A-Fa-f]+ +notype +External +\\| +((llama_|ggml_|gguf_)[A-Za-z0-9_]+) *$")
            list(APPEND _data "${CMAKE_MATCH_1}")
            math(EXPR _n_data "${_n_data} + 1")
        elseif (_line MATCHES "^[0-9A-Fa-f]+ ([0-9A-Fa-f]+) UNDEF +notype +External +\\| +((llama_|ggml_|gguf_)[A-Za-z0-9_]+) *$")
            # COMMON data if the value (the size) is non-zero; a plain reference otherwise.
            # (Copy the captures first: the MATCHES below overwrites CMAKE_MATCH_n.)
            set(_value "${CMAKE_MATCH_1}")
            set(_name  "${CMAKE_MATCH_2}")
            if (NOT _value MATCHES "^0+$")
                list(APPEND _data "${_name}")
                math(EXPR _n_data "${_n_data} + 1")
            endif()
        endif()
    endforeach()
    get_filename_component(_archive_name "${_archive}" NAME)
    message(STATUS "codebrix_llama exports: ${_archive_name}: ${_n_fn} functions, ${_n_data} data symbols")
    file(REMOVE "${_listing}")
endforeach()

list(REMOVE_DUPLICATES _functions)
list(SORT _functions)
list(REMOVE_DUPLICATES _data)
list(SORT _data)

# A name that is both a function in one object and data in another would be a bug in the
# source; refuse rather than guess.
foreach (_d IN LISTS _data)
    if ("${_d}" IN_LIST _functions)
        message(FATAL_ERROR "exports-windows.cmake: ${_d} appears as both a function and a data symbol")
    endif()
endforeach()

list(LENGTH _functions _n_functions)
list(LENGTH _data _n_data)
if (_n_functions LESS 100)
    # The public API alone is over 200 C functions; anything smaller means the archives were
    # not read correctly, and the resulting DLL would be useless.
    message(FATAL_ERROR "exports-windows.cmake: only ${_n_functions} llama_/ggml_/gguf_ functions found - the dumpbin output was not understood")
endif()

set(_content "; codebrix_llama.dll export list - GENERATED by wrapper/exports-windows.cmake, do not edit.\n")
string(APPEND _content "; Every C-linkage llama_* / ggml_* / gguf_* symbol defined in the static archives:\n")
string(APPEND _content "; ${_n_functions} functions, ${_n_data} data symbols. Same rule as exports-linux.map / exports-macos.txt.\n")
string(APPEND _content "EXPORTS\n")
foreach (_f IN LISTS _functions)
    string(APPEND _content "    ${_f}\n")
endforeach()
foreach (_d IN LISTS _data)
    string(APPEND _content "    ${_d} DATA\n")
endforeach()
file(WRITE "${OUTPUT}" "${_content}")
message(STATUS "codebrix_llama exports: wrote ${OUTPUT} (${_n_functions} functions, ${_n_data} data symbols)")
