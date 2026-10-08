namespace ComposeSharp.Loader.Models;

/// <summary>
/// A short-syntax port mapping. Long port syntax is rejected at load time.
/// <see cref="ContainerPort"/> keeps the <c>/protocol</c> suffix as written;
/// <see cref="HostPort"/> is null for container-only entries, which expose the port
/// without creating a host binding. Port ranges and host IPs beyond the short forms are not modeled.
/// </summary>
public sealed record ComposePort(string? HostPort, string ContainerPort, string Protocol = "tcp");

/// <summary>
/// A service healthcheck. Durations use the loader's restricted syntax
/// (.NET <see cref="TimeSpan"/> format or a single whole-number <c>ms</c>/<c>s</c>/<c>m</c>/<c>h</c> unit).
/// A scalar <c>test</c> string is normalized to a <c>["CMD-SHELL", ...]</c> list at parse time;
/// <c>start_interval</c> is not modeled.
/// </summary>
/// <param name="Disabled">Mirrors the YAML key <c>disable</c>; when true the check is turned off.</param>
/// <param name="Test">Test command list, already normalized as described above.</param>
/// <param name="Interval">Delay between checks.</param>
/// <param name="Timeout">Per-check timeout.</param>
/// <param name="Retries">Consecutive failures tolerated before the container is unhealthy.</param>
/// <param name="StartPeriod">Grace period during which failures do not count toward <paramref name="Retries"/>.</param>
public sealed record ComposeHealthcheck(
    bool Disabled,
    IReadOnlyList<string> Test,
    TimeSpan? Interval,
    TimeSpan? Timeout,
    int? Retries,
    TimeSpan? StartPeriod);
