namespace ComposeSharp.Api;

public sealed record ComposeKillOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public string Signal { get; init; } = "SIGKILL";
    public bool RemoveOrphans { get; init; }
}
