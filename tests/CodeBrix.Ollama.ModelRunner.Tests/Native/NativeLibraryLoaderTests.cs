using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelRunner.Tests;

/// <summary>
/// Covers finding and loading the native engine: the runtime identifier and file name this platform needs,
/// the order the probe paths are tried in, the message when nothing loads, and what the loaded library
/// reports about itself.
/// </summary>
public sealed class NativeLibraryLoaderTests
{
    /// <summary>The runtime identifier is computed from the running process, not from a build constant.</summary>
    [Fact]
    public void RuntimeIdentifier_matches_this_process()
    {
        //Arrange
        string expectedOs =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "win"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "osx"
            : "linux";
        string expectedArchitecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.RiscV64 => "riscv64",
            _ => "unsupported",
        };

        //Act
        string actual = NativeLibraryLoader.RuntimeIdentifier;

        //Assert
        actual.Should().Be(expectedOs + "-" + expectedArchitecture);
    }

    /// <summary>The file name follows the platform's own convention for a shared library.</summary>
    [Fact]
    public void NativeFileName_follows_the_platform_convention()
    {
        //Arrange
        string expected =
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "codebrix_llama.dll"
            : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "libcodebrix_llama.dylib"
            : "libcodebrix_llama.so";

        //Act
        string actual = NativeLibraryLoader.NativeFileName;

        //Assert
        actual.Should().Be(expected);
    }

    /// <summary>The package's own runtimes layout is probed before a loose copy beside the assembly.</summary>
    [Fact]
    public void EnumerateProbePaths_puts_the_runtimes_layout_first()
    {
        //Arrange
        IReadOnlyList<string> directories = new[] { "/base", "/other" };

        //Act
        IReadOnlyList<string> paths = NativeLibraryLoader.EnumerateProbePaths(
            directories, "osx-x64", "libcodebrix_llama.dylib");

        //Assert
        paths.Should().HaveCount(4);
        paths[0].Should().Be(Path.Combine("/base", "runtimes", "osx-x64", "native", "libcodebrix_llama.dylib"));
        paths[1].Should().Be(Path.Combine("/base", "libcodebrix_llama.dylib"));
        paths[2].Should().Be(Path.Combine("/other", "runtimes", "osx-x64", "native", "libcodebrix_llama.dylib"));
        paths[3].Should().Be(Path.Combine("/other", "libcodebrix_llama.dylib"));
    }

    /// <summary>Duplicate and empty base directories are dropped rather than probed twice.</summary>
    [Fact]
    public void EnumerateProbePaths_drops_duplicate_and_empty_directories()
    {
        //Act
        IReadOnlyList<string> paths = NativeLibraryLoader.EnumerateProbePaths(
            new[] { "/base", "", "/base", null }, "linux-x64", "libcodebrix_llama.so");

        //Assert
        paths.Should().HaveCount(2);
    }

    /// <summary>The failure message names every path that was tried, so a packaging fault can be read off it.</summary>
    [Fact]
    public void DescribeLoadFailure_lists_every_path_it_would_try()
    {
        //Arrange
        IReadOnlyList<string> directories = new[] { "/base" };

        //Act
        string message = NativeLibraryLoader.DescribeLoadFailure(directories, "osx-x64", "libcodebrix_llama.dylib");

        //Assert
        message.Should().Contain(Path.Combine("/base", "runtimes", "osx-x64", "native", "libcodebrix_llama.dylib"));
        message.Should().Contain(Path.Combine("/base", "libcodebrix_llama.dylib"));
        message.Should().Contain("the operating system's own search path");
        message.Should().Contain("runtimes/osx-x64/native/");
    }

    /// <summary>The engine loads from the runtimes layout the test output carries.</summary>
    [Fact]
    public void EnsureLoaded_finds_the_library_beside_the_test_assembly()
    {
        //Act
        NativeLibraryLoader.EnsureLoaded();

        //Assert
        NativeLibraryLoader.IsLoaded.Should().BeTrue();
        NativeLibraryLoader.LoadedPath.Should().Contain(NativeLibraryLoader.NativeFileName);
    }

    /// <summary>The loaded library is the build this binding was written against.</summary>
    [Fact]
    public void GetNativeRuntimeInfo_reports_the_expected_build()
    {
        //Act
        NativeRuntimeInfo info = ModelRunner.GetNativeRuntimeInfo();

        //Assert
        info.BuildInfo.Should().Contain("815a2a59");
        info.RuntimeIdentifier.Should().Be(NativeLibraryLoader.RuntimeIdentifier);
        info.LoadedPath.Should().Contain(NativeLibraryLoader.NativeFileName);
    }

    /// <summary>The engine reports at least one compute device and a non-empty system-information line.</summary>
    [Fact]
    public void GetNativeRuntimeInfo_reports_devices_and_system_information()
    {
        //Act
        NativeRuntimeInfo info = ModelRunner.GetNativeRuntimeInfo();

        //Assert
        info.Devices.Should().NotBeEmpty();
        info.Devices[0].Name.Should().NotBeEmpty();
        info.Devices[0].Type.Should().NotBeEmpty();
        info.SystemInfo.Should().NotBeEmpty();
    }

    /// <summary>Every platform this package ships a native for supports memory-mapped loading.</summary>
    [Fact]
    public void GetNativeRuntimeInfo_reports_memory_mapping_support()
    {
        //Act
        NativeRuntimeInfo info = ModelRunner.GetNativeRuntimeInfo();

        //Assert
        info.SupportsMemoryMapping.Should().BeTrue();
    }

    /// <summary>A library that answers to the name but exports no identity probe is reported as a load fault.</summary>
    /// <param name="entryPoint">The probe the library was asked for.</param>
    [Theory]
    [InlineData("codebrix_llama_build_info")]
    [InlineData("codebrix_llama_rid")]
    public void ProbeIdentity_reports_a_missing_export_as_a_load_fault(string entryPoint)
    {
        //Act
        NativeLibraryException failure = Assert.Throws<NativeLibraryException>(
            () => NativeLibraryLoader.ProbeIdentity(
                entryPoint, () => throw new EntryPointNotFoundException(entryPoint)));

        //Assert
        failure.Message.Should().Contain(entryPoint);
        (failure.InnerException is EntryPointNotFoundException).Should().BeTrue();
    }

    /// <summary>A library that vanishes between the load and the probe is reported the same way.</summary>
    [Fact]
    public void ProbeIdentity_reports_a_missing_library_as_a_load_fault()
    {
        //Act
        NativeLibraryException failure = Assert.Throws<NativeLibraryException>(
            () => NativeLibraryLoader.ProbeIdentity(
                "codebrix_llama_rid", () => throw new DllNotFoundException("codebrix_llama")));

        //Assert
        failure.Message.Should().Contain("codebrix_llama_rid");
        (failure.InnerException is DllNotFoundException).Should().BeTrue();
    }

    /// <summary>A probe that answers hands its value straight back.</summary>
    [Fact]
    public void ProbeIdentity_passes_a_successful_probe_through()
    {
        NativeLibraryLoader.ProbeIdentity("codebrix_llama_rid", () => "osx-x64").Should().Be("osx-x64");
    }

    /// <summary>Threads arriving together run the identity check and the backend start once between them.</summary>
    [Fact]
    public async Task EnsureLoaded_initializes_once_when_threads_arrive_together()
    {
        //Arrange
        const int Arrivals = 16;
        using Barrier barrier = new Barrier(Arrivals);
        Task[] threads = new Task[Arrivals];

        //Act
        for (int i = 0; i < Arrivals; i++)
        {
            threads[i] = Task.Factory.StartNew(
                () =>
                {
                    barrier.SignalAndWait();
                    NativeLibraryLoader.EnsureLoaded();
                },
                TestContext.Current.CancellationToken,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default);
        }

        await Task.WhenAll(threads);

        //Assert
        NativeLibraryLoader.IsLoaded.Should().BeTrue();
        NativeLibraryLoader.InitializationCount.Should().Be(1);
    }

    /// <summary>A device kind maps to the short name the public contract documents.</summary>
    [Fact]
    public void TypeName_maps_every_device_kind()
    {
        NativeRuntime.TypeName(GgmlBackendDevType.Cpu).Should().Be("CPU");
        NativeRuntime.TypeName(GgmlBackendDevType.Gpu).Should().Be("GPU");
        NativeRuntime.TypeName(GgmlBackendDevType.IGpu).Should().Be("IGPU");
        NativeRuntime.TypeName(GgmlBackendDevType.Accel).Should().Be("ACCEL");
        NativeRuntime.TypeName(GgmlBackendDevType.Meta).Should().Be("META");
        NativeRuntime.TypeName((GgmlBackendDevType)99).Should().Be("UNKNOWN");
    }
}
