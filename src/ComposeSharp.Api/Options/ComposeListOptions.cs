// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for listing Compose projects known to the Docker daemon.</summary>
public sealed record ComposeListOptions
{
    /// <summary>Include projects with containers excluded by Docker's default listing.</summary>
    public bool All { get; init; }

    /// <summary>Output format template applied to each project row.</summary>
    public string? Format { get; init; }

    /// <summary>Filter expression applied to the project listing.</summary>
    public string? Filter { get; init; }

    /// <summary>Return only project names, omitting summary details.</summary>
    public bool Quiet { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore CheckNamespace
