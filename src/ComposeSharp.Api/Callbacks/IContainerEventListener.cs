// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
// Rider inspection rationale: This public type is part of the package contract and is intended for downstream callers.
// ReSharper disable UnusedType.Global
namespace ComposeSharp.Api;

/// <summary>Callback interface for observing lifecycle events of project containers.</summary>
public interface IContainerEventListener
{
    /// <summary>Called once per container lifecycle event observed by the engine.</summary>
    void OnContainerEvent(ContainerEvent containerEvent);
}

/// <summary>One container lifecycle event delivered to an <see cref="IContainerEventListener"/>.</summary>
public sealed record ContainerEvent
{
    /// <summary>Event type, for example <c>start</c>, <c>exit</c>, or <c>log</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Event time as a Unix timestamp in milliseconds.</summary>
    public required long Timestamp { get; init; }

    /// <summary>Container the event refers to.</summary>
    public required string ContainerId { get; init; }

    /// <summary>Compose service the container belongs to.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Log line associated with the event, when the event carries output.</summary>
    public string? Line { get; init; }

    /// <summary>Process exit code, present on exit events.</summary>
    public int? ExitCode { get; init; }

    /// <summary>Whether the container is being restarted rather than staying stopped.</summary>
    public bool Restarting { get; init; }
}
// ReSharper restore UnusedType.Global
// ReSharper restore UnusedMember.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
