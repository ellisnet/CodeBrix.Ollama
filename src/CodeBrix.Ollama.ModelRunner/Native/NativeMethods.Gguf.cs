using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/gguf.h;

/// <summary>
/// The native entry points of the GGUF reader and writer, bound whole. The engine reads a model's own
/// metadata through these, which is how a file can be examined without loading its weights.
/// </summary>
internal static unsafe partial class NativeMethods
{
    /// <summary>Creates an empty GGUF context to write into.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr gguf_init_empty();

    /// <summary>Reads a GGUF file; returns zero when the file is not GGUF or cannot be read.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr gguf_init_from_file(string fname, GgufInitParams initParams);

    /// <summary>Reads a GGUF file from an already open C <c>FILE *</c>.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr gguf_init_from_file_ptr(IntPtr file, GgufInitParams initParams);

    /// <summary>Reads a GGUF file already in memory.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr gguf_init_from_buffer(void* data, nuint size, GgufInitParams initParams);

    /// <summary>Reads a GGUF file through a caller-supplied reader callback.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial IntPtr gguf_init_from_callback(delegate* unmanaged[Cdecl]<void*, void*, ulong, nuint, nuint> callback, void* userdata, nuint maxChunkRead, ulong maxExpectedSize, GgufInitParams initParams);

    /// <summary>Releases a GGUF context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_free(IntPtr ctx);

    /// <summary>The name of a GGUF value type, owned by the library.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* gguf_type_name(GgufType type);

    /// <summary>The file format version.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint gguf_get_version(IntPtr ctx);

    /// <summary>The tensor data alignment.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_alignment(IntPtr ctx);

    /// <summary>The offset the tensor data blob starts at.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_data_offset(IntPtr ctx);

    /// <summary>How many key/value pairs the file carries.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_get_n_kv(IntPtr ctx);

    /// <summary>The index of a key, or -1 when it is not there.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_find_key(IntPtr ctx, string key);

    /// <summary>The key at an index, owned by the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* gguf_get_key(IntPtr ctx, long keyId);

    /// <summary>The type of the value at an index.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial GgufType gguf_get_kv_type(IntPtr ctx, long keyId);

    /// <summary>The element type of the array at an index.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial GgufType gguf_get_arr_type(IntPtr ctx, long keyId);

    /// <summary>The unsigned 8-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte gguf_get_val_u8(IntPtr ctx, long keyId);

    /// <summary>The signed 8-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial sbyte gguf_get_val_i8(IntPtr ctx, long keyId);

    /// <summary>The unsigned 16-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ushort gguf_get_val_u16(IntPtr ctx, long keyId);

    /// <summary>The signed 16-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial short gguf_get_val_i16(IntPtr ctx, long keyId);

    /// <summary>The unsigned 32-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial uint gguf_get_val_u32(IntPtr ctx, long keyId);

    /// <summary>The signed 32-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial int gguf_get_val_i32(IntPtr ctx, long keyId);

    /// <summary>The 32-bit float value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial float gguf_get_val_f32(IntPtr ctx, long keyId);

    /// <summary>The unsigned 64-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial ulong gguf_get_val_u64(IntPtr ctx, long keyId);

    /// <summary>The signed 64-bit value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_get_val_i64(IntPtr ctx, long keyId);

    /// <summary>The 64-bit float value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial double gguf_get_val_f64(IntPtr ctx, long keyId);

    /// <summary>The boolean value at an index. The engine aborts if the type is wrong.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool gguf_get_val_bool(IntPtr ctx, long keyId);

    /// <summary>The string value at an index, owned by the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* gguf_get_val_str(IntPtr ctx, long keyId);

    /// <summary>A raw pointer to the value at an index.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void* gguf_get_val_data(IntPtr ctx, long keyId);

    /// <summary>How many elements the array at an index has.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_arr_n(IntPtr ctx, long keyId);

    /// <summary>A raw pointer to the first element of the array at an index. Boolean arrays are stored as bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void* gguf_get_arr_data(IntPtr ctx, long keyId);

    /// <summary>One string of the string array at an index, owned by the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* gguf_get_arr_str(IntPtr ctx, long keyId, nuint i);

    /// <summary>How many tensors the file carries.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_get_n_tensors(IntPtr ctx);

    /// <summary>The index of a tensor by name, or -1 when it is not there.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_find_tensor(IntPtr ctx, string name);

    /// <summary>A tensor's offset inside the data blob.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_tensor_offset(IntPtr ctx, long tensorId);

    /// <summary>A tensor's name, owned by the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial byte* gguf_get_tensor_name(IntPtr ctx, long tensorId);

    /// <summary>A tensor's four dimension lengths; entries past its rank are 1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long* gguf_get_tensor_ne(IntPtr ctx, long tensorId);

    /// <summary>A tensor's element type.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial GgmlType gguf_get_tensor_type(IntPtr ctx, long tensorId);

    /// <summary>A tensor's size in bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_tensor_size(IntPtr ctx, long tensorId);

    /// <summary>Removes a key and returns the index it had, or -1.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial long gguf_remove_key(IntPtr ctx, string key);

    /// <summary>Sets an unsigned 8-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_u8(IntPtr ctx, string key, byte val);

    /// <summary>Sets a signed 8-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_i8(IntPtr ctx, string key, sbyte val);

    /// <summary>Sets an unsigned 16-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_u16(IntPtr ctx, string key, ushort val);

    /// <summary>Sets a signed 16-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_i16(IntPtr ctx, string key, short val);

    /// <summary>Sets an unsigned 32-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_u32(IntPtr ctx, string key, uint val);

    /// <summary>Sets a signed 32-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_i32(IntPtr ctx, string key, int val);

    /// <summary>Sets a 32-bit float value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_f32(IntPtr ctx, string key, float val);

    /// <summary>Sets an unsigned 64-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_u64(IntPtr ctx, string key, ulong val);

    /// <summary>Sets a signed 64-bit value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_i64(IntPtr ctx, string key, long val);

    /// <summary>Sets a 64-bit float value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_f64(IntPtr ctx, string key, double val);

    /// <summary>Sets a boolean value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_bool(IntPtr ctx, string key, [MarshalAs(UnmanagedType.U1)] bool val);

    /// <summary>Sets a string value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_val_str(IntPtr ctx, string key, string val);

    /// <summary>Sets an array value from a block of elements, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_arr_data(IntPtr ctx, byte* key, GgufType type, void* data, nuint n);

    /// <summary>Sets a string-array value, replacing any earlier one.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_arr_str(IntPtr ctx, byte* key, byte** data, nuint n);

    /// <summary>Copies every key/value pair of one context into another.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_kv(IntPtr ctx, IntPtr src);

    /// <summary>Adds a tensor; its name must be unique in the context.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_add_tensor(IntPtr ctx, IntPtr tensor);

    /// <summary>Changes a tensor's element type and recomputes the later offsets.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_tensor_type(IntPtr ctx, string name, GgmlType type);

    /// <summary>Points a tensor at its data, which must hold at least <c>gguf_get_tensor_size</c> bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_set_tensor_data(IntPtr ctx, string name, void* data);

    /// <summary>Writes a context to an already open C <c>FILE *</c>.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool gguf_write_to_file_ptr(IntPtr ctx, IntPtr file, [MarshalAs(UnmanagedType.U1)] bool onlyMeta);

    /// <summary>Writes a context to a file.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static partial bool gguf_write_to_file(IntPtr ctx, string fname, [MarshalAs(UnmanagedType.U1)] bool onlyMeta);

    /// <summary>The size in bytes of the metadata block, padding included.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial nuint gguf_get_meta_size(IntPtr ctx);

    /// <summary>Copies the metadata block into a buffer of <c>gguf_get_meta_size</c> bytes.</summary>
    [LibraryImport(NativeLibraryLoader.LibraryName)]
    [UnmanagedCallConv(CallConvs = new[] { typeof(CallConvCdecl) })]
    internal static partial void gguf_get_meta_data(IntPtr ctx, void* data);
}
