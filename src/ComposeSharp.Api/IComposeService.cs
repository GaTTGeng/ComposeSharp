namespace ComposeSharp.Api;

/// <summary>
/// In-process operations over a Compose-shaped project and its Docker Engine resources.
/// This is not a <c>docker compose</c> process wrapper and does not claim full CLI compatibility;
/// every method is scoped to resources labeled with the project name from <see cref="ComposeProjectContext"/>.
/// </summary>
public interface IComposeService
{
    /// <summary>Builds images for services that define a <c>build</c> section.</summary>
    /// <remarks>
    /// Builds run through the Docker Engine image-build API using a generated build-context archive.
    /// Only services with build definitions are built; services without one are skipped.
    /// </remarks>
    Task BuildAsync(ComposeProjectContext context, ComposeBuildOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates (or reconciles) and starts the project's services, including networks and volumes.</summary>
    /// <remarks>
    /// <c>depends_on</c> is modeled but does not yet provide complete dependency ordering or health readiness.
    /// Explicit <c>Scale</c> counts take precedence over <c>deploy.replicas</c>.
    /// </remarks>
    Task UpAsync(ComposeProjectContext context, ComposeUpOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Stops and removes the project's containers, and its networks when the whole project is taken down.</summary>
    Task DownAsync(ComposeProjectContext context, ComposeDownOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Creates containers (and required infrastructure) for the project's services without guaranteeing they stay started.</summary>
    Task CreateAsync(ComposeProjectContext context, ComposeCreateOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Starts already-created containers of the project's services.</summary>
    Task StartAsync(ComposeProjectContext context, ComposeStartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Stops running containers of the project's services.</summary>
    Task StopAsync(ComposeProjectContext context, ComposeStopOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Restarts containers of the project's services.</summary>
    Task RestartAsync(ComposeProjectContext context, ComposeRestartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Pulls the images referenced by the project's services.</summary>
    Task PullAsync(ComposeProjectContext context, ComposePullOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Pushes built images of the project's services to their registries.</summary>
    Task PushAsync(ComposeProjectContext context, ComposePushOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Sends a signal (default <c>SIGKILL</c>) to the project's containers.</summary>
    Task KillAsync(ComposeProjectContext context, ComposeKillOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Runs a one-off container for a service and returns its container ID.</summary>
    /// <remarks>The one-off container carries the project's ownership labels so it can be found and removed with the project.</remarks>
    Task<string> RunAsync(ComposeProjectContext context, string serviceName, ComposeRunOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Removes containers of the project's services.</summary>
    Task RemoveAsync(ComposeProjectContext context, ComposeRemoveOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Executes a command inside a service container and captures its output and exit code.</summary>
    Task<ExecResult> ExecAsync(ComposeProjectContext context, string serviceName, ComposeExecOptions options, CancellationToken cancellationToken = default);

    /// <summary>Streams a service container's output until the stream ends or cancellation is requested.</summary>
    Task AttachAsync(ComposeProjectContext context, string serviceName, ComposeAttachOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Copies files between the host and a service container.</summary>
    /// <remarks>
    /// Uses the Docker Engine archive endpoints. Exactly one side of the copy must use the
    /// <c>service:/container/path</c> form; the other side is a host path. Byte counts are reported
    /// in <see cref="CopyResult.BytesCopied"/>.
    /// </remarks>
    Task<CopyResult> CopyAsync(ComposeProjectContext context, ComposeCopyOptions options, CancellationToken cancellationToken = default);

    /// <summary>Pauses (freezes) containers of the project's services.</summary>
    Task PauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Unpauses previously paused containers of the project's services.</summary>
    Task UnPauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists the project's containers with their state, ports, and labels.</summary>
    Task<IReadOnlyList<ContainerSummary>> PsAsync(ComposeProjectContext context, ComposePsOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists Compose projects discovered from containers carrying the project label.</summary>
    Task<IReadOnlyList<Stack>> ListAsync(ComposeListOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists running processes inside the project's running service containers.</summary>
    /// <remarks>
    /// Uses the Docker Engine process-list API and returns one <see cref="ContainerProcSummary"/>
    /// per running container of the selected services. Non-running containers are omitted.
    /// </remarks>
    Task<IReadOnlyList<ContainerProcSummary>> TopAsync(ComposeProjectContext context, ComposeTopOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists images used by the project's services.</summary>
    Task<IReadOnlyList<ImageSummary>> ImagesAsync(ComposeProjectContext context, ComposeImagesOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Resolves the host address and published port for a container port of a service.</summary>
    Task<(string Host, int Port)> PortAsync(ComposeProjectContext context, string serviceName, int containerPort, ComposePortOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Streams container logs to an <see cref="ILogConsumer"/> (or discards them when none is supplied).</summary>
    Task LogsAsync(ComposeProjectContext context, ComposeLogsOptions? options = null, ILogConsumer? consumer = null, CancellationToken cancellationToken = default);

    /// <summary>Streams Docker Engine events for the project's resources as compose events.</summary>
    /// <remarks>
    /// Subscribes to container, network, and volume Docker events and yields live events until
    /// cancellation. Only project-owned resources are reported: containers are matched by the
    /// Compose project label, and networks/volumes by their Compose name prefix
    /// (<c>projectName_*</c>). Service-labeled events honor the profile-selected service set.
    /// Events without a Compose service label are reported with a null
    /// <see cref="ComposeEvent.Service"/> and are excluded only when the caller selects specific services.
    /// </remarks>
    IAsyncEnumerable<ComposeEvent> EventsAsync(ComposeProjectContext context, ComposeEventsOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Adjusts replica counts per service and reports the resulting service status.</summary>
    Task<IReadOnlyList<ServiceStatus>> ScaleAsync(ComposeProjectContext context, ComposeScaleOptions options, CancellationToken cancellationToken = default);

    /// <summary>Waits for the project's containers to exit and collects their exit codes.</summary>
    Task<WaitResult> WaitAsync(ComposeProjectContext context, ComposeWaitOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Reports build-context file changes for the project's services.</summary>
    /// <remarks>
    /// Reports changed build-context paths only; it does not rebuild images or synchronize files into
    /// containers. Services without a build context are ignored.
    /// </remarks>
    IAsyncEnumerable<WatchEvent> WatchAsync(ComposeProjectContext context, ComposeWatchOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Exports a service container's filesystem to a local archive.</summary>
    /// <remarks>
    /// Uses the Docker Engine export endpoint and supports replica selection via the options
    /// rather than assuming the replica-1 container name.
    /// </remarks>
    Task ExportAsync(ComposeProjectContext context, ComposeExportOptions options, CancellationToken cancellationToken = default);

    /// <summary>Commits a service container to a new image and returns the image reference.</summary>
    /// <remarks>
    /// Uses the Docker Engine commit endpoint. Honors author, message, Dockerfile-style changes,
    /// pause, and replica selection via <c>ComposeCommitOptions.Index</c>. Returns the image
    /// reference recorded on the new image (the resolved <c>Reference</c>).
    /// </remarks>
    Task<string> CommitAsync(ComposeProjectContext context, ComposeCommitOptions options, CancellationToken cancellationToken = default);

    /// <summary>Renders the project's service dependency graph as Graphviz DOT text.</summary>
    Task<string> VizAsync(ComposeProjectContext context, CancellationToken cancellationToken = default);

    /// <summary>Returns a summary of the loaded project configuration.</summary>
    /// <remarks>Currently returns the loaded project summary rather than generating a new configuration artifact.</remarks>
    Task<ComposeProjectConfig> GenerateAsync(ComposeProjectContext context, ComposeGenerateOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Lists volumes owned by the project.</summary>
    Task<IReadOnlyList<VolumesSummary>> VolumesAsync(ComposeProjectContext context, ComposeVolumesOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Loads and returns the project's configuration without touching Docker resources.</summary>
    /// <remarks>Multi-file loading currently replaces same-named services instead of applying the full Compose merge rules.</remarks>
    ComposeProjectConfig LoadProject(ComposeProjectContext context);

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
    Task<IReadOnlyList<PublishResult>> PublishAsync(ComposeProjectContext context, string repository, ComposePublishOptions? options = null, CancellationToken cancellationToken = default);
}
