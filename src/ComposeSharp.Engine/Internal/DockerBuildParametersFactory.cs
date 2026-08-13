using ComposeSharp.Api;
using ComposeSharp.Loader.Models;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

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
            Dockerfile = build.Dockerfile,
            BuildArgs = Merge(build.Args, options?.BuildArgs),
            Labels = Merge(build.Labels, options?.Labels),
            CacheFrom = build.CacheFrom?.ToList(),
            Target = options?.Target ?? build.Target,
            Platform = options?.Platform ?? GetSinglePlatform(service.Name, build.Platforms),
            NetworkMode = build.Network,
            ExtraHosts = build.ExtraHosts?.Select(host => $"{host.Key}:{host.Value}").ToList(),
            ShmSize = ParseBytes(build.ShmSize, "build.shm_size"),
            Memory = ParseBytes(options?.Memory, nameof(options.Memory))
        };
    }

    private static Dictionary<string, string>? Merge(
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
        var multiplier = 1L;
        if (char.IsLetter(text[^1]))
        {
            multiplier = char.ToUpperInvariant(text[^1]) switch
            {
                'K' => 1024L,
                'M' => 1024L * 1024,
                'G' => 1024L * 1024 * 1024,
                _ => throw new ArgumentException($"{property} must be an integer byte count or use a K, M, or G suffix.", property)
            };
            text = text[..^1];
        }

        if (!long.TryParse(text, out var bytes) || bytes < 0)
            throw new ArgumentException($"{property} must be an integer byte count or use a K, M, or G suffix.", property);

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
