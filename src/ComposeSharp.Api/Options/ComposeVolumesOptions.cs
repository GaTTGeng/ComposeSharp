namespace ComposeSharp.Api;

/// <summary>Options for listing volumes owned by the project.</summary>
public sealed record ComposeVolumesOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
