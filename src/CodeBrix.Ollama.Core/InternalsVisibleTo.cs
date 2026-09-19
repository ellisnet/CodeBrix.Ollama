using System.Runtime.CompilerServices;

// Every type in this assembly is internal, so each assembly that uses one is named here. The list is
// deliberately short - an entry is added only when something in that assembly cannot compile without it.

// The two libraries. Both reference this project and pack this assembly inside their own package.
[assembly: InternalsVisibleTo("CodeBrix.Ollama.ModelManager")]
[assembly: InternalsVisibleTo("CodeBrix.Ollama.ModelRunner")]

// This project's own test suite: the codec tests, the contract guard and the surface-hash fence.
[assembly: InternalsVisibleTo("CodeBrix.Ollama.Core.Tests")]

// The ONNX quantizer stayed in ModelManager when the codec moved here, and its tests build graphs out
// of the codec's own message classes (OnnxTensorProto, OnnxGraphProto, OnnxNodeProto and the rest).
[assembly: InternalsVisibleTo("CodeBrix.Ollama.ModelManager.Tests")]

// One live test there reads both reduction engines' output back through OnnxModel to compare them.
[assembly: InternalsVisibleTo("CodeBrix.Ollama.ModelManager.Python.Tests")]
