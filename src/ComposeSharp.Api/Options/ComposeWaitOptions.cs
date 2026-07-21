namespace ComposeSharp.Api;

public sealed record ComposeWaitOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool DownProjectOnContainerExit { get; init; }
}
