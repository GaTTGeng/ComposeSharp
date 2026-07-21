namespace ComposeSharp.Api;

public sealed record ComposeProjectContext
{
    public required string ProjectName { get; init; }
    public required string WorkingDirectory { get; init; }
    public string ComposeFileName { get; init; } = "docker-compose.yml";
    public IReadOnlyList<string>? Profiles { get; init; }
    public string? SocketPath { get; init; }
    public DockerRegistryAuth? RegistryAuth { get; init; }
    public bool Offline { get; init; }
    public bool Compatibility { get; init; }
}

public sealed record DockerRegistryAuth
{
    public string? Username { get; init; }
    public string? Password { get; init; }
    public string? Email { get; init; }
    public string? ServerAddress { get; init; }
}
