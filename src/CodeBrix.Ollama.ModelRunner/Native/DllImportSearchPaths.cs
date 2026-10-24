using System.Runtime.InteropServices;

//Every native import in this assembly resolves only from the safe directories when the runtime does the
//searching itself. codebrix_llama never gets that far: NativeLibraryLoader's resolver answers for it first,
//loads only a file whose SHA-256 is in the compiled-in table, and throws rather than hand the name back to
//the runtime. What this attribute governs is the operating system's own libraries (libc, kernel32), and on
//Windows it keeps the current directory and PATH out of their search.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
