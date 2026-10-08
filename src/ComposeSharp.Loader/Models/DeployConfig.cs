namespace ComposeSharp.Loader.Models;

/// <summary>
/// Service <c>deploy</c> block. Only <see cref="Replicas"/> currently affects the engine
/// (via <c>UpAsync</c>); everything else is parsed for inspection. Scalar <c>endpoint_mode</c>
/// and standard <c>deploy.resources.*.cpus</c> values are not retained.
/// </summary>
/// <param name="Replicas">Desired replica count; the only deploy value the engine applies.</param>
/// <param name="Resources">Resource limits and reservations; parsed only.</param>
/// <param name="RestartPolicy">Swarm-style restart policy; parsed only and not the service-level <c>restart</c> key.</param>
/// <param name="Placement">Scheduling constraints; parsed only.</param>
/// <param name="UpdateConfig">Rolling-update settings; parsed only.</param>
/// <param name="RollbackConfig">Rollback settings; parsed only.</param>
/// <param name="Labels">Labels attached to the deployed service; parsed only.</param>
/// <param name="Mode">Orchestration mode such as <c>replicated</c> or <c>global</c>.</param>
/// <param name="EndpointMode">Endpoint mode wrapper; only mapping form is retained.</param>
public sealed record DeployConfig(
    int? Replicas,
    ResourceConfig? Resources,
    RestartPolicyConfig? RestartPolicy,
    PlacementConfig? Placement,
    UpdateConfig? UpdateConfig,
    RollbackConfig? RollbackConfig,
    IReadOnlyDictionary<string, string>? Labels,
    string? Mode,
    EndpointModeConfig? EndpointMode);

/// <summary>Resource limits and reservations under <c>deploy.resources</c>.</summary>
/// <param name="Limits">Hard resource ceilings.</param>
/// <param name="Reservations">Guaranteed resource floors.</param>
public sealed record ResourceConfig(LimitsConfig? Limits, ReservationConfig? Reservations);

/// <summary>
/// Resource ceilings. <see cref="Memory"/> accepts integer/K/M/G byte forms;
/// <see cref="NanoCpus"/> keeps the literal <c>nano_cpus</c> string and is not applied.
/// </summary>
/// <param name="Memory">Memory ceiling in bytes.</param>
/// <param name="NanoCpus">Literal <c>nano_cpus</c> value; parsed only.</param>
/// <param name="CpuCount">CPU count limit from the non-standard <c>cpus</c> key; not applied.</param>
public sealed record LimitsConfig(long? Memory, string? NanoCpus, long? CpuCount);

/// <summary>Resource reservations; memory is parsed, <c>nano_cpus</c> is parsed only.</summary>
/// <param name="Memory">Reserved memory in bytes.</param>
/// <param name="NanoCpus">Literal <c>nano_cpus</c> value; parsed only.</param>
public sealed record ReservationConfig(long? Memory, string? NanoCpus);

/// <summary>
/// Swarm restart policy under <c>deploy.restart_policy</c>. Distinct from the service-level
/// <c>restart</c> key; none of these fields are sent to Docker today. Durations use the
/// loader's restricted duration syntax.
/// </summary>
/// <param name="Condition">Policy condition such as <c>on-failure</c>.</param>
/// <param name="MaxRetries">Retry ceiling for <c>on-failure</c>.</param>
/// <param name="Delay">Delay between restart attempts.</param>
/// <param name="Window">Window in which failures count toward a restart decision.</param>
/// <param name="Period">Additional swarm-only period field retained from the mapping.</param>
public sealed record RestartPolicyConfig(
    string? Condition,
    int? MaxRetries,
    TimeSpan? Delay,
    TimeSpan? Window,
    TimeSpan? Period);

/// <summary>Placement constraints under <c>deploy.placement</c>; parsed only.</summary>
/// <param name="Constraints">Constraint expressions such as node labels.</param>
public sealed record PlacementConfig(IReadOnlyList<string>? Constraints);

/// <summary>
/// Rolling-update settings. <see cref="Parallelism"/> is kept as the raw string;
/// durations use the loader's restricted duration syntax. Parsed only.
/// </summary>
public sealed record UpdateConfig(
    string? Parallelism,
    TimeSpan? Delay,
    string? Order,
    TimeSpan? Monitor,
    string? FailureAction,
    double? MaxFailureRatio);

/// <summary>Rollback settings; same shape and parsed-only status as <see cref="UpdateConfig"/>.</summary>
public sealed record RollbackConfig(
    string? Parallelism,
    TimeSpan? Delay,
    string? Order,
    TimeSpan? Monitor,
    string? FailureAction,
    double? MaxFailureRatio);

/// <summary>Mapping form of <c>deploy.endpoint_mode</c>; scalar form is not retained.</summary>
/// <param name="Mode">Endpoint mode name.</param>
public sealed record EndpointModeConfig(string? Mode);
