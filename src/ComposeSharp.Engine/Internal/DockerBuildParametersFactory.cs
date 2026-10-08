using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>
/// Maps a service's build configuration (plus caller overrides) onto Docker Engine image build parameters.
/// </summary>
internal static class DockerBuildParametersFactory
{
    public static ImageBuildParameters Create(ServiceDefinition service, ComposeBuildOptions? options)
    {
        var build = service.Build ?? throw new ArgumentException($"Service '{service.Name}' does not have a build configuration.", nameof(service));
        var tags = new List<string> { service.Image ?? service.Name };
        if (build.Tags is not null)
            tags.AddRange(build.Tags);

        return new ImageBuildParameters
        {
            Tags = tags.Distinct(StringComparer.Ordinal).ToList(),
            SuppressOutput = options?.Quiet == true,
            NoCache = options?.NoCache == true || build.NoCache == true,
            Pull = options?.Pull == true || build.Pull == true ? "true" : null,
            // The caller rewrites Dockerfile to the path inside the build-context archive
            // (see DockerBuildContextArchive) before the build request is sent.
            Dockerfile = build.Dockerfile,
            BuildArgs = MergeBuildArgs(build.Args, options?.BuildArgs),
            Labels = MergeStrings(build.Labels, options?.Labels),
            CacheFrom = build.CacheFrom?.ToList(),
            Target = options?.Target ?? build.Target,
            Platform = options?.Platform ?? GetSinglePlatform(service.Name, build.Platforms) ?? service.Platform,
            NetworkMode = build.Network,
            ExtraHosts = build.ExtraHosts?.Select(host => $"{host.Key}:{host.Value}").ToList(),
            ShmSize = ParseBytes(build.ShmSize, "build.shm_size"),
            Memory = ParseBytes(options?.Memory, nameof(options.Memory))
        };
    }

    // A null-valued build arg means "take the value from the process environment",
    // matching Compose build-arg semantics; explicit option overrides win last.
    private static Dictionary<string, string>? MergeBuildArgs(
        IReadOnlyDictionary<string, string?>? configured,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (configured is null && overrides is null)
            return null;

        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (configured is not null)
        {
            foreach (var (key, value) in configured)
            {
                if (value is not null)
                    result[key] = value;
                else if (Environment.GetEnvironmentVariable(key) is { } environmentValue)
                    result[key] = environmentValue;
            }
        }

        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                result[key] = value;
        }

        return result;
    }

    private static Dictionary<string, string>? MergeStrings(
        IReadOnlyDictionary<string, string>? configured,
        IReadOnlyDictionary<string, string>? overrides)
    {
        if (configured is null && overrides is null)
            return null;

        var result = configured is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(configured, StringComparer.Ordinal);
        if (overrides is not null)
        {
            foreach (var (key, value) in overrides)
                result[key] = value;
        }
        return result;
    }

    // Docker Engine builds accept one platform per request, so multi-platform lists are rejected instead of silently dropped.
    private static string? GetSinglePlatform(string serviceName, IReadOnlyList<string>? platforms)
    {
        if (platforms is not { Count: > 1 })
            return platforms?.SingleOrDefault();

        throw new NotSupportedException(
            $"Service '{serviceName}' configures multiple build platforms. Docker Engine builds currently support one platform per request.");
    }

    private static long? ParseBytes(string? value, string property)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();
        var suffixStart = text.Length;
        while (suffixStart > 0 && char.IsLetter(text[suffixStart - 1]))
            suffixStart--;
        var suffix = text[suffixStart..].ToUpperInvariant();
        var multiplier = suffix switch
        {
            "" or "B" => 1L,
            "K" or "KB" or "KIB" => 1024L,
            "M" or "MB" or "MIB" => 1024L * 1024,
            "G" or "GB" or "GIB" => 1024L * 1024 * 1024,
            "T" or "TB" or "TIB" => 1024L * 1024 * 1024 * 1024,
            _ => throw new ArgumentException($"{property} must be an integer byte count or use a supported byte-unit suffix.", property)
        };
        text = text[..suffixStart];

        if (!long.TryParse(text, out var bytes) || bytes < 0)
            throw new ArgumentException($"{property} must be an integer byte count or use a supported byte-unit suffix.", property);

        try
        {
            return checked(bytes * multiplier);
        }
        catch (OverflowException exception)
        {
            throw new ArgumentException($"{property} is too large.", property, exception);
        }
    }
}
