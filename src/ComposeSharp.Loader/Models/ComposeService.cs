namespace ComposeSharp.Loader.Models;

/// <summary>
/// A single Compose service after loading and interpolation.
/// The loader retains more fields than the engine currently applies; see
/// <c>docs/compose-field-matrix.md</c> for which members are applied, partial, or parsed only.
/// </summary>
/// <remarks>
/// Non-obvious semantics:
/// <list type="bullet">
/// <item><description><c>Command</c>/<c>Entrypoint</c>: scalar YAML becomes a single-element list rather than being shell-split.</description></item>
/// <item><description><c>Environment</c>: flattened <c>KEY=VALUE</c> entries — values from each <c>env_file</c> first, then the inline <c>environment</c> block. <c>env_file</c> contents are container inputs and are not interpolation sources for the YAML text.</description></item>
/// <item><description><c>Ports</c>: short syntax only; long syntax is rejected at load time.</description></item>
/// <item><description><c>DependsOn</c>: dependency names only — long-form conditions are dropped, and the engine does not yet provide complete ordering or health readiness.</description></item>
/// <item><description><c>Profiles</c>: selection happens later via <c>ComposeProjectContext.Profiles</c>; the loader does not filter services here.</description></item>
/// <item><description><c>Deploy</c>: only <c>replicas</c> currently affects the engine.</description></item>
/// <item><description><c>Secrets</c>/<c>Configs</c>: names are parsed and surfaced but never provisioned or mounted.</description></item>
/// <item><description><c>ExtendsService</c>/<c>ExtendsFile</c>: the <c>extends</c> reference is recorded but never resolved or merged.</description></item>
/// <item><description><c>Develop</c>: watch configuration is not interpreted.</description></item>
/// <item><description><c>EnvFile</c>: original entries kept for inspection; their values are already folded into <c>Environment</c>.</description></item>
/// <item><description><c>StopSignal</c>/<c>StopGracePeriod</c>/<c>Sysctls</c>/<c>Logging</c>/<c>Platform</c>/<c>PullPolicy</c>/<c>Dns</c>/<c>DnsSearch</c>/<c>Links</c>/<c>CpuQuota</c>/<c>Annotations</c>/<c>OomScoreAdj</c>: parsed only, not sent to Docker.</description></item>
/// <item><description><c>RestartMaxRetries</c>: retry count stripped from an <c>on-failure:N</c> restart value; parsed but not sent.</description></item>
/// <item><description><c>OomKillDisable</c>: retained as <c>1</c> only when the YAML value is <c>true</c>; an explicit <c>false</c> is indistinguishable from omission.</description></item>
/// </list>
/// </remarks>
public sealed record ServiceDefinition(
    string Name,
    string? Image,
    BuildConfig? Build,
    string? ContainerName,
    IReadOnlyList<string> Command,
    IReadOnlyList<string> Entrypoint,
    IReadOnlyList<string> Environment,
    IReadOnlyList<ComposePort> Ports,
    IReadOnlyList<string> Volumes,
    string? Restart,
    ComposeHealthcheck? Healthcheck,
    IReadOnlyList<string> DependsOn,
    IReadOnlyList<string> Networks,
    IReadOnlyList<string> ExtraHosts,
    bool Privileged,
    string? NetworkMode,
    string? Ipc,
    long? ShmSize,
    IReadOnlyList<string> Profiles,
    DeployConfig? Deploy,
    IReadOnlyList<string> Secrets,
    IReadOnlyList<string> Configs,
    IReadOnlyDictionary<string, string> Labels,
    LoggingConfig? Logging,
    string? Hostname,
    string? Domainname,
    string? User,
    string? WorkingDir,
    bool Tty,
    bool StdinOpen,
    string? StopSignal,
    TimeSpan? StopGracePeriod,
    bool ReadOnly,
    IReadOnlyList<string> Tmpfs,
    IReadOnlyList<string> CapAdd,
    IReadOnlyList<string> CapDrop,
    IReadOnlyList<string> Devices,
    IReadOnlyDictionary<string, string> Sysctls,
    IReadOnlyList<string> SecurityOpt,
    bool? Init,
    string? Platform,
    string? PullPolicy,
    IReadOnlyList<string> Dns,
    IReadOnlyList<string> DnsSearch,
    string? Pid,
    string? MacAddress,
    string? CgroupParent,
    string? ExtendsService,
    string? ExtendsFile,
    string? Develop,
    IReadOnlyList<string> EnvFile,
    IReadOnlyList<string> Links,
    string? CpuShares,
    string? CpuQuota,
    string? Cpuset,
    long? Memory,
    long? MemorySwap,
    long? MemoryReservation,
    long? OomKillDisable,
    string? OomScoreAdj,
    IReadOnlyList<string> GroupAdd,
    string? RestartMaxRetries,
    IReadOnlyDictionary<string, string> Annotations);
