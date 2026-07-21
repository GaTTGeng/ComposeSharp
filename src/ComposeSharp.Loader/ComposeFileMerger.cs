using ComposeSharp.Loader.Models;

namespace ComposeSharp.Loader;

public sealed class ComposeFileMerger
{
    private readonly ComposeFileLoader _loader = new();

    public ComposeProject Merge(string workingDirectory, IReadOnlyList<string> composeFiles)
    {
        return _loader.LoadMerged(workingDirectory, composeFiles);
    }

    public ComposeProject MergeWithEnv(string workingDirectory, IReadOnlyList<string> composeFiles, IReadOnlyDictionary<string, string> overrides)
    {
        foreach (var (key, value) in overrides)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        return _loader.LoadMerged(workingDirectory, composeFiles);
    }
}
