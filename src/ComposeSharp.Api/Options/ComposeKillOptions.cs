// Rider inspection rationale: The init setter supports caller-side option construction and must remain configurable.
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for sending a signal to the project's containers.</summary>
public sealed record ComposeKillOptions
{
    /// <summary>Restricts the signal to these service names; null signals every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Signal name or number sent to the container processes.</summary>
    public string Signal { get; init; } = "SIGKILL";

    /// <summary>Also signal containers of services that no longer exist in the Compose file.</summary>
    public bool RemoveOrphans { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
