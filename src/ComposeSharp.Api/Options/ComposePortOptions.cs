namespace ComposeSharp.Api;

/// <summary>Options for resolving a published container port.</summary>
public sealed record ComposePortOptions
{
    /// <summary>Transport protocol of the container port.</summary>
    public string Protocol { get; init; } = "tcp";

    /// <summary>Replica index within the service; null resolves the first matching container.</summary>
    public int? Index { get; init; }
}
