namespace ComposeSharp.Loader.Models;

/// <summary>
/// Service <c>logging</c> block. Parsed only: the driver and options are exposed
/// for inspection but are not sent to Docker.
/// </summary>
/// <param name="Driver">Logging driver name.</param>
/// <param name="Options">Driver-specific options.</param>
public sealed record LoggingConfig(
    string? Driver,
    IReadOnlyDictionary<string, string>? Options);
