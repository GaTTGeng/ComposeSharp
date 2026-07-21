namespace ComposeSharp.Api;

public interface IComposeService
{
    Task BuildAsync(ComposeProjectContext context, ComposeBuildOptions? options = null, CancellationToken cancellationToken = default);

    Task UpAsync(ComposeProjectContext context, ComposeUpOptions? options = null, CancellationToken cancellationToken = default);

    Task DownAsync(ComposeProjectContext context, ComposeDownOptions? options = null, CancellationToken cancellationToken = default);

    Task CreateAsync(ComposeProjectContext context, ComposeCreateOptions? options = null, CancellationToken cancellationToken = default);

    Task StartAsync(ComposeProjectContext context, ComposeStartOptions? options = null, CancellationToken cancellationToken = default);

    Task StopAsync(ComposeProjectContext context, ComposeStopOptions? options = null, CancellationToken cancellationToken = default);

    Task RestartAsync(ComposeProjectContext context, ComposeRestartOptions? options = null, CancellationToken cancellationToken = default);

    Task PullAsync(ComposeProjectContext context, ComposePullOptions? options = null, CancellationToken cancellationToken = default);

    Task PushAsync(ComposeProjectContext context, ComposePushOptions? options = null, CancellationToken cancellationToken = default);

    Task KillAsync(ComposeProjectContext context, ComposeKillOptions? options = null, CancellationToken cancellationToken = default);

    Task<string> RunAsync(ComposeProjectContext context, string serviceName, ComposeRunOptions? options = null, CancellationToken cancellationToken = default);

    Task RemoveAsync(ComposeProjectContext context, ComposeRemoveOptions? options = null, CancellationToken cancellationToken = default);

    Task<ExecResult> ExecAsync(ComposeProjectContext context, string serviceName, ComposeExecOptions options, CancellationToken cancellationToken = default);

    Task AttachAsync(ComposeProjectContext context, string serviceName, ComposeAttachOptions? options = null, CancellationToken cancellationToken = default);

    Task<CopyResult> CopyAsync(ComposeProjectContext context, ComposeCopyOptions options, CancellationToken cancellationToken = default);

    Task PauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default);

    Task UnPauseAsync(ComposeProjectContext context, ComposePauseOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerSummary>> PsAsync(ComposeProjectContext context, ComposePsOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Stack>> ListAsync(ComposeListOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContainerProcSummary>> TopAsync(ComposeProjectContext context, ComposeTopOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ImageSummary>> ImagesAsync(ComposeProjectContext context, ComposeImagesOptions? options = null, CancellationToken cancellationToken = default);

    Task<(string Host, int Port)> PortAsync(ComposeProjectContext context, string serviceName, int containerPort, ComposePortOptions? options = null, CancellationToken cancellationToken = default);

    Task LogsAsync(ComposeProjectContext context, ComposeLogsOptions? options = null, ILogConsumer? consumer = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ComposeEvent> EventsAsync(ComposeProjectContext context, ComposeEventsOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ServiceStatus>> ScaleAsync(ComposeProjectContext context, ComposeScaleOptions options, CancellationToken cancellationToken = default);

    Task<WaitResult> WaitAsync(ComposeProjectContext context, ComposeWaitOptions? options = null, CancellationToken cancellationToken = default);

    IAsyncEnumerable<WatchEvent> WatchAsync(ComposeProjectContext context, ComposeWatchOptions? options = null, CancellationToken cancellationToken = default);

    Task ExportAsync(ComposeProjectContext context, ComposeExportOptions options, CancellationToken cancellationToken = default);

    Task<string> CommitAsync(ComposeProjectContext context, ComposeCommitOptions options, CancellationToken cancellationToken = default);

    Task<string> VizAsync(ComposeProjectContext context, CancellationToken cancellationToken = default);

    Task<ComposeProjectConfig> GenerateAsync(ComposeProjectContext context, ComposeGenerateOptions? options = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<VolumesSummary>> VolumesAsync(ComposeProjectContext context, ComposeVolumesOptions? options = null, CancellationToken cancellationToken = default);

    ComposeProjectConfig LoadProject(ComposeProjectContext context);

    Task PublishAsync(ComposeProjectContext context, string repository, ComposePublishOptions? options = null, CancellationToken cancellationToken = default);
}
