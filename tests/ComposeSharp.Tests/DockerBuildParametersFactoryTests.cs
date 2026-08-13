using System.Formats.Tar;
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
    public void CreateArchive_AppliesDockerIgnoreRules()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"build-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "private"));
        File.WriteAllText(Path.Combine(directory, ".dockerignore"), "*.log\n!keep.log\nprivate/\n");
        File.WriteAllText(Path.Combine(directory, "ignored.log"), "ignored");
        File.WriteAllText(Path.Combine(directory, "keep.log"), "included");
        File.WriteAllText(Path.Combine(directory, "private", "secret.txt"), "secret");

        try
        {
            var entries = ReadArchiveEntries(directory);

            Assert.Contains("keep.log", entries);
            Assert.DoesNotContain("ignored.log", entries);
            Assert.DoesNotContain("private", entries);
            Assert.DoesNotContain("private/secret.txt", entries);
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
