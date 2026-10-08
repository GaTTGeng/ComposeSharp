namespace ComposeSharp.Api;

/// <summary>Options selecting which services to pause.</summary>
public sealed record ComposePauseOptions
{
    /// <summary>Restricts the pause to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }
}
