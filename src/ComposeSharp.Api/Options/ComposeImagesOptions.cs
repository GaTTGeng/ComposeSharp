// Rider inspection rationale: This folder layout organizes source files, while the established public namespace is kept stable for callers.
// ReSharper disable CheckNamespace
// Rider inspection rationale: The public contract type is instantiated by downstream callers, outside this solution.
// ReSharper disable ClassNeverInstantiated.Global
// Rider inspection rationale: Public DTO accessors are consumed by callers outside this solution and remain part of the contract.
// ReSharper disable UnusedAutoPropertyAccessor.Global
// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
namespace ComposeSharp.Api;

/// <summary>Options for listing images used by the project.</summary>
public sealed record ComposeImagesOptions
{
    /// <summary>Restricts the listing to these service names; null covers every project service.</summary>
    public IReadOnlyList<string>? Services { get; init; }

    /// <summary>Output format template applied to each image row.</summary>
    public string? Format { get; init; }

    /// <summary>Return only image IDs, omitting summary details.</summary>
    public bool Quiet { get; init; }
}
// ReSharper restore UnusedMember.Global
// ReSharper restore UnusedAutoPropertyAccessor.Global
// ReSharper restore ClassNeverInstantiated.Global
// ReSharper restore CheckNamespace
