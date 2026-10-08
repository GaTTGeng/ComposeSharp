namespace ComposeSharp.Api;

/// <summary>Options filtering the project event stream.</summary>
public sealed record ComposeEventsOptions
{
    /// <summary>Only report events for these service names; null reports every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Request JSON-shaped event payloads.</summary>
    public bool Json { get; init; }
}
