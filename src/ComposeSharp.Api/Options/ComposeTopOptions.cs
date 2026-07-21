namespace ComposeSharp.Api;

public sealed record ComposeTopOptions
{
    public IReadOnlyList<string>? Services { get; init; }
}
