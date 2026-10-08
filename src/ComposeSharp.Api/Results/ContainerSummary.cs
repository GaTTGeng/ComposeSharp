namespace ComposeSharp.Api;

/// <summary>Summary of one project container as returned by listing operations.</summary>
public sealed record ContainerSummary
{
    /// <summary>Container ID.</summary>
    public required string ID { get; init; }

    /// <summary>Primary container name.</summary>
    public required string Name { get; init; }

    /// <summary>All names the container is known by.</summary>
    public IReadOnlyList<string> Names { get; init; } = [];

    /// <summary>Image reference the container was created from.</summary>
    public required string Image { get; init; }

    /// <summary>Command the container runs.</summary>
    public string? Command { get; init; }

    /// <summary>Compose project that owns the container.</summary>
    public required string Project { get; init; }

    /// <summary>Compose service the container belongs to.</summary>
    public required string Service { get; init; }

    /// <summary>Creation time as a Unix timestamp in seconds.</summary>
    public long Created { get; init; }

    /// <summary>Container state, for example <c>running</c> or <c>exited</c>.</summary>
    public required string State { get; init; }

    /// <summary>Human-readable status line as shown by the Docker CLI.</summary>
    public string? Status { get; init; }

    /// <summary>Health check status when the container defines one.</summary>
    public string? Health { get; init; }

    /// <summary>Process exit code when the container has stopped.</summary>
    public int ExitCode { get; init; }

    /// <summary>Published port mappings of the container.</summary>
    public IReadOnlyList<PortPublisher> Publishers { get; init; } = [];

    /// <summary>Labels applied to the container, including the Compose ownership labels.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>Names of the networks the container is attached to.</summary>
    public IReadOnlyList<string> Networks { get; init; } = [];

    /// <summary>Mounts of the container.</summary>
    public IReadOnlyList<MountInfo> Mounts { get; init; } = [];

    /// <summary>Number of named volumes attached to the container.</summary>
    public int LocalVolumes { get; init; }

    /// <summary>Size of the container's writable layer in bytes.</summary>
    public long SizeRw { get; init; }

    /// <summary>Size of the container's root filesystem in bytes.</summary>
    public long SizeRootFs { get; init; }
}

/// <summary>One published port mapping of a container.</summary>
public sealed record PortPublisher
{
    /// <summary>Convenience URL for the published endpoint, when a host address is known.</summary>
    public string? URL { get; init; }

    /// <summary>Port inside the container.</summary>
    public int TargetPort { get; init; }

    /// <summary>Port published on the host, or 0 when not published.</summary>
    public int PublishedPort { get; init; }

    /// <summary>Transport protocol of the mapping.</summary>
    public string Protocol { get; init; } = "tcp";

    /// <summary>Host IP the port is bound to.</summary>
    public string? HostIP { get; init; }
}

/// <summary>One mount of a container.</summary>
public sealed record MountInfo
{
    /// <summary>Mount type, for example <c>bind</c> or <c>volume</c>.</summary>
    public string? Type { get; init; }

    /// <summary>Host path or volume name backing the mount.</summary>
    public string? Source { get; init; }

    /// <summary>Path the mount is attached to inside the container.</summary>
    public string? Destination { get; init; }

    /// <summary>Mount mode and options, as understood by the Docker Engine.</summary>
    public string? Mode { get; init; }

    /// <summary>Whether the mount is writable inside the container.</summary>
    public bool RW { get; init; }
}
