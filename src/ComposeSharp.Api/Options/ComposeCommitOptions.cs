namespace ComposeSharp.Api;

/// <summary>Options for committing a service container to a new image.</summary>
public sealed record ComposeCommitOptions
{
    /// <summary>Service whose container is committed.</summary>
    public required string Service { get; init; }

    /// <summary>Image reference for the committed image; defaults to <c>project-service:latest</c>.</summary>
    public string? Reference { get; init; }

    /// <summary>Author recorded on the new image.</summary>
    public string? Author { get; init; }

    /// <summary>Commit message recorded on the new image.</summary>
    public string? Message { get; init; }

    /// <summary>Additional image configuration changes (Dockerfile-style instructions) applied at commit time.</summary>
    public IReadOnlyDictionary<string, string>? Changes { get; init; }

    /// <summary>Pause the container while committing so the filesystem is consistent.</summary>
    public bool Pause { get; init; } = true;

    /// <summary>Replica index within the service; null commits the first matching container.</summary>
    public int? Index { get; init; }
}
