using System.Runtime.CompilerServices;
using System.Text;
using ComposeSharp.Api;
using ComposeSharp.Engine.Internal;
using ComposeSharp.Loader;
using ComposeSharp.Loader.Models;
using Docker.DotNet;
using Docker.DotNet.Models;
using ServiceStatus = ComposeSharp.Api.ServiceStatus;

namespace ComposeSharp.Engine;

public sealed class ComposeService : IComposeService
{
    private readonly ComposeFileLoader _loader = new();
    private readonly DockerClientFactory _clientFactory = new();
    private readonly ContainerLifecycle _containers = new();
    private readonly NetworkManager _networks = new();
    private readonly ImageManager _images = new();
    private readonly LogStreamer _logs = new();

    public async Task BuildAsync(ComposeProjectContext context, ComposeBuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        var targetServices = options?.Services is { Count: > 0 }
            ? project.Services.Where(s => options.Services.Contains(s.Name) && s.Build is not null).ToList()
            : project.Services.Where(s => s.Build is not null).ToList();

        foreach (var service in targetServices)
        {
            var build = service.Build!;
            var psi = new System.Diagnostics.ProcessStartInfo("docker", $"build -t {service.Image ?? service.Name}")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            if (build.Context is not null) psi.ArgumentList.Add(build.Context);
            if (build.Dockerfile is not null) psi.ArgumentList.Add($"--file={build.Dockerfile}");
            if (build.Target is not null) psi.ArgumentList.Add($"--target={build.Target}");
            if (options?.NoCache == true || build.NoCache == true) psi.ArgumentList.Add("--no-cache");

            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is not null) await proc.WaitForExitAsync(cancellationToken);
        }
    }

    public async Task UpAsync(ComposeProjectContext context, ComposeUpOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);

        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        var targetServices = GetOrderedServices(project, options?.Services);

        foreach (var service in targetServices)
        {
            var replicas = 1;
            if (options?.Scale is { Count: > 0 } scale && scale.TryGetValue(service.Name, out var s))
                replicas = s;
            else if (service.Deploy?.Replicas is { } r)
                replicas = r;

            if (options?.Pull == "always" && service.Image is not null)
                await _images.PullImageAsync(client, context.RegistryAuth, service.Image, cancellationToken);

            await foreach (var line in _containers.ReconcileServiceAsync(
                client, context.ProjectName, project, service, replicas,
                options?.Pull == "always", context.RegistryAuth, cancellationToken))
            {
                options?.LogConsumer?.OnStatus(service.Name, line);
            }
        }
    }

    public async Task DownAsync(ComposeProjectContext context, ComposeDownOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var timeout = options?.TimeoutSeconds.HasValue == true
            ? new ContainerStopParameters { WaitBeforeKillSeconds = (uint)options.TimeoutSeconds.Value }
            : new ContainerStopParameters { WaitBeforeKillSeconds = 10 };

        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, true, cancellationToken);

        if (options?.Services is { Count: > 0 })
        {
            containers = containers.Where(c =>
            {
                var labels = c.Labels ?? new Dictionary<string, string>();
                return labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc) && svc is not null && options.Services.Contains(svc);
            }).ToList();
        }

        foreach (var container in containers)
        {
            try { await client.Containers.StopContainerAsync(container.ID, timeout, cancellationToken); }
            catch (DockerApiException) { }
            await _containers.RemoveContainerAsync(client, container.ID, true, cancellationToken);
        }

        if (options?.Services is not { Count: > 0 })
        {
            await _networks.CleanupNetworksAsync(client, context.ProjectName, cancellationToken);
            if (options?.RemoveVolumes == true)
                await _networks.CleanupVolumesAsync(client, context.ProjectName, cancellationToken);
        }
    }

    public async Task CreateAsync(ComposeProjectContext context, ComposeCreateOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        var targetServices = GetOrderedServices(project, options?.Services);
        foreach (var service in targetServices)
        {
            var replicas = 1;
            if (options?.Scale is { Count: > 0 } scale && scale.TryGetValue(service.Name, out var s))
                replicas = s;

            var existing = await _containers.ListServiceContainersAsync(client, context.ProjectName, service.Name, true, cancellationToken);
            if (options?.NoRecreate == true && existing.Count > 0) continue;

            foreach (var c in existing)
                await _containers.RemoveContainerAsync(client, c.ID, true, cancellationToken);

            for (var i = 1; i <= replicas; i++)
            {
                var name = replicas == 1 && !string.IsNullOrWhiteSpace(service.ContainerName)
                    ? service.ContainerName!
                    : $"{context.ProjectName}-{service.Name}-{i}";
                await _containers.CreateAndStartAsync(client, context.ProjectName, project, service, name, i, false, cancellationToken);
            }
        }
    }

    public async Task StartAsync(ComposeProjectContext context, ComposeStartOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.StartContainersAsync(client, context.ProjectName, options?.Services, cancellationToken);
    }

    public async Task StopAsync(ComposeProjectContext context, ComposeStopOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.StopContainersAsync(client, context.ProjectName, options?.Services, options?.TimeoutSeconds, cancellationToken);
    }

    public async Task RestartAsync(ComposeProjectContext context, ComposeRestartOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.RestartContainersAsync(client, context.ProjectName, options?.Services, options?.TimeoutSeconds, cancellationToken);
    }

    public async Task PullAsync(ComposeProjectContext context, ComposePullOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _images.PullImagesAsync(client, project, context.RegistryAuth, options?.Services, cancellationToken);
    }

    public async Task PushAsync(ComposeProjectContext context, ComposePushOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var targetServices = options?.Services is { Count: > 0 }
            ? project.Services.Where(s => options.Services.Contains(s.Name))
            : project.Services;

        foreach (var service in targetServices)
        {
            if (service.Image is not null)
            {
                try { await _images.PushImageAsync(client, context.RegistryAuth, service.Image, cancellationToken); }
                catch (DockerApiException) when (options?.IgnoreFailures == true) { }
            }
        }
    }

    public async Task KillAsync(ComposeProjectContext context, ComposeKillOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.KillContainersAsync(client, context.ProjectName, options?.Services, options?.Signal ?? "SIGKILL", cancellationToken);
    }

    public async Task<string> RunAsync(ComposeProjectContext context, string serviceName, ComposeRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        var service = project.Services.FirstOrDefault(s => s.Name == serviceName)
            ?? throw new InvalidOperationException($"Service '{serviceName}' not found.");

        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        var name = options?.Name ?? $"{context.ProjectName}-{serviceName}-run-{Guid.NewGuid().ToString()[..8]}";
        var containerId = await _containers.CreateAndStartAsync(client, context.ProjectName, project, service, name, 0, true, cancellationToken);

        if (options?.Detach == true) return containerId;

        using var stream = await client.Containers.GetContainerLogsAsync(containerId, false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = true, Tail = "all" }, cancellationToken);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
        Console.Write(stdout);
        Console.Error.Write(stderr);

        if (options?.Remove != false)
            await _containers.RemoveContainerAsync(client, containerId, true, cancellationToken);

        return containerId;
    }

    public async Task RemoveAsync(ComposeProjectContext context, ComposeRemoveOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        if (options?.Stop == true)
            await _containers.StopContainersAsync(client, context.ProjectName, options.Services, null, cancellationToken);
        await _containers.RemoveContainersAsync(client, context.ProjectName, options?.Services, options?.Force ?? false, options?.Volumes ?? false, cancellationToken);
    }

    public async Task<ExecResult> ExecAsync(ComposeProjectContext context, string serviceName, ComposeExecOptions options, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options.Index, cancellationToken);

        var exec = await client.Exec.ExecCreateContainerAsync(container.ID, new ContainerExecCreateParameters
        {
            AttachStderr = true,
            AttachStdout = true,
            Cmd = options.Command.ToList(),
            User = options.User,
            Privileged = options.Privileged,
            WorkingDir = options.Workdir,
            Env = options.Env?.ToList()
        }, cancellationToken);

        if (options.Detach)
        {
            _ = client.Exec.StartContainerExecAsync(exec.ID, cancellationToken);
            return new ExecResult { ExitCode = 0 };
        }

        using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
        var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);
        return new ExecResult { ExitCode = (int)inspect.ExitCode, StandardOutput = stdout, StandardError = stderr };
    }

    public async Task AttachAsync(ComposeProjectContext context, string serviceName, ComposeAttachOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options?.Index, cancellationToken);

        using var stream = await client.Containers.AttachContainerAsync(container.ID, false,
            new ContainerAttachParameters { Stream = true, Stdout = options?.Stdout ?? true, Stderr = options?.Stderr ?? true, Stdin = options?.Stdin ?? false },
            cancellationToken);

        var buffer = new byte[8192];
        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (result.EOF) break;
            if (result.Count > 0)
                Console.Write(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    public Task<CopyResult> CopyAsync(ComposeProjectContext context, ComposeCopyOptions options, CancellationToken cancellationToken = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("docker", $"cp {options.Source} {options.Destination}")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit();
        return Task.FromResult(new CopyResult { BytesCopied = 0, ExitCode = proc?.ExitCode ?? -1 });
    }

    public async Task PauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.PauseContainersAsync(client, context.ProjectName, options?.Services, cancellationToken);
    }

    public async Task UnPauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.UnPauseContainersAsync(client, context.ProjectName, options?.Services, cancellationToken);
    }

    public async Task<IReadOnlyList<ContainerSummary>> PsAsync(ComposeProjectContext context, ComposePsOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, options?.All ?? true, cancellationToken);

        var result = new List<ContainerSummary>();
        foreach (var container in containers)
        {
            var labels = container.Labels ?? new Dictionary<string, string>();
            labels.TryGetValue(ComposeConstants.ServiceLabel, out var serviceName);

            if (options?.Services is { Count: > 0 } && (serviceName is null || !options.Services.Contains(serviceName)))
                continue;

            result.Add(new ContainerSummary
            {
                ID = container.ID,
                Name = container.Names?.FirstOrDefault()?.TrimStart('/') ?? container.ID[..12],
                Names = container.Names?.Select(n => n.TrimStart('/')).ToList() ?? [],
                Image = container.Image,
                Command = container.Command,
                Project = context.ProjectName,
                Service = serviceName ?? "",
                Created = new DateTimeOffset(container.Created).ToUnixTimeSeconds(),
                State = container.State,
                Status = container.Status,
                ExitCode = container.Status.Contains("Exit") ? 1 : 0,
                Publishers = (container.Ports ?? []).Select(p => new PortPublisher
                {
                    TargetPort = (int)p.PrivatePort,
                    PublishedPort = (int)p.PublicPort,
                    Protocol = p.Type ?? "tcp",
                    HostIP = p.IP
                }).ToList(),
                Labels = labels is IDictionary<string, string> dict ? dict.ToDictionary(kv => kv.Key, kv => kv.Value) : new Dictionary<string, string>(),
                Networks = container.NetworkSettings?.Networks?.Keys.ToList() ?? []
            });
        }

        return result;
    }

    public async Task<IReadOnlyList<Stack>> ListAsync(ComposeListOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient();
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = options?.All ?? false,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{ComposeConstants.ProjectLabel}"] = true }
            }
        }, cancellationToken);

        return containers
            .Select(c => c.Labels != null && c.Labels.TryGetValue(ComposeConstants.ProjectLabel, out var p) ? p : null)
            .Where(p => p is not null)
            .Distinct()
            .Select(name => new Stack { ID = name!, Name = name!, Status = "running", ConfigFiles = "" })
            .ToList();
    }

    public Task<IReadOnlyList<ContainerProcSummary>> TopAsync(ComposeProjectContext context, ComposeTopOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<ContainerProcSummary>>([]);
    }

    public async Task<IReadOnlyList<ImageSummary>> ImagesAsync(ComposeProjectContext context, ComposeImagesOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        return await _images.ListImagesAsync(client, context.ProjectName, options?.Services, cancellationToken);
    }

    public async Task<(string Host, int Port)> PortAsync(ComposeProjectContext context, string serviceName, int containerPort, ComposePortOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options?.Index, cancellationToken);

        var inspect = await client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var ports = inspect.NetworkSettings?.Ports ?? new Dictionary<string, IList<PortBinding>>();
        var key = $"{containerPort}/{options?.Protocol ?? "tcp"}";

        if (ports.TryGetValue(key, out var bindings) && bindings is { Count: > 0 })
            return (bindings[0].HostIP ?? "0.0.0.0", int.Parse(bindings[0].HostPort));

        throw new InvalidOperationException($"Port {containerPort}/{options?.Protocol ?? "tcp"} is not published for service '{serviceName}'.");
    }

    public async Task LogsAsync(ComposeProjectContext context, ComposeLogsOptions? options = null, ILogConsumer? consumer = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _logs.LogsAsync(client, context.ProjectName, options ?? new ComposeLogsOptions(), consumer, cancellationToken);
    }

    public async IAsyncEnumerable<ComposeEvent> EventsAsync(ComposeProjectContext context, ComposeEventsOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var since = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        while (!cancellationToken.IsCancellationRequested)
        {
            var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, true, cancellationToken);
            foreach (var container in containers)
            {
                var labels = container.Labels ?? new Dictionary<string, string>();
                labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc);

                if (options?.Services is { Count: > 0 } && (svc is null || !options.Services.Contains(svc)))
                    continue;

                yield return new ComposeEvent
                {
                    Timestamp = DateTime.UtcNow,
                    Type = "container",
                    Action = container.State,
                    ID = container.ID,
                    Service = svc,
                    Container = container.ID,
                    Attributes = labels is IDictionary<string, string> dict ? dict.ToDictionary(kv => kv.Key, kv => kv.Value) : null
                };
            }

            await Task.Delay(2000, cancellationToken);
        }
    }

    public async Task<IReadOnlyList<ServiceStatus>> ScaleAsync(ComposeProjectContext context, ComposeScaleOptions options, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        var result = new List<ServiceStatus>();
        foreach (var (serviceName, replicas) in options.Services)
        {
            var service = project.Services.FirstOrDefault(s => s.Name == serviceName)
                ?? throw new InvalidOperationException($"Service '{serviceName}' not found.");

            await foreach (var _ in _containers.ReconcileServiceAsync(
                client, context.ProjectName, project, service, replicas, false, context.RegistryAuth, cancellationToken)) { }

            var containers = await _containers.ListServiceContainersAsync(client, context.ProjectName, serviceName, true, cancellationToken);
            var running = containers.Count(c => string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase));
            result.Add(new ServiceStatus { ServiceName = serviceName, Desired = replicas, Running = running });
        }

        return result;
    }

    public async Task<WaitResult> WaitAsync(ComposeProjectContext context, ComposeWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, false, cancellationToken);

        if (options?.Services is { Count: > 0 })
            containers = containers.Where(c =>
            {
                var labels = c.Labels ?? new Dictionary<string, string>();
                return labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc) && svc is not null && options.Services.Contains(svc);
            }).ToList();

        var exitCodes = new Dictionary<string, int>();
        var tasks = containers.Select(async container =>
        {
            try
            {
                var result = await client.Containers.WaitContainerAsync(container.ID, cancellationToken);
                exitCodes[container.ID] = (int)result.StatusCode;
            }
            catch (DockerApiException) { exitCodes[container.ID] = -1; }
        }).ToArray();

        await Task.WhenAll(tasks);
        return new WaitResult { ExitCodes = exitCodes, Code = exitCodes.Values.FirstOrDefault(c => c != 0) };
    }

    public async IAsyncEnumerable<WatchEvent> WatchAsync(ComposeProjectContext context, ComposeWatchOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        var targetServices = options?.Services is { Count: > 0 }
            ? project.Services.Where(s => options.Services.Contains(s.Name)).ToList()
            : project.Services;

        foreach (var service in targetServices)
        {
            if (service.Build?.Context is null) continue;
            var watchDir = Path.GetFullPath(Path.Combine(context.WorkingDirectory, service.Build.Context));
            if (!Directory.Exists(watchDir)) continue;

            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.TrySetResult(true));

            using var watcher = new FileSystemWatcher(watchDir) { IncludeSubdirectories = true, EnableRaisingEvents = true };

            watcher.Changed += (_, e) =>
            {
                if (e.ChangeType == WatcherChangeTypes.Changed)
                    tcs.TrySetResult(true);
            };

            await tcs.Task;

            yield return new WatchEvent { ServiceName = service.Name, Action = "rebuild", Path = watchDir };
        }
    }

    public Task ExportAsync(ComposeProjectContext context, ComposeExportOptions options, CancellationToken cancellationToken = default)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("docker", $"export {context.ProjectName}-{options.Service}-1 -o {options.OutputPath}")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = System.Diagnostics.Process.Start(psi);
        proc?.WaitForExit();
        return Task.CompletedTask;
    }

    public Task<string> CommitAsync(ComposeProjectContext context, ComposeCommitOptions options, CancellationToken cancellationToken = default)
    {
        var reference = options.Reference ?? $"{context.ProjectName}-{options.Service}:latest";
        var args = $"commit {context.ProjectName}-{options.Service}-1 {reference}";
        if (options.Author is not null) args += $" --author \"{options.Author}\"";
        if (options.Message is not null) args += $" --message \"{options.Message}\"";

        var psi = new System.Diagnostics.ProcessStartInfo("docker", args)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        var proc = System.Diagnostics.Process.Start(psi);
        var output = proc?.StandardOutput.ReadToEnd();
        proc?.WaitForExit();
        return Task.FromResult(output?.Trim() ?? "");
    }

    public Task<string> VizAsync(ComposeProjectContext context, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        var sb = new StringBuilder();
        sb.AppendLine($"digraph {context.ProjectName} {{");
        sb.AppendLine("  rankdir=LR;");
        foreach (var service in project.Services)
        {
            sb.AppendLine($"  \"{service.Name}\" [label=\"{service.Name}\\n{service.Image ?? "build"}\"];");
            foreach (var dep in service.DependsOn)
                sb.AppendLine($"  \"{service.Name}\" -> \"{dep}\";");
        }
        sb.AppendLine("}");
        return Task.FromResult(sb.ToString());
    }

    public Task<ComposeProjectConfig> GenerateAsync(ComposeProjectContext context, ComposeGenerateOptions? options = null, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(LoadProject(context));
    }

    public async Task<IReadOnlyList<VolumesSummary>> VolumesAsync(ComposeProjectContext context, ComposeVolumesOptions? options = null, CancellationToken cancellationToken = default)
    {
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var volumes = await client.Volumes.ListAsync(new VolumesListParameters
        {
            Filters = LabelHelper.ProjectLabelFilter(context.ProjectName)
        }, cancellationToken);

        return (volumes.Volumes ?? []).Select(v => new VolumesSummary
        {
            Name = v.Name,
            Driver = v.Driver,
            Mountpoint = v.Mountpoint,
            Labels = v.Labels is IDictionary<string, string> dict ? dict.ToDictionary(kv => kv.Key, kv => kv.Value) : new Dictionary<string, string>(),
            Scope = v.Scope,
            Options = v.Options is IDictionary<string, string> opts ? opts.ToDictionary(kv => kv.Key, kv => kv.Value) : null,
            CreatedAt = v.CreatedAt
        }).ToList();
    }

    public ComposeProjectConfig LoadProject(ComposeProjectContext context)
    {
        var project = LoadProjectInternal(context);
        return new ComposeProjectConfig
        {
            Name = context.ProjectName,
            WorkingDirectory = context.WorkingDirectory,
            ConfigFiles = [context.ComposeFileName],
            Services = project.Services.Select(s => s.Name).ToList(),
            Networks = project.Networks.ToList(),
            Volumes = project.Volumes.ToList(),
            Secrets = project.Secrets.ToList(),
            Configs = project.Configs.ToList()
        };
    }

    public async Task PublishAsync(ComposeProjectContext context, string repository, ComposePublishOptions? options = null, CancellationToken cancellationToken = default)
    {
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);

        foreach (var service in project.Services)
        {
            if (service.Image is not null)
            {
                try
                {
                    await client.Images.TagImageAsync(service.Image, new ImageTagParameters { RepositoryName = repository, Tag = service.Name }, cancellationToken);
                }
                catch (DockerApiException) { }
            }
        }
    }

    private ComposeProject LoadProjectInternal(ComposeProjectContext context)
    {
        return _loader.Load(context.WorkingDirectory, context.ComposeFileName);
    }

    private static IReadOnlyList<ServiceDefinition> GetOrderedServices(ComposeProject project, IReadOnlyList<string>? services)
    {
        var filtered = services is { Count: > 0 }
            ? project.Services.Where(s => services.Contains(s.Name)).ToList()
            : project.Services.ToList();

        return OrderServices(filtered);
    }

    private static IReadOnlyList<ServiceDefinition> OrderServices(IReadOnlyList<ServiceDefinition> services)
    {
        var result = new List<ServiceDefinition>();
        var remaining = services.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        while (remaining.Count > 0)
        {
            var ready = remaining.Values
                .Where(service => service.DependsOn.All(dep => !remaining.ContainsKey(dep)))
                .ToList();
            if (ready.Count == 0)
            {
                result.AddRange(remaining.Values);
                break;
            }
            foreach (var service in ready)
            {
                result.Add(service);
                remaining.Remove(service.Name);
            }
        }
        return result;
    }
}
