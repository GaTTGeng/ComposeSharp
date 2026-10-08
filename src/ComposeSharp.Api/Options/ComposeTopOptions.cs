namespace ComposeSharp.Api;

/// <summary>Options for listing processes inside the project's containers.</summary>
public sealed record ComposeTopOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
