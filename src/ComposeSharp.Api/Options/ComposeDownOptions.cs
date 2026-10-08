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
