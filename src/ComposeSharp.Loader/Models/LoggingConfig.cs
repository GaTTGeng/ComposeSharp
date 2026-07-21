namespace ComposeSharp.Loader.Models;

public sealed record LoggingConfig(
    string? Driver,
    IReadOnlyDictionary<string, string>? Options);
