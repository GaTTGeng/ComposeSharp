namespace ComposeSharp.Api;

public sealed record ServiceStatus
{
    public required string ServiceName { get; init; }
    public int Desired { get; init; }
    public int Running { get; init; }
    public IReadOnlyList<string> Ports { get; init; } = [];
    public IReadOnlyList<PortPublisher> Publishers { get; init; } = [];
}
