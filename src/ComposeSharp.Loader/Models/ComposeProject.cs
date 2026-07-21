namespace ComposeSharp.Loader.Models;

public sealed record ComposeProject(
    string WorkingDirectory,
    IReadOnlyList<ServiceDefinition> Services,
    IReadOnlyList<string> Volumes,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> Secrets,
    IReadOnlyList<string> Configs,
    IReadOnlyDictionary<string, string> Extensions);
