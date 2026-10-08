namespace ComposeSharp.Loader.Models;

/// <summary>
/// Service <c>build</c> configuration. A scalar <c>build: ./path</c> becomes
/// <see cref="Context"/> alone. The engine applies context, dockerfile, args, cache_from,
/// target, tags, labels, network, extra_hosts, shm_size, one platform, pull, and no_cache;
/// cache_to, multiple platforms, and privileged are parsed but not applied.
/// </summary>
/// <param name="Context">Build context path; resolved relative to the Compose file directory.</param>
/// <param name="Dockerfile">Dockerfile path relative to the context, when not the default.</param>
/// <param name="Args">
/// Build arguments in declaration order. A null value means the argument is declared without a
/// value and should inherit from the process environment at build time.
/// </param>
/// <param name="CacheFrom">Cache source images or configurations; applied by the engine.</param>
/// <param name="CacheTo">Cache export targets; parsed only.</param>
/// <param name="Target">Multi-stage target to stop at.</param>
/// <param name="Tags">Additional image tags applied to the built image.</param>
/// <param name="Labels">Labels applied to the built image.</param>
/// <param name="Network">Network mode used during the build.</param>
/// <param name="ExtraHosts">Extra host mappings for the build container.</param>
/// <param name="Privileged">Privileged build mode; parsed only.</param>
/// <param name="ShmSize">Shared memory size for the build container, in the string form accepted by the loader.</param>
/// <param name="Platforms">Target platforms; only a single platform is applied by the engine.</param>
/// <param name="Pull">Forces pulling base images even when cached.</param>
/// <param name="NoCache">Disables the build cache.</param>
/// <param name="ContextDirectory">Auxiliary context directory metadata retained from the build mapping.</param>
public sealed record BuildConfig(
    string? Context,
    string? Dockerfile,
    IReadOnlyDictionary<string, string?>? Args,
    IReadOnlyList<string>? CacheFrom,
    IReadOnlyList<string>? CacheTo,
    string? Target,
    IReadOnlyList<string>? Tags,
    IReadOnlyDictionary<string, string>? Labels,
    string? Network,
    IReadOnlyDictionary<string, string>? ExtraHosts,
    bool? Privileged,
    string? ShmSize,
    IReadOnlyList<string>? Platforms,
    bool? Pull,
    bool? NoCache,
    string? ContextDirectory);
