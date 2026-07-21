namespace ComposeSharp.Api;

public sealed record VolumesSummary
{
    public required string Name { get; init; }
    public required string Driver { get; init; }
    public string? Mountpoint { get; init; }
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();
    public string? Scope { get; init; }
    public IReadOnlyDictionary<string, string>? Options { get; init; }
    public string? CreatedAt { get; init; }
}
