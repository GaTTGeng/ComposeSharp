namespace ComposeSharp.Api;

/// <summary>Options for pulling images referenced by the project.</summary>
public sealed record ComposePullOptions
{
    /// <summary>Restricts the pull to these service names; null pulls every project service's image.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Also pull images of services the selection depends on.</summary>
    public bool IncludeDeps { get; init; }

    /// <summary>Suppress pull progress output.</summary>
    public bool Quiet { get; init; }

    /// <summary>Target platform for the pulled image, for example <c>linux/amd64</c>.</summary>
    public string? Platform { get; init; }

    /// <summary>Continue pulling remaining images when one pull fails.</summary>
    public bool IgnoreFailures { get; init; }
}
