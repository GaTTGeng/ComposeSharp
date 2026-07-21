namespace ComposeSharp.Api;

public sealed record ComposeVolumesOptions
{
    public IReadOnlyList<string>? Services { get; init; }
}
