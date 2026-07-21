namespace ComposeSharp.Api;

public interface ILogConsumer
{
    void OnLog(string serviceName, string message, bool isStdErr);
    void OnLogComplete(string serviceName);
    void OnStatus(string serviceName, string message);
}
