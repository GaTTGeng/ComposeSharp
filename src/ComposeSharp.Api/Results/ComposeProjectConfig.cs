namespace ComposeSharp.Api;

/// <summary>
/// Loaded Compose project summary and, when generated, a normalized YAML snapshot within the project scope.
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

    /// <summary>Normalized YAML generated from the loader's retained project model; null for summary-only loads.</summary>
    /// <remarks>
    /// This is a snapshot of the loader's supported model, not a full Compose Specification
    /// serialization. The loader does not retain every source field or top-level resource option;
    /// some retained deploy fields use its own limited spelling and shape, and scalar
    /// <c>develop</c> content is omitted because its watch rules are not modeled. Resolved service
    /// environment values are included and may contain secrets; handle this text accordingly.
    /// </remarks>
    public string? RenderedYaml { get; init; }
}
