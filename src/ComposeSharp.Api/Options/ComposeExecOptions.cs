namespace ComposeSharp.Api;

public sealed record ComposeExecOptions
{
    public required IReadOnlyList<string> Command { get; init; }
    public bool Detach { get; init; }
    public IReadOnlyList<string>? Env { get; init; }
    public int? Index { get; init; }
    public bool Privileged { get; init; }
    public string? User { get; init; }
    public string? Workdir { get; init; }
    public bool Tty { get; init; }
    public bool Interactive { get; init; }
}
