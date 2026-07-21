namespace ComposeSharp.Api;

public interface IContainerEventListener
{
    void OnContainerEvent(ContainerEvent containerEvent);
}

public sealed record ContainerEvent
{
    public required string Type { get; init; }
    public required long Timestamp { get; init; }
    public required string ContainerId { get; init; }
    public required string ServiceName { get; init; }
    public string? Line { get; init; }
    public int? ExitCode { get; init; }
    public bool Restarting { get; init; }
}
