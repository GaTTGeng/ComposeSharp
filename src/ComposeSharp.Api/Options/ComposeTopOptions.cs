namespace ComposeSharp.Api;

/// <summary>Options for listing processes inside the project's containers.</summary>
public sealed record ComposeTopOptions
{
    /// <summary>
    /// Restricts the listing to these service names. Null covers every project service; an empty
    /// list selects no services and yields an empty listing.
    /// </summary>
    public IReadOnlyList<string>? Services { get; init; }
}
