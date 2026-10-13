// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for listing volumes owned by the project.</summary>
public sealed record ComposeVolumesOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
