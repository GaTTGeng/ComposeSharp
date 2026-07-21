namespace ComposeSharp.Api;

public sealed record ComposePortOptions
{
    public string Protocol { get; init; } = "tcp";
    public int? Index { get; init; }
}
