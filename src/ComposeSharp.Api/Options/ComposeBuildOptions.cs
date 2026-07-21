namespace ComposeSharp.Api;

public sealed record ComposeBuildOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool NoCache { get; init; }
    public bool Pull { get; init; }
    public bool Quiet { get; init; }
    public IReadOnlyDictionary<string, string>? BuildArgs { get; init; }
    public IReadOnlyDictionary<string, string>? Labels { get; init; }
    public string? Platform { get; init; }
    public string? Progress { get; init; }
    public string? Target { get; init; }
    public string? Memory { get; init; }
    public string? Builder { get; init; }
}
