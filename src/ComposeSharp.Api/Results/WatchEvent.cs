namespace ComposeSharp.Api;

/// <summary>
/// One reported build-context change. Events indicate which paths changed; they do not trigger a
/// rebuild or file synchronization by themselves.
/// </summary>
public sealed record WatchEvent
{
    /// <summary>Service whose build context changed.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Recommended response to the change, for example <c>rebuild</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Build-context path that changed.</summary>
    public required string Path { get; init; }
}
