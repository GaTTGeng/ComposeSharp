namespace ComposeSharp.Api;

/// <summary>Options for stopping the project's containers.</summary>
public sealed record ComposeStopOptions
{
    /// <summary>Restricts the stop to these service names; null stops every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds before containers are force-killed.</summary>
    public int? TimeoutSeconds { get; init; }
}
