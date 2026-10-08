namespace ComposeSharp.Api;

/// <summary>Options for listing images used by the project.</summary>
public sealed record ComposeImagesOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Output format template applied to each image row.</summary>
    public string? Format { get; init; }

    /// <summary>Return only image IDs, omitting summary details.</summary>
    public bool Quiet { get; init; }
}
