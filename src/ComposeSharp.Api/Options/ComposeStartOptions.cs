namespace ComposeSharp.Api;

/// <summary>Options for starting already-created containers.</summary>
public sealed record ComposeStartOptions
{
    /// <summary>Restricts the start to these service names; null starts every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Receives output from the started containers; null does not attach.</summary>
    public ILogConsumer? Attach { get; init; }

    /// <summary>Stream names to attach to when <see cref="Attach"/> is set.</summary>
    public IReadOnlyList<string>? AttachTo { get; init; }

    /// <summary>Block until the started containers are running (or healthy), instead of returning once start is issued.</summary>
    public bool Wait { get; init; }

    /// <summary>Upper bound in seconds for <see cref="Wait"/> before the call fails.</summary>
    public int? WaitTimeoutSeconds { get; init; }
}
