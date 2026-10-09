namespace ComposeSharp.Api;

/// <summary>Options filtering the project event stream.</summary>
public sealed record ComposeEventsOptions
{
    /// <summary>Only report events for these service names; null reports every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Request JSON-shaped event payloads.</summary>
    public bool Json { get; init; }

    /// <summary>
    /// Invoked only after the Docker event response has been established and project resources
    /// have been resolved. Callers that race a lifecycle operation against the stream can await
    /// this signal before triggering that operation; the callback is not invoked when subscription
    /// setup fails.
    /// </summary>
    public Action? OnSubscribed { get; init; }
}
