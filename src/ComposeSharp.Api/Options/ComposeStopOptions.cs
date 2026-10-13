// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace ComposeSharp.Api;

/// <summary>Options for stopping the project's containers.</summary>
public sealed record ComposeStopOptions
{
    /// <summary>Restricts the stop to these service names; null stops every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds before containers are force-killed.</summary>
    public int? TimeoutSeconds { get; init; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
