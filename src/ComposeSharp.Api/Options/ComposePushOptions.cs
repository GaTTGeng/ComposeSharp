namespace ComposeSharp.Api;

public sealed record ComposePushOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool IgnoreFailures { get; init; }
    public bool Quiet { get; init; }
}
