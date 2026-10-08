namespace ComposeSharp.Api;

/// <summary>Options for listing the project's containers.</summary>
public sealed record ComposePsOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Include stopped containers, not only running ones.</summary>
    public bool All { get; init; }

    /// <summary>Output format template applied to each container row.</summary>
    public string? Format { get; init; }

    /// <summary>Only include containers in this state (for example <c>running</c> or <c>exited</c>).</summary>
    public string? Status { get; init; }

    /// <summary>Return only container IDs, omitting summary details.</summary>
    public bool Quiet { get; init; }

    /// <summary>Filter expression applied to the container listing.</summary>
    public string? Filter { get; init; }
}
