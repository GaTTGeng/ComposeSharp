namespace ComposeSharp.Loader.Models;

public sealed record ComposePort(string? HostPort, string ContainerPort, string Protocol = "tcp");

public sealed record ComposeHealthcheck(
    bool Disabled,
    IReadOnlyList<string> Test,
    TimeSpan? Interval,
    TimeSpan? Timeout,
    int? Retries,
    TimeSpan? StartPeriod);
