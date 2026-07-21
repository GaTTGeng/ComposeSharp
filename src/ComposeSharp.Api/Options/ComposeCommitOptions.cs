namespace ComposeSharp.Api;

public sealed record ComposeCommitOptions
{
    public required string Service { get; init; }
    public string? Reference { get; init; }
    public string? Author { get; init; }
    public string? Message { get; init; }
    public IReadOnlyDictionary<string, string>? Changes { get; init; }
    public bool Pause { get; init; } = true;
    public int? Index { get; init; }
}
