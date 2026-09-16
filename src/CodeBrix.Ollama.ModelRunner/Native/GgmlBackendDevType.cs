namespace CodeBrix.Ollama.ModelRunner; //was previously: ggml-org/llama.cpp ggml/include/ggml-backend.h;


/// <summary>
/// The kind of compute device. Mirrors <c>enum ggml_backend_dev_type</c>.
/// </summary>
internal enum GgmlBackendDevType : int
{
    /// <summary>A CPU device using system memory.</summary>
    Cpu = 0,

    /// <summary>A GPU device with its own memory.</summary>
    Gpu = 1,

    /// <summary>An integrated GPU sharing host memory.</summary>
    IGpu = 2,

    /// <summary>An accelerator meant to be used alongside the CPU backend.</summary>
    Accel = 3,

    /// <summary>A device wrapping several others for tensor parallelism.</summary>
    Meta = 4,
}
