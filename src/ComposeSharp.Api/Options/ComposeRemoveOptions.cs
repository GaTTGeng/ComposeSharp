// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
namespace ComposeSharp.Api;

/// <summary>Options for removing the project's containers.</summary>
public sealed record ComposeRemoveOptions
{
    /// <summary>Restricts the removal to these service names; null removes every project service's containers.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Remove containers even if they cannot be stopped cleanly.</summary>
    public bool Force { get; init; }

    /// <summary>Stop running containers before removing them.</summary>
    public bool Stop { get; init; }

    /// <summary>Also remove anonymous volumes attached to the containers.</summary>
    public bool Volumes { get; init; }
}
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
