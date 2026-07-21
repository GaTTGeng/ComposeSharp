namespace ComposeSharp.Api;

public sealed record ComposeCopyOptions
{
    public required string Source { get; init; }
    public required string Destination { get; init; }
    public bool FollowLink { get; init; }
    public bool CopyUidGid { get; init; }
    public int? Index { get; init; }
    public bool All { get; init; }
}
