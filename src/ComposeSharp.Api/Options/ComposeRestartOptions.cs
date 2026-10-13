// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for restarting the project's containers.</summary>
public sealed record ComposeRestartOptions
{
    /// <summary>Restricts the restart to these service names; null restarts every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds before containers are force-killed on shutdown.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Do not restart dependent services along with the selection.</summary>
    public bool NoDeps { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
