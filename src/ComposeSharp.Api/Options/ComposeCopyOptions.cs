// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for copying files between the host and a service container.</summary>
public sealed record ComposeCopyOptions
{
    /// <summary>Copy source: a host path, or a <c>service:/container/path</c> reference.</summary>
    public required string Source { get; init; }

    /// <summary>Copy destination: a host path, or a <c>service:/container/path</c> reference.</summary>
    public required string Destination { get; init; }

    /// <summary>Follow symlinks in the source location instead of copying them as links.</summary>
    public bool FollowLink { get; init; }

    /// <summary>Preserve the source UID/GID on copied files (requires sufficient privileges).</summary>
    public bool CopyUidGid { get; init; }

    /// <summary>Replica index within the service; null targets the first matching container.</summary>
    public int? Index { get; init; }

    /// <summary>Apply the copy to every replica of the service instead of a single container.</summary>
    public bool All { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
