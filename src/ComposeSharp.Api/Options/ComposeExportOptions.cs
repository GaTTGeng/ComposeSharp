namespace ComposeSharp.Api;

public sealed record ComposeExportOptions
{
    public required string Service { get; init; }
    public required string OutputPath { get; init; }
    public int? Index { get; init; }
}
