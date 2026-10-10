namespace ComposeSharp.Api;

/// <summary>Options for rendering the loaded configuration within one Compose project.</summary>
public sealed record ComposeGenerateOptions
{
    /// <summary>Project name written to the rendered configuration; defaults to the context's project name.</summary>
    public string? ProjectName { get; init; }

    /// <summary>Service names to include in the rendered configuration; null includes services active under the context's profiles.</summary>
    /// <remarks>
    /// Explicit services may include services gated by inactive profiles. An empty list selects none;
    /// unknown names are rejected. Selection does not automatically include dependencies.
    /// </remarks>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Legacy container names or IDs; generation cannot inspect containers and rejects nonempty values.</summary>
    public IReadOnlyList<string>? Containers { get; init; }
}
