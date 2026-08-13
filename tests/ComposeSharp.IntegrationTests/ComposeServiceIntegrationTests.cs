using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.IntegrationTests;

public class ComposeServiceIntegrationTests
{
    private static readonly bool DockerAvailable = CheckDockerAvailable();

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
        Directory.CreateDirectory(composeDirectory);
        File.WriteAllText(Path.Combine(composeDirectory, "Containerfile"), """
            FROM busybox:1.36 AS build
            ARG MESSAGE
            RUN printf '%s\n' "$MESSAGE" > /message

            FROM busybox:1.36 AS runtime
            COPY --from=build /message /message
            CMD ["cat", "/message"]
            """);
        File.WriteAllText(Path.Combine(composeDirectory, "compose.yaml"), $$"""
            services:
              app:
                image: {{image}}
                build:
                  context: .
                  dockerfile: Containerfile
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

    private sealed class TestLogConsumer : ILogConsumer
    {
        public List<string> Logs { get; } = [];
        public List<string> Statuses { get; } = [];

        public void OnLog(string serviceName, string message, bool isStdErr) => Logs.Add(message);
        public void OnLogComplete(string serviceName) { }
        public void OnStatus(string serviceName, string message) => Statuses.Add(message);
    }
}
