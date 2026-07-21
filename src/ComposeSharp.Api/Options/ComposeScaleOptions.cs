namespace ComposeSharp.Api;

public sealed record ComposeScaleOptions
{
    public required IReadOnlyDictionary<string, int> Services { get; init; }
    public int? TimeoutSeconds { get; init; }
    public bool NoDeps { get; init; }
}
