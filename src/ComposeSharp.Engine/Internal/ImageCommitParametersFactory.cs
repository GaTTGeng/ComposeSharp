using ComposeSharp.Api;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Maps commit options onto Docker Engine container-commit parameters.
/// </summary>
internal static class ImageCommitParametersFactory
{
    /// <summary>
    /// Builds commit parameters for <paramref name="reference"/>; the container ID is filled in by the caller.
    /// </summary>
    public static CommitContainerChangesParameters Create(string reference, ComposeCommitOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(options);

        var (repository, tag) = ImageManager.SplitImage(reference);
        return new CommitContainerChangesParameters
        {
            RepositoryName = repository,
            Tag = tag,
            Author = options.Author,
            Comment = options.Message,
            Pause = options.Pause,
            Changes = ToDockerfileChanges(options.Changes)
        };
    }

    // Docker's commit endpoint accepts Dockerfile instructions; the options model is a
    // key/value map so callers can pass structured data. Each pair becomes "KEY VALUE".
    internal static List<string>? ToDockerfileChanges(IReadOnlyDictionary<string, string>? changes)
    {
        if (changes is null || changes.Count == 0)
            return null;

        var result = new List<string>(changes.Count);
        foreach (var (key, value) in changes)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentException("Commit change keys must be non-empty Dockerfile instructions.", nameof(changes));

            result.Add(string.IsNullOrWhiteSpace(value) ? key.Trim() : $"{key.Trim()} {value}");
        }
        return result;
    }
}
