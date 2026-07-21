using System.Text.RegularExpressions;
using ComposeSharp.Loader.Interpolation;
using ComposeSharp.Loader.Models;
using YamlDotNet.Serialization;

namespace ComposeSharp.Loader;

public sealed class ComposeFileLoader
{
    public ComposeProject Load(string workingDirectory, string composeFileName)
    {
        var composePath = ResolveComposePath(workingDirectory, composeFileName);
        var env = LoadDotEnv(Path.Combine(Path.GetDirectoryName(composePath)!, ".env"));
        var raw = File.ReadAllText(composePath);
        var expanded = VariableInterpolator.Expand(raw, env);
        var deserializer = new DeserializerBuilder().Build();
        var root = deserializer.Deserialize<Dictionary<object, object?>>(expanded)
                   ?? throw new InvalidOperationException("Compose file is empty.");

        var servicesMap = GetMap(root, "services")
                          ?? throw new NotSupportedException("Compose file must contain a services map.");

        var volumes = ParseNameList(root, "volumes");
        var networks = ParseNameList(root, "networks");
        var secrets = ParseNameList(root, "secrets");
        var configs = ParseNameList(root, "configs");
        var extensions = ParseExtensions(root);

        var services = new List<ServiceDefinition>();
        foreach (var (serviceNameObj, serviceValue) in servicesMap)
        {
            var serviceName = serviceNameObj.ToString()!;
            var map = serviceValue as Dictionary<object, object?>
                      ?? throw new NotSupportedException($"Service '{serviceName}' must be a YAML map.");

            services.Add(ParseService(serviceName, map, composePath, env));
        }

        return new ComposeProject(
            Path.GetDirectoryName(composePath)!,
            services,
            volumes,
            networks,
            secrets,
            configs,
            extensions);
    }

    public ComposeProject LoadMerged(string workingDirectory, IReadOnlyList<string> composeFiles)
    {
        if (composeFiles.Count == 0)
            throw new ArgumentException("At least one compose file is required.");
        if (composeFiles.Count == 1)
            return Load(workingDirectory, composeFiles[0]);

        ComposeProject? merged = null;
        foreach (var file in composeFiles)
        {
            var project = Load(workingDirectory, file);
            merged = merged is null ? project : MergeProjects(merged, project);
        }
        return merged!;
    }

    private static string ResolveComposePath(string workingDirectory, string composeFileName)
    {
        var composePath = Path.Combine(workingDirectory, composeFileName);
        if (File.Exists(composePath)) return composePath;

        var candidates = new[] { "compose.yml", "compose.yaml", "docker-compose.yaml", "docker-compose.yml" };
        foreach (var candidate in candidates)
        {
            var path = Path.Combine(workingDirectory, candidate);
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Compose file was not found.", composePath);
    }

    private static ComposeProject MergeProjects(ComposeProject baseProject, ComposeProject overlay)
    {
        var services = new List<ServiceDefinition>(baseProject.Services);
        foreach (var overlayService in overlay.Services)
        {
            var existingIdx = services.FindIndex(s => s.Name == overlayService.Name);
            if (existingIdx >= 0)
                services[existingIdx] = overlayService;
            else
                services.Add(overlayService);
        }

        return new ComposeProject(
            overlay.WorkingDirectory,
            services,
            overlay.Volumes.Count > 0 ? overlay.Volumes : baseProject.Volumes,
            overlay.Networks.Count > 0 ? overlay.Networks : baseProject.Networks,
            overlay.Secrets.Count > 0 ? overlay.Secrets : baseProject.Secrets,
            overlay.Configs.Count > 0 ? overlay.Configs : baseProject.Configs,
            overlay.Extensions.Count > 0 ? overlay.Extensions : baseProject.Extensions);
    }

    private ServiceDefinition ParseService(
        string serviceName,
        Dictionary<object, object?> map,
        string composePath,
        IReadOnlyDictionary<string, string> env)
    {
        var image = GetString(map, "image");
        var build = ParseBuildConfig(map);

        if (image is null && build is null)
            throw new NotSupportedException($"Service '{serviceName}' must specify either 'image' or 'build'.");

        var serviceEnv = new List<string>();
        foreach (var envFile in GetStringList(map, "env_file"))
        {
            var envFilePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(composePath)!, envFile));
            serviceEnv.AddRange(LoadDotEnv(envFilePath).Select(kv => $"{kv.Key}={kv.Value}"));
        }
        serviceEnv.AddRange(ReadEnvironment(map));

        var dependsOnRaw = ReadDependsOn(map);
        var profiles = GetStringList(map, "profiles");
        var deploy = ParseDeployConfig(map);
        var logging = ParseLoggingConfig(map);
        var (extendsService, extendsFile) = ParseExtends(map);

        return new ServiceDefinition(
            Name: serviceName,
            Image: image,
            Build: build,
            ContainerName: GetString(map, "container_name"),
            Command: GetStringList(map, "command"),
            Entrypoint: GetStringList(map, "entrypoint"),
            Environment: serviceEnv,
            Ports: ReadPorts(map),
            Volumes: GetStringList(map, "volumes"),
            Restart: GetString(map, "restart"),
            Healthcheck: ReadHealthcheck(map),
            DependsOn: dependsOnRaw,
            Networks: GetStringList(map, "networks"),
            ExtraHosts: GetStringList(map, "extra_hosts"),
            Privileged: GetBool(map, "privileged") ?? false,
            NetworkMode: GetString(map, "network_mode"),
            Ipc: GetString(map, "ipc"),
            ShmSize: GetLongBytes(map, "shm_size"),
            Profiles: profiles,
            Deploy: deploy,
            Secrets: GetStringList(map, "secrets"),
            Configs: GetStringList(map, "configs"),
            Labels: GetStringDictionary(map, "labels"),
            Logging: logging,
            Hostname: GetString(map, "hostname"),
            Domainname: GetString(map, "domainname"),
            User: GetString(map, "user"),
            WorkingDir: GetString(map, "working_dir"),
            Tty: GetBool(map, "tty") ?? false,
            StdinOpen: GetBool(map, "stdin_open") ?? false,
            StopSignal: GetString(map, "stop_signal"),
            StopGracePeriod: ParseDuration(GetString(map, "stop_grace_period")),
            ReadOnly: GetBool(map, "read_only") ?? false,
            Tmpfs: GetStringList(map, "tmpfs"),
            CapAdd: GetStringList(map, "cap_add"),
            CapDrop: GetStringList(map, "cap_drop"),
            Devices: GetStringList(map, "devices"),
            Sysctls: GetStringDictionary(map, "sysctls"),
            SecurityOpt: GetStringList(map, "security_opt"),
            Init: GetBool(map, "init"),
            Platform: GetString(map, "platform"),
            PullPolicy: GetString(map, "pull_policy"),
            Dns: GetStringList(map, "dns"),
            DnsSearch: GetStringList(map, "dns_search"),
            Pid: GetString(map, "pid"),
            MacAddress: GetString(map, "mac_address"),
            CgroupParent: GetString(map, "cgroup_parent"),
            ExtendsService: extendsService,
            ExtendsFile: extendsFile,
            Develop: GetString(map, "develop"),
            EnvFile: GetStringList(map, "env_file"),
            Links: GetStringList(map, "links"),
            CpuShares: GetString(map, "cpu_shares"),
            CpuQuota: GetString(map, "cpu_quota"),
            Cpuset: GetString(map, "cpuset"),
            Memory: GetLongBytes(map, "mem_limit"),
            MemorySwap: GetLongBytes(map, "memswap_limit"),
            MemoryReservation: GetLongBytes(map, "mem_reservation"),
            OomKillDisable: GetBool(map, "oom_kill_disable") ?? false ? 1L : null,
            OomScoreAdj: GetString(map, "oom_score_adj"),
            GroupAdd: GetStringList(map, "group_add"),
            RestartMaxRetries: GetString(map, "restart")?.Contains(":") == true
                ? GetString(map, "restart")!.Split(':').Last()
                : null,
            Annotations: GetStringDictionary(map, "annotations"));
    }

    private static BuildConfig? ParseBuildConfig(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("build", out var buildValue) || buildValue is null)
            return null;

        if (buildValue is string context)
            return new BuildConfig(Context: context, Dockerfile: null, Args: null, CacheFrom: null, CacheTo: null,
                Target: null, Tags: null, Labels: null, Network: null, ExtraHosts: null, Privileged: null, ShmSize: null,
                Platforms: null, Pull: null, NoCache: null, ContextDirectory: null);

        if (buildValue is Dictionary<object, object?> buildMap)
        {
            return new BuildConfig(
                Context: GetString(buildMap, "context"),
                Dockerfile: GetString(buildMap, "dockerfile"),
                Args: GetStringDictionaryOrNull(buildMap, "args"),
                CacheFrom: GetStringListOrNull(buildMap, "cache_from"),
                CacheTo: GetStringListOrNull(buildMap, "cache_to"),
                Target: GetString(buildMap, "target"),
                Tags: GetStringListOrNull(buildMap, "tags"),
                Labels: GetStringDictionaryOrNull(buildMap, "labels"),
                Network: GetString(buildMap, "network"),
                ExtraHosts: GetStringDictionaryOrNull(buildMap, "extra_hosts"),
                Privileged: GetBool(buildMap, "privileged"),
                ShmSize: GetString(buildMap, "shm_size"),
                Platforms: GetStringListOrNull(buildMap, "platforms"),
                Pull: GetBool(buildMap, "pull"),
                NoCache: GetBool(buildMap, "no_cache"),
                ContextDirectory: GetString(buildMap, "context_directory"));
        }

        return null;
    }

    private static DeployConfig? ParseDeployConfig(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("deploy", out var deployValue) || deployValue is not Dictionary<object, object?> deployMap)
            return null;

        return new DeployConfig(
            Replicas: GetInt(deployMap, "replicas"),
            Resources: ParseResourceConfig(deployMap),
            RestartPolicy: ParseRestartPolicyConfig(deployMap),
            Placement: ParsePlacementConfig(deployMap),
            UpdateConfig: ParseUpdateConfig(deployMap),
            RollbackConfig: ParseRollbackConfig(deployMap),
            Labels: GetStringDictionaryOrNull(deployMap, "labels"),
            Mode: GetString(deployMap, "mode"),
            EndpointMode: ParseEndpointModeConfig(deployMap));
    }

    private static ResourceConfig? ParseResourceConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("resources", out var resValue) || resValue is not Dictionary<object, object?> resMap)
            return null;

        return new ResourceConfig(
            Limits: ParseLimitsConfig(resMap),
            Reservations: ParseReservationConfig(resMap));
    }

    private static LimitsConfig? ParseLimitsConfig(Dictionary<object, object?> resMap)
    {
        if (!resMap.TryGetValue("limits", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new LimitsConfig(Memory: GetLongBytes(m, "memory"), NanoCpus: GetString(m, "nano_cpus"), CpuCount: GetInt(m, "cpus"));
    }

    private static ReservationConfig? ParseReservationConfig(Dictionary<object, object?> resMap)
    {
        if (!resMap.TryGetValue("reservations", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new ReservationConfig(Memory: GetLongBytes(m, "memory"), NanoCpus: GetString(m, "nano_cpus"));
    }

    private static RestartPolicyConfig? ParseRestartPolicyConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("restart_policy", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new RestartPolicyConfig(
            Condition: GetString(m, "condition"),
            MaxRetries: GetInt(m, "max_retries"),
            Delay: ParseDuration(GetString(m, "delay")),
            Window: ParseDuration(GetString(m, "window")),
            Period: ParseDuration(GetString(m, "period")));
    }

    private static PlacementConfig? ParsePlacementConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("placement", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new PlacementConfig(Constraints: GetStringListOrNull(m, "constraints"));
    }

    private static UpdateConfig? ParseUpdateConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("update_config", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new UpdateConfig(
            Parallelism: GetString(m, "parallelism"),
            Delay: ParseDuration(GetString(m, "delay")),
            Order: GetString(m, "order"),
            Monitor: ParseDuration(GetString(m, "monitor")),
            FailureAction: GetString(m, "failure_action"),
            MaxFailureRatio: GetDouble(m, "max_failure_ratio"));
    }

    private static RollbackConfig? ParseRollbackConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("rollback_config", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new RollbackConfig(
            Parallelism: GetString(m, "parallelism"),
            Delay: ParseDuration(GetString(m, "delay")),
            Order: GetString(m, "order"),
            Monitor: ParseDuration(GetString(m, "monitor")),
            FailureAction: GetString(m, "failure_action"),
            MaxFailureRatio: GetDouble(m, "max_failure_ratio"));
    }

    private static EndpointModeConfig? ParseEndpointModeConfig(Dictionary<object, object?> deployMap)
    {
        if (!deployMap.TryGetValue("endpoint_mode", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new EndpointModeConfig(Mode: GetString(m, "mode"));
    }

    private static LoggingConfig? ParseLoggingConfig(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("logging", out var val) || val is not Dictionary<object, object?> m)
            return null;
        return new LoggingConfig(Driver: GetString(m, "driver"), Options: GetStringDictionaryOrNull(m, "options"));
    }

    private static (string? Service, string? File) ParseExtends(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("extends", out var val) || val is null)
            return (null, null);

        if (val is Dictionary<object, object?> m)
            return (GetString(m, "service"), GetString(m, "file"));

        if (val is string serviceName)
            return (serviceName, null);

        return (null, null);
    }

    private static List<string> ReadDependsOn(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("depends_on", out var value) || value is null)
            return [];

        if (value is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(x => x.Length > 0).ToList();

        if (value is Dictionary<object, object?> dict)
            return dict.Keys.Select(x => x.ToString()!).ToList();

        return [];
    }

    private static Dictionary<string, string> LoadDotEnv(string path)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path)) return result;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = line[(idx + 1)..].Trim().Trim('"').Trim('\'');
            result[key] = value;
        }
        return result;
    }

    private static List<string> ReadEnvironment(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("environment", out var value) || value is null) return [];

        if (value is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        if (value is Dictionary<object, object?> dict)
            return dict.Select(kv => $"{kv.Key}={kv.Value}").ToList();

        throw new NotSupportedException("environment must be a list or map.");
    }

    private static List<ComposePort> ReadPorts(Dictionary<object, object?> map)
    {
        return GetStringList(map, "ports").Select(ParsePort).ToList();
    }

    private static ComposePort ParsePort(string value)
    {
        var protocol = "tcp";
        var text = value.Trim('"', '\'');
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            protocol = text[(slash + 1)..];
            text = text[..slash];
        }

        var parts = text.Split(':', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 => new ComposePort(null, $"{parts[0]}/{protocol}", protocol),
            2 => new ComposePort(parts[0], $"{parts[1]}/{protocol}", protocol),
            >= 3 => new ComposePort(parts[^2], $"{parts[^1]}/{protocol}", protocol),
            _ => throw new NotSupportedException($"Invalid port mapping: {value}")
        };
    }

    private static ComposeHealthcheck? ReadHealthcheck(Dictionary<object, object?> map)
    {
        var healthMap = GetMap(map, "healthcheck");
        if (healthMap is null) return null;

        var disabled = GetBool(healthMap, "disable") ?? false;
        var test = GetStringList(healthMap, "test");
        if (test.Count == 0 && GetString(healthMap, "test") is { } testString)
            test = ["CMD-SHELL", testString];

        return new ComposeHealthcheck(
            disabled, test,
            ParseDuration(GetString(healthMap, "interval")),
            ParseDuration(GetString(healthMap, "timeout")),
            GetInt(healthMap, "retries"),
            ParseDuration(GetString(healthMap, "start_period")));
    }

    internal static TimeSpan? ParseDuration(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (TimeSpan.TryParse(value, out var parsed)) return parsed;

        var match = Regex.Match(value, @"^(?<num>\d+)(?<unit>ms|s|m|h)$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var number = int.Parse(match.Groups["num"].Value);
        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "ms" => TimeSpan.FromMilliseconds(number),
            "s" => TimeSpan.FromSeconds(number),
            "m" => TimeSpan.FromMinutes(number),
            "h" => TimeSpan.FromHours(number),
            _ => null
        };
    }

    private static IReadOnlyList<string> ParseNameList(Dictionary<object, object?> root, string key)
    {
        if (!root.TryGetValue(key, out var value) || value is not Dictionary<object, object?> dict)
            return [];
        return dict.Keys.Select(x => x.ToString()!).ToList();
    }

    private static IReadOnlyDictionary<string, string> ParseExtensions(Dictionary<object, object?> root)
    {
        var result = new Dictionary<string, string>();
        foreach (var kv in root)
        {
            var key = kv.Key.ToString()!;
            if (key.StartsWith("x-") && kv.Value is string s)
                result[key] = s;
        }
        return result;
    }

    private static Dictionary<object, object?>? GetMap(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value as Dictionary<object, object?> : null;

    private static string? GetString(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) ? value?.ToString() : null;

    private static bool? GetBool(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) && bool.TryParse(value?.ToString(), out var parsed) ? parsed : null;

    private static int? GetInt(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) && int.TryParse(value?.ToString(), out var parsed) ? parsed : null;

    private static double? GetDouble(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) && double.TryParse(value?.ToString(), out var parsed) ? parsed : null;

    private static long? GetLongBytes(Dictionary<object, object?> map, string key)
        => map.TryGetValue(key, out var value) ? ParseBytes(value?.ToString()) : null;

    private static long? ParseBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (long.TryParse(value, out var bytes)) return bytes;

        var match = Regex.Match(value, @"^(?<num>\d+)(?<unit>[kmg])b?$", RegexOptions.IgnoreCase);
        if (!match.Success) return null;

        var number = long.Parse(match.Groups["num"].Value);
        return match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "k" => number * 1024,
            "m" => number * 1024 * 1024,
            "g" => number * 1024 * 1024 * 1024,
            _ => null
        };
    }

    private static List<string> GetStringList(Dictionary<object, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null) return [];

        if (value is List<object?> list)
            return list.Select(x => x?.ToString() ?? "").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        if (value is Dictionary<object, object?> dict)
            return dict.Keys.Select(x => x.ToString()!).ToList();

        var text = value.ToString();
        return string.IsNullOrWhiteSpace(text) ? [] : [text];
    }

    private static IReadOnlyList<string>? GetStringListOrNull(Dictionary<object, object?> map, string key)
    {
        var list = GetStringList(map, key);
        return list.Count > 0 ? list : null;
    }

    private static IReadOnlyDictionary<string, string> GetStringDictionary(Dictionary<object, object?> map, string key)
    {
        if (!map.TryGetValue(key, out var value) || value is null)
            return new Dictionary<string, string>();

        if (value is Dictionary<object, object?> dict)
            return dict.ToDictionary(kv => kv.Key.ToString()!, kv => kv.Value?.ToString() ?? "");

        return new Dictionary<string, string>();
    }

    private static IReadOnlyDictionary<string, string>? GetStringDictionaryOrNull(Dictionary<object, object?> map, string key)
    {
        var dict = GetStringDictionary(map, key);
        return dict.Count > 0 ? dict : null;
    }
}
