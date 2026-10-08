namespace ComposeSharp.Api;

/// <summary>Summary of one volume owned by the project.</summary>
public sealed record VolumesSummary
{
    /// <summary>Volume name.</summary>
    public required string Name { get; init; }

    /// <summary>Volume driver providing the storage.</summary>
    public required string Driver { get; init; }

    /// <summary>Host path where the volume is mounted, when the driver exposes one.</summary>
    public string? Mountpoint { get; init; }

    /// <summary>Labels applied to the volume, including the Compose ownership labels.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; init; } = new Dictionary<string, string>();

    /// <summary>Scope of the volume (for example <c>local</c> or <c>global</c>).</summary>
    public string? Scope { get; init; }

    /// <summary>Driver-specific volume options.</summary>
    public IReadOnlyDictionary<string, string>? Options { get; init; }

    /// <summary>Creation time as reported by the Docker Engine.</summary>
    public string? CreatedAt { get; init; }
}
