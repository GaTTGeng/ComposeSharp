namespace ComposeSharp.Api;

public sealed record ComposeImagesOptions
{
    public IReadOnlyList<string>? Services { get; init; }
    public string? Format { get; init; }
    public bool Quiet { get; init; }
}
