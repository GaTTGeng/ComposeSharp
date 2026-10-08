namespace ComposeSharp.Api;

/// <summary>
/// Identifies one Compose project and the environment it is loaded from.
/// All engine-created resources are owned by <see cref="ProjectName"/> and are never operated on
/// outside that boundary.
/// </summary>
public sealed record ComposeProjectContext
{
    /// <summary>Project name; becomes the <c>com.docker.compose.project</c> label on owned resources.</summary>
    public required string ProjectName { get; init; }

    /// <summary>Directory that contains the Compose file and serves as the relative base for paths in it.</summary>
    public required string WorkingDirectory { get; init; }

    /// <summary>Compose file name resolved under <see cref="WorkingDirectory"/>.</summary>
    public string ComposeFileName { get; init; } = "docker-compose.yml";

    /// <summary>
    /// Active profiles. Services gated on profiles are included only when one of their profiles is listed here.
    /// </summary>
    public IReadOnlyList<string>? Profiles { get; init; }

    /// <summary>
    /// Docker daemon endpoint override. Accepts a full <c>unix://</c> or <c>npipe://</c> URI, or a bare
    /// socket/named-pipe path; when null the platform default endpoint is used.
    /// </summary>
    public string? SocketPath { get; init; }

    /// <summary>Registry credentials used for image pulls and pushes.</summary>
    public DockerRegistryAuth? RegistryAuth { get; init; }

    /// <summary>When true, operations avoid network-dependent steps such as image pulls.</summary>
    public bool Offline { get; init; }

    /// <summary>When true, apply legacy Compose CLI compatibility behavior.</summary>
    public bool Compatibility { get; init; }
}

/// <summary>
/// Credentials for a Docker registry. Supplied by local configuration; never treated as project content.
/// </summary>
public sealed record DockerRegistryAuth
{
    /// <summary>Registry username.</summary>
    public string? Username { get; init; }

    /// <summary>Registry password or token.</summary>
    public string? Password { get; init; }

    /// <summary>Account email associated with the registry login, when required.</summary>
    public string? Email { get; init; }

    /// <summary>Registry server address; null lets the engine infer it from the image reference.</summary>
    public string? ServerAddress { get; init; }
}
