namespace ComposeSharp.Api;

/// <summary>Options for executing a command inside a service container.</summary>
public sealed record ComposeExecOptions
{
    /// <summary>Command and arguments to run inside the container.</summary>
    public required IReadOnlyList<string> Command { get; init; }

    /// <summary>Run detached and return immediately instead of capturing output.</summary>
    public bool Detach { get; init; }

    /// <summary>Extra environment variables for the command, in <c>NAME=value</c> form.</summary>
    public IReadOnlyList<string>? Env { get; init; }

    /// <summary>Replica index within the service; null targets the first matching container.</summary>
    public int? Index { get; init; }

    /// <summary>Run the command with extended privileges.</summary>
    public bool Privileged { get; init; }

    /// <summary>User (name or UID) the command runs as.</summary>
    public string? User { get; init; }

    /// <summary>Working directory for the command inside the container.</summary>
    public string? Workdir { get; init; }

    /// <summary>Allocate a TTY for the command.</summary>
    public bool Tty { get; init; }

    /// <summary>Keep standard input open for the command.</summary>
    public bool Interactive { get; init; }
}
