namespace ComposeSharp.Api;

/// <summary>Options for observing build-context changes of the project's services.</summary>
public sealed record ComposeWatchOptions
{
    /// <summary>Restricts observation to these service names; null covers every service with a build context.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Do not start the project before observing.</summary>
    public bool NoUp { get; init; }

    /// <summary>Suppress progress output during observation.</summary>
    public bool Quiet { get; init; }

    /// <summary>Drop stale build-context artifacts reported as changed.</summary>
    public bool Prune { get; init; }
}
