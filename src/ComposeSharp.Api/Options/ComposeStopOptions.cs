namespace ComposeSharp.Api;

public sealed record ComposeStopOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public int? TimeoutSeconds { get; init; }
}
