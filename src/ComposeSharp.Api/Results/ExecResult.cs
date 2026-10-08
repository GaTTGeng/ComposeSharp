namespace ComposeSharp.Api;

/// <summary>Outcome of executing a command inside a service container.</summary>
public sealed record ExecResult
{
    /// <summary>Exit code returned by the command.</summary>
    public int ExitCode { get; init; }

    /// <summary>Everything the command wrote to standard output.</summary>
    public string StandardOutput { get; init; } = string.Empty;

    /// <summary>Everything the command wrote to standard error.</summary>
    public string StandardError { get; init; } = string.Empty;

    /// <summary>Whether the command exited with code 0.</summary>
    public bool Succeeded => ExitCode == 0;
}
