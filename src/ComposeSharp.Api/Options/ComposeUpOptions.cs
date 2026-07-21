namespace ComposeSharp.Api;

public sealed record ComposeUpOptions
{
    public bool Detach { get; init; } = true;
    public bool Build { get; init; }
    public bool ForceRecreate { get; init; }
    public bool NoRecreate { get; init; }
    public bool NoStart { get; init; }
    public string Pull { get; init; } = "policy";
    public bool RemoveOrphans { get; init; }
    public IReadOnlyDictionary<string, int>? Scale { get; init; }
    public IReadOnlyList<string>? Services { get; init; }
    public int? TimeoutSeconds { get; init; }
    public bool Wait { get; init; }
    public bool NoBuild { get; init; }
    public bool QuietPull { get; init; }
    public bool RenewAnonVolumes { get; init; }
    public bool AlwaysRecreateDeps { get; init; }
    public ILogConsumer? LogConsumer { get; init; }
}
