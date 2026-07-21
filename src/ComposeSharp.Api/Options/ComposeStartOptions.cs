namespace ComposeSharp.Api;

public sealed record ComposeStartOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public ILogConsumer? Attach { get; init; }
    public IReadOnlyList<string>? AttachTo { get; init; }
    public bool Wait { get; init; }
    public int? WaitTimeoutSeconds { get; init; }
}
