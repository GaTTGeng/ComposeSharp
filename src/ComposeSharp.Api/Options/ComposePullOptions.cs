// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for pulling images referenced by the project.</summary>
public sealed record ComposePullOptions
{
    /// <summary>Restricts the pull to these service names; null pulls every project service's image.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Also pull images of services the selection depends on.</summary>
    public bool IncludeDeps { get; init; }

    /// <summary>Suppress pull progress output.</summary>
    public bool Quiet { get; init; }

    /// <summary>Target platform for the pulled image, for example <c>linux/amd64</c>.</summary>
    public string? Platform { get; init; }

    /// <summary>Continue pulling remaining images when one pull fails.</summary>
    public bool IgnoreFailures { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
