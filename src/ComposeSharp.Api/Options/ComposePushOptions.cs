// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for pushing built images to their registries.</summary>
public sealed record ComposePushOptions
{
    /// <summary>Restricts the push to these service names; null pushes every project service's image.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Continue pushing remaining images when one push fails.</summary>
    public bool IgnoreFailures { get; init; }

    /// <summary>Suppress push progress output.</summary>
    public bool Quiet { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
