using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

internal sealed class ContainerLifecycle
{
    private readonly LabelHelper _labels = new();
    private readonly NetworkManager _networks = new();

    public async Task<string> CreateAndStartAsync(
        DockerClient client, string projectName, ComposeProject project, ServiceDefinition service,
        string name, int index, bool oneOff, CancellationToken ct)
    {
        var labels = _labels.CreateServiceLabels(projectName, service, index, oneOff);
        var hostConfig = BuildHostConfig(projectName, project, service);

        var parameters = new CreateContainerParameters
        {
            Image = service.Image ?? throw new InvalidOperationException($"Service '{service.Name}' has no image."),
            Name = name,
            Cmd = service.Command.Count > 0 ? service.Command.ToList() : null,
            Entrypoint = service.Entrypoint.Count > 0 ? service.Entrypoint.ToList() : null,
            Env = service.Environment.ToList(),
            Labels = labels,
            ExposedPorts = service.Ports.ToDictionary(x => x.ContainerPort, _ => default(EmptyStruct)),
            HostConfig = hostConfig,
            Healthcheck = BuildHealthcheck(service.Healthcheck),
            Tty = service.Tty,
            OpenStdin = service.StdinOpen,
            User = service.User,
            WorkingDir = service.WorkingDir,
            Hostname = service.Hostname,
            Domainname = service.Domainname,
            MacAddress = service.MacAddress
        };

        if (service.Init == true)
            parameters.HostConfig.Init = true;

        var response = await client.Containers.CreateContainerAsync(parameters, ct);
        await client.Containers.StartContainerAsync(response.ID, new ContainerStartParameters(), ct);
        return response.ID;
    }

    public async IAsyncEnumerable<string> ReconcileServiceAsync(
        DockerClient client, string projectName, ComposeProject project, ServiceDefinition service,
        int replicas, bool pullAlways, DockerRegistryAuth? auth,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (replicas < 0) throw new ArgumentOutOfRangeException(nameof(replicas));

        if (pullAlways && service.Image is not null)
        {
            yield return $"Pulling {service.Image}";
            var imageManager = new ImageManager();
            await imageManager.PullImageAsync(client, auth, service.Image, ct);
        }

        var existing = await ListServiceContainersAsync(client, projectName, service.Name, true, ct);
        foreach (var container in existing)
        {
            await RemoveContainerAsync(client, container.ID, true, ct);
            yield return $"Removed {container.Names.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12]}";
        }

        for (var i = 1; i <= replicas; i++)
        {
            var name = replicas == 1 && !string.IsNullOrWhiteSpace(service.ContainerName)
                ? service.ContainerName!
                : $"{projectName}-{service.Name}-{i}";
            var id = await CreateAndStartAsync(client, projectName, project, service, name, i, false, ct);
            yield return $"Started {name} ({id[..12]})";
        }
    }

    public async Task StopContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, int? timeout, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, true, ct);
        var timeoutParam = new ContainerStopParameters { WaitBeforeKillSeconds = (uint)(timeout ?? 10) };
        foreach (var container in containers)
        {
            try { await client.Containers.StopContainerAsync(container.ID, timeoutParam, ct); }
            catch (DockerApiException) { }
        }
    }

    public async Task StartContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, true, ct);
        foreach (var container in containers)
        {
            try { await client.Containers.StartContainerAsync(container.ID, new ContainerStartParameters(), ct); }
            catch (DockerApiException) { }
        }
    }

    public async Task RestartContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, int? timeout, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, true, ct);
        foreach (var container in containers)
        {
            await client.Containers.RestartContainerAsync(container.ID,
                new ContainerRestartParameters { WaitBeforeKillSeconds = (uint)(timeout ?? 10) }, ct);
        }
    }

    public async Task KillContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, string signal, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, true, ct);
        foreach (var container in containers)
        {
            try { await client.Containers.KillContainerAsync(container.ID, new ContainerKillParameters { Signal = signal }, ct); }
            catch (DockerApiException) { }
        }
    }

    public async Task RemoveContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, bool force, bool removeVolumes, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, true, ct);
        foreach (var container in containers)
        {
            try
            {
                await client.Containers.RemoveContainerAsync(container.ID,
                    new ContainerRemoveParameters { Force = force, RemoveVolumes = removeVolumes }, ct);
            }
            catch (DockerApiException) { }
        }
    }

    public async Task PauseContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, false, ct);
        foreach (var container in containers)
        {
            try { await client.Containers.PauseContainerAsync(container.ID, ct); }
            catch (DockerApiException) { }
        }
    }

    public async Task UnPauseContainersAsync(DockerClient client, string projectName, IReadOnlyList<string>? services, CancellationToken ct)
    {
        var containers = await GetTargetContainersAsync(client, projectName, services, false, ct);
        foreach (var container in containers)
        {
            try { await client.Containers.UnpauseContainerAsync(container.ID, ct); }
            catch (DockerApiException) { }
        }
    }

    public async Task RemoveContainerAsync(DockerClient client, string containerId, bool force, CancellationToken ct)
    {
        try
        {
            await client.Containers.RemoveContainerAsync(containerId,
                new ContainerRemoveParameters { Force = force }, ct);
        }
        catch (DockerApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) { }
    }

    public async Task<IReadOnlyList<ContainerListResponse>> ListProjectContainersAsync(
        DockerClient client, string projectName, bool all, CancellationToken ct)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = all,
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct);
        return containers.ToList();
    }

    public async Task<IReadOnlyList<ContainerListResponse>> ListServiceContainersAsync(
        DockerClient client, string projectName, string serviceName, bool all, CancellationToken ct)
    {
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = all,
            Filters = LabelHelper.ServiceLabelFilter(projectName, serviceName)
        }, ct);
        return containers.ToList();
    }

    private async Task<IReadOnlyList<ContainerListResponse>> GetTargetContainersAsync(
        DockerClient client, string projectName, IReadOnlyList<string>? services, bool all, CancellationToken ct)
    {
        if (services is { Count: > 0 })
        {
            var allContainers = new List<ContainerListResponse>();
            foreach (var svc in services)
                allContainers.AddRange(await ListServiceContainersAsync(client, projectName, svc, all, ct));
            return allContainers;
        }
        return await ListProjectContainersAsync(client, projectName, all, ct);
    }

    public async Task<ContainerListResponse> FindRunningContainerAsync(
        DockerClient client, string projectName, string serviceName, int? index, CancellationToken ct)
    {
        var containers = await ListServiceContainersAsync(client, projectName, serviceName, false, ct);
        var running = containers.Where(c => string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase)).ToList();

        if (index.HasValue && index.Value > 0)
            return running.ElementAtOrDefault(index.Value - 1)
                ?? throw new InvalidOperationException($"No running container at index {index} for service '{serviceName}'.");

        return running.FirstOrDefault()
            ?? throw new InvalidOperationException($"No running container found for service '{serviceName}'.");
    }

    private HostConfig BuildHostConfig(string projectName, ComposeProject project, ServiceDefinition service)
    {
        return new HostConfig
        {
            Binds = service.Volumes.Select(v => ResolveVolume(projectName, project.WorkingDirectory, v)).ToList(),
            PortBindings = BuildPortBindings(service.Ports),
            RestartPolicy = BuildRestartPolicy(service.Restart),
            ExtraHosts = service.ExtraHosts.ToList(),
            Privileged = service.Privileged,
            NetworkMode = service.NetworkMode ?? NetworkManager.GetPrimaryNetworkName(projectName, project, service),
            IpcMode = service.Ipc,
            ShmSize = service.ShmSize ?? 0,
            ReadonlyRootfs = service.ReadOnly,
            Tmpfs = service.Tmpfs.Count > 0 ? service.Tmpfs.ToDictionary(t => t.Split(':').First(), t => t.Contains(':') ? t.Split(':').Last() : "") : null,
            CapAdd = service.CapAdd.Count > 0 ? service.CapAdd.ToList() : null,
            CapDrop = service.CapDrop.Count > 0 ? service.CapDrop.ToList() : null,
            Devices = service.Devices.Count > 0 ? service.Devices.Select(d => new DeviceMapping { PathOnHost = d.Split(':').First(), PathInContainer = d.Contains(':') ? d.Split(':').Last() : d }).ToList() : null,
            SecurityOpt = service.SecurityOpt.Count > 0 ? service.SecurityOpt.ToList() : null,
            PidMode = service.Pid,
            CgroupParent = service.CgroupParent,
            Memory = service.Memory ?? 0,
            MemorySwap = service.MemorySwap ?? 0,
            MemoryReservation = service.MemoryReservation ?? 0,
            CPUShares = !string.IsNullOrEmpty(service.CpuShares) && long.TryParse(service.CpuShares, out var cpuShares) ? cpuShares : 0,
            CpusetCpus = service.Cpuset,
            GroupAdd = service.GroupAdd.Count > 0 ? service.GroupAdd.ToList() : null
        };
    }

    private static Dictionary<string, IList<PortBinding>>? BuildPortBindings(IReadOnlyList<ComposePort> ports)
    {
        if (ports.Count == 0) return null;
        var result = new Dictionary<string, IList<PortBinding>>();
        foreach (var port in ports)
        {
            if (port.HostPort is null) continue;
            result[port.ContainerPort] = [new PortBinding { HostPort = port.HostPort }];
        }
        return result.Count == 0 ? null : result;
    }

    private static RestartPolicy? BuildRestartPolicy(string? restart)
    {
        if (string.IsNullOrWhiteSpace(restart)) return null;
        var normalized = restart.Split(':').First();
        var kind = normalized.ToLowerInvariant() switch
        {
            "always" => RestartPolicyKind.Always,
            "unless-stopped" => RestartPolicyKind.UnlessStopped,
            "on-failure" => RestartPolicyKind.OnFailure,
            "no" => RestartPolicyKind.No,
            _ => RestartPolicyKind.Undefined
        };
        return new RestartPolicy { Name = kind };
    }

    private static HealthConfig? BuildHealthcheck(ComposeHealthcheck? healthcheck)
    {
        if (healthcheck is null) return null;
        if (healthcheck.Disabled) return new HealthConfig { Test = ["NONE"] };
        return new HealthConfig
        {
            Test = healthcheck.Test.ToList(),
            Interval = healthcheck.Interval ?? TimeSpan.Zero,
            Timeout = healthcheck.Timeout ?? TimeSpan.Zero,
            Retries = healthcheck.Retries ?? 0,
            StartPeriod = (long)((healthcheck.StartPeriod ?? TimeSpan.Zero).TotalMilliseconds * 1_000_000)
        };
    }

    public static string ResolveVolume(string projectName, string workingDirectory, string value)
    {
        var parts = value.Split(':');
        if (parts.Length < 2) return value;

        var source = parts[0];
        if (source.StartsWith('.') || source.StartsWith('/') || source.StartsWith('\\') || source.Contains('\\') || source.Contains('/'))
            source = Path.GetFullPath(Path.Combine(workingDirectory, source));
        else
            source = NetworkManager.GetVolumeName(projectName, source);

        parts[0] = source;
        return string.Join(':', parts);
    }
}
