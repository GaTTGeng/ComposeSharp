namespace ComposeSharp.Api;

public sealed record Stack
{
    public required string ID { get; init; }
    public required string Name { get; init; }
    public required string Status { get; init; }
    public string? ConfigFiles { get; init; }
    public string? Reason { get; init; }
}
