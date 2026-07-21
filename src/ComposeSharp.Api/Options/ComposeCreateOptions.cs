namespace ComposeSharp.Api;

public sealed record ComposeCreateOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool Build { get; init; }
    public bool ForceRecreate { get; init; }
    public bool NoRecreate { get; init; }
    public string Pull { get; init; } = "policy";
    public IReadOnlyDictionary<string, int>? Scale { get; init; }
    public bool RemoveOrphans { get; init; }
    public bool QuietPull { get; init; }
    public int? TimeoutSeconds { get; init; }
}
