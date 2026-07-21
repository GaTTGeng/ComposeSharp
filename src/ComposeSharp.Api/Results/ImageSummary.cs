namespace ComposeSharp.Api;

public sealed record ImageSummary
{
    public required string ID { get; init; }
    public required string Repository { get; init; }
    public required string Tag { get; init; }
    public string? Platform { get; init; }
    public long Size { get; init; }
    public DateTime? Created { get; init; }
    public DateTime? LastTagTime { get; init; }
}
