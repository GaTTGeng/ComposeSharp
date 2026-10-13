// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace ComposeSharp.Api;

/// <summary>Options selecting which services to pause.</summary>
public sealed record ComposePauseOptions
{
    /// <summary>Restricts the pause to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
