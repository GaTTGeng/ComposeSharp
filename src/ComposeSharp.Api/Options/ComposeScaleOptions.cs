// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options describing the desired replica counts per service.</summary>
public sealed record ComposeScaleOptions
{
    /// <summary>
    /// Desired replica count per service name. These counts are applied directly and take precedence
    /// over <c>deploy.replicas</c> declared in the Compose file.
    /// </summary>
    public required IReadOnlyDictionary<string, int> Services { get; init; }

    /// <summary>Grace period in seconds when stopping surplus containers.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Do not also rescale the selection's dependency services.</summary>
    public bool NoDeps { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
