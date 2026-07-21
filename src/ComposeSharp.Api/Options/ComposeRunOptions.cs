namespace ComposeSharp.Api;

public sealed record ComposeRunOptions
{
    public IReadOnlyList<string>? Command { get; init; }
    public string? Entrypoint { get; init; }
    public string? Name { get; init; }
    public IReadOnlyList<string>? Env { get; init; }
    public IReadOnlyList<string>? Labels { get; init; }
    public IReadOnlyList<string>? Volumes { get; init; }
    public bool Remove { get; init; }
    public bool Detach { get; init; }
    public bool NoDeps { get; init; }
    public bool Tty { get; init; } = true;
    public bool Interactive { get; init; }
    public string? Workdir { get; init; }
    public string? User { get; init; }
    public IReadOnlyList<string>? CapAdd { get; init; }
    public IReadOnlyList<string>? CapDrop { get; init; }
    public bool Privileged { get; init; }
    public bool UseNetworkAliases { get; init; }
    public int? Index { get; init; }
}
