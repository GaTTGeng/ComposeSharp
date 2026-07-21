namespace ComposeSharp.Api;

public sealed record ContainerSummary
{
    public required string ID { get; init; }
    public required string Name { get; init; }
    public IReadOnlyList<string> Names { get; init; } = [];
    public required string Image { get; init; }
    public string? Command { get; init; }
    public required string Project { get; init; }
    public required string Service { get; init; }
    public long Created { get; init; }
    public required string State { get; init; }
    public string? Status { get; init; }
    public string? Health { get; init; }
    public int ExitCode { get; init; }
    public IReadOnlyList<PortPublisher> Publishers { get; init; } = [];
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<string> Networks { get; init; } = [];
    public IReadOnlyList<MountInfo> Mounts { get; init; } = [];
    public int LocalVolumes { get; init; }
    public long SizeRw { get; init; }
    public long SizeRootFs { get; init; }
}

public sealed record PortPublisher
{
    public string? URL { get; init; }
    public int TargetPort { get; init; }
    public int PublishedPort { get; init; }
    public string Protocol { get; init; } = "tcp";
    public string? HostIP { get; init; }
}

public sealed record MountInfo
{
    public string? Type { get; init; }
    public string? Source { get; init; }
    public string? Destination { get; init; }
    public string? Mode { get; init; }
    public bool RW { get; init; }
}
