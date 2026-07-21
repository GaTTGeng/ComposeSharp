namespace ComposeSharp.Api;

public sealed record ComposeDownOptions
{
    public bool RemoveVolumes { get; init; }
    public bool RemoveOrphans { get; init; }
    public string? RemoveImages { get; init; }
    public IReadOnlyList<string>? Services { get; init; }
    public int? TimeoutSeconds { get; init; }
}
