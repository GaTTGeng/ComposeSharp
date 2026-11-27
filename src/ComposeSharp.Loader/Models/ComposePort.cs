// Rider inspection rationale: Public model properties are consumed by package callers even when this solution does not read them.
// ReSharper disable NotAccessedPositionalProperty.Global
namespace ComposeSharp.Loader.Models;

/// <summary>
/// A published port mapping. Target ranges are expanded by the loader; a host port
/// range on a single target is allocated by Docker. A null host port requests a
/// dynamically assigned port. An unspecified host IP uses the daemon default.
/// </summary>
public sealed record ComposePort(string? HostPort, string ContainerPort, string Protocol = "tcp")
{
    /// <summary>The explicit host IP address, without IPv6 brackets, or null when unspecified.</summary>
    public string? HostIp { get; init; }

    /// <summary>The optional descriptive name from long syntax.</summary>
    // Rider inspection rationale: Public model initializers are available to downstream callers.
    // ReSharper disable once UnusedAutoPropertyAccessor.Global
    public string? Name { get; init; }
}

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
// ReSharper restore NotAccessedPositionalProperty.Global
