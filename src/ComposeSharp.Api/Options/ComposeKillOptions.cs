namespace ComposeSharp.Api;

/// <summary>Options for sending a signal to the project's containers.</summary>
public sealed record ComposeKillOptions
{
    /// <summary>Restricts the signal to these service names; null signals every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Signal name or number sent to the container processes.</summary>
    public string Signal { get; init; } = "SIGKILL";

    /// <summary>Also signal containers of services that no longer exist in the Compose file.</summary>
    public bool RemoveOrphans { get; init; }
}
