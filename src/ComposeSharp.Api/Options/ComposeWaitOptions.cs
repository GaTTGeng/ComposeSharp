namespace ComposeSharp.Api;

/// <summary>Options for waiting on container exit of the project.</summary>
public sealed record ComposeWaitOptions
{
    /// <summary>Restricts the wait to these service names; null waits for every project container.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Tear the whole project down as soon as any watched container exits.</summary>
    public bool DownProjectOnContainerExit { get; init; }
}
