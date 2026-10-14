// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace ComposeSharp.Api;

/// <summary>Exit codes collected after waiting for the project's containers to exit.</summary>
public sealed record WaitResult
{
    /// <summary>Exit code per container ID.</summary>
    public IReadOnlyDictionary<string, int> ExitCodes { get; init; } = new Dictionary<string, int>();

    /// <summary>First non-zero exit code by ordinal container ID, or 0 when every container exited cleanly.</summary>
    public int Code { get; init; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
