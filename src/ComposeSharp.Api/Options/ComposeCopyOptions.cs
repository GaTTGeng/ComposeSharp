namespace ComposeSharp.Api;

/// <summary>Options for copying files between the host and a service container.</summary>
public sealed record ComposeCopyOptions
{
    /// <summary>Copy source: a host path, or a <c>container:path</c> reference.</summary>
    public required string Source { get; init; }

    /// <summary>Copy destination: a host path, or a <c>container:path</c> reference.</summary>
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
