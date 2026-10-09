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

/// <summary>
/// Default <see cref="IComposeService"/> implementation backed by Docker.DotNet.
/// Loads a Compose project from disk and manages only the Docker resources labeled with that project's name.
/// </summary>
public sealed class ComposeService : IComposeService
{
    private readonly ComposeFileLoader _loader = new();
    private readonly DockerClientFactory _clientFactory = new();
    private readonly ContainerLifecycle _containers = new();
    private readonly NetworkManager _networks = new();
    private readonly ImageManager _images = new();
    private readonly LogStreamer _logs = new();

    /// <summary>
    /// Builds images for the selected services that define a <c>build</c> section.
    /// </summary>
    public async Task BuildAsync(ComposeProjectContext context, ComposeBuildOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project -> open client -> keep only services that define a build section.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var targetServices = ProfileServiceSelector
            .Select(project, context.Profiles, options?.Services)
            .Where(service => service.Build is not null)
            .ToList();

        foreach (var service in targetServices)
        {
            // Resolve the build context directory and API parameters for this service.
            var build = service.Build!;
            var contextDirectory = Path.GetFullPath(Path.Combine(project.WorkingDirectory, build.Context ?? build.ContextDirectory ?? "."));
            var parameters = DockerBuildParametersFactory.Create(service, options);
            var dockerfile = parameters.Dockerfile;
            // The build request's Dockerfile path is rewritten to the path the Dockerfile will have
            // inside the context tar stream, which also covers Dockerfiles living outside the context
            // (they are staged under a reserved archive name).
            var dockerfileArchivePath = DockerBuildContextArchive.GetDockerfileArchivePath(contextDirectory, dockerfile);
            parameters.Dockerfile = dockerfileArchivePath;
            // Stage the context tar stream, progress sink, and optional registry credentials.
            await using var archive = DockerBuildContextArchive.Create(
                contextDirectory, dockerfile, dockerfileArchivePath, cancellationToken);
            var progress = new BuildProgress(service.Name, options?.LogConsumer);
            var authConfigs = context.RegistryAuth is { } auth
                ? new[] { ImageManager.CreateAuthConfig(auth) }
                : [];

            // Execute the build, then surface any error the progress stream captured.
            await client.Images.BuildImageFromDockerfileAsync(
                parameters,
                archive,
                authConfigs,
                null,
                progress,
                cancellationToken);
            progress.ThrowIfFailed();
        }
    }

    /// <summary>
    /// Creates project networks/volumes, then reconciles each selected service to its desired replica count.
    /// </summary>
    public async Task UpAsync(ComposeProjectContext context, ComposeUpOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project -> open client.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);

        // Ensure project networks and volumes exist before any containers are created.
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        // Ordered so dependencies are created before their dependents (see OrderServices).
        var targetServices = GetOrderedServices(project, context.Profiles, options?.Services);

        foreach (var service in targetServices)
        {
            // Replica precedence: an explicit Scale option wins over deploy.replicas, which wins over 1.
            var replicas = 1;
            if (options?.Scale is { Count: > 0 } scale && scale.TryGetValue(service.Name, out var s))
                replicas = s;
            else if (service.Deploy?.Replicas is { } r)
                replicas = r;

            // Honor the "always" pull policy before reconciling the service.
            if (options?.Pull == "always" && service.Image is not null)
                await _images.PullImageAsync(client, context.RegistryAuth, service.Image, cancellationToken);

            // Reconcile containers toward the desired replica count and report progress lines.
            await foreach (var line in _containers.ReconcileServiceAsync(
                client, context.ProjectName, project, service, replicas,
                options?.Pull == "always", context.RegistryAuth, cancellationToken))
            {
                options?.LogConsumer?.OnStatus(service.Name, line);
            }
        }
    }

    /// <summary>
    /// Stops and removes the project's containers (optionally limited to a subset of services).
    /// </summary>
    /// <remarks>
    /// Networks — and volumes when <c>RemoveVolumes</c> is set — are removed only when every defined
    /// service is being torn down, so a partial down cannot break the services left running.
    /// </remarks>
    public async Task DownAsync(ComposeProjectContext context, ComposeDownOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Resolve the service selection first; a missing Compose file falls back to label-only mode.
        var selection = SelectExistingServices(context, options?.Services);
        var targetServiceNames = selection.ServiceNames?.ToHashSet(StringComparer.Ordinal);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        // Default stop grace is 10 seconds before the container is killed.
        var timeout = options?.TimeoutSeconds.HasValue == true
            ? new ContainerStopParameters { WaitBeforeKillSeconds = (uint)options.TimeoutSeconds.Value }
            : new ContainerStopParameters { WaitBeforeKillSeconds = 10 };

        // Limit the teardown to the selected services via the service label.
        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, true, cancellationToken);
        containers = FilterContainersByService(containers, targetServiceNames);

        // Stop then force-remove each container; a missing/already-stopped container is not an error.
        foreach (var container in containers)
        {
            try { await client.Containers.StopContainerAsync(container.ID, timeout, cancellationToken); }
            catch (DockerApiException) { }
            await _containers.RemoveContainerAsync(client, container.ID, true, cancellationToken);
        }

        // Networks (and volumes only when asked) are removed only for a full-project down so
        // a partial teardown cannot break services that keep running.
        if (selection.IncludesAllDefinedServices)
        {
            await _networks.CleanupNetworksAsync(client, context.ProjectName, cancellationToken);
            if (options?.RemoveVolumes == true)
                await _networks.CleanupVolumesAsync(client, context.ProjectName, cancellationToken);
        }
    }

    /// <summary>
    /// Creates and starts project containers without pulling images; existing service containers are
    /// removed first unless <c>NoRecreate</c> is set.
    /// </summary>
    public async Task CreateAsync(ComposeProjectContext context, ComposeCreateOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project -> open client -> ensure networks/volumes exist.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        // Create services in dependency order so dependents come after their depends_on targets.
        var targetServices = GetOrderedServices(project, context.Profiles, options?.Services);
        foreach (var service in targetServices)
        {
            // Replica count comes from an explicit Scale option, else default to 1.
            var replicas = 1;
            if (options?.Scale is { Count: > 0 } scale && scale.TryGetValue(service.Name, out var s))
                replicas = s;

            // Skip recreation when NoRecreate is set and the service already has containers.
            var existing = await _containers.ListServiceContainersAsync(client, context.ProjectName, service.Name, true, cancellationToken);
            if (options?.NoRecreate == true && existing.Count > 0) continue;

            // Replace existing containers rather than reconciling in place.
            foreach (var c in existing)
                await _containers.RemoveContainerAsync(client, c.ID, true, cancellationToken);

            for (var i = 1; i <= replicas; i++)
            {
                // See ContainerLifecycle.ReconcileServiceAsync: single replicas may use container_name.
                var name = replicas == 1 && !string.IsNullOrWhiteSpace(service.ContainerName)
                    ? service.ContainerName!
                    : $"{context.ProjectName}-{service.Name}-{i}";
                // Create without pulling; image pulls are PullAsync/UpAsync's concern.
                await _containers.CreateAndStartAsync(client, context.ProjectName, project, service, name, i, false, cancellationToken);
            }
        }
    }

    /// <summary>Starts existing containers of the selected services.</summary>
    public async Task StartAsync(ComposeProjectContext context, ComposeStartOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services, then start their existing containers.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.StartContainersAsync(client, context.ProjectName, services, cancellationToken);
    }

    /// <summary>Stops existing containers of the selected services, waiting up to <c>TimeoutSeconds</c> before killing them.</summary>
    public async Task StopAsync(ComposeProjectContext context, ComposeStopOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services; TimeoutSeconds null falls through to the lifecycle default grace period.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.StopContainersAsync(client, context.ProjectName, services, options?.TimeoutSeconds, cancellationToken);
    }

    /// <summary>Restarts existing containers of the selected services.</summary>
    public async Task RestartAsync(ComposeProjectContext context, ComposeRestartOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services; restart reuses the same stop-grace timeout default as StopAsync.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.RestartContainersAsync(client, context.ProjectName, services, options?.TimeoutSeconds, cancellationToken);
    }

    /// <summary>Pulls the images of the selected services.</summary>
    public async Task PullAsync(ComposeProjectContext context, ComposePullOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project -> open client -> profile-filter services, then pull their images.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var targetServices = ProfileServiceSelector.Select(project, context.Profiles, options?.Services);
        await _images.PullImagesAsync(client, targetServices, context.RegistryAuth, cancellationToken);
    }

    /// <summary>
    /// Pushes the images of the selected services to their registries.
    /// </summary>
    /// <remarks>Per-service push failures are swallowed when <c>IgnoreFailures</c> is set.</remarks>
    public async Task PushAsync(ComposeProjectContext context, ComposePushOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project -> open client -> profile-filter services.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var targetServices = ProfileServiceSelector.Select(project, context.Profiles, options?.Services);

        foreach (var service in targetServices)
        {
            // Only services with a concrete image reference can be pushed.
            if (service.Image is not null)
            {
                try { await _images.PushImageAsync(client, context.RegistryAuth, service.Image, cancellationToken); }
                catch (DockerApiException) when (options?.IgnoreFailures == true) { }
            }
        }
    }

    /// <summary>Sends a signal (default SIGKILL) to the containers of the selected services.</summary>
    public async Task KillAsync(ComposeProjectContext context, ComposeKillOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services, then signal their containers (default SIGKILL).
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.KillContainersAsync(client, context.ProjectName, services, options?.Signal ?? "SIGKILL", cancellationToken);
    }

    /// <summary>
    /// Runs a one-off container for a service and returns its container ID.
    /// </summary>
    /// <remarks>
    /// The container is labeled one-off with container number 0 so service reconciliation ignores it.
    /// Unless detached, the container's output is written to the console and the container is then
    /// removed unless <c>Remove</c> is explicitly false.
    /// </remarks>
    public async Task<string> RunAsync(ComposeProjectContext context, string serviceName, ComposeRunOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Load project and resolve the single target service (profile-filtered).
        var project = LoadProjectInternal(context);
        var service = ProfileServiceSelector.Select(project, context.Profiles, [serviceName]).SingleOrDefault()
            ?? throw new InvalidOperationException($"Service '{serviceName}' not found.");

        // One-off containers still need the project networks/volumes in place.
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        // Unique default name plus replica index 0 and the one-off label keep reconciliation from adopting this container.
        var name = options?.Name ?? $"{context.ProjectName}-{serviceName}-run-{Guid.NewGuid().ToString()[..8]}";
        var containerId = await _containers.CreateAndStartAsync(client, context.ProjectName, project, service, name, 0, true, cancellationToken);

        // Detached mode returns as soon as the container is running.
        if (options?.Detach == true) return containerId;

        // Attached mode captures the combined stdout/stderr stream and echoes it to the console.
        using var stream = await client.Containers.GetContainerLogsAsync(containerId, false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = true, Tail = "all" }, cancellationToken);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
        Console.Write(stdout);
        Console.Error.Write(stderr);

        // Default cleanup removes the one-off container unless Remove is explicitly false.
        if (options?.Remove != false)
            await _containers.RemoveContainerAsync(client, containerId, true, cancellationToken);

        return containerId;
    }

    /// <summary>Removes containers of the selected services, optionally stopping them first.</summary>
    public async Task RemoveAsync(ComposeProjectContext context, ComposeRemoveOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services; optionally stop first so removal does not hit running containers.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        if (options?.Stop == true)
            await _containers.StopContainersAsync(client, context.ProjectName, services, null, cancellationToken);
        await _containers.RemoveContainersAsync(client, context.ProjectName, services, options?.Force ?? false, options?.Volumes ?? false, cancellationToken);
    }

    /// <summary>Runs a command inside a running container of a service and returns its exit code and output.</summary>
    public async Task<ExecResult> ExecAsync(ComposeProjectContext context, string serviceName, ComposeExecOptions options, CancellationToken cancellationToken = default)
    {
        // Resolve the target running container (replica index honored).
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options.Index, cancellationToken);

        // Create the exec process with both output streams attached so they can be captured.
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
            // Detached execs are fire-and-forget: no exit code is available, so 0 is reported immediately.
            _ = client.Exec.StartContainerExecAsync(exec.ID, cancellationToken);
            return new ExecResult { ExitCode = 0 };
        }

        // Attached execs drain the multiplexed stdout/stderr stream, then read the exit code from inspect.
        using var stream = await client.Exec.StartAndAttachContainerExecAsync(exec.ID, false, cancellationToken);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(cancellationToken);
        var inspect = await client.Exec.InspectContainerExecAsync(exec.ID, cancellationToken);
        return new ExecResult { ExitCode = (int)inspect.ExitCode, StandardOutput = stdout, StandardError = stderr };
    }

    /// <summary>Streams a running container's combined output to the console until the stream ends.</summary>
    public async Task AttachAsync(ComposeProjectContext context, string serviceName, ComposeAttachOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Resolve the target running container, then open its multiplexed output stream.
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options?.Index, cancellationToken);

        using var stream = await client.Containers.AttachContainerAsync(container.ID, false,
            new ContainerAttachParameters { Stream = true, Stdout = options?.Stdout ?? true, Stderr = options?.Stderr ?? true, Stdin = options?.Stdin ?? false },
            cancellationToken);

        // Pump the stream to the console until EOF or cancellation.
        var buffer = new byte[8192];
        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, cancellationToken);
            if (result.EOF) break;
            if (result.Count > 0)
                Console.Write(Encoding.UTF8.GetString(buffer, 0, result.Count));
        }
    }

    /// <summary>
    /// Copies a file or directory between the host and a service container.
    /// </summary>
    /// <remarks>
    /// Uses the Docker Engine archive endpoints: <c>service:/path</c> forms name a container path,
    /// plain paths name a host path, and exactly one side must be a container path.
    /// </remarks>
    public async Task<CopyResult> CopyAsync(ComposeProjectContext context, ComposeCopyOptions options, CancellationToken cancellationToken = default)
    {
        // Exactly one side is a container path ("service:/path"); parse and validate both first.
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        var source = ContainerCopyPath.Parse(options.Source);
        var destination = ContainerCopyPath.Parse(options.Destination);
        if (source.IsContainer == destination.IsContainer)
            throw new ArgumentException("Exactly one copy path must use the 'service:/container/path' form.", nameof(options));

        using var client = _clientFactory.CreateClient(context.SocketPath);

        // Container → host: stream the container archive out and extract it to the host path.
        if (source.IsContainer)
        {
            var container = await _containers.FindContainerForArchiveAsync(
                client, context.ProjectName, source.Service!, options.Index, cancellationToken, options.All);
            var pathParameters = new GetArchiveFromContainerParameters { Path = source.Path! };
            var stat = await client.Containers.GetArchiveFromContainerAsync(
                container.ID,
                pathParameters,
                true,
                cancellationToken);
            var response = await client.Containers.GetArchiveFromContainerAsync(
                container.ID, pathParameters, false, cancellationToken);
            await using var archive = response.Stream;
            var bytesCopied = await ContainerArchive.ExtractToDirectoryAsync(
                archive, destination.Path!, cancellationToken);
            return new CopyResult
            {
                IsDirectory = (stat.Stat.Mode & ContainerArchive.DirectoryMode) == ContainerArchive.DirectoryMode,
                BytesCopied = bytesCopied,
                ExitCode = 0
            };
        }

        var sourcePath = Path.GetFullPath(source.Path!);
        if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            throw new FileNotFoundException("The local copy source does not exist.", sourcePath);

        await using var localArchive = await ContainerArchive.CreateFromPathAsync(sourcePath, cancellationToken);
        var bytes = ContainerArchive.GetContentLength(sourcePath);
        var targetContainers = await _containers.ListContainersForArchiveAsync(
            client, context.ProjectName, destination.Service!, cancellationToken, options.All);
        if (options.Index is { } index)
            targetContainers = [ContainerLifecycle.SelectContainerForArchive(targetContainers, destination.Service!, index)];
        else if (targetContainers.Count == 0)
            throw new InvalidOperationException($"No container found for service '{destination.Service}'.");

        foreach (var container in targetContainers)
        {
            localArchive.Position = 0;
            await client.Containers.ExtractArchiveToContainerAsync(
                container.ID,
                new ContainerPathStatParameters { Path = destination.Path!, AllowOverwriteDirWithFile = false },
                localArchive,
                cancellationToken);
        }
        return new CopyResult
        {
            IsDirectory = Directory.Exists(sourcePath),
            BytesCopied = checked(bytes * targetContainers.Count),
            ExitCode = 0
        };
    }

    /// <summary>Pauses the containers of the selected services.</summary>
    public async Task PauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services, then pause their containers.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.PauseContainersAsync(client, context.ProjectName, services, cancellationToken);
    }

    /// <summary>Resumes paused containers of the selected services.</summary>
    public async Task UnPauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services, then resume their paused containers.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _containers.UnPauseContainersAsync(client, context.ProjectName, services, cancellationToken);
    }

    /// <summary>Lists the project's containers (including stopped ones by default) as status summaries.</summary>
    public async Task<IReadOnlyList<ContainerSummary>> PsAsync(ComposeProjectContext context, ComposePsOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services; All defaults to true so stopped containers are included.
        var services = SelectExistingServices(context, options?.Services).ServiceNames?.ToHashSet(StringComparer.Ordinal);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, options?.All ?? true, cancellationToken);
        containers = FilterContainersByService(containers, services);

        // Project Docker list responses into status summaries.
        var result = new List<ContainerSummary>();
        foreach (var container in containers)
        {
            var labels = container.Labels ?? new Dictionary<string, string>();
            labels.TryGetValue(ComposeConstants.ServiceLabel, out var serviceName);

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

    /// <summary>
    /// Lists known Compose stacks by finding containers that carry the project label on this daemon.
    /// </summary>
    /// <remarks>Only label-owned resources are considered; unlabeled containers are never reported.</remarks>
    public async Task<IReadOnlyList<Stack>> ListAsync(ComposeListOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Query every container carrying the project label; unlabeled containers are never reported.
        using var client = _clientFactory.CreateClient();
        var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = options?.All ?? false,
            Filters = new Dictionary<string, IDictionary<string, bool>>
            {
                ["label"] = new Dictionary<string, bool> { [$"{ComposeConstants.ProjectLabel}"] = true }
            }
        }, cancellationToken);

        // Collapse the label values into distinct stack names.
        return containers
            .Select(c => c.Labels != null && c.Labels.TryGetValue(ComposeConstants.ProjectLabel, out var p) ? p : null)
            .Where(p => p is not null)
            .Distinct()
            .Select(name => new Stack { ID = name!, Name = name!, Status = "running", ConfigFiles = "" })
            .ToList();
    }

    /// <summary>Returns the process listings of the project's containers.</summary>
    /// <remarks>Known limitation: currently a placeholder that always returns an empty list.</remarks>
    public Task<IReadOnlyList<ContainerProcSummary>> TopAsync(ComposeProjectContext context, ComposeTopOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Placeholder: process listing is not implemented yet, so always return empty.
        return Task.FromResult<IReadOnlyList<ContainerProcSummary>>([]);
    }

    /// <summary>Lists the images used by the project's containers.</summary>
    public async Task<IReadOnlyList<ImageSummary>> ImagesAsync(ComposeProjectContext context, ComposeImagesOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Filter by selected services, then collect the images their containers use.
        var services = SelectExistingServices(context, options?.Services).ServiceNames;
        using var client = _clientFactory.CreateClient(context.SocketPath);
        return await _images.ListImagesAsync(client, context.ProjectName, services, cancellationToken);
    }

    /// <summary>Resolves the host address and port published for a container port of a running service.</summary>
    public async Task<(string Host, int Port)> PortAsync(ComposeProjectContext context, string serviceName, int containerPort, ComposePortOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Resolve the running container, then inspect its published port bindings.
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindRunningContainerAsync(client, context.ProjectName, serviceName, options?.Index, cancellationToken);

        // Match bindings by "port/protocol" (tcp when no protocol is given).
        var inspect = await client.Containers.InspectContainerAsync(container.ID, cancellationToken);
        var ports = inspect.NetworkSettings?.Ports ?? new Dictionary<string, IList<PortBinding>>();
        var key = $"{containerPort}/{options?.Protocol ?? "tcp"}";

        if (ports.TryGetValue(key, out var bindings) && bindings is { Count: > 0 })
            return (bindings[0].HostIP ?? "0.0.0.0", int.Parse(bindings[0].HostPort));

        throw new InvalidOperationException($"Port {containerPort}/{options?.Protocol ?? "tcp"} is not published for service '{serviceName}'.");
    }

    /// <summary>Streams or prints logs of the selected services' containers.</summary>
    public async Task LogsAsync(ComposeProjectContext context, ComposeLogsOptions? options = null, ILogConsumer? consumer = null, CancellationToken cancellationToken = default)
    {
        // Resolve existing services first so logs only cover selected containers.
        var effectiveOptions = (options ?? new ComposeLogsOptions()) with
        {
            Services = SelectExistingServices(context, options?.Services).ServiceNames
        };
        // LogStreamer follows the live stream when requested, otherwise dumps the retained log buffer.
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _logs.LogsAsync(client, context.ProjectName, effectiveOptions, consumer, cancellationToken);
    }

    /// <summary>Yields the current state of the project's containers as compose events.</summary>
    /// <remarks>
    /// Known limitation: this polls container state on an interval instead of subscribing to the
    /// Docker event stream, so events are snapshots of state rather than a true change feed.
    /// </remarks>
    public async IAsyncEnumerable<ComposeEvent> EventsAsync(ComposeProjectContext context, ComposeEventsOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Filter by selected services and stamp the poll window start.
        var services = SelectExistingServices(context, options?.Services).ServiceNames?.ToHashSet(StringComparer.Ordinal);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var since = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Poll loop: each cycle snapshots container state instead of subscribing to the Docker event stream.
        while (!cancellationToken.IsCancellationRequested)
        {
            var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, true, cancellationToken);
            foreach (var container in containers)
            {
                var labels = container.Labels ?? new Dictionary<string, string>();
                labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc);

                if (services is not null && (svc is null || !services.Contains(svc)))
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

            // Fixed 2s interval between snapshots.
            await Task.Delay(2000, cancellationToken);
        }
    }

    /// <summary>Adjusts the replica count of the given services and reports desired versus running containers.</summary>
    public async Task<IReadOnlyList<ServiceStatus>> ScaleAsync(ComposeProjectContext context, ComposeScaleOptions options, CancellationToken cancellationToken = default)
    {
        // Load project -> open client -> ensure infrastructure so new replicas can attach.
        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        await _networks.EnsureProjectInfrastructureAsync(client, context.ProjectName, project, cancellationToken);

        // For each requested service, reconcile toward the new replica count and report desired vs running.
        var result = new List<ServiceStatus>();
        foreach (var (serviceName, replicas) in options.Services)
        {
            var service = ProfileServiceSelector.Select(project, context.Profiles, [serviceName]).SingleOrDefault()
                ?? throw new InvalidOperationException($"Service '{serviceName}' not found.");

            // Reconcile creates missing replicas and removes surplus ones (replica diffing lives in the lifecycle helper).
            await foreach (var _ in _containers.ReconcileServiceAsync(
                client, context.ProjectName, project, service, replicas, false, context.RegistryAuth, cancellationToken)) { }

            var containers = await _containers.ListServiceContainersAsync(client, context.ProjectName, serviceName, true, cancellationToken);
            var running = containers.Count(c => string.Equals(c.State, "running", StringComparison.OrdinalIgnoreCase));
            result.Add(new ServiceStatus { ServiceName = serviceName, Desired = replicas, Running = running });
        }

        return result;
    }

    /// <summary>Waits for the selected services' containers to exit and returns their exit codes.</summary>
    public async Task<WaitResult> WaitAsync(ComposeProjectContext context, ComposeWaitOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Only running containers of the selected services are awaited.
        var services = SelectExistingServices(context, options?.Services).ServiceNames?.ToHashSet(StringComparer.Ordinal);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var containers = await _containers.ListProjectContainersAsync(client, context.ProjectName, false, cancellationToken);
        containers = FilterContainersByService(containers, services);

        // Wait on all containers in parallel; a Docker failure is recorded as exit code -1.
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
        // Aggregate code is the first non-zero exit code, else 0.
        return new WaitResult { ExitCodes = exitCodes, Code = exitCodes.Values.FirstOrDefault(c => c != 0) };
    }

    /// <summary>Observes service build contexts and reports when files in them change.</summary>
    /// <remarks>
    /// Known limitation: this only signals that a rebuild may be needed; it does not rebuild images
    /// or synchronize files into running containers. Each service reports one event per first change.
    /// </remarks>
    public async IAsyncEnumerable<WatchEvent> WatchAsync(ComposeProjectContext context, ComposeWatchOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Load project and select services; only those with a build context can be watched.
        var project = LoadProjectInternal(context);
        var targetServices = ProfileServiceSelector.Select(project, context.Profiles, options?.Services);

        foreach (var service in targetServices)
        {
            if (service.Build?.Context is null) continue;
            var watchDir = Path.GetFullPath(Path.Combine(context.WorkingDirectory, service.Build.Context));
            if (!Directory.Exists(watchDir)) continue;

            // One event per service: complete on the first file change or on cancellation.
            var tcs = new TaskCompletionSource<bool>();
            cancellationToken.Register(() => tcs.TrySetResult(true));

            using var watcher = new FileSystemWatcher(watchDir) { IncludeSubdirectories = true, EnableRaisingEvents = true };

            watcher.Changed += (_, e) =>
            {
                if (e.ChangeType == WatcherChangeTypes.Changed)
                    tcs.TrySetResult(true);
            };

            await tcs.Task;

            // Signals that a rebuild may be needed; does not rebuild or sync files itself.
            yield return new WatchEvent { ServiceName = service.Name, Action = "rebuild", Path = watchDir };
        }
    }

    /// <summary>Writes a container filesystem tarball for a service replica to <c>OutputPath</c>.</summary>
    /// <remarks>
    /// Uses the Docker Engine export endpoint and supports replica selection via <c>Index</c>
    /// rather than assuming the replica-1 container name.
    /// </remarks>
    public async Task ExportAsync(ComposeProjectContext context, ComposeExportOptions options, CancellationToken cancellationToken = default)
    {
        // Validate inputs, then resolve the target container and stream its filesystem to the output file.
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Service);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.OutputPath);

        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindContainerForArchiveAsync(
            client, context.ProjectName, options.Service, options.Index, cancellationToken);
        var outputPath = Path.GetFullPath(options.OutputPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (string.IsNullOrEmpty(outputDirectory))
            throw new ArgumentException("The output path must include a parent directory.", nameof(options));
        Directory.CreateDirectory(outputDirectory);

        await using var archive = await client.Containers.ExportContainerAsync(container.ID, cancellationToken);
        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
        await archive.CopyToAsync(output, cancellationToken);
    }

    /// <summary>Creates an image from a service container and returns the resulting image reference.</summary>
    /// <remarks>
    /// Uses the Docker Engine commit endpoint. Honors author, message, Dockerfile-style changes,
    /// pause, and replica selection via <c>Index</c> rather than assuming the replica-1 container name.
    /// </remarks>
    public async Task<string> CommitAsync(ComposeProjectContext context, ComposeCommitOptions options, CancellationToken cancellationToken = default)
    {
        // Validate before connecting, then resolve the target container and commit it through Docker Engine.
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Service);

        var reference = options.Reference ?? $"{context.ProjectName}-{options.Service}:latest";
        var parameters = ImageCommitParametersFactory.Create(reference, options);

        using var client = _clientFactory.CreateClient(context.SocketPath);
        var container = await _containers.FindContainerForArchiveAsync(
            client, context.ProjectName, options.Service, options.Index, cancellationToken);
        parameters.ContainerID = container.ID;

        await client.Images.CommitContainerChangesAsync(parameters, cancellationToken);
        return reference;
    }

    /// <summary>Renders the project's services and depends_on edges as a Graphviz digraph.</summary>
    public Task<string> VizAsync(ComposeProjectContext context, CancellationToken cancellationToken = default)
    {
        // Load project, then render profile-selected services and depends_on edges as digraph text.
        var project = LoadProjectInternal(context);
        var sb = new StringBuilder();
        sb.AppendLine($"digraph {context.ProjectName} {{");
        sb.AppendLine("  rankdir=LR;");
        foreach (var service in ProfileServiceSelector.Select(project, context.Profiles))
        {
            sb.AppendLine($"  \"{service.Name}\" [label=\"{service.Name}\\n{service.Image ?? "build"}\"];");
            // One edge per depends_on entry.
            foreach (var dep in service.DependsOn)
                sb.AppendLine($"  \"{service.Name}\" -> \"{dep}\";");
        }
        sb.AppendLine("}");
        return Task.FromResult(sb.ToString());
    }

    /// <summary>Returns the loaded project configuration as a summary for the active profiles.</summary>
    /// <remarks>Known limitation: this only summarizes the loaded project; nothing is generated on disk.</remarks>
    public Task<ComposeProjectConfig> GenerateAsync(ComposeProjectContext context, ComposeGenerateOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Placeholder for real generation: returns the profile-filtered project summary only.
        return Task.FromResult(LoadProject(context));
    }

    /// <summary>Lists the project-labeled volumes on the daemon.</summary>
    public async Task<IReadOnlyList<VolumesSummary>> VolumesAsync(ComposeProjectContext context, ComposeVolumesOptions? options = null, CancellationToken cancellationToken = default)
    {
        // List only volumes owned by this project's label, then map to summaries.
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

    /// <summary>Loads the project and returns its configuration with services filtered by the active profiles.</summary>
    public ComposeProjectConfig LoadProject(ComposeProjectContext context)
    {
        // Load from disk, then project only the profile-selected services plus top-level resource lists.
        var project = LoadProjectInternal(context);
        return new ComposeProjectConfig
        {
            Name = context.ProjectName,
            WorkingDirectory = context.WorkingDirectory,
            ConfigFiles = [context.ComposeFileName],
            Services = ProfileServiceSelector.Select(project, context.Profiles).Select(service => service.Name).ToList(),
            Networks = project.Networks.ToList(),
            Volumes = project.Volumes.ToList(),
            Secrets = project.Secrets.ToList(),
            Configs = project.Configs.ToList()
        };
    }

    /// <summary>
    /// Tags every selected service image into <paramref name="repository"/> as <c>repository:serviceName</c>
    /// and pushes each tagged image, reporting per-service tag and push outcomes.
    /// </summary>
    /// <remarks>
    /// Uses <see cref="ComposeProjectContext.RegistryAuth"/> for registry authentication. Tag and push
    /// outcomes are collected for every selected service instead of aborting on the first failure;
    /// cancellation still aborts the operation. <c>ResolveImageDigests</c>, <c>WithEnvironment</c>,
    /// <c>OcIVersion</c>, and <c>InsecureRegistry</c> are not applied.
    /// </remarks>
    public async Task<IReadOnlyList<PublishResult>> PublishAsync(ComposeProjectContext context, string repository, ComposePublishOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Validate before connecting, then load project -> open client -> profile-filter services.
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);

        var project = LoadProjectInternal(context);
        using var client = _clientFactory.CreateClient(context.SocketPath);
        var results = new List<PublishResult>();

        // Every selected service gets an outcome, including services without a concrete image.
        foreach (var service in ProfileServiceSelector.Select(project, context.Profiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (service.Image is null)
            {
                results.Add(new PublishResult
                {
                    Service = service.Name,
                    Error = "Service does not define an image reference."
                });
                continue;
            }

            var taggedImage = $"{repository}:{service.Name}";
            try
            {
                await client.Images.TagImageAsync(service.Image, new ImageTagParameters
                {
                    RepositoryName = repository,
                    Tag = service.Name
                }, cancellationToken);
            }
            catch (DockerApiException ex)
            {
                results.Add(new PublishResult
                {
                    Service = service.Name,
                    SourceImage = service.Image,
                    TaggedImage = taggedImage,
                    Error = $"Tag failed: {ex.Message}"
                });
                continue;
            }

            try
            {
                // Registry credentials come from the project context; missing auth is an anonymous push.
                await _images.PushImageAsync(client, context.RegistryAuth, taggedImage, cancellationToken);
                results.Add(new PublishResult
                {
                    Service = service.Name,
                    SourceImage = service.Image,
                    TaggedImage = taggedImage,
                    Tagged = true,
                    Pushed = true
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is DockerApiException or InvalidOperationException)
            {
                results.Add(new PublishResult
                {
                    Service = service.Name,
                    SourceImage = service.Image,
                    TaggedImage = taggedImage,
                    Tagged = true,
                    Error = $"Push failed: {ex.Message}"
                });
            }
        }

        return results;
    }

    private ComposeProject LoadProjectInternal(ComposeProjectContext context)
    {
        // Single entry point for Compose file loading; all public methods funnel through here.
        return _loader.Load(context.WorkingDirectory, context.ComposeFileName);
    }

    // A missing Compose file (without active profiles) falls back to label-only selection so
    // existing containers can still be listed, stopped, or removed without project source on disk.
    private ExistingServiceSelection SelectExistingServices(
        ComposeProjectContext context,
        IReadOnlyList<string>? explicitServices)
    {
        // Try to load the project; the catch below handles the no-Compose-file case.
        ComposeProject project;
        try
        {
            project = LoadProjectInternal(context);
        }
        catch (FileNotFoundException) when (context.Profiles is not { Count: > 0 })
        {
            return explicitServices is { Count: > 0 }
                ? new ExistingServiceSelection(explicitServices.ToList(), IncludesAllDefinedServices: false)
                : new ExistingServiceSelection(ServiceNames: null, IncludesAllDefinedServices: true);
        }

        // Profile-filter the definitions, and flag "all defined services" only when nothing was
        // explicitly requested and the selection covers every service in the file.
        var selected = ProfileServiceSelector.Select(project, context.Profiles, explicitServices);
        return new ExistingServiceSelection(
            selected.Select(service => service.Name).ToList(),
            explicitServices is not { Count: > 0 } && selected.Count == project.Services.Count);
    }

    // Matching goes through the service label, never container names, so foreign containers are excluded.
    private static IReadOnlyList<ContainerListResponse> FilterContainersByService(
        IReadOnlyList<ContainerListResponse> containers,
        IReadOnlySet<string>? services)
    {
        if (services is null)
            return containers;

        return containers.Where(container =>
        {
            var labels = container.Labels ?? new Dictionary<string, string>();
            return labels.TryGetValue(ComposeConstants.ServiceLabel, out var service) &&
                   service is not null &&
                   services.Contains(service);
        }).ToList();
    }

    // A null service list preserves label-only behavior when no Compose file exists.
    // An empty list is a verified profile selection that must remain a no-op.
    private sealed record ExistingServiceSelection(
        IReadOnlyList<string>? ServiceNames,
        bool IncludesAllDefinedServices);

    private static IReadOnlyList<ServiceDefinition> GetOrderedServices(
        ComposeProject project,
        IReadOnlyList<string>? profiles,
        IReadOnlyList<string>? services)
    {
        // Profile-filter first, then apply depends_on ordering.
        return OrderServices(ProfileServiceSelector.Select(project, profiles, services));
    }

    // Topological order over depends_on: services whose dependencies are all scheduled go first.
    // Cycles cannot be ordered, so any leftovers are appended as-is rather than failing the up.
    private static IReadOnlyList<ServiceDefinition> OrderServices(IReadOnlyList<ServiceDefinition> services)
    {
        var result = new List<ServiceDefinition>();
        var remaining = services.ToDictionary(x => x.Name, StringComparer.OrdinalIgnoreCase);
        while (remaining.Count > 0)
        {
            // Ready = every depends_on entry already scheduled.
            var ready = remaining.Values
                .Where(service => service.DependsOn.All(dep => !remaining.ContainsKey(dep)))
                .ToList();
            if (ready.Count == 0)
            {
                // Cycle fallback: drain the rest in dictionary order instead of failing.
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

    // Docker's build API streams progress but does not fail the call on a build error, so the first
    // error message is captured here and rethrown after the stream completes.
    private sealed class BuildProgress(string serviceName, ILogConsumer? consumer) : IProgress<JSONMessage>
    {
        private string? _error;

        public void Report(JSONMessage value)
        {
            // Capture the first build error message; forward every progress line to the consumer.
            var message = value.ErrorMessage ?? value.Error?.Message ?? value.Stream ?? value.ProgressMessage ?? value.Status;
            if (!string.IsNullOrWhiteSpace(value.ErrorMessage ?? value.Error?.Message))
                Interlocked.CompareExchange(ref _error, message, null);

            if (!string.IsNullOrWhiteSpace(message))
                consumer?.OnStatus(serviceName, message.TrimEnd());
        }

        public void ThrowIfFailed()
        {
            // Rethrow after the stream completes, since the build call itself does not fail on build errors.
            var error = Volatile.Read(ref _error);
            if (error is not null)
                throw new InvalidOperationException($"Docker build for service '{serviceName}' failed: {error}");
        }
    }
}
