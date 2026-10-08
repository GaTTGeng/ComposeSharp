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
