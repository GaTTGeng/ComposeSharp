namespace ComposeSharp.Api;

/// <summary>Summary of one image used by the project.</summary>
public sealed record ImageSummary
{
    /// <summary>Image ID.</summary>
    public required string ID { get; init; }

    /// <summary>Repository part of the image reference.</summary>
    public required string Repository { get; init; }

    /// <summary>Tag part of the image reference.</summary>
    public required string Tag { get; init; }

    /// <summary>Platform the image targets, for example <c>linux/amd64</c>.</summary>
    public string? Platform { get; init; }

    /// <summary>Image size in bytes.</summary>
    public long Size { get; init; }

    /// <summary>Time the image was created.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Time the image was last tagged.</summary>
    public DateTime? LastTagTime { get; init; }
}
