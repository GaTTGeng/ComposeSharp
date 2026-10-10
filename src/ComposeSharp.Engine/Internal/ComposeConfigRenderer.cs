using System.Globalization;
using ComposeSharp.Loader.Models;
using YamlDotNet.Serialization;

namespace ComposeSharp.Engine.Internal;

internal static class ComposeConfigRenderer
{
    public static string Render(
        ComposeProject project,
        IReadOnlyList<ServiceDefinition> services,
        string projectName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = new Dictionary<string, object?>
        {
            ["name"] = projectName,
            ["services"] = services.ToDictionary(
                service => service.Name,
                service => (object?)RenderService(service, cancellationToken),
                StringComparer.Ordinal)
        };

        AddNames(root, "networks", project.Networks);
        AddNames(root, "volumes", project.Volumes);
        AddNames(root, "secrets", project.Secrets);
        AddNames(root, "configs", project.Configs);
        foreach (var extension in project.Extensions.OrderBy(pair => pair.Key, StringComparer.Ordinal))
            root[extension.Key] = extension.Value;

        cancellationToken.ThrowIfCancellationRequested();
        // The loader interpolates raw YAML before parsing it. Doubling dollars in scalar values
        // makes a second load preserve the already-resolved values in this snapshot.
        var yaml = new SerializerBuilder().Build().Serialize(EscapeInterpolation(root));
        cancellationToken.ThrowIfCancellationRequested();
        return yaml;
    }

    private static Dictionary<string, object?> RenderService(ServiceDefinition service, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = new Dictionary<string, object?>();
        Add(result, "image", service.Image);
        if (service.Build is { } build)
            result["build"] = RenderBuild(build);
        Add(result, "container_name", service.ContainerName);
        AddList(result, "command", service.Command);
        AddList(result, "entrypoint", service.Entrypoint);
        // The loader has already folded env_file values into Environment. Emitting env_file as
        // well would read local files again and could change the snapshot on a later load.
        AddList(result, "environment", service.Environment);
        AddList(result, "ports", service.Ports.Select(port =>
            port.HostPort is null ? port.ContainerPort : $"{port.HostPort}:{port.ContainerPort}").ToList());
        AddList(result, "volumes", service.Volumes);
        Add(result, "restart", service.Restart);
        if (service.Healthcheck is { } healthcheck)
            result["healthcheck"] = RenderHealthcheck(healthcheck);
        AddList(result, "depends_on", service.DependsOn);
        AddList(result, "networks", service.Networks);
        AddList(result, "extra_hosts", service.ExtraHosts);
        AddTrue(result, "privileged", service.Privileged);
        Add(result, "network_mode", service.NetworkMode);
        Add(result, "ipc", service.Ipc);
        AddNumber(result, "shm_size", service.ShmSize);
        AddList(result, "profiles", service.Profiles);
        if (service.Deploy is { } deploy)
            result["deploy"] = RenderDeploy(deploy);
        AddList(result, "secrets", service.Secrets);
        AddList(result, "configs", service.Configs);
        AddMap(result, "labels", service.Labels);
        if (service.Logging is { } logging)
        {
            var log = new Dictionary<string, object?>();
            Add(log, "driver", logging.Driver);
            AddMap(log, "options", logging.Options);
            result["logging"] = log;
        }
        Add(result, "hostname", service.Hostname);
        Add(result, "domainname", service.Domainname);
        Add(result, "user", service.User);
        Add(result, "working_dir", service.WorkingDir);
        AddTrue(result, "tty", service.Tty);
        AddTrue(result, "stdin_open", service.StdinOpen);
        Add(result, "stop_signal", service.StopSignal);
        AddDuration(result, "stop_grace_period", service.StopGracePeriod);
        AddTrue(result, "read_only", service.ReadOnly);
        AddList(result, "tmpfs", service.Tmpfs);
        AddList(result, "cap_add", service.CapAdd);
        AddList(result, "cap_drop", service.CapDrop);
        AddList(result, "devices", service.Devices);
        AddMap(result, "sysctls", service.Sysctls);
        AddList(result, "security_opt", service.SecurityOpt);
        if (service.Init is { } init) result["init"] = init;
        Add(result, "platform", service.Platform);
        Add(result, "pull_policy", service.PullPolicy);
        AddList(result, "dns", service.Dns);
        AddList(result, "dns_search", service.DnsSearch);
        Add(result, "pid", service.Pid);
        Add(result, "mac_address", service.MacAddress);
        Add(result, "cgroup_parent", service.CgroupParent);
        if (service.ExtendsService is { } extendsService)
        {
            var extends = new Dictionary<string, object?> { ["service"] = extendsService };
            Add(extends, "file", service.ExtendsFile);
            result["extends"] = extends;
        }
        AddList(result, "links", service.Links);
        Add(result, "cpu_shares", service.CpuShares);
        Add(result, "cpu_quota", service.CpuQuota);
        Add(result, "cpuset", service.Cpuset);
        AddNumber(result, "mem_limit", service.Memory);
        AddNumber(result, "memswap_limit", service.MemorySwap);
        AddNumber(result, "mem_reservation", service.MemoryReservation);
        if (service.OomKillDisable is not null) result["oom_kill_disable"] = true;
        Add(result, "oom_score_adj", service.OomScoreAdj);
        AddList(result, "group_add", service.GroupAdd);
        AddMap(result, "annotations", service.Annotations);
        return result;
    }

    private static Dictionary<string, object?> RenderBuild(BuildConfig build)
    {
        var result = new Dictionary<string, object?>();
        Add(result, "context", build.Context);
        Add(result, "dockerfile", build.Dockerfile);
        AddMap(result, "args", build.Args);
        AddList(result, "cache_from", build.CacheFrom);
        AddList(result, "cache_to", build.CacheTo);
        Add(result, "target", build.Target);
        AddList(result, "tags", build.Tags);
        AddMap(result, "labels", build.Labels);
        Add(result, "network", build.Network);
        AddMap(result, "extra_hosts", build.ExtraHosts);
        if (build.Privileged is { } privileged) result["privileged"] = privileged;
        Add(result, "shm_size", build.ShmSize);
        AddList(result, "platforms", build.Platforms);
        if (build.Pull is { } pull) result["pull"] = pull;
        if (build.NoCache is { } noCache) result["no_cache"] = noCache;
        Add(result, "context_directory", build.ContextDirectory);
        return result;
    }

    private static Dictionary<string, object?> RenderHealthcheck(ComposeHealthcheck healthcheck)
    {
        var result = new Dictionary<string, object?>();
        AddTrue(result, "disable", healthcheck.Disabled);
        AddList(result, "test", healthcheck.Test);
        AddDuration(result, "interval", healthcheck.Interval);
        AddDuration(result, "timeout", healthcheck.Timeout);
        if (healthcheck.Retries is { } retries) result["retries"] = retries;
        AddDuration(result, "start_period", healthcheck.StartPeriod);
        return result;
    }

    private static Dictionary<string, object?> RenderDeploy(DeployConfig deploy)
    {
        var result = new Dictionary<string, object?>();
        if (deploy.Replicas is { } replicas) result["replicas"] = replicas;
        if (deploy.Resources is { } resources)
        {
            var resourceMap = new Dictionary<string, object?>();
            if (resources.Limits is { } limits)
            {
                var map = new Dictionary<string, object?>();
                AddNumber(map, "memory", limits.Memory);
                Add(map, "nano_cpus", limits.NanoCpus);
                if (limits.CpuCount is { } count) map["cpus"] = count;
                resourceMap["limits"] = map;
            }
            if (resources.Reservations is { } reservations)
            {
                var map = new Dictionary<string, object?>();
                AddNumber(map, "memory", reservations.Memory);
                Add(map, "nano_cpus", reservations.NanoCpus);
                resourceMap["reservations"] = map;
            }
            result["resources"] = resourceMap;
        }
        if (deploy.RestartPolicy is { } restart)
        {
            var map = new Dictionary<string, object?>();
            Add(map, "condition", restart.Condition);
            if (restart.MaxRetries is { } retries) map["max_retries"] = retries;
            AddDuration(map, "delay", restart.Delay);
            AddDuration(map, "window", restart.Window);
            AddDuration(map, "period", restart.Period);
            result["restart_policy"] = map;
        }
        if (deploy.Placement?.Constraints is { } constraints)
            result["placement"] = new Dictionary<string, object?> { ["constraints"] = constraints.ToArray() };
        if (deploy.UpdateConfig is { } update)
            result["update_config"] = RenderUpdate(update.Parallelism, update.Delay, update.Order,
                update.Monitor, update.FailureAction, update.MaxFailureRatio);
        if (deploy.RollbackConfig is { } rollback)
            result["rollback_config"] = RenderUpdate(rollback.Parallelism, rollback.Delay, rollback.Order,
                rollback.Monitor, rollback.FailureAction, rollback.MaxFailureRatio);
        AddMap(result, "labels", deploy.Labels);
        Add(result, "mode", deploy.Mode);
        if (deploy.EndpointMode is { } endpoint)
            result["endpoint_mode"] = new Dictionary<string, object?> { ["mode"] = endpoint.Mode };
        return result;
    }

    private static Dictionary<string, object?> RenderUpdate(string? parallelism, TimeSpan? delay,
        string? order, TimeSpan? monitor, string? failureAction, double? maxFailureRatio)
    {
        var result = new Dictionary<string, object?>();
        Add(result, "parallelism", parallelism);
        AddDuration(result, "delay", delay);
        Add(result, "order", order);
        AddDuration(result, "monitor", monitor);
        Add(result, "failure_action", failureAction);
        if (maxFailureRatio is { } ratio)
            result["max_failure_ratio"] = ratio.ToString("R", CultureInfo.InvariantCulture);
        return result;
    }

    private static void Add(Dictionary<string, object?> target, string key, string? value)
    {
        if (value is not null) target[key] = value;
    }

    private static void AddTrue(Dictionary<string, object?> target, string key, bool value)
    {
        if (value) target[key] = true;
    }

    private static void AddNumber(Dictionary<string, object?> target, string key, long? value)
    {
        if (value is { } number) target[key] = number;
    }

    private static void AddDuration(Dictionary<string, object?> target, string key, TimeSpan? value)
    {
        if (value is { } duration)
            target[key] = duration.Ticks % TimeSpan.TicksPerMillisecond == 0
                ? (duration.Ticks / TimeSpan.TicksPerMillisecond).ToString(CultureInfo.InvariantCulture) + "ms"
                : duration.ToString("c", CultureInfo.InvariantCulture);
    }

    private static void AddList(Dictionary<string, object?> target, string key, IReadOnlyList<string>? values)
    {
        if (values is { Count: > 0 }) target[key] = values.ToArray();
    }

    private static void AddMap<T>(Dictionary<string, object?> target, string key, IReadOnlyDictionary<string, T>? values)
    {
        if (values is { Count: > 0 })
            target[key] = values.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
    }

    private static void AddNames(Dictionary<string, object?> target, string key, IReadOnlyList<string> names)
    {
        if (names.Count > 0)
            target[key] = names.OrderBy(name => name, StringComparer.Ordinal)
                .ToDictionary(name => name, _ => (object?)null, StringComparer.Ordinal);
    }

    private static object? EscapeInterpolation(object? value) => value switch
    {
        string scalar => scalar.Replace("$", "$$", StringComparison.Ordinal),
        IDictionary<string, string> stringsMap => stringsMap.ToDictionary(
            pair => pair.Key.Replace("$", "$$", StringComparison.Ordinal),
            pair => pair.Value?.Replace("$", "$$", StringComparison.Ordinal),
            StringComparer.Ordinal),
        IDictionary<string, object?> map => map.ToDictionary(
            pair => pair.Key.Replace("$", "$$", StringComparison.Ordinal),
            pair => EscapeInterpolation(pair.Value), StringComparer.Ordinal),
        IEnumerable<string> strings => strings.Select(item => item.Replace("$", "$$", StringComparison.Ordinal)).ToArray(),
        _ => value
    };
}
