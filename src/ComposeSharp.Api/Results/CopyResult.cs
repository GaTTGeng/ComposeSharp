namespace ComposeSharp.Api;

/// <summary>Outcome of a host/container copy operation.</summary>
public sealed record CopyResult
{
    /// <summary>In-memory file content when the copy target was read into memory; null for on-disk or CLI-backed copies.</summary>
    public byte[]? Content { get; init; }

    /// <summary>Whether the copied source was a directory rather than a single file.</summary>
    public bool IsDirectory { get; init; }

    /// <summary>Number of bytes copied, when the implementation can report it.</summary>
    public long BytesCopied { get; init; }

    /// <summary>Process exit code of the underlying copy operation.</summary>
    public int ExitCode { get; init; }
}
