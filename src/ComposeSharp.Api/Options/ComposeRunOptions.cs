namespace ComposeSharp.Api;

/// <summary>Options for running a one-off container for a service.</summary>
/// <remarks>The container is labeled as one-off (<c>com.docker.compose.oneoff</c>) so it stays identifiable apart from the service's regular containers.</remarks>
public sealed record ComposeRunOptions
{
    /// <summary>Command overriding the service image's default command.</summary>
    public IReadOnlyList<string>? Command { get; init; }

    /// <summary>Entrypoint overriding the service image's default entrypoint.</summary>
    public string? Entrypoint { get; init; }

    /// <summary>Explicit container name; defaults to a generated project-service-index name.</summary>
    public string? Name { get; init; }

    /// <summary>Extra environment variables for the container, in <c>NAME=value</c> form.</summary>
    public IReadOnlyList<string>? Env { get; init; }

    /// <summary>Extra labels applied to the one-off container, in <c>key=value</c> form.</summary>
    public IReadOnlyList<string>? Labels { get; init; }

    /// <summary>Extra bind/volume mounts applied to the container, in <c>source:target[:mode]</c> form.</summary>
    public IReadOnlyList<string>? Volumes { get; init; }

    /// <summary>Remove the container automatically when it exits.</summary>
    public bool Remove { get; init; }

    /// <summary>Run detached and return the container ID immediately.</summary>
    public bool Detach { get; init; }

    /// <summary>Do not start the service's dependency services first.</summary>
    public bool NoDeps { get; init; }

    /// <summary>Allocate a TTY for the container.</summary>
    public bool Tty { get; init; } = true;

    /// <summary>Keep standard input open for the container.</summary>
    public bool Interactive { get; init; }

    /// <summary>Working directory inside the container.</summary>
    public string? Workdir { get; init; }

    /// <summary>User (name or UID) the container process runs as.</summary>
    public string? User { get; init; }

    /// <summary>Linux capabilities to add to the container.</summary>
    public IReadOnlyList<string>? CapAdd { get; init; }

    /// <summary>Linux capabilities to drop from the container.</summary>
    public IReadOnlyList<string>? CapDrop { get; init; }

    /// <summary>Run the container with extended privileges.</summary>
    public bool Privileged { get; init; }

    /// <summary>Connect the container to the service's network aliases so dependents can resolve it.</summary>
    public bool UseNetworkAliases { get; init; }

    /// <summary>Replica index used in the generated container name and labels.</summary>
    public int? Index { get; init; }
}
