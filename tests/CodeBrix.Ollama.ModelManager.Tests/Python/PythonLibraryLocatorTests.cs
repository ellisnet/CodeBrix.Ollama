using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Ollama.ModelManager.Tests;

/// <summary>
/// Covers the part of the Python story that needs no interpreter at all: reading a version out of a file
/// name or a configuration file, deciding whether the operating system will load a library, and finding
/// the library that belongs to a virtual environment's base interpreter.
/// </summary>
public sealed class PythonLibraryLocatorTests
{
    [Theory]
    [InlineData("libpython3.13.so", 3, 13)]
    [InlineData("/usr/lib/x86_64-linux-gnu/libpython3.13.so", 3, 13)]
    [InlineData("libpython3.13t.so", 3, 13)]
    [InlineData("libpython3.9.dylib", 3, 9)]
    [InlineData("python313.dll", 3, 13)]
    [InlineData("python39t.dll", 3, 9)]
    [InlineData("libpython3.13.so.1.0", 3, 13)]
    public void ParseVersionFromFileName_reads_the_names_a_CPython_library_carries(string path, int major, int minor)
        => PythonLibraryLocator.ParseVersionFromFileName(path).Should().Be(new Version(major, minor));

    [Theory]
    [InlineData("libcrypto.so")]
    [InlineData("python.dll")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseVersionFromFileName_returns_null_when_the_name_says_nothing(string path)
        => PythonLibraryLocator.ParseVersionFromFileName(path).Should().BeNull();

    [Theory]
    [InlineData("3.13.5", "3.13.5")]
    [InlineData("3.13", "3.13")]
    [InlineData("3.13.5.final.0", "3.13.5")]
    [InlineData("3.13.5.candidate.1", "3.13.5")]
    public void ParseConfiguredVersion_reads_both_dialects_a_configuration_file_uses(
        string value, string expected)
        => PythonLibraryLocator.ParseConfiguredVersion(value).Should().Be(Version.Parse(expected));

    [Theory]
    [InlineData("three")]
    [InlineData("3")]
    [InlineData("")]
    [InlineData(null)]
    public void ParseConfiguredVersion_returns_null_without_a_major_and_minor_pair(string value)
        => PythonLibraryLocator.ParseConfiguredVersion(value).Should().BeNull();

    [Fact]
    public void TryLoad_with_no_path_says_none_was_named()
    {
        //Act
        bool loaded = PythonLibraryLocator.TryLoad(null, out string error);

        //Assert
        loaded.Should().BeFalse();
        error.Should().Be("no library was named");
    }

    [Fact]
    public void TryLoad_with_a_missing_file_says_it_is_not_there()
    {
        //Arrange
        string missing = Path.Combine(Path.GetTempPath(), "no-such-" + Guid.NewGuid().ToString("N") + ".so");

        //Act
        bool loaded = PythonLibraryLocator.TryLoad(missing, out string error);

        //Assert
        loaded.Should().BeFalse();
        error.Should().Be("the file does not exist");
    }

    [Fact]
    public void TryLoad_with_a_file_that_is_not_a_library_answers_rather_than_throwing()
    {
        //Arrange
        string notALibrary = Path.Combine(Path.GetTempPath(), "not-a-library-" + Guid.NewGuid().ToString("N") + ".so");
        File.WriteAllText(notALibrary, "not an object file");

        try
        {
            //Act
            bool loaded = PythonLibraryLocator.TryLoad(notALibrary, out string error);

            //Assert
            loaded.Should().BeFalse();
            error.Should().NotBeNull();
        }
        finally
        {
            File.Delete(notALibrary);
        }
    }

    [Fact]
    public void ReadVersion_with_a_missing_file_returns_null()
        => PythonLibraryLocator.ReadVersion(
            Path.Combine(Path.GetTempPath(), "no-such-libpython3.13.so")).Should().BeNull();

    [Fact]
    public void ReadVersion_falls_back_to_the_file_name_when_the_file_cannot_be_called()
    {
        //Arrange
        string named = Path.Combine(Path.GetTempPath(), "libpython3.11-" + Guid.NewGuid().ToString("N"));
        string path = named + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".dll" : ".so");
        File.WriteAllText(path, "not an object file");

        try
        {
            //Act and assert
            PythonLibraryLocator.ReadVersion(path).Should().Be(new Version(3, 11));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void GetLibraryFileName_names_the_library_the_running_platform_uses()
    {
        //Arrange
        var version = new Version(3, 13, 5);

        //Act
        string ordinary = PythonLibraryLocator.GetLibraryFileName(version, false);
        string freeThreaded = PythonLibraryLocator.GetLibraryFileName(version, true);

        //Assert
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ordinary.Should().Be("python313.dll");
            freeThreaded.Should().Be("python313t.dll");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ordinary.Should().Be("libpython3.13.dylib");
            freeThreaded.Should().Be("libpython3.13t.dylib");
        }
        else
        {
            ordinary.Should().Be("libpython3.13.so");
            freeThreaded.Should().Be("libpython3.13t.so");
        }
    }

    [Fact]
    public void GetLibraryFileName_with_no_version_returns_null()
        => PythonLibraryLocator.GetLibraryFileName(null, false).Should().BeNull();

    [Theory]
    [InlineData(Architecture.X64, "x86_64-linux-gnu")]
    [InlineData(Architecture.Arm64, "aarch64-linux-gnu")]
    [InlineData(Architecture.X86, "i386-linux-gnu")]
    public void GetLinuxMultiarchTuples_names_the_folder_the_multiarch_distributions_use(
        Architecture architecture, string expected)
        => PythonLibraryLocator.GetLinuxMultiarchTuples(architecture)[0].Should().Be(expected);

    [Fact]
    public void GetLibrarySearchDirectories_is_never_empty()
        => PythonLibraryLocator.GetLibrarySearchDirectories().Should().NotBeEmpty();

    [Fact]
    public void ReadConfiguration_reads_the_key_equals_value_lines()
    {
        //Arrange
        string folder = CreateTemporaryFolder();
        string configuration = Path.Combine(folder, PythonLibraryLocator.ConfigurationFileName);
        File.WriteAllText(configuration, "home = /usr/bin\ninclude-system-site-packages = false\nversion = 3.13.5\n");

        try
        {
            //Act
            IReadOnlyDictionary<string, string> settings = PythonLibraryLocator.ReadConfiguration(configuration);

            //Assert
            settings["home"].Should().Be("/usr/bin");
            settings["version"].Should().Be("3.13.5");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void FindLibraryForConfiguration_finds_the_library_beside_the_base_interpreter()
    {
        //Arrange
        string root = CreateTemporaryFolder();
        var version = new Version(3, 13, 5);
        string home = Path.Combine(root, "base", "bin");
        string relative = PythonLibraryLocator.GetLibrarySearchDirectories()[^1];
        string libraryFolder = Path.GetFullPath(Path.Combine(home, relative));
        Directory.CreateDirectory(home);
        Directory.CreateDirectory(libraryFolder);
        string library = Path.Combine(libraryFolder, PythonLibraryLocator.GetLibraryFileName(version, false));
        File.WriteAllText(library, "pretend this is a CPython shared library");

        string venv = Path.Combine(root, "venv");
        Directory.CreateDirectory(venv);
        string configuration = Path.Combine(venv, PythonLibraryLocator.ConfigurationFileName);
        File.WriteAllText(configuration, "home = " + home + "\nversion = 3.13.5\n");

        try
        {
            //Act and assert
            PythonLibraryLocator.FindLibraryForConfiguration(configuration).Should().Be(library);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FindLibraryForConfiguration_without_a_home_key_returns_null()
    {
        //Arrange
        string folder = CreateTemporaryFolder();
        string configuration = Path.Combine(folder, PythonLibraryLocator.ConfigurationFileName);
        File.WriteAllText(configuration, "version = 3.13.5\n");

        try
        {
            //Act and assert
            PythonLibraryLocator.FindLibraryForConfiguration(configuration).Should().BeNull();
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Resolve_with_a_folder_that_is_not_a_virtual_environment_says_so()
    {
        //Arrange
        string folder = CreateTemporaryFolder();

        try
        {
            //Act
            PythonResolution resolution = PythonLibraryLocator.Resolve(
                new PythonOptions { VirtualEnvironment = folder });

            //Assert
            resolution.VirtualEnvironment.Should().Be(folder);
            resolution.VirtualEnvironmentSource.Should().Be(PythonVirtualEnvironmentSource.Code);
            string.Join(" ", resolution.Problems).Should().Contain(PythonLibraryLocator.ConfigurationFileName);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [Fact]
    public void Resolve_removes_a_trailing_separator_from_a_virtual_environment()
    {
        //Arrange
        string folder = CreateTemporaryFolder();

        try
        {
            //Act
            PythonResolution resolution = PythonLibraryLocator.Resolve(
                new PythonOptions { VirtualEnvironment = folder + Path.DirectorySeparatorChar });

            //Assert
            resolution.VirtualEnvironment.Should().Be(folder);
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    /// <summary>
    /// Makes an empty folder under the system temporary directory for one test to work in.
    /// </summary>
    /// <returns>The folder's full path.</returns>
    private static string CreateTemporaryFolder()
    {
        string folder = Path.Combine(Path.GetTempPath(), "codebrix-python-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }
}
