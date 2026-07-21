namespace ComposeSharp.Api;

public sealed record ComposeWatchOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool NoUp { get; init; }
    public bool Quiet { get; init; }
    public bool Prune { get; init; }
}
