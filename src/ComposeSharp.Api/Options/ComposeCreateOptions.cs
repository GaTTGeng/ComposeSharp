namespace ComposeSharp.Api;

/// <summary>Options for creating service containers without a full <c>up</c>.</summary>
public sealed record ComposeCreateOptions
{
    /// <summary>Restricts creation to these service names; null creates every selected service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Build images before creating containers.</summary>
    public bool Build { get; init; }

    /// <summary>Recreate containers even when their configuration did not change.</summary>
    public bool ForceRecreate { get; init; }

    /// <summary>Keep existing containers instead of recreating them.</summary>
    public bool NoRecreate { get; init; }

    /// <summary>
    /// Image pull policy: <c>policy</c> (honor each service's pull_policy), <c>always</c>, <c>missing</c>, or <c>never</c>.
    /// </summary>
    public string Pull { get; init; } = "policy";

    /// <summary>Per-service replica counts; these take precedence over <c>deploy.replicas</c> in the Compose file.</summary>
    public IReadOnlyDictionary<string, int>? Scale { get; init; }

    /// <summary>Remove containers of services that no longer exist in the Compose file.</summary>
    public bool RemoveOrphans { get; init; }

    /// <summary>Suppress pull progress output.</summary>
    public bool QuietPull { get; init; }

    /// <summary>Grace period in seconds when stopping containers during recreation.</summary>
    public int? TimeoutSeconds { get; init; }
}
