namespace ComposeSharp.Loader.Models;

public sealed record ComposeSecret(
    string Name,
    string? File,
    string? Environment,
    bool External,
    IReadOnlyDictionary<string, string>? Labels);

public sealed record ComposeConfig(
    string Name,
    string? File,
    string? Environment,
    bool External,
    IReadOnlyDictionary<string, string>? Labels);
