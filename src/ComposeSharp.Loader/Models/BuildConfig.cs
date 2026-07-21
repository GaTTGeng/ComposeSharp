namespace ComposeSharp.Loader.Models;

public sealed record BuildConfig(
    string? Context,
    string? Dockerfile,
    IReadOnlyDictionary<string, string>? Args,
    IReadOnlyList<string>? CacheFrom,
    IReadOnlyList<string>? CacheTo,
    string? Target,
    IReadOnlyList<string>? Tags,
    IReadOnlyDictionary<string, string>? Labels,
    string? Network,
    IReadOnlyDictionary<string, string>? ExtraHosts,
    bool? Privileged,
    string? ShmSize,
    IReadOnlyList<string>? Platforms,
    bool? Pull,
    bool? NoCache,
    string? ContextDirectory);
