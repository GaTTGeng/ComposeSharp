namespace ComposeSharp.Api;

public sealed record ComposeGenerateOptions
{
    public string? ProjectName { get; init; }
    public IReadOnlyList<string>? Containers { get; init; }
}
