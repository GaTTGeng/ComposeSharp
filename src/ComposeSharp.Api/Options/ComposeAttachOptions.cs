namespace ComposeSharp.Api;

public sealed record ComposeAttachOptions
{
    public string? ServiceName { get; init; }
    public int? Index { get; init; }
    public bool Stdin { get; init; }
    public bool Stdout { get; init; } = true;
    public bool Stderr { get; init; } = true;
    public string? DetachKeys { get; init; }
}
