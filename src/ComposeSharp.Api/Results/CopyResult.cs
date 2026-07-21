namespace ComposeSharp.Api;

public sealed record CopyResult
{
    public byte[]? Content { get; init; }
    public bool IsDirectory { get; init; }
    public long BytesCopied { get; init; }
    public int ExitCode { get; init; }
}
