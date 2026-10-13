// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options filtering the project event stream.</summary>
public sealed record ComposeEventsOptions
{
    /// <summary>
    /// Only report events for these service names. Null selects every project service and keeps
    /// project-level network/volume events; a non-null list filters to those services and drops
    /// events with no service label. An empty list is a no-op and yields no events.
    /// </summary>
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
// ReSharper restore UnusedMember.Global
// ReSharper restore CheckNamespace
