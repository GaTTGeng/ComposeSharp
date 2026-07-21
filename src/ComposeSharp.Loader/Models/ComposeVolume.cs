namespace ComposeSharp.Loader.Models;

public sealed record ComposeVolume(
    string Name,
    bool External,
    string? Driver,
    IReadOnlyDictionary<string, string>? Labels,
    IReadOnlyDictionary<string, string>? DriverOpts);
