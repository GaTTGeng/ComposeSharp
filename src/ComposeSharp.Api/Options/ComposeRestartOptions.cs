namespace ComposeSharp.Api;

/// <summary>Options for restarting the project's containers.</summary>
public sealed record ComposeRestartOptions
{
    /// <summary>Restricts the restart to these service names; null restarts every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds before containers are force-killed on shutdown.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Do not restart dependent services along with the selection.</summary>
    public bool NoDeps { get; init; }
}
