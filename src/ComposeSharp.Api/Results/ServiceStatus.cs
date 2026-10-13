// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
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
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
