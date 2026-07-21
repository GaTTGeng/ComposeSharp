namespace ComposeSharp.Api;

public sealed record ComposePsOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public bool All { get; init; }
    public string? Format { get; init; }
    public string? Status { get; init; }
    public bool Quiet { get; init; }
    public string? Filter { get; init; }
}
