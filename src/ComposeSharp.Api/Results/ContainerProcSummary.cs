namespace ComposeSharp.Api;

public sealed record ContainerProcSummary
{
    public required string ID { get; init; }
    public required string Name { get; init; }
    public required string Service { get; init; }
    public string? Replica { get; init; }
    public IReadOnlyList<string> Titles { get; init; } = [];
    public IReadOnlyList<IReadOnlyList<string>> Processes { get; init; } = [];
}
