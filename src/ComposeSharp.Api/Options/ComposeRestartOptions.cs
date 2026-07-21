namespace ComposeSharp.Api;

public sealed record ComposeRestartOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public int? TimeoutSeconds { get; init; }
    public bool NoDeps { get; init; }
}
