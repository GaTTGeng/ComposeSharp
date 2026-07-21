namespace ComposeSharp.Api;

public sealed record ComposePauseOptions
{
    public IReadOnlyList<string>? Services { get; init; }
}
