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
