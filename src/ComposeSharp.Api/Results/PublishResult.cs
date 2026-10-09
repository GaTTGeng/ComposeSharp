namespace ComposeSharp.Api;

/// <summary>Outcome of tagging and pushing one service image during publish.</summary>
public sealed record PublishResult
{
    /// <summary>Service whose image was published.</summary>
    public required string Service { get; init; }

    /// <summary>Source image reference from the service definition.</summary>
    public string? SourceImage { get; init; }

    /// <summary>Image reference after tagging into the target repository (<c>repository:service</c>).</summary>
    public string? TaggedImage { get; init; }

    /// <summary>Whether the source image was tagged into the target repository.</summary>
    public bool Tagged { get; init; }

    /// <summary>Whether the tagged image was pushed to its registry.</summary>
    public bool Pushed { get; init; }

    /// <summary>Error message when tagging or pushing failed.</summary>
    public string? Error { get; init; }

    /// <summary>Whether both tagging and pushing succeeded.</summary>
    public bool Succeeded => Tagged && Pushed;
}
