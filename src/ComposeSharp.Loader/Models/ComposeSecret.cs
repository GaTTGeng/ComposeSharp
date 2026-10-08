namespace ComposeSharp.Loader.Models;

/// <summary>
/// Shape of a top-level Compose secret definition.
/// Secrets are parsed and surfaced by name but are not provisioned or mounted;
/// long secret syntax is not retained correctly.
/// </summary>
/// <param name="Name">Secret key from the top-level <c>secrets</c> mapping.</param>
/// <param name="File">Source file path, when given as a file-backed secret.</param>
/// <param name="Environment">Source environment variable name, when given as an environment-backed secret.</param>
/// <param name="External">When true, the secret is expected to pre-exist outside the project.</param>
/// <param name="Labels">Secret labels; not applied by the engine.</param>
public sealed record ComposeSecret(
    string Name,
    string? File,
    string? Environment,
    bool External,
    IReadOnlyDictionary<string, string>? Labels);

/// <summary>
/// Shape of a top-level Compose config definition.
/// Configs share the secret model in Compose and share its limitations here:
/// parsed and surfaced by name, never provisioned or mounted.
/// </summary>
/// <param name="Name">Config key from the top-level <c>configs</c> mapping.</param>
/// <param name="File">Source file path, when given as a file-backed config.</param>
/// <param name="Environment">Source environment variable name, when given as an environment-backed config.</param>
/// <param name="External">When true, the config is expected to pre-exist outside the project.</param>
/// <param name="Labels">Config labels; not applied by the engine.</param>
public sealed record ComposeConfig(
    string Name,
    string? File,
    string? Environment,
    bool External,
    IReadOnlyDictionary<string, string>? Labels);
