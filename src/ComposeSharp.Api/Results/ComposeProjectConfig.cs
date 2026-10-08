namespace ComposeSharp.Api;

/// <summary>
/// Summary of a loaded Compose project configuration. Lists names of top-level elements only;
/// it is not a full serialization of the merged Compose document.
/// </summary>
public sealed record ComposeProjectConfig
{
    /// <summary>Project name the configuration was loaded under.</summary>
    public required string Name { get; init; }

    /// <summary>Working directory the Compose file was loaded from.</summary>
    public string? WorkingDirectory { get; init; }

    /// <summary>Compose files that contributed to this configuration.</summary>
    public IReadOnlyList<string> ConfigFiles { get; init; } = [];

    /// <summary>Names of the defined services, after profile filtering.</summary>
    public IReadOnlyList<string> Services { get; init; } = [];

    /// <summary>Names of the declared networks.</summary>
    public IReadOnlyList<string> Networks { get; init; } = [];

    /// <summary>Names of the declared volumes.</summary>
    public IReadOnlyList<string> Volumes { get; init; } = [];

    /// <summary>Names of the declared secrets.</summary>
    public IReadOnlyList<string> Secrets { get; init; } = [];

    /// <summary>Names of the declared configs.</summary>
    public IReadOnlyList<string> Configs { get; init; } = [];

    /// <summary>Environment values resolved for the project, if requested.</summary>
    public IReadOnlyDictionary<string, string> Environment { get; init; } = new Dictionary<string, string>();
}
