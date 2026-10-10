namespace ComposeSharp.Api;

/// <summary>Options for observing build-context changes of selected project services.</summary>
/// <remarks>
/// Observation only reports changes. It does not start services, rebuild images, synchronize files,
/// or prune build artifacts. <see cref="Prune"/> is unsupported and causes an exception when enabled.
/// </remarks>
public sealed record ComposeWatchOptions
{
    /// <summary>
    /// Restricts observation to these service names. Null uses active-profile selection, an empty list
    /// observes nothing, and unknown names are rejected. Explicit names bypass profile filtering.
    /// </summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Compatibility option with no effect because observation never starts the project.</summary>
    public bool NoUp { get; init; }

    /// <summary>Compatibility option with no effect because observation writes no progress output.</summary>
    public bool Quiet { get; init; }

    /// <summary>Requests pruning, which is unsupported by the notification-only observer.</summary>
    public bool Prune { get; init; }
}
