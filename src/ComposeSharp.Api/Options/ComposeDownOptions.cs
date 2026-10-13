// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for taking a project down.</summary>
public sealed record ComposeDownOptions
{
    /// <summary>Also remove named volumes declared by the project.</summary>
    public bool RemoveVolumes { get; init; }

    /// <summary>Also remove containers of services that no longer exist in the Compose file.</summary>
    public bool RemoveOrphans { get; init; }

    /// <summary>Image removal policy: <c>all</c> removes every project image, <c>local</c> only untagged ones.</summary>
    public string? RemoveImages { get; init; }

    /// <summary>Restricts the teardown to these service names; networks are cleaned up only when the whole project goes down.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds when stopping containers before force-removal.</summary>
    public int? TimeoutSeconds { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
