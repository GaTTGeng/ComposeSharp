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
    /// Same as <see cref="Merge"/>, but each override is injected into the process environment
    /// first so it wins over the project's <c>.env</c> during interpolation.
    /// </summary>
    public ComposeProject MergeWithEnv(string workingDirectory, IReadOnlyList<string> composeFiles, IReadOnlyDictionary<string, string> overrides)
    {
        // Overrides are process-wide and are not reverted afterwards; callers must not rely on
        // the previous environment being restored between calls.
        foreach (var (key, value) in overrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        return _loader.LoadMerged(workingDirectory, composeFiles);
    }
}
