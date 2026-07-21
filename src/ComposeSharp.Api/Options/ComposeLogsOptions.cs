namespace ComposeSharp.Api;

public sealed record ComposeLogsOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool Follow { get; init; }
    public string? Since { get; init; }
    public string? Until { get; init; }
    public bool Timestamps { get; init; }
    public string Tail { get; init; } = "all";
    public bool NoColor { get; init; }
    public bool NoLogPrefix { get; init; }
}
