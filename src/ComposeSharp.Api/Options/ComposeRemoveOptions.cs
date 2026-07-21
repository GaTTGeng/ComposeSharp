namespace ComposeSharp.Api;

public sealed record ComposeRemoveOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool Force { get; init; }
    public bool Stop { get; init; }
    public bool Volumes { get; init; }
}
