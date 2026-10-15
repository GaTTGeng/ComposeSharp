// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
// Rider inspection rationale: This public type is part of the package contract and is intended for downstream callers.
// ReSharper disable UnusedType.Global
using ComposeSharp.Loader.Models;

namespace ComposeSharp.Loader;

/// <summary>
/// Convenience facade over <see cref="ComposeFileLoader"/> for multi-file Compose projects.
/// </summary>
public sealed class ComposeFileMerger
{
    private readonly ComposeFileLoader _loader = new();

    /// <summary>
    /// Loads and merges Compose files in order: the first file is the base document and each
    /// later file is an overlay. The merge is an incremental subset of Compose semantics, not
    /// the full Compose Specification merge rules.
    /// </summary>
    public ComposeProject Merge(string workingDirectory, IReadOnlyList<string> composeFiles)
    {
        return _loader.LoadMerged(workingDirectory, composeFiles);
    }

    /// <summary>
    /// Same as <see cref="Merge"/>, but explicit values take precedence over the process
    /// environment and project <c>.env</c> files for this call only.
    /// </summary>
    /// <remarks>
    /// Overrides are applied to interpolation without changing process-wide environment
    /// variables, so concurrent merges remain isolated and callers need no environment cleanup.
    /// </remarks>
    public ComposeProject MergeWithEnv(string workingDirectory, IReadOnlyList<string> composeFiles, IReadOnlyDictionary<string, string> overrides)
    {
        return _loader.LoadMergedWithEnvironment(workingDirectory, composeFiles, overrides);
    }
}
// ReSharper restore UnusedType.Global
// ReSharper restore UnusedMember.Global
