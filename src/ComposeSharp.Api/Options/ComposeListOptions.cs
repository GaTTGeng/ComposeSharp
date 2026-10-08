namespace ComposeSharp.Api;

/// <summary>Options for listing Compose projects known to the Docker daemon.</summary>
public sealed record ComposeListOptions
{
    /// <summary>Include projects that have no running containers.</summary>
    public bool All { get; init; }

    /// <summary>Output format template applied to each project row.</summary>
    public string? Format { get; init; }

    /// <summary>Filter expression applied to the project listing.</summary>
    public string? Filter { get; init; }

    /// <summary>Return only project names, omitting summary details.</summary>
    public bool Quiet { get; init; }
}
