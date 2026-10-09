using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader;
using Docker.DotNet;
using Docker.DotNet.Models;
using System.Formats.Tar;

namespace ComposeSharp.IntegrationTests;

/// <summary>
/// Docker-backed smoke tests for ComposeService lifecycle, build, and profile behavior.
/// These tests require a reachable Docker daemon and return early when Docker is unavailable,
/// so a green run without Docker is not proof of Docker behavior.
/// </summary>
public class ComposeServiceIntegrationTests
{
    private static readonly bool DockerAvailable = CheckDockerAvailable();

    // Probes `docker info` once per test class; a missing CLI or unreachable daemon disables the Docker cases.
    private static bool CheckDockerAvailable()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            var proc = System.Diagnostics.Process.Start(psi);
            return proc?.WaitForExit(5000) == true && proc.ExitCode == 0;
        }
        catch { return false; }
    }

    [Fact]
    public async Task PsAsync_ReturnsEmpty_WhenNoContainers()
    {
        if (!DockerAvailable) return;

        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "test-empty-" + Guid.NewGuid().ToString()[..8],
            WorkingDirectory = Path.GetTempPath()
        };

        var result = await service.PsAsync(context);
        Assert.NotNull(result);
    }

    [Fact]
    public async Task ListAsync_ReturnsResult()
    {
        if (!DockerAvailable) return;

        var service = new ComposeService();
        var result = await service.ListAsync();
        Assert.NotNull(result);
    }

    // Pure loader path; does not need Docker, so it has no DockerAvailable guard.
    [Fact]
    public void LoadProject_ParsesConfig()
    {
        var dir = Path.Combine(Path.GetTempPath(), "compose-int-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "docker-compose.yml"), """
            services:
              web:
                image: nginx:latest
              api:
                image: node:18
            """);

        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "test",
            WorkingDirectory = dir
        };

        var config = service.LoadProject(context);
        Assert.Equal(2, config.Services.Count);
        Assert.Contains("web", config.Services);
        Assert.Contains("api", config.Services);

        Directory.Delete(dir, recursive: true);
    }

    // Operation build args override the compose build args ("configured" is replaced by "overridden").
    [Fact]
    public async Task BuildAsync_BuildsConfiguredTargetThroughDockerEngine()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"managed-build-{suffix}");
        var image = $"managed-build-{suffix}:latest";
        var alternateTag = $"managed-build-{suffix}:alternate";
        var projectName = $"managed-build-{suffix}";
        var composeDirectory = Path.Combine(directory, "deploy");
        Directory.CreateDirectory(Path.Combine(composeDirectory, "app"));
        File.WriteAllText(Path.Combine(composeDirectory, "Containerfile"), """
            FROM busybox:1.36 AS build
            ARG MESSAGE
            RUN printf '%s\n' "$MESSAGE" > /message

            FROM busybox:1.36 AS runtime
            COPY --from=build /message /message
            CMD ["cat", "/message"]
            """);
        File.WriteAllText(Path.Combine(composeDirectory, "Containerfile.dockerignore"), "Containerfile\n");
        File.WriteAllText(Path.Combine(composeDirectory, "compose.yaml"), $$"""
            services:
              app:
                image: {{image}}
                build:
                  context: app
                  dockerfile: ../Containerfile
                  args:
                    MESSAGE: configured
                  target: runtime
                  tags: [{{alternateTag}}]
            """);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "deploy/compose.yaml"
        };
        var service = new ComposeService();
        var buildLogs = new TestLogConsumer();

        try
        {
            await service.BuildAsync(context, new ComposeBuildOptions
            {
                BuildArgs = new Dictionary<string, string> { ["MESSAGE"] = "overridden" },
                NoCache = true,
                LogConsumer = buildLogs
            });

            await service.RunAsync(context, "app", new ComposeRunOptions { Tty = false });
            var runtimeLogs = new TestLogConsumer();
            await service.LogsAsync(context, new ComposeLogsOptions { Services = ["app"] }, runtimeLogs);

            Assert.Contains(buildLogs.Statuses, status => !string.IsNullOrWhiteSpace(status));
            Assert.Contains(runtimeLogs.Logs, log => log.Contains("overridden", StringComparison.Ordinal));
        }
        finally
        {
            try { await service.DownAsync(context); }
            finally
            {
                using var client = new DockerClientFactory().CreateClient();
                try
                {
                    await client.Images.DeleteImageAsync(image, new ImageDeleteParameters { Force = true }, CancellationToken.None);
                }
                catch (DockerImageNotFoundException) { }
                try
                {
                    await client.Images.DeleteImageAsync(alternateTag, new ImageDeleteParameters { Force = true }, CancellationToken.None);
                }
                catch (DockerImageNotFoundException) { }
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task BuildAsync_Throws_WhenDockerBuildReportsAnError()
    {
        if (!DockerAvailable) return;

        var directory = Path.Combine(Path.GetTempPath(), $"managed-build-failure-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Containerfile"), "FROM busybox:1.36\nRUN exit 42\n");
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
            services:
              app:
                image: managed-build-failure
                build:
                  context: .
                  dockerfile: Containerfile
            """);

        try
        {
            var service = new ComposeService();
            var context = new ComposeProjectContext
            {
                ProjectName = "managed-build-failure",
                WorkingDirectory = directory,
                ComposeFileName = "compose.yaml"
            };

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.BuildAsync(context));

            Assert.Contains("Docker build", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    // Default-context lifecycle operations act only on services selected without profiles, while an
    // explicit service name reaches profiled services; Down on the default context leaves the
    // profiled container untouched.
    [Fact]
    public async Task LifecycleOperations_ApplyProfilesAndAllowExplicitServiceSelection()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"profile-lifecycle-{suffix}");
        var projectName = $"profile-lifecycle-{suffix}";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
            services:
              app:
                image: busybox:1.36
                command: ["sleep", "300"]
              debug:
                image: busybox:1.36
                command: ["sleep", "300"]
                profiles: [debug]
            """);

        var defaultContext = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml"
        };
        var profileContext = defaultContext with { Profiles = ["debug"] };
        var service = new ComposeService();

        try
        {
            await service.UpAsync(profileContext);
            await service.StopAsync(defaultContext);

            var defaultContainers = await service.PsAsync(defaultContext, new ComposePsOptions { All = true });
            var explicitDebugContainers = await service.PsAsync(defaultContext, new ComposePsOptions
            {
                All = true,
                Services = ["debug"]
            });

            var app = Assert.Single(defaultContainers);
            var debug = Assert.Single(explicitDebugContainers);
            Assert.Equal("app", app.Service);
            Assert.Equal("exited", app.State, ignoreCase: true);
            Assert.Equal("debug", debug.Service);
            Assert.Equal("running", debug.State, ignoreCase: true);

            await service.StartAsync(defaultContext);
            app = Assert.Single(await service.PsAsync(defaultContext));
            Assert.Equal("running", app.State, ignoreCase: true);

            await service.DownAsync(defaultContext);
            Assert.Empty(await service.PsAsync(defaultContext, new ComposePsOptions { All = true }));
            Assert.Single(await service.PsAsync(defaultContext, new ComposePsOptions
            {
                All = true,
                Services = ["debug"]
            }));
        }
        finally
        {
            try { await service.DownAsync(profileContext); }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task CopyAndExportAsync_UseDockerArchiveEndpoints()
    {
        if (!DockerAvailable) return;

        var directory = Path.Combine(Path.GetTempPath(), $"archive-transfer-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var projectName = $"archive-transfer-{Guid.NewGuid():N}";
        var sourcePath = Path.Combine(directory, "fixture.txt");
        var copiedDirectory = Path.Combine(directory, "copied");
        var exportPath = Path.Combine(directory, "filesystem.tar");
        var stoppedExportPath = Path.Combine(directory, "stopped-filesystem.tar");
        const string contents = "archive-data";
        await File.WriteAllTextAsync(sourcePath, contents);
        await File.WriteAllTextAsync(Path.Combine(directory, "compose.yaml"), """
            services:
              app:
                image: busybox:1.36
                command: ["sh", "-c", "mkdir -p /data && sleep 300"]
                deploy:
                  replicas: 2
            """);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml"
        };
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);
            var intoContainer = await service.CopyAsync(context, new ComposeCopyOptions
            {
                Source = sourcePath,
                Destination = "app:/data"
            });
            Assert.Equal(contents.Length * 2, intoContainer.BytesCopied);
            Assert.Equal(0, intoContainer.ExitCode);

            var outOfContainer = await service.CopyAsync(context, new ComposeCopyOptions
            {
                Source = "app:/data",
                Destination = copiedDirectory
            });
            Assert.True(outOfContainer.IsDirectory);
            Assert.Equal(contents.Length, outOfContainer.BytesCopied);
            Assert.Equal(contents, await File.ReadAllTextAsync(Path.Combine(copiedDirectory, "data", "fixture.txt")));

            var secondReplicaDirectory = Path.Combine(directory, "copied-replica-two");
            await service.CopyAsync(context, new ComposeCopyOptions
            {
                Source = "app:/data",
                Destination = secondReplicaDirectory,
                Index = 2
            });
            Assert.Equal(contents, await File.ReadAllTextAsync(Path.Combine(secondReplicaDirectory, "data", "fixture.txt")));

            await service.ExportAsync(context, new ComposeExportOptions
            {
                Service = "app",
                OutputPath = exportPath
            });
            Assert.True(new FileInfo(exportPath).Length > 0);
            using var tar = new TarReader(File.OpenRead(exportPath));
            var hasCopiedFile = false;
            TarEntry? entry;
            while ((entry = tar.GetNextEntry()) is not null)
            {
                if (entry.Name.Replace('\\', '/').TrimStart('/').EndsWith("data/fixture.txt", StringComparison.Ordinal))
                    hasCopiedFile = true;
            }
            Assert.True(hasCopiedFile);

            await service.StopAsync(context);
            var copiedToStoppedContainer = await service.CopyAsync(context, new ComposeCopyOptions
            {
                Source = sourcePath,
                Destination = "app:/data"
            });
            Assert.Equal(contents.Length * 2, copiedToStoppedContainer.BytesCopied);

            var copiedFromStoppedContainer = await service.CopyAsync(context, new ComposeCopyOptions
            {
                Source = "app:/data",
                Destination = Path.Combine(directory, "stopped-copy")
            });
            Assert.Equal(contents, await File.ReadAllTextAsync(Path.Combine(directory, "stopped-copy", "data", "fixture.txt")));

            await service.ExportAsync(context, new ComposeExportOptions
            {
                Service = "app",
                OutputPath = stoppedExportPath
            });
            Assert.True(new FileInfo(stoppedExportPath).Length > 0);
        }
        finally
        {
            try { await service.DownAsync(context); }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    [Fact]
    public async Task CopyAsync_RejectsAmbiguousAndCancelledRequestsBeforeConnecting()
    {
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "archive-validation",
            WorkingDirectory = Path.GetTempPath()
        };

        await Assert.ThrowsAsync<ArgumentException>(() => service.CopyAsync(context, new ComposeCopyOptions
        {
            Source = "source.txt",
            Destination = "destination.txt"
        }));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CopyAsync(context, new ComposeCopyOptions
        {
            Source = "source.txt",
            Destination = "app:/data"
        }, cancellation.Token));
    }

    [Fact]
    public async Task ExportAsync_ThrowsWhenCancelledBeforeConnecting()
    {
        var service = new ComposeService();
        var context = new ComposeProjectContext
        {
            ProjectName = "archive-validation",
            WorkingDirectory = Path.GetTempPath()
        };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.ExportAsync(context, new ComposeExportOptions
        {
            Service = "app",
            OutputPath = Path.Combine(Path.GetTempPath(), "archive.tar")
        }, cancellation.Token));
    }

    [Fact]
    public async Task LifecycleOperations_DoNothing_WhenProfileSelectionIsEmpty()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"profile-empty-{suffix}");
        var projectName = $"profile-empty-{suffix}";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
            services:
              debug:
                image: busybox:1.36
                command: ["sleep", "300"]
                profiles: [debug]
            """);

        var defaultContext = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml"
        };
        var profileContext = defaultContext with { Profiles = ["debug"] };
        var service = new ComposeService();

        try
        {
            await service.UpAsync(profileContext);
            await service.StopAsync(defaultContext);

            var debug = Assert.Single(await service.PsAsync(defaultContext, new ComposePsOptions
            {
                All = true,
                Services = ["debug"]
            }));
            Assert.Equal("running", debug.State, ignoreCase: true);
        }
        finally
        {
            try { await service.DownAsync(profileContext); }
            finally { Directory.Delete(directory, recursive: true); }
        }
    }

    // Commits a running service container through the Docker Engine commit endpoint (no docker CLI).
    [Fact]
    public async Task CommitAsync_CreatesImageThroughDockerEngine()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"managed-commit-{suffix}");
        var projectName = $"managed-commit-{suffix}";
        var reference = $"managed-commit-image-{suffix}:latest";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
            services:
              app:
                image: busybox:1.36
                command: ["sleep", "300"]
            """);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml"
        };
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);
            await service.ExecAsync(context, "app", new ComposeExecOptions
            {
                Command = ["sh", "-c", "echo committed > /marker"]
            });

            var image = await service.CommitAsync(context, new ComposeCommitOptions
            {
                Service = "app",
                Reference = reference,
                Author = "ComposeSharp Tests",
                Message = "managed commit",
                Pause = true
            });

            Assert.Equal(reference, image);
            using var client = new DockerClientFactory().CreateClient();
            var inspect = await client.Images.InspectImageAsync(reference, CancellationToken.None);
            Assert.Contains(reference, inspect.RepoTags ?? []);
        }
        finally
        {
            try { await service.DownAsync(context); }
            finally
            {
                using var client = new DockerClientFactory().CreateClient();
                try
                {
                    await client.Images.DeleteImageAsync(reference, new ImageDeleteParameters { Force = true }, CancellationToken.None);
                }
                catch (DockerImageNotFoundException) { }
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // Tags every selected service image and reports per-service tag/push outcomes without aborting
    // on the first failure. The push target is unreachable, so tagging succeeds and pushing fails.
    [Fact]
    public async Task PublishAsync_ReportsTagAndPushOutcomesForEverySelectedService()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"managed-publish-{suffix}");
        var projectName = $"managed-publish-{suffix}";
        var repository = "127.0.0.1:1/composesharp-publish";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), """
            services:
              web:
                image: busybox:1.36
                command: ["sleep", "300"]
              worker:
                image: busybox:1.36
                command: ["sleep", "300"]
            """);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml",
            RegistryAuth = new DockerRegistryAuth
            {
                Username = "publisher",
                Password = "not-a-real-password"
            }
        };
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);
            var results = await service.PublishAsync(context, repository);

            Assert.Equal(2, results.Count);
            Assert.All(results, result =>
            {
                Assert.True(result.Tagged);
                Assert.False(result.Pushed);
                Assert.False(result.Succeeded);
                Assert.NotNull(result.TaggedImage);
                Assert.StartsWith($"{repository}:", result.TaggedImage, StringComparison.Ordinal);
                Assert.NotNull(result.Error);
            });
            Assert.Contains(results, result => result.Service == "web" && result.TaggedImage == $"{repository}:web");
            Assert.Contains(results, result => result.Service == "worker" && result.TaggedImage == $"{repository}:worker");
        }
        finally
        {
            try { await service.DownAsync(context); }
            finally
            {
                using var client = new DockerClientFactory().CreateClient();
                foreach (var tag in new[] { $"{repository}:web", $"{repository}:worker" })
                {
                    try
                    {
                        await client.Images.DeleteImageAsync(tag, new ImageDeleteParameters { Force = true }, CancellationToken.None);
                    }
                    catch (DockerImageNotFoundException) { }
                }
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // IgnoreFailures must swallow both Docker API errors and registry errors reported through the
    // push progress stream, so one failing service does not abort the remaining pushes.
    [Fact]
    public async Task PushAsync_IgnoreFailures_SwallowsStreamedPushErrors()
    {
        if (!DockerAvailable) return;

        var suffix = Guid.NewGuid().ToString("N")[..12];
        var directory = Path.Combine(Path.GetTempPath(), $"push-ignore-{suffix}");
        var projectName = $"push-ignore-{suffix}";
        // An unreachable registry forces a push failure after the request is issued.
        var image = $"127.0.0.1:1/composesharp-push/{suffix}:latest";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), $$"""
            services:
              app:
                image: {{image}}
                command: ["sleep", "300"]
            """);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml",
            RegistryAuth = new DockerRegistryAuth
            {
                Username = "publisher",
                Password = "not-a-real-password"
            }
        };
        var service = new ComposeService();

        try
        {
            // Materialize a local image under the unreachable registry name so push is attempted.
            using (var client = new DockerClientFactory().CreateClient())
            {
                await client.Images.CreateImageAsync(
                    new ImagesCreateParameters { FromImage = "busybox", Tag = "1.36" },
                    authConfig: null,
                    new Progress<JSONMessage>(),
                    CancellationToken.None);
                await client.Images.TagImageAsync("busybox:1.36", new ImageTagParameters
                {
                    RepositoryName = $"127.0.0.1:1/composesharp-push/{suffix}",
                    Tag = "latest"
                }, CancellationToken.None);
            }

            await Assert.ThrowsAnyAsync<Exception>(() => service.PushAsync(context));

            await service.PushAsync(context, new ComposePushOptions { IgnoreFailures = true });
        }
        finally
        {
            try { await service.DownAsync(context); }
            finally
            {
                using var client = new DockerClientFactory().CreateClient();
                try
                {
                    await client.Images.DeleteImageAsync(image, new ImageDeleteParameters { Force = true }, CancellationToken.None);
                }
                catch (DockerImageNotFoundException) { }
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    // Collects build statuses and container log lines for assertions.
    private sealed class TestLogConsumer : ILogConsumer
    {
        public List<string> Logs { get; } = [];
        public List<string> Statuses { get; } = [];

        public void OnLog(string serviceName, string message, bool isStdErr) => Logs.Add(message);
        public void OnLogComplete(string serviceName) { }
        public void OnStatus(string serviceName, string message) => Statuses.Add(message);
    }
}
