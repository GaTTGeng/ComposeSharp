namespace ComposeSharp.Api;

/// <summary>Replica status of one service after a scaling or listing operation.</summary>
public sealed record ServiceStatus
{
    /// <summary>Service name the status belongs to.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Number of replicas requested for the service.</summary>
    public int Desired { get; init; }

    /// <summary>Number of replicas currently in the running state.</summary>
    public int Running { get; init; }

    /// <summary>Published port descriptions of the service's containers.</summary>
    public IReadOnlyList<string> Ports { get; init; } = [];

    /// <summary>Structured published port mappings of the service's containers.</summary>
    public IReadOnlyList<PortPublisher> Publishers { get; init; } = [];
}
