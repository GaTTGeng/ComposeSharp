// Rider inspection rationale: The init setter supports caller-side option construction and must remain configurable.
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace ComposeSharp.Api;

/// <summary>Options for resolving a published container port.</summary>
public sealed record ComposePortOptions
{
    /// <summary>Transport protocol of the container port.</summary>
    public string Protocol { get; init; } = "tcp";

    /// <summary>Replica index within the service; null resolves the first matching container.</summary>
    public int? Index { get; init; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
