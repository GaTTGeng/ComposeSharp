namespace ComposeSharp.Api;

/// <summary>Options for pushing built images to their registries.</summary>
public sealed record ComposePushOptions
{
    /// <summary>Restricts the push to these service names; null pushes every project service's image.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Continue pushing remaining images when one push fails.</summary>
    public bool IgnoreFailures { get; init; }

    /// <summary>Suppress push progress output.</summary>
    public bool Quiet { get; init; }
}
