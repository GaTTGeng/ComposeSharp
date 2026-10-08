namespace ComposeSharp.Api;

/// <summary>Options for tagging project images under a repository.</summary>
public sealed record ComposePublishOptions
{
    /// <summary>Prefer content digests when resolving image references.</summary>
    public bool ResolveImageDigests { get; init; }

    /// <summary>Include service environment configuration alongside the tagged image.</summary>
    public bool WithEnvironment { get; init; }

    /// <summary>OCI image-spec version the published artifacts should target.</summary>
    public string? OcIVersion { get; init; }

    /// <summary>Allow tagging/pushing against a registry without TLS verification.</summary>
    public bool InsecureRegistry { get; init; }
}
