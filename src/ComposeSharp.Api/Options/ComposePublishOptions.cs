// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
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
// ReSharper restore UnusedMember.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
