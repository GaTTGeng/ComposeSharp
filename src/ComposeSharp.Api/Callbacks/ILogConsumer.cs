// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: This parameter is part of a public interface contract and is consumed by implementations or callers outside this solution.
// ReSharper disable UnusedParameter.Global
namespace ComposeSharp.Api;

/// <summary>
/// Receives log, status, and completion callbacks during streaming operations.
/// Implementations must be prepared for concurrent calls when multiple services stream in parallel.
/// </summary>
public interface ILogConsumer
{
    /// <summary>Called for each log line of a service.</summary>
    void OnLog(string serviceName, string message, bool isStdErr);

    /// <summary>Called once when a service's log stream ends.</summary>
    void OnLogComplete(string serviceName);

    /// <summary>Called with progress or status updates that are not container log output.</summary>
    void OnStatus(string serviceName, string message);
}
// ReSharper restore UnusedParameter.Global
// ReSharper restore CheckNamespace
