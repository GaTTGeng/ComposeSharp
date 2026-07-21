namespace ComposeSharp.Loader.Models;

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

public sealed record ResourceConfig(LimitsConfig? Limits, ReservationConfig? Reservations);

public sealed record LimitsConfig(long? Memory, string? NanoCpus, long? CpuCount);

public sealed record ReservationConfig(long? Memory, string? NanoCpus);

public sealed record RestartPolicyConfig(
    string? Condition,
    int? MaxRetries,
    TimeSpan? Delay,
    TimeSpan? Window,
    TimeSpan? Period);

public sealed record PlacementConfig(IReadOnlyList<string>? Constraints);

public sealed record UpdateConfig(
    string? Parallelism,
    TimeSpan? Delay,
    string? Order,
    TimeSpan? Monitor,
    string? FailureAction,
    double? MaxFailureRatio);

public sealed record RollbackConfig(
    string? Parallelism,
    TimeSpan? Delay,
    string? Order,
    TimeSpan? Monitor,
    string? FailureAction,
    double? MaxFailureRatio);

public sealed record EndpointModeConfig(string? Mode);
