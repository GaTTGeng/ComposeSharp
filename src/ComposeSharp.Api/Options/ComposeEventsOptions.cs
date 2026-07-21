namespace ComposeSharp.Api;

public sealed record ComposeEventsOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool Json { get; init; }
}
