namespace ComposeSharp.Api;

public sealed record ComposeListOptions
{
    public bool All { get; init; }
    public string? Format { get; init; }
    public string? Filter { get; init; }
    public bool Quiet { get; init; }
}
