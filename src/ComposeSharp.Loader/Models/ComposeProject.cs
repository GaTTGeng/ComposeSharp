namespace ComposeSharp.Loader.Models;

/// <summary>
/// A loaded Compose project: parsed services plus the top-level resource names and extensions.
/// </summary>
/// <param name="WorkingDirectory">Directory of the primary Compose file; all relative paths resolve from here.</param>
/// <param name="Services">Service definitions in document order.</param>
/// <param name="Volumes">Top-level volume names only; driver options and external flags are not modeled.</param>
/// <param name="Networks">Top-level network names only; driver, IPAM, and external flags are not modeled.</param>
/// <param name="Secrets">Top-level secret names; parsed for inspection but not provisioned.</param>
/// <param name="Configs">Top-level config names; parsed for inspection but not provisioned.</param>
/// <param name="Extensions">String-valued <c>x-*</c> extensions; parsed only, with no engine behavior.</param>
public sealed record ComposeProject(
    string WorkingDirectory,
    IReadOnlyList<ServiceDefinition> Services,
    IReadOnlyList<string> Volumes,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> Secrets,
    IReadOnlyList<string> Configs,
    IReadOnlyDictionary<string, string> Extensions);
