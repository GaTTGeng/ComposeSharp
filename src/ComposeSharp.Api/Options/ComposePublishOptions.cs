namespace ComposeSharp.Api;

public sealed record ComposePublishOptions
{
    public bool ResolveImageDigests { get; init; }
    public bool WithEnvironment { get; init; }
    public string? OcIVersion { get; init; }
    public bool InsecureRegistry { get; init; }
}
