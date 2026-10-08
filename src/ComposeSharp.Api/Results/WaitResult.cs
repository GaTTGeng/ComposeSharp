namespace ComposeSharp.Api;

/// <summary>Exit codes collected after waiting for the project's containers to exit.</summary>
public sealed record WaitResult
{
    /// <summary>Exit code per container ID.</summary>
    public IReadOnlyDictionary<string, int> ExitCodes { get; init; } = new Dictionary<string, int>();

    /// <summary>First non-zero exit code observed, or 0 when every container exited cleanly.</summary>
    public int Code { get; init; }
}
