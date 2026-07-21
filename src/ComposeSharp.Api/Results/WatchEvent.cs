namespace ComposeSharp.Api;

public sealed record WatchEvent
{
    public required string ServiceName { get; init; }
    public required string Action { get; init; }
    public required string Path { get; init; }
}
