// Rider inspection rationale: Public model properties are consumed by package callers even when this solution does not read them.
// ReSharper disable NotAccessedPositionalProperty.Global
// Rider inspection rationale: This public type is part of the package contract and is intended for downstream callers.
// ReSharper disable UnusedType.Global
namespace ComposeSharp.Loader.Models;

/// <summary>
/// Shape of a top-level Compose volume definition.
/// The loader currently exposes project volumes as name lists on <see cref="ComposeProject"/>;
/// this record captures the richer Compose shape for inspection but is not populated yet.
/// </summary>
/// <param name="Name">Volume key from the top-level <c>volumes</c> mapping.</param>
/// <param name="External">When true, the volume is expected to pre-exist outside the project.</param>
/// <param name="Driver">Volume driver name; not applied by the engine.</param>
/// <param name="Labels">Volume labels; not applied by the engine.</param>
/// <param name="DriverOpts">Driver-specific options; not applied by the engine.</param>
public sealed record ComposeVolume(
    string Name,
    bool External,
    string? Driver,
    IReadOnlyDictionary<string, string>? Labels,
    IReadOnlyDictionary<string, string>? DriverOpts);
// ReSharper restore UnusedType.Global
// ReSharper restore NotAccessedPositionalProperty.Global
