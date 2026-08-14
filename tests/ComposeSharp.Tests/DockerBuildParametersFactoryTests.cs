using System.Formats.Tar;
using System.Runtime.InteropServices;
using System.Text;
using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader;

namespace ComposeSharp.Tests;

public sealed class DockerBuildParametersFactoryTests
{
    [Fact]
    public void Create_MapsSupportedBuildAndOperationSettings()
    {
        var service = LoadService("""
            services:
              app:
                image: example/app:latest
                build:
                  context: .
                  dockerfile: Containerfile
                  args:
                    MODE: debug
                    KEEP: configured
                  cache_from: [example/cache:latest]
                  target: runtime
                  tags: [example/app:stable]
                  labels:
                    build.owner: loader
                  network: host
                  extra_hosts:
                    gateway: host-gateway
                  shm_size: 64M
                  platforms: [linux/amd64]
                  pull: true
                  no_cache: true
            """);

        var parameters = DockerBuildParametersFactory.Create(service, new ComposeBuildOptions
        {
            BuildArgs = new Dictionary<string, string> { ["MODE"] = "release" },
            Labels = new Dictionary<string, string> { ["build.owner"] = "operation" },
            Target = "test",
            Platform = "linux/arm64",
            Memory = "128M"
        });

        Assert.Equal(["example/app:latest", "example/app:stable"], parameters.Tags);
        Assert.Equal("Containerfile", parameters.Dockerfile);
        Assert.Equal("release", parameters.BuildArgs!["MODE"]);
        Assert.Equal("configured", parameters.BuildArgs["KEEP"]);
        Assert.Equal("operation", parameters.Labels!["build.owner"]);
        Assert.Equal(["example/cache:latest"], parameters.CacheFrom);
        Assert.Equal("test", parameters.Target);
        Assert.Equal("linux/arm64", parameters.Platform);
        Assert.Equal("host", parameters.NetworkMode);
        Assert.Equal(["gateway:host-gateway"], parameters.ExtraHosts);
        Assert.Equal(64L * 1024 * 1024, parameters.ShmSize);
        Assert.Equal(128L * 1024 * 1024, parameters.Memory);
        Assert.Equal("true", parameters.Pull);
        Assert.True(parameters.NoCache);
    }

    [Fact]
    public void Create_RejectsUnsupportedByteFormat()
    {
        var service = LoadService("""
            services:
              app:
                build: .
            """);

        var exception = Assert.Throws<ArgumentException>(() => DockerBuildParametersFactory.Create(service, new ComposeBuildOptions { Memory = "1.5M" }));

        Assert.Contains("Memory", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateArchive_IncludesBuildFilesUsingRelativePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "src"));
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(directory, "src", "app.txt"), "content");

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            var entries = new List<string>();
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
                entries.Add(entry.Name);

            Assert.Contains("Containerfile", entries);
            Assert.Contains("src/app.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Create_ResolvesValuelessBuildArgsFromTheEnvironment()
    {
        var service = LoadService("""
            services:
              app:
                build:
                  context: .
                  args: [BUILD_TEST_VALUE, EXPLICIT_EMPTY=]
            """);
        const string environmentVariable = "BUILD_TEST_VALUE";
        var previousValue = Environment.GetEnvironmentVariable(environmentVariable);
        Environment.SetEnvironmentVariable(environmentVariable, "inherited");

        try
        {
            var parameters = DockerBuildParametersFactory.Create(service, options: null);

            Assert.Equal("inherited", parameters.BuildArgs![environmentVariable]);
            Assert.Equal(string.Empty, parameters.BuildArgs["EXPLICIT_EMPTY"]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, previousValue);
        }
    }

    [Fact]
    public void GetDockerfileArchivePath_UsesOnDiskCasingOnWindows()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(directory, "dockerfile");

            Assert.Equal("Dockerfile", archivePath);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_AppliesDockerIgnoreRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "private"));
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "*.log\n[0-9].secret\n!keep.log\nprivate/\n");
        File.WriteAllText(Path.Combine(directory, "ignored.log"), "ignored");
        File.WriteAllText(Path.Combine(directory, "keep.log"), "included");
        File.WriteAllText(Path.Combine(directory, "1.secret"), "ignored");
        File.WriteAllText(Path.Combine(directory, "private", "secret.txt"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains("keep.log", entries);
            Assert.DoesNotContain("ignored.log", entries);
            Assert.DoesNotContain("1.secret", entries);
            Assert.DoesNotContain("private", entries);
            Assert.DoesNotContain("private/secret.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_NormalizesDockerIgnorePaths()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "./secrets.env\n");
        File.WriteAllText(Path.Combine(directory, "secrets.env"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("secrets.env", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_HonorsEscapedDockerIgnoreMetacharactersOnUnix()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "report\\[old\\].txt\n");
        File.WriteAllText(Path.Combine(directory, "report[old].txt"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("report[old].txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_SkipsIgnoredDirectoryUnlessAnIncludeRuleCanRestoreEntries()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var ignoredDirectory = Path.Combine(directory, "ignored");
        Directory.CreateDirectory(ignoredDirectory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "ignored/\n!ignored/keep.txt\n");
        File.WriteAllText(Path.Combine(ignoredDirectory, "skip.txt"), "skip");
        File.WriteAllText(Path.Combine(ignoredDirectory, "keep.txt"), "keep");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("ignored/skip.txt", entries);
            Assert.Contains("ignored/keep.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_TraversesIgnoredWildcardDirectoryForIncludedDescendant()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var cacheDirectory = Path.Combine(directory, "cache1");
        Directory.CreateDirectory(cacheDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "cache*/\n!cache*/keep.txt\n");
        File.WriteAllText(Path.Combine(cacheDirectory, "skip.txt"), "skip");
        File.WriteAllText(Path.Combine(cacheDirectory, "keep.txt"), "keep");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("cache1/skip.txt", entries);
            Assert.Contains("cache1/keep.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_RetainsDockerfileNestedInIgnoredDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(directory, "docker");
        Directory.CreateDirectory(dockerDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "docker/\n");
        File.WriteAllText(Path.Combine(dockerDirectory, "Dockerfile"), "FROM scratch");

        try
        {
            var entries = ReadArchiveEntries(directory, "docker/Dockerfile");

            Assert.Contains("docker/Dockerfile", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_AppliesFirstDockerIgnoreRuleWhenFileHasUtf8Bom()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "secrets.env\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        File.WriteAllText(Path.Combine(directory, "secrets.env"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.DoesNotContain("secrets.env", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesUnixExecutablePermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "entrypoint.sh");
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.WriteAllText(scriptPath, "#!/bin/sh\n");
        File.SetUnixFileMode(scriptPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                          UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                                          UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "entrypoint.sh")
                    continue;

                Assert.True(entry.Mode.HasFlag(UnixFileMode.UserExecute));
                return;
            }

            Assert.Fail("The executable file was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesFileModificationTime()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var filePath = Path.Combine(directory, "source.txt");
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.WriteAllText(filePath, "content");
        File.SetLastWriteTimeUtc(filePath, modificationTime);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "source.txt")
                    continue;

                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The source file was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesUnixDirectoryPermissions()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var privateDirectory = Path.Combine(directory, "private");
        Directory.CreateDirectory(privateDirectory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        File.SetUnixFileMode(privateDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "private")
                    continue;

                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, entry.Mode);
                return;
            }

            Assert.Fail("The directory was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesDirectoryModificationTime()
    {
        if (OperatingSystem.IsWindows())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(directory, "source");
        Directory.CreateDirectory(sourceDirectory);
        var modificationTime = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(sourceDirectory, modificationTime);

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "source")
                    continue;

                Assert.Equal(modificationTime, entry.ModificationTime.UtcDateTime);
                return;
            }

            Assert.Fail("The source directory was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_UsesDockerfileSpecificIgnoreFileAndRetainsDockerfile()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var dockerDirectory = Path.Combine(directory, "docker");
        Directory.CreateDirectory(dockerDirectory);
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "root-only.txt\n");
        File.WriteAllText(Path.Combine(directory, "root-only.txt"), "root");
        File.WriteAllText(Path.Combine(directory, "custom-secret.txt"), "secret");
        File.WriteAllText(Path.Combine(dockerDirectory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(dockerDirectory, "Containerfile.dockerignore"), "custom-secret.txt\nContainerfile\n");

        try
        {
            var entries = ReadArchiveEntries(directory, "docker/Containerfile");

            Assert.Contains("docker/Containerfile", entries);
            Assert.Contains("root-only.txt", entries);
            Assert.DoesNotContain("custom-secret.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesDockerfileOutsideTheContext()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(contextDirectory, "app.txt"), "content");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "../Containerfile");
            var entries = ReadArchiveEntries(contextDirectory, "../Containerfile");

            Assert.Equal("__external_dockerfile__", archivePath);
            Assert.Contains(archivePath, entries);
            Assert.Contains("app.txt", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_StagesExternalDockerfileAtAnUnusedPath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var contextDirectory = Path.Combine(directory, "app");
        Directory.CreateDirectory(contextDirectory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM scratch");
        File.WriteAllText(Path.Combine(contextDirectory, "__external_dockerfile__"), "context file");

        try
        {
            var archivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, "../Containerfile");
            var entries = ReadArchiveEntries(contextDirectory, "../Containerfile");

            Assert.NotEqual("__external_dockerfile__", archivePath);
            Assert.Equal(1, entries.Count(entry => entry == archivePath));
            Assert.Contains("__external_dockerfile__", entries);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_ThrowsWhenCancellationIsRequested()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Dockerfile"), "FROM scratch");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        try
        {
            Assert.Throws<OperationCanceledException>(() => DockerBuildContextArchive.Create(directory, cancellationToken: cancellation.Token));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesSymbolicLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "target.txt"), "content");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "linked.txt"), "target.txt");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            Assert.IsType<FileStream>(archive);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "linked.txt")
                    continue;

                Assert.Equal(TarEntryType.SymbolicLink, entry.EntryType);
                Assert.Equal("target.txt", entry.LinkName);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_NormalizesRelativeSymbolicLinkTargets()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        var targetDirectory = Path.Combine(directory, "target");
        Directory.CreateDirectory(targetDirectory);
        File.WriteAllText(Path.Combine(targetDirectory, "file.txt"), "content");
        try
        {
            File.CreateSymbolicLink(Path.Combine(directory, "linked.txt"), "target\\file.txt");
        }
        catch (IOException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }
        catch (UnauthorizedAccessException)
        {
            Directory.Delete(directory, recursive: true);
            return;
        }

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "linked.txt")
                    continue;

                Assert.Equal("target/file.txt", entry.LinkName);
                return;
            }

            Assert.Fail("The symbolic link was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CreateArchive_PreservesNamedPipes()
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
            return;

        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var pipePath = Path.Combine(directory, "events");
        if (MkFifo(pipePath, 0x1A4) != 0)
            throw new IOException("Unable to create a named pipe for the archive test.");

        try
        {
            using var archive = DockerBuildContextArchive.Create(directory);
            using var reader = new TarReader(archive);
            TarEntry? entry;
            while ((entry = reader.GetNextEntry()) is not null)
            {
                if (entry.Name != "events")
                    continue;

                Assert.Equal(TarEntryType.Fifo, entry.EntryType);
                return;
            }

            Assert.Fail("The named pipe was not included in the build context archive.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [DllImport("libc", EntryPoint = "mkfifo", SetLastError = true)]
    private static extern int MkFifo(string path, uint mode);

    private static IReadOnlyList<string> ReadArchiveEntries(string directory, string? dockerfile = null)
    {
        using var archive = DockerBuildContextArchive.Create(directory, dockerfile);
        using var reader = new TarReader(archive);
        var entries = new List<string>();
        TarEntry? entry;
        while ((entry = reader.GetNextEntry()) is not null)
            entries.Add(entry.Name);
        return entries;
    }

    private static ComposeSharp.Loader.Models.ServiceDefinition LoadService(string compose)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-parameters-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "compose.yaml");
            File.WriteAllText(path, compose);
            return new ComposeFileLoader().Load(directory, "compose.yaml").Services.Single();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
