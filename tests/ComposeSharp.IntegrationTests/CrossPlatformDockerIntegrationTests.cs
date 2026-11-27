using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Loader;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.IntegrationTests;

/// <summary>
/// Cross-platform Docker integration matrix covering the default platform daemon endpoints
/// (<c>npipe://./pipe/docker_engine</c> on Windows and <c>unix:///var/run/docker.sock</c> on Unix)
/// across lifecycle, labels, ports, logs, volumes, and cleanup.
/// </summary>
[Trait("Category", "CrossPlatformDocker")]
public class CrossPlatformDockerIntegrationTests
{
    private const string DefaultLinuxImage = "busybox:1.36";
    private const string DefaultWindowsImage = "mcr.microsoft.com/windows/nanoserver:ltsc2022";

    private static readonly Lazy<DockerEnvironment?> DetectedEnvironment = new(ProbeDockerEnvironment);
    private static readonly SemaphoreSlim ImageGate = new(1, 1);
    private static string? _preparedImage;

    [Fact]
    public async Task DefaultEndpoint_ConnectsToPlatformDockerDaemon()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;

        var expectedEndpoint = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? "npipe://./pipe/docker_engine"
            : "unix:///var/run/docker.sock";
        Assert.Equal(expectedEndpoint, DockerClientFactory.GetDockerSocketEndpoint(null));

        var ciExpectedEndpoint = Environment.GetEnvironmentVariable("COMPOSESHARP_EXPECTED_DOCKER_ENDPOINT");
        if (!string.IsNullOrWhiteSpace(ciExpectedEndpoint))
            Assert.Equal(ciExpectedEndpoint, DockerClientFactory.GetDockerSocketEndpoint(null));

        var ciExpectedOs = Environment.GetEnvironmentVariable("COMPOSESHARP_EXPECTED_DOCKER_OS");
        if (!string.IsNullOrWhiteSpace(ciExpectedOs))
            Assert.Equal(ciExpectedOs, env.DaemonOs, ignoreCase: true);

        using var client = new DockerClientFactory().CreateClient();
        await client.System.PingAsync();
        var info = await client.System.GetSystemInfoAsync();
        Assert.False(string.IsNullOrWhiteSpace(info.ServerVersion));
        Assert.Equal(env.DaemonOs, info.OSType, ignoreCase: true);

        var service = new ComposeService();
        var stacks = await service.ListAsync();
        Assert.NotNull(stacks);
    }

    [Fact]
    public async Task Lifecycle_UpPsStopStartRestartDown_WorksOverDefaultEndpoint()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var (context, directory) = CreateTempProject("xp-lifecycle", $"""
            services:
              app:
                image: {env.Image}
                command: {env.SleepCommandYaml}
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);
            var running = Assert.Single(await service.PsAsync(context));
            Assert.Equal("app", running.Service);
            Assert.Equal("running", running.State, ignoreCase: true);

            await service.StopAsync(context, new ComposeStopOptions { TimeoutSeconds = 2 });
            var stopped = Assert.Single(await service.PsAsync(context, new ComposePsOptions { All = true }));
            Assert.Equal("exited", stopped.State, ignoreCase: true);

            await service.StartAsync(context);
            var started = Assert.Single(await service.PsAsync(context));
            Assert.Equal("running", started.State, ignoreCase: true);

            await service.RestartAsync(context, new ComposeRestartOptions { TimeoutSeconds = 2 });
            var restarted = Assert.Single(await service.PsAsync(context));
            Assert.Equal("running", restarted.State, ignoreCase: true);

            await service.DownAsync(context, new ComposeDownOptions { TimeoutSeconds = 2 });
            Assert.Empty(await service.PsAsync(context, new ComposePsOptions { All = true }));
            await AssertNoProjectResourcesAsync(context.ProjectName, checkVolumes: false);
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    [Fact]
    public async Task Labels_AppliesComposeAndCustomLabelsToProjectResources()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var (context, directory) = CreateTempProject("xp-labels", $"""
            services:
              app:
                image: {env.Image}
                command: {env.SleepCommandYaml}
                labels:
                  com.composesharp.test.suite: cross-platform
                  com.composesharp.test.os: {env.DaemonOs}
                volumes:
                  - '{env.VolumeMount("data")}'
            volumes:
              data:
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);

            using var client = new DockerClientFactory().CreateClient();
            var containers = await ListProjectContainersAsync(client, context.ProjectName);
            var inspected = await client.Containers.InspectContainerAsync(Assert.Single(containers).ID);

            Assert.Equal(context.ProjectName, inspected.Config.Labels[ComposeConstants.ProjectLabel]);
            Assert.Equal("app", inspected.Config.Labels[ComposeConstants.ServiceLabel]);
            Assert.Equal("1", inspected.Config.Labels[ComposeConstants.ContainerNumberLabel]);
            Assert.Equal("False", inspected.Config.Labels[ComposeConstants.OneOffLabel]);
            Assert.False(string.IsNullOrWhiteSpace(inspected.Config.Labels[ComposeConstants.ConfigHashLabel]));
            Assert.Equal("cross-platform", inspected.Config.Labels["com.composesharp.test.suite"]);
            Assert.Equal(env.DaemonOs, inspected.Config.Labels["com.composesharp.test.os"]);

            var networks = await ListProjectNetworksAsync(client, context.ProjectName);
            var network = Assert.Single(networks);
            Assert.Equal($"{context.ProjectName}_default", network.Name);
            Assert.Equal(context.ProjectName, network.Labels[ComposeConstants.ProjectLabel]);

            var volumes = await ListProjectVolumesAsync(client, context.ProjectName);
            var volume = Assert.Single(volumes);
            Assert.Equal($"{context.ProjectName}_data", volume.Name);
            Assert.Equal(context.ProjectName, volume.Labels[ComposeConstants.ProjectLabel]);
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    [Fact]
    public async Task Ports_AppliesPortBindingsAndReportsMappings()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var hostPort = GetAvailableTcpPort();
        var portSpec = env.IsWindowsDaemon
            ? $"{hostPort}:8080/tcp"
            : $"127.0.0.1:{hostPort}:8080/tcp";
        var rangePort = env.IsWindowsDaemon ? "" : "- target: 8081\n        published: '49152-49252'";

        var (context, directory) = CreateTempProject("xp-ports", $"""
            services:
              app:
                image: {env.Image}
                command: {env.SleepCommandYaml}
                ports:
                  - '{portSpec}'
                  - target: 5353
                    protocol: udp
                  {rangePort}
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);

            using var client = new DockerClientFactory().CreateClient();
            var containers = await ListProjectContainersAsync(client, context.ProjectName);
            var inspected = await client.Containers.InspectContainerAsync(Assert.Single(containers).ID);

            var binding = Assert.Single(inspected.HostConfig.PortBindings["8080/tcp"]);
            Assert.Equal(hostPort.ToString(), binding.HostPort);
            if (!env.IsWindowsDaemon)
                Assert.Equal("127.0.0.1", binding.HostIP);

            var reportedPort = await service.PortAsync(context, "app", 8080);
            Assert.Equal(hostPort, reportedPort.Port);
            if (!env.IsWindowsDaemon)
                Assert.Equal("127.0.0.1", reportedPort.Host);

            var udpBinding = inspected.NetworkSettings.Ports["5353/udp"].First();
            Assert.True(int.Parse(udpBinding.HostPort) > 0);
            var reportedUdp = await service.PortAsync(context, "app", 5353, new ComposePortOptions { Protocol = "udp" });
            Assert.Equal(int.Parse(udpBinding.HostPort), reportedUdp.Port);

            if (!env.IsWindowsDaemon)
            {
                var rangedPort = await service.PortAsync(context, "app", 8081);
                Assert.InRange(rangedPort.Port, 49152, 49252);
            }
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    [Fact]
    public async Task Ports_WindowsRangeIsRejectedBeforeCreatingResources()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null || !env.IsWindowsDaemon) return;

        var (context, directory) = CreateTempProject("xp-port-range", $"""
            services:
              app:
                image: {env.Image}
                ports:
                  - target: 80
                    published: '49152-49252'
            """);
        try
        {
            var error = await Assert.ThrowsAsync<ComposeValidationException>(() => new ComposeService().UpAsync(context));
            Assert.Equal(Path.Combine(directory, "compose.yaml"), error.SourceFile);
            Assert.Equal("app", error.ServiceName);
            Assert.Equal("services.app.ports[0]", error.PropertyPath);
            await AssertNoProjectResourcesAsync(context.ProjectName, checkVolumes: true);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Logs_PreservesStdoutAndStderrStreamIdentity()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var (context, directory) = CreateTempProject("xp-logs", $"""
            services:
              app:
                image: {env.Image}
                command: {env.LogStreamsCommandYaml}
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);

            using var client = new DockerClientFactory().CreateClient();
            var container = Assert.Single(await ListProjectContainersAsync(client, context.ProjectName));
            await client.Containers.WaitContainerAsync(container.ID);

            var consumer = new CapturingLogConsumer();
            await service.LogsAsync(context, new ComposeLogsOptions { Services = ["app"] }, consumer);

            var stdout = consumer.Entries.Where(entry => !entry.IsStdErr).Select(entry => entry.Message.Trim()).ToList();
            var stderr = consumer.Entries.Where(entry => entry.IsStdErr).Select(entry => entry.Message.Trim()).ToList();
            Assert.Equal(["cross-out-1", "cross-out-2"], stdout);
            Assert.Equal(["cross-err-1", "cross-err-2"], stderr);
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    [Fact]
    public async Task Volumes_MountsNamedVolumeAndPersistsDataAcrossDown()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var markerValue = $"persisted-{Guid.NewGuid():N}";
        var (context, directory) = CreateTempProject("xp-volumes", $"""
            services:
              writer:
                image: {env.Image}
                command: {env.WriteVolumeMarkerCommandYaml(markerValue)}
                volumes:
                  - '{env.VolumeMount("data")}'
              reader:
                image: {env.Image}
                command: {env.ReadVolumeMarkerCommandYaml}
                volumes:
                  - '{env.VolumeMount("data")}'
            volumes:
              data:
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context, new ComposeUpOptions { Services = ["writer"] });
            using var client = new DockerClientFactory().CreateClient();
            var writer = Assert.Single(await ListProjectContainersAsync(client, context.ProjectName));
            var waitResult = await client.Containers.WaitContainerAsync(writer.ID);
            Assert.Equal(0, waitResult.StatusCode);

            // Tear down containers and networks without removing named volumes.
            await service.DownAsync(context, new ComposeDownOptions { TimeoutSeconds = 2, RemoveVolumes = false });
            Assert.Single(await ListProjectVolumesAsync(client, context.ProjectName));

            // Start a fresh container mounting the same named volume and verify the marker persisted.
            await service.UpAsync(context, new ComposeUpOptions { Services = ["reader"] });
            var reader = Assert.Single(await ListProjectContainersAsync(client, context.ProjectName));
            var readerWait = await client.Containers.WaitContainerAsync(reader.ID);
            Assert.Equal(0, readerWait.StatusCode);

            var consumer = new CapturingLogConsumer();
            await service.LogsAsync(context, new ComposeLogsOptions { Services = ["reader"] }, consumer);
            Assert.Contains(consumer.Entries, entry => entry.Message.Contains(markerValue, StringComparison.Ordinal));
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    [Fact]
    public async Task Cleanup_DownRemovesContainersNetworksAndOptionalVolumes()
    {
        var env = GetRequiredEnvironmentOrSkip();
        if (env is null) return;
        await EnsureImageReadyAsync(env);

        var (context, directory) = CreateTempProject("xp-cleanup", $"""
            services:
              app:
                image: {env.Image}
                command: {env.SleepCommandYaml}
                volumes:
                  - '{env.VolumeMount("data")}'
            volumes:
              data:
            """);
        var service = new ComposeService();

        try
        {
            await service.UpAsync(context);

            using var client = new DockerClientFactory().CreateClient();
            Assert.Single(await ListProjectContainersAsync(client, context.ProjectName));
            Assert.Single(await ListProjectNetworksAsync(client, context.ProjectName));
            Assert.Single(await ListProjectVolumesAsync(client, context.ProjectName));

            // Default Down removes containers and project networks while retaining named volumes.
            await service.DownAsync(context, new ComposeDownOptions { TimeoutSeconds = 2, RemoveVolumes = false });
            Assert.Empty(await ListProjectContainersAsync(client, context.ProjectName));
            Assert.Empty(await ListProjectNetworksAsync(client, context.ProjectName));
            Assert.Single(await ListProjectVolumesAsync(client, context.ProjectName));

            // Down with RemoveVolumes removes the remaining project-labeled volume.
            await service.DownAsync(context, new ComposeDownOptions { TimeoutSeconds = 2, RemoveVolumes = true });
            await AssertNoProjectResourcesAsync(context.ProjectName, checkVolumes: true);
        }
        finally
        {
            await SafeCleanupAsync(service, context, directory, removeVolumes: true);
        }
    }

    private static DockerEnvironment? GetRequiredEnvironmentOrSkip()
    {
        var env = DetectedEnvironment.Value;
        if (env is not null)
            return env;

        if (IsDockerRequired())
        {
            Assert.Fail(
                $"Docker daemon is required (COMPOSESHARP_REQUIRE_DOCKER=true), but the default endpoint " +
                $"'{DockerClientFactory.GetDockerSocketEndpoint(null)}' was unreachable.");
        }

        return null;
    }

    private static bool IsDockerRequired()
    {
        var value = Environment.GetEnvironmentVariable("COMPOSESHARP_REQUIRE_DOCKER");
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static DockerEnvironment? ProbeDockerEnvironment()
    {
        try
        {
            using var client = new DockerClientFactory().CreateClient();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var info = client.System.GetSystemInfoAsync(cts.Token).GetAwaiter().GetResult();
            if (string.IsNullOrWhiteSpace(info.OSType))
                return null;

            var isWindowsDaemon = string.Equals(info.OSType, "windows", StringComparison.OrdinalIgnoreCase);
            var image = isWindowsDaemon
                ? Environment.GetEnvironmentVariable("COMPOSESHARP_WINDOWS_TEST_IMAGE") is { Length: > 0 } configuredWindows
                    ? configuredWindows
                    : DefaultWindowsImage
                : Environment.GetEnvironmentVariable("COMPOSESHARP_LINUX_TEST_IMAGE") is { Length: > 0 } configuredLinux
                    ? configuredLinux
                    : DefaultLinuxImage;

            return new DockerEnvironment(info.OSType, isWindowsDaemon, image);
        }
        catch
        {
            return null;
        }
    }

    private static async Task EnsureImageReadyAsync(DockerEnvironment env)
    {
        if (string.Equals(_preparedImage, env.Image, StringComparison.Ordinal))
            return;

        await ImageGate.WaitAsync();
        try
        {
            if (string.Equals(_preparedImage, env.Image, StringComparison.Ordinal))
                return;

            using var client = new DockerClientFactory().CreateClient();
            try
            {
                await client.Images.InspectImageAsync(env.Image);
                _preparedImage = env.Image;
                return;
            }
            catch (DockerImageNotFoundException) { }

            var (fromImage, tag) = SplitImageReference(env.Image);
            await client.Images.CreateImageAsync(
                new ImagesCreateParameters { FromImage = fromImage, Tag = tag },
                authConfig: null,
                new Progress<JSONMessage>(),
                CancellationToken.None);
            _preparedImage = env.Image;
        }
        finally
        {
            ImageGate.Release();
        }
    }

    private static (string FromImage, string Tag) SplitImageReference(string image)
    {
        var lastSlash = image.LastIndexOf('/');
        var lastColon = image.LastIndexOf(':');
        if (lastColon > lastSlash)
            return (image[..lastColon], image[(lastColon + 1)..]);

        return (image, "latest");
    }

    private static (ComposeProjectContext Context, string Directory) CreateTempProject(string prefix, string composeYaml)
    {
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var projectName = $"{prefix}-{suffix}";
        var directory = Path.Combine(Path.GetTempPath(), projectName);
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "compose.yaml"), composeYaml);

        var context = new ComposeProjectContext
        {
            ProjectName = projectName,
            WorkingDirectory = directory,
            ComposeFileName = "compose.yaml"
        };
        return (context, directory);
    }

    private static int GetAvailableTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static async Task<IList<ContainerListResponse>> ListProjectContainersAsync(DockerClient client, string projectName)
        => await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = ProjectFilter(projectName)
        });

    private static async Task<IList<NetworkResponse>> ListProjectNetworksAsync(DockerClient client, string projectName)
        => await client.Networks.ListNetworksAsync(new NetworksListParameters
        {
            Filters = ProjectFilter(projectName)
        });

    private static async Task<IList<VolumeResponse>> ListProjectVolumesAsync(DockerClient client, string projectName)
    {
        var response = await client.Volumes.ListAsync(new VolumesListParameters
        {
            Filters = ProjectFilter(projectName)
        });
        return response.Volumes ?? [];
    }

    private static Dictionary<string, IDictionary<string, bool>> ProjectFilter(string projectName)
        => new()
        {
            ["label"] = new Dictionary<string, bool>
            {
                [$"{ComposeConstants.ProjectLabel}={projectName}"] = true
            }
        };

    private static async Task AssertNoProjectResourcesAsync(string projectName, bool checkVolumes)
    {
        using var client = new DockerClientFactory().CreateClient();
        Assert.Empty(await ListProjectContainersAsync(client, projectName));
        Assert.Empty(await ListProjectNetworksAsync(client, projectName));
        if (checkVolumes)
            Assert.Empty(await ListProjectVolumesAsync(client, projectName));
    }

    private static async Task SafeCleanupAsync(ComposeService service, ComposeProjectContext context, string directory, bool removeVolumes)
    {
        try
        {
            await service.DownAsync(context, new ComposeDownOptions
            {
                TimeoutSeconds = 2,
                RemoveVolumes = removeVolumes
            });
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private sealed record DockerEnvironment(string DaemonOs, bool IsWindowsDaemon, string Image)
    {
        public string SleepCommandYaml => IsWindowsDaemon
            ? """["cmd", "/c", "ping -n 300 127.0.0.1 > nul"]"""
            : """["sleep", "300"]""";

        public string LogStreamsCommandYaml => IsWindowsDaemon
            ? """["cmd", "/c", "echo cross-out-1& (1>&2 echo cross-err-1)& echo cross-out-2& (1>&2 echo cross-err-2)"]"""
            : """["sh", "-c", "echo cross-out-1; echo cross-err-1 >&2; echo cross-out-2; echo cross-err-2 >&2"]""";

        public string VolumeMount(string volumeName) => IsWindowsDaemon
            ? $"{volumeName}:C:\\data"
            : $"{volumeName}:/data";

        public string WriteVolumeMarkerCommandYaml(string markerValue) => IsWindowsDaemon
            ? $$"""["cmd", "/c", "echo {{markerValue}}> C:\\data\\marker.txt"]"""
            : $$"""["sh", "-c", "echo {{markerValue}} > /data/marker.txt"]""";

        public string ReadVolumeMarkerCommandYaml => IsWindowsDaemon
            ? """["cmd", "/c", "type C:\\data\\marker.txt"]"""
            : """["cat", "/data/marker.txt"]""";
    }

    private sealed class CapturingLogConsumer : ILogConsumer
    {
        // Rider inspection rationale: System.Threading.Lock is unavailable on the net8.0 and net9.0 targets.
        // ReSharper disable once ChangeFieldTypeToSystemThreadingLock
        private readonly object _sync = new();
        private readonly List<(string Message, bool IsStdErr)> _entries = [];

        public IReadOnlyList<(string Message, bool IsStdErr)> Entries
        {
            get
            {
                lock (_sync)
                    return _entries.ToList();
            }
        }

        public void OnLog(string serviceName, string message, bool isStdErr)
        {
            lock (_sync)
                _entries.Add((message, isStdErr));
        }

        public void OnLogComplete(string serviceName) { }
        public void OnStatus(string serviceName, string message) { }
    }
}
