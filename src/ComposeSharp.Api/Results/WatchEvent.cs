namespace ComposeSharp.Api;

/// <summary>
/// One reported build-context change for a selected service. Changes are reported continuously;
/// they do not trigger a rebuild or file synchronization by themselves.
/// </summary>
public sealed record WatchEvent
{
    /// <summary>Service whose build context changed.</summary>
    public required string ServiceName { get; init; }

    /// <summary>Recommended response to the change; currently <c>rebuild</c>.</summary>
    public required string Action { get; init; }

    /// <summary>Absolute path of the changed, created, deleted, or renamed build-context entry.</summary>
    public required string Path { get; init; }
}
