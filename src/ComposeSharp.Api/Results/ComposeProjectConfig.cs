namespace ComposeSharp.Api;

public sealed record ComposeProjectConfig
{
    public required string Name { get; init; }
    public string? WorkingDirectory { get; init; }
    public IReadOnlyList<string> ConfigFiles { get; init; } = [];
    public IReadOnlyList<string> Services { get; init; } = [];
    public IReadOnlyList<string> Networks { get; init; } = [];
    public IReadOnlyList<string> Volumes { get; init; } = [];
    public IReadOnlyList<string> Secrets { get; init; } = [];
    public IReadOnlyList<string> Configs { get; init; } = [];
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
