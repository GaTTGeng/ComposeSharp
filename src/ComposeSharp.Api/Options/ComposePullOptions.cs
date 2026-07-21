namespace ComposeSharp.Api;

public sealed record ComposePullOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool IncludeDeps { get; init; }
    public bool Quiet { get; init; }
    public string? Platform { get; init; }
    public bool IgnoreFailures { get; init; }
}
