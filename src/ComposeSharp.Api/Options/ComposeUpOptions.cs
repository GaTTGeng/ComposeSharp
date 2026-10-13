// Rider inspection rationale: The init setter supports caller-side option construction and must remain configurable.
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Global
// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options controlling a full <c>up</c> of the project.</summary>
public sealed record ComposeUpOptions
{
    /// <summary>Return once containers are started instead of following their output.</summary>
    public bool Detach { get; init; } = true;

    /// <summary>Build images before starting containers, even if an image already exists.</summary>
    public bool Build { get; init; }

    /// <summary>Recreate containers even when their configuration did not change.</summary>
    public bool ForceRecreate { get; init; }

    /// <summary>Keep existing containers instead of recreating them.</summary>
    public bool NoRecreate { get; init; }

    /// <summary>Create containers but do not start them.</summary>
    public bool NoStart { get; init; }

    /// <summary>
    /// Image pull policy: <c>policy</c> (honor each service's pull_policy), <c>always</c>, <c>missing</c>, or <c>never</c>.
    /// </summary>
    public string Pull { get; init; } = "policy";

    /// <summary>Remove containers of services that no longer exist in the Compose file.</summary>
    public bool RemoveOrphans { get; init; }

    /// <summary>Per-service replica counts; these take precedence over <c>deploy.replicas</c> in the Compose file.</summary>
    public IReadOnlyDictionary<string, int>? Scale { get; init; }

    /// <summary>Restricts the up to these service names (plus their dependencies); null targets every service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Grace period in seconds when stopping containers during recreation.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Block until the started containers are running (or healthy) before returning.</summary>
    public bool Wait { get; init; }

    /// <summary>Maximum seconds to wait for each dependency to become ready; null waits until readiness, failure, or cancellation.</summary>
    public int? DependencyWaitTimeoutSeconds { get; init; }

    /// <summary>Never build images, failing when a required image is missing.</summary>
    public bool NoBuild { get; init; }

    /// <summary>Suppress pull progress output.</summary>
    public bool QuietPull { get; init; }

    /// <summary>Recreate anonymous volumes instead of reusing them across recreations.</summary>
    public bool RenewAnonVolumes { get; init; }

    /// <summary>Always recreate dependency services, even when their configuration did not change.</summary>
    public bool AlwaysRecreateDeps { get; init; }

    /// <summary>Receives container output and status lines during the up; null discards them.</summary>
    public ILogConsumer? LogConsumer { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore CheckNamespace
// ReSharper restore AutoPropertyCanBeMadeGetOnly.Global
