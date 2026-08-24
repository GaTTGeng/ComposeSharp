using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using ComposeSharp.Loader.Interpolation;
using ComposeSharp.Loader.Models;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace ComposeSharp.Loader;

public sealed class ComposeFileLoader
{
    public ComposeProject Load(string workingDirectory, string composeFileName)
    {
        var composePath = ResolveComposePath(workingDirectory, composeFileName);
        var document = LoadDocument(composePath);
        return ParseProject(composePath, document.Root, document.Environment, document.Provenance);
    }

    public ComposeProject LoadMerged(string workingDirectory, IReadOnlyList<string> composeFiles)
    {
        if (composeFiles.Count == 0)
            throw new ArgumentException("At least one compose file is required.");
        if (composeFiles.Count == 1)
            return Load(workingDirectory, composeFiles[0]);

        Dictionary<object, object?>? mergedRoot = null;
        string? primaryComposePath = null;
        IReadOnlyDictionary<string, string>? primaryEnvironment = null;
        Dictionary<string, string>? provenance = null;
        foreach (var file in composeFiles)
        {
            var composePath = ResolveComposePath(workingDirectory, file);
            var document = LoadDocument(composePath);
            if (mergedRoot is null)
            {
                mergedRoot = document.Root;
                primaryComposePath = composePath;
                primaryEnvironment = document.Environment;
                provenance = document.Provenance;
            }
            else
            {
                var baseRoot = mergedRoot;
                mergedRoot = MergeDocuments(baseRoot, document.Root, composePath);
                provenance = MergeProvenance(
                    baseRoot, document.Root, mergedRoot, provenance!, document.Provenance);
            }
        }

        return ParseProject(primaryComposePath!, mergedRoot!, primaryEnvironment!, provenance!);
    }

    private static LoadedDocument LoadDocument(string composePath)
    {
        var env = LoadDotEnv(Path.Combine(Path.GetDirectoryName(composePath)!, ".env"));
        var raw = File.ReadAllText(composePath);
        string expanded;
        try
        {
            expanded = VariableInterpolator.Expand(raw, env);
        }
        catch (InvalidOperationException exception)
        {
            throw new ComposeValidationException(
                composePath, "$", $"interpolation failed: {exception.Message}", innerException: exception);
        }
        try
        {
            RejectUnsupportedMergeTags(expanded, composePath);
            var deserializer = new DeserializerBuilder().Build();
            var root = deserializer.Deserialize<Dictionary<object, object?>>(expanded)
                       ?? throw new ComposeValidationException(composePath, "$", "the document is empty.");

            return new LoadedDocument(root, env, BuildProvenance(root, composePath));
        }
        catch (ComposeValidationException)
        {
            throw;
        }
        catch (YamlException exception)
        {
            throw new ComposeValidationException(
                composePath,
                "$",
                "the YAML document is malformed.",
                line: exception.Start.Line + 1,
                column: exception.Start.Column + 1,
                innerException: exception);
        }
    }

    private ComposeProject ParseProject(
        string composePath,
        Dictionary<object, object?> root,
        IReadOnlyDictionary<string, string> env,
        IReadOnlyDictionary<string, string> provenance)
    {
        var validation = new ValidationContext(composePath, provenance);
        ValidateProject(root, validation);
        var servicesMap = (Dictionary<object, object?>)root["services"]!;

        var volumes = ParseNameList(root, "volumes");
        var networks = ParseNameList(root, "networks");
        var secrets = ParseNameList(root, "secrets");
        var configs = ParseNameList(root, "configs");
        var extensions = ParseExtensions(root);

        var services = new List<ServiceDefinition>();
        foreach (var (serviceNameObj, serviceValue) in servicesMap)
        {
            var serviceName = serviceNameObj.ToString()!;
            var map = (Dictionary<object, object?>)serviceValue!;

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

    private sealed record LoadedDocument(
        Dictionary<object, object?> Root,
        IReadOnlyDictionary<string, string> Environment,
        Dictionary<string, string> Provenance);

    private sealed record ValidationContext(
        string PrimarySource,
        IReadOnlyDictionary<string, string> Provenance,
        string? ServiceName = null)
    {
        public string SourceFor(string path)
        {
            var candidate = path;
            while (!string.IsNullOrEmpty(candidate))
            {
                if (Provenance.TryGetValue(candidate, out var source))
                    return source;

                var list = candidate.LastIndexOf('[');
                var member = candidate.LastIndexOf('.');
                candidate = list > member ? candidate[..list] : member >= 0 ? candidate[..member] : string.Empty;
            }

            return PrimarySource;
        }

        public ValidationContext ForService(string serviceName) => this with { ServiceName = serviceName };
    }

    private static Dictionary<string, string> BuildProvenance(
        Dictionary<object, object?> root,
        string composePath)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal) { ["$"] = composePath };
        AddProvenance(root, string.Empty, composePath, result);
        return result;
    }

    private static void AddProvenance(
        object? node,
        string path,
        string composePath,
        Dictionary<string, string> result)
    {
        switch (node)
        {
            case Dictionary<object, object?> map:
                foreach (var (key, value) in map)
                {
                    var memberPath = string.IsNullOrEmpty(path) ? key.ToString()! : $"{path}.{key}";
                    result[memberPath] = composePath;
                    AddProvenance(value, memberPath, composePath, result);
                }
                break;
            case List<object?> list:
                for (var index = 0; index < list.Count; index++)
                {
                    var itemPath = $"{path}[{index}]";
                    result[itemPath] = composePath;
                    AddProvenance(list[index], itemPath, composePath, result);
                }
                break;
        }
    }

    private static Dictionary<string, string> MergeProvenance(
        Dictionary<object, object?> baseDocument,
        Dictionary<object, object?> overlay,
        Dictionary<object, object?> mergedDocument,
        IReadOnlyDictionary<string, string> baseProvenance,
        IReadOnlyDictionary<string, string> overlayProvenance)
    {
        var result = new Dictionary<string, string>(baseProvenance, StringComparer.Ordinal);
        ApplyOverlayProvenance(
            baseDocument, overlay, mergedDocument, string.Empty, result, overlayProvenance);
        return result;
    }

    private static void ApplyOverlayProvenance(
        Dictionary<object, object?> baseMap,
        Dictionary<object, object?> overlayMap,
        Dictionary<object, object?> mergedMap,
        string path,
        Dictionary<string, string> result,
        IReadOnlyDictionary<string, string> overlayProvenance)
    {
        foreach (var (keyNode, overlayValue) in overlayMap)
        {
            var key = keyNode.ToString()!;
            var memberPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";
            TryGetValue(mergedMap, key, out var mergedValue);
            if (!TryGetValue(baseMap, key, out var baseValue))
            {
                ReplaceProvenance(result, overlayProvenance, memberPath, memberPath);
                continue;
            }

            if (baseValue is Dictionary<object, object?> baseChild &&
                overlayValue is Dictionary<object, object?> overlayChild &&
                mergedValue is Dictionary<object, object?> mergedChild)
            {
                ApplyOverlayProvenance(
                    baseChild, overlayChild, mergedChild, memberPath, result, overlayProvenance);
                continue;
            }

            if (baseValue is List<object?> baseList &&
                overlayValue is List<object?> overlayList &&
                mergedValue is List<object?> mergedList &&
                !ReplacementListFields.Contains(GetLeaf(memberPath)) &&
                !memberPath.EndsWith(".healthcheck.test", StringComparison.Ordinal))
            {
                for (var overlayIndex = 0; overlayIndex < overlayList.Count; overlayIndex++)
                {
                    var keyValue = GetListMergeKey(overlayList[overlayIndex], memberPath);
                    var mergedIndex = mergedList.FindIndex(item =>
                        string.Equals(GetListMergeKey(item, memberPath), keyValue, StringComparison.Ordinal));
                    if (mergedIndex >= 0)
                    {
                        ReplaceProvenance(
                            result,
                            overlayProvenance,
                            $"{memberPath}[{overlayIndex}]",
                            $"{memberPath}[{mergedIndex}]");
                    }
                }
                continue;
            }

            ReplaceProvenance(result, overlayProvenance, memberPath, memberPath);
        }
    }

    private static void ReplaceProvenance(
        Dictionary<string, string> target,
        IReadOnlyDictionary<string, string> source,
        string sourcePrefix,
        string targetPrefix)
    {
        foreach (var key in target.Keys
                     .Where(key => IsPathAtOrBelow(key, targetPrefix))
                     .ToList())
        {
            target.Remove(key);
        }

        foreach (var (path, composePath) in source.Where(pair => IsPathAtOrBelow(pair.Key, sourcePrefix)))
        {
            var suffix = path[sourcePrefix.Length..];
            target[$"{targetPrefix}{suffix}"] = composePath;
        }
    }

    private static bool IsPathAtOrBelow(string path, string prefix)
        => string.Equals(path, prefix, StringComparison.Ordinal) ||
           path.StartsWith($"{prefix}.", StringComparison.Ordinal) ||
           path.StartsWith($"{prefix}[", StringComparison.Ordinal);

    private static void ValidateProject(Dictionary<object, object?> root, ValidationContext context)
    {
        if (!TryGetValue(root, "services", out var servicesValue))
            throw Validation(context, "services", "the required services mapping is missing.");
        if (servicesValue is not Dictionary<object, object?> services)
            throw Validation(context, "services", $"expected a YAML mapping, but found {DescribeNode(servicesValue)}.");

        foreach (var projectMap in new[] { "volumes", "networks", "secrets", "configs" })
            ValidateOptionalMap(root, projectMap, projectMap, context);

        foreach (var (nameNode, serviceValue) in services)
        {
            var serviceName = nameNode.ToString() ?? string.Empty;
            var servicePath = $"services.{serviceName}";
            var serviceContext = context.ForService(serviceName);
            if (serviceValue is not Dictionary<object, object?> service)
                throw Validation(serviceContext, servicePath, $"expected a YAML mapping, but found {DescribeNode(serviceValue)}.");

            ValidateService(service, servicePath, serviceContext);
        }
    }

    private static void ValidateService(
        Dictionary<object, object?> service,
        string path,
        ValidationContext context)
    {
        ValidateOptionalScalar(service, "image", $"{path}.image", context);
        ValidateBuild(service, path, context);

        var hasImage = TryGetValue(service, "image", out var image) && image is not null;
        var hasBuild = TryGetValue(service, "build", out var build) && build is not null;
        if (!hasImage && !hasBuild)
        {
            var imagePath = $"{path}.image";
            var buildPath = $"{path}.build";
            var serviceSource = context.SourceFor(path);
            var failurePath = context.SourceFor(imagePath) != serviceSource
                ? imagePath
                : context.SourceFor(buildPath) != serviceSource
                    ? buildPath
                    : path;
            throw Validation(context, failurePath, "the service must specify either 'image' or 'build'.");
        }

        ValidateListOrMap(service, "environment", $"{path}.environment", context);
        ValidatePorts(service, path, context);
        ValidateOptionalMap(service, "deploy", $"{path}.deploy", context);
        ValidateOptionalMap(service, "healthcheck", $"{path}.healthcheck", context);
        ValidateOptionalMap(service, "logging", $"{path}.logging", context);
        ValidateScalarOrMap(service, "extends", $"{path}.extends", context);

        ValidateOptionalMapPath(service, "deploy", $"{path}.deploy", context, deploy =>
        {
            ValidateOptionalMap(deploy, "resources", $"{path}.deploy.resources", context);
            ValidateOptionalMap(deploy, "restart_policy", $"{path}.deploy.restart_policy", context);
            ValidateOptionalMap(deploy, "placement", $"{path}.deploy.placement", context);
            ValidateOptionalMap(deploy, "update_config", $"{path}.deploy.update_config", context);
            ValidateOptionalMap(deploy, "rollback_config", $"{path}.deploy.rollback_config", context);
            ValidateInteger(deploy, "replicas", $"{path}.deploy.replicas", context);

            ValidateOptionalMapPath(deploy, "resources", $"{path}.deploy.resources", context, resources =>
            {
                ValidateOptionalMap(resources, "limits", $"{path}.deploy.resources.limits", context);
                ValidateOptionalMap(resources, "reservations", $"{path}.deploy.resources.reservations", context);
                ValidateOptionalMapPath(resources, "limits", $"{path}.deploy.resources.limits", context, limits =>
                {
                    ValidateBytes(limits, "memory", $"{path}.deploy.resources.limits.memory", context);
                });
                ValidateOptionalMapPath(resources, "reservations", $"{path}.deploy.resources.reservations", context,
                    reservations => ValidateBytes(
                        reservations, "memory", $"{path}.deploy.resources.reservations.memory", context));
            });

            ValidateOptionalMapPath(deploy, "restart_policy", $"{path}.deploy.restart_policy", context, policy =>
            {
                ValidateInteger(policy, "max_retries", $"{path}.deploy.restart_policy.max_retries", context);
                ValidateDurations(policy, $"{path}.deploy.restart_policy", context, "delay", "window", "period");
            });
            ValidateOptionalMapPath(deploy, "update_config", $"{path}.deploy.update_config", context, update =>
            {
                ValidateDurations(update, $"{path}.deploy.update_config", context, "delay", "monitor");
                ValidateDouble(update, "max_failure_ratio", $"{path}.deploy.update_config.max_failure_ratio", context);
            });
            ValidateOptionalMapPath(deploy, "rollback_config", $"{path}.deploy.rollback_config", context, rollback =>
            {
                ValidateDurations(rollback, $"{path}.deploy.rollback_config", context, "delay", "monitor");
                ValidateDouble(rollback, "max_failure_ratio", $"{path}.deploy.rollback_config.max_failure_ratio", context);
            });
        });

        ValidateOptionalMapPath(service, "healthcheck", $"{path}.healthcheck", context, healthcheck =>
        {
            ValidateBoolean(healthcheck, "disable", $"{path}.healthcheck.disable", context);
            ValidateInteger(healthcheck, "retries", $"{path}.healthcheck.retries", context);
            ValidateDurations(healthcheck, $"{path}.healthcheck", context, "interval", "timeout", "start_period");
        });

        ValidateDurations(service, path, context, "stop_grace_period");
        foreach (var byteField in new[] { "shm_size", "mem_limit", "memswap_limit", "mem_reservation" })
            ValidateBytes(service, byteField, $"{path}.{byteField}", context);
        foreach (var booleanField in new[]
                 {
                     "privileged", "tty", "stdin_open", "read_only", "init", "oom_kill_disable"
                 })
            ValidateBoolean(service, booleanField, $"{path}.{booleanField}", context);
    }

    private static void ValidateBuild(Dictionary<object, object?> service, string path, ValidationContext context)
    {
        if (!TryGetValue(service, "build", out var build) || build is null)
            return;
        if (IsScalar(build))
            return;
        if (build is not Dictionary<object, object?> buildMap)
            throw Validation(context, $"{path}.build", $"expected a scalar or YAML mapping, but found {DescribeNode(build)}.");

        ValidateBoolean(buildMap, "privileged", $"{path}.build.privileged", context);
        ValidateBoolean(buildMap, "pull", $"{path}.build.pull", context);
        ValidateBoolean(buildMap, "no_cache", $"{path}.build.no_cache", context);
    }

    private static void ValidatePorts(Dictionary<object, object?> service, string path, ValidationContext context)
    {
        if (!TryGetValue(service, "ports", out var value) || value is null)
            return;
        var portsPath = $"{path}.ports";
        if (value is not List<object?> ports)
            throw Validation(context, portsPath, $"expected a YAML list, but found {DescribeNode(value)}.");

        for (var index = 0; index < ports.Count; index++)
        {
            var item = ports[index];
            var itemPath = $"{portsPath}[{index}]";
            if (!IsScalar(item))
                throw Validation(context, itemPath, $"long port syntax is not supported; found {DescribeNode(item)}.");
            var text = item?.ToString() ?? string.Empty;
            if (!IsValidPort(text))
                throw ConversionValidation(context, itemPath, text, "a supported short port mapping");
        }
    }

    private static bool IsValidPort(string value)
    {
        var text = value.Trim('"', '\'');
        var slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            if (slash == text.Length - 1 || text[(slash + 1)..] is not ("tcp" or "udp" or "sctp"))
                return false;
            text = text[..slash];
        }

        var parts = text.Split(':');
        return parts.Length switch
        {
            1 or 2 => parts.All(IsValidPortNumber),
            3 => IPAddress.TryParse(parts[0], out _) && parts.Skip(1).All(IsValidPortNumber),
            _ => false
        };
    }

    private static bool IsValidPortNumber(string value)
        => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) && port is >= 1 and <= 65535;

    private static void ValidateOptionalMapPath(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context,
        Action<Dictionary<object, object?>> validate)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (value is not Dictionary<object, object?> child)
            throw Validation(context, path, $"expected a YAML mapping, but found {DescribeNode(value)}.");
        validate(child);
    }

    private static void ValidateOptionalMap(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
        => ValidateOptionalMapPath(map, key, path, context, _ => { });

    private static void ValidateListOrMap(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (value is not List<object?> && value is not Dictionary<object, object?>)
            throw Validation(context, path, $"expected a YAML list or mapping, but found {DescribeNode(value)}.");
    }

    private static void ValidateScalarOrMap(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (!IsScalar(value) && value is not Dictionary<object, object?>)
            throw Validation(context, path, $"expected a scalar or YAML mapping, but found {DescribeNode(value)}.");
    }

    private static void ValidateOptionalScalar(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (TryGetValue(map, key, out var value) && value is not null && !IsScalar(value))
            throw Validation(context, path, $"expected a scalar, but found {DescribeNode(value)}.");
    }

    private static void ValidateDurations(
        Dictionary<object, object?> map,
        string parentPath,
        ValidationContext context,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            if (!TryGetValue(map, key, out var value) || value is null)
                continue;
            var path = $"{parentPath}.{key}";
            if (!IsScalar(value) || !TryParseDuration(value.ToString(), out _))
                throw ConversionValidation(context, path, value, "a supported duration");
        }
    }

    private static void ValidateBytes(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (!IsScalar(value) || !TryParseBytes(value.ToString(), out _))
            throw ConversionValidation(context, path, value, "a supported byte value");
    }

    private static void ValidateBoolean(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (!IsScalar(value) || !bool.TryParse(value.ToString(), out _))
            throw ConversionValidation(context, path, value, "a boolean value");
    }

    private static void ValidateInteger(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (!IsScalar(value) || !int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            throw ConversionValidation(context, path, value, "an integer value");
    }

    private static void ValidateDouble(
        Dictionary<object, object?> map,
        string key,
        string path,
        ValidationContext context)
    {
        if (!TryGetValue(map, key, out var value) || value is null)
            return;
        if (!IsScalar(value) || !double.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out _))
            throw ConversionValidation(context, path, value, "a numeric value");
    }

    private static ComposeValidationException Validation(ValidationContext context, string path, string message)
        => new(context.SourceFor(path), path, message, context.ServiceName);

    private static ComposeValidationException ConversionValidation(
        ValidationContext context,
        string path,
        object? value,
        string expected)
    {
        var conversionException = new FormatException($"Value '{value}' could not be converted to {expected}.");
        return new ComposeValidationException(
            context.SourceFor(path),
            path,
            $"'{value}' is not {expected}.",
            context.ServiceName,
            innerException: conversionException);
    }

    private static bool IsScalar(object? value)
        => value is not Dictionary<object, object?> && value is not List<object?>;

    private static string DescribeNode(object? value) => value switch
    {
        null => "null",
        Dictionary<object, object?> => "a YAML mapping",
        List<object?> => "a YAML list",
        _ => $"scalar '{value}'"
    };

    private static string ResolveComposePath(string workingDirectory, string composeFileName)
    {
        var composePath = Path.GetFullPath(Path.Combine(workingDirectory, composeFileName));
        if (File.Exists(composePath)) return composePath;

        var candidates = new[] { "compose.yml", "compose.yaml", "docker-compose.yaml", "docker-compose.yml" };
        foreach (var candidate in candidates)
        {
            var path = Path.GetFullPath(Path.Combine(workingDirectory, candidate));
            if (File.Exists(path)) return path;
        }

        throw new FileNotFoundException("Compose file was not found.", composePath);
    }

    private static readonly HashSet<string> ReplacementListFields = new(StringComparer.Ordinal)
    {
        "command",
        "entrypoint"
    };

    private static Dictionary<object, object?> MergeDocuments(
        Dictionary<object, object?> baseDocument,
        Dictionary<object, object?> overlay,
        string overlayPath)
    {
        return MergeMap(baseDocument, overlay, "", overlayPath);
    }

    private static Dictionary<object, object?> MergeMap(
        Dictionary<object, object?> baseMap,
        Dictionary<object, object?> overlay,
        string path,
        string overlayPath)
    {
        var merged = CloneMap(baseMap);
        foreach (var (keyObject, overlayValue) in overlay)
        {
            var key = keyObject.ToString() ?? throw new NotSupportedException(
                $"Compose map keys in '{overlayPath}' must be strings.");
            var memberPath = string.IsNullOrEmpty(path) ? key : $"{path}.{key}";

            if (!TryGetValue(merged, key, out var baseValue))
            {
                merged[key] = CloneNode(overlayValue);
                continue;
            }

            merged[key] = MergeNode(baseValue, overlayValue, memberPath, overlayPath);
        }

        return merged;
    }

    private static object? MergeNode(object? baseValue, object? overlayValue, string path, string overlayPath)
    {
        if (baseValue is Dictionary<object, object?> baseMap && overlayValue is Dictionary<object, object?> overlayMap)
            return MergeMap(baseMap, overlayMap, path, overlayPath);

        if (baseValue is List<object?> baseList && overlayValue is List<object?> overlayList)
            return MergeList(baseList, overlayList, path);

        // Scalars, null values, and changes between YAML shapes deliberately replace the earlier value.
        return CloneNode(overlayValue);
    }

    private static List<object?> MergeList(List<object?> baseList, List<object?> overlayList, string path)
    {
        if (ReplacementListFields.Contains(GetLeaf(path)) || path.EndsWith(".healthcheck.test", StringComparison.Ordinal))
            return CloneList(overlayList);

        var merged = CloneList(baseList);
        foreach (var item in overlayList)
        {
            var key = GetListMergeKey(item, path);
            var existingIndex = merged.FindIndex(existing =>
                string.Equals(GetListMergeKey(existing, path), key, StringComparison.Ordinal));
            if (existingIndex >= 0)
                merged[existingIndex] = CloneNode(item);
            else
                merged.Add(CloneNode(item));
        }

        return merged;
    }

    private static string GetListMergeKey(object? item, string path)
    {
        var text = item?.ToString() ?? string.Empty;
        var leaf = GetLeaf(path);
        if ((leaf is "environment" or "labels" or "sysctls" or "args" or "extra_hosts" or "annotations") &&
            item is not Dictionary<object, object?>)
        {
            var separator = GetDictionaryEntrySeparator(text, leaf == "extra_hosts");
            return separator < 0 ? text : text[..separator];
        }

        if (leaf is "volumes" or "secrets" or "configs")
        {
            if (item is Dictionary<object, object?> map && TryGetValue(map, "target", out var target))
                return target?.ToString() ?? text;

            var shortSyntaxTarget = GetShortSyntaxTarget(text);
            if (shortSyntaxTarget is not null)
                return shortSyntaxTarget;
        }

        return text;
    }

    private static string? GetShortSyntaxTarget(string value)
    {
        var firstSeparator = value.IndexOf(':');
        if (firstSeparator < 0)
            return null;

        if (firstSeparator == 1 && char.IsLetter(value[0]) && value.Length > 2 &&
            (value[2] == '\\' || value[2] == '/'))
        {
            var drivePathSeparator = value.IndexOf(':', firstSeparator + 1);
            if (drivePathSeparator >= 0)
                firstSeparator = drivePathSeparator;
            else if (value[2] == '\\')
                return value;
        }

        var targetStart = firstSeparator + 1;
        var modeSeparator = value.IndexOf(':', targetStart);
        if (modeSeparator == targetStart + 1 && char.IsLetter(value[targetStart]) && modeSeparator + 1 < value.Length &&
            (value[modeSeparator + 1] == '\\' || value[modeSeparator + 1] == '/'))
        {
            modeSeparator = value.IndexOf(':', modeSeparator + 1);
        }
        return modeSeparator < 0 ? value[targetStart..] : value[targetStart..modeSeparator];
    }

    private static int GetDictionaryEntrySeparator(string value, bool allowColonSeparator)
    {
        var separator = value.IndexOf('=');
        return separator >= 0 || !allowColonSeparator ? separator : value.IndexOf(':');
    }

    private static string GetLeaf(string path)
    {
        var separator = path.LastIndexOf('.');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static bool TryGetValue(Dictionary<object, object?> map, string key, out object? value)
    {
        foreach (var (candidate, candidateValue) in map)
        {
            if (string.Equals(candidate.ToString(), key, StringComparison.Ordinal))
            {
                value = candidateValue;
                return true;
            }
        }

        value = null;
        return false;
    }

    private static Dictionary<object, object?> CloneMap(Dictionary<object, object?> map)
        => map.ToDictionary(pair => pair.Key, pair => CloneNode(pair.Value));

    private static List<object?> CloneList(List<object?> list)
        => list.Select(CloneNode).ToList();

    private static object? CloneNode(object? value) => value switch
    {
        Dictionary<object, object?> map => CloneMap(map),
        List<object?> list => CloneList(list),
        _ => value
    };

    private static void RejectUnsupportedMergeTags(string yaml, string composePath)
    {
        var parser = new Parser(new StringReader(yaml));
        while (parser.MoveNext())
        {
            if (parser.Current is not NodeEvent node || node.Tag.ToString() is not ("!reset" or "!override"))
                continue;

            throw new NotSupportedException(
                $"Compose merge tag '{node.Tag}' in '{composePath}' is not supported. " +
                "Use the documented scalar, mapping, and list merge rules instead.");
        }
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
                Args: GetBuildArgs(buildMap),
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
            if (line.StartsWith("export ", StringComparison.Ordinal)) line = line[7..].TrimStart();
            var idx = line.IndexOf('=');
            if (idx <= 0) continue;
            var key = line[..idx].Trim();
            var value = ParseDotEnvValue(line[(idx + 1)..]);
            result[key] = value;
        }
        return result;
    }

    private static string ParseDotEnvValue(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return value[1..^1].Replace("\\\"", "\"", StringComparison.Ordinal);
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1];

        var commentIndex = value.IndexOf(" #", StringComparison.Ordinal);
        return commentIndex >= 0 ? value[..commentIndex].TrimEnd() : value;
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
        return TryParseDuration(value, out var parsed) ? parsed : null;
    }

    private static bool TryParseDuration(string? value, out TimeSpan parsed)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out parsed))
            return true;

        var match = Regex.Match(value ?? string.Empty, @"^(?<num>\d+)(?<unit>ms|s|m|h)$", RegexOptions.IgnoreCase);
        if (!match.Success ||
            !long.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            parsed = default;
            return false;
        }

        try
        {
            parsed = match.Groups["unit"].Value.ToLowerInvariant() switch
            {
                "ms" => TimeSpan.FromMilliseconds(number),
                "s" => TimeSpan.FromSeconds(number),
                "m" => TimeSpan.FromMinutes(number),
                "h" => TimeSpan.FromHours(number),
                _ => default
            };
            return true;
        }
        catch (OverflowException)
        {
            parsed = default;
            return false;
        }
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
        return TryParseBytes(value, out var parsed) ? parsed : null;
    }

    private static bool TryParseBytes(string? value, out long parsed)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
            return true;

        var match = Regex.Match(value ?? string.Empty, @"^(?<num>\d+)(?<unit>[kmg])b?$", RegexOptions.IgnoreCase);
        if (!match.Success ||
            !long.TryParse(match.Groups["num"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
        {
            parsed = default;
            return false;
        }

        var multiplier = match.Groups["unit"].Value.ToLowerInvariant() switch
        {
            "k" => 1024L,
            "m" => 1024L * 1024,
            "g" => 1024L * 1024 * 1024,
            _ => 0L
        };
        try
        {
            parsed = checked(number * multiplier);
            return true;
        }
        catch (OverflowException)
        {
            parsed = default;
            return false;
        }
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

        if (value is List<object?> list)
        {
            return list
                .Select(item => item?.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item =>
                {
                    var separator = GetDictionaryEntrySeparator(item, key == "extra_hosts");
                    return separator < 0
                        ? new KeyValuePair<string, string>(item, string.Empty)
                        : new KeyValuePair<string, string>(item[..separator], item[(separator + 1)..]);
                })
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return new Dictionary<string, string>();
    }

    private static IReadOnlyDictionary<string, string>? GetStringDictionaryOrNull(Dictionary<object, object?> map, string key)
    {
        var dict = GetStringDictionary(map, key);
        return dict.Count > 0 ? dict : null;
    }

    private static IReadOnlyDictionary<string, string?>? GetBuildArgs(Dictionary<object, object?> map)
    {
        if (!map.TryGetValue("args", out var value) || value is null)
            return null;

        if (value is Dictionary<object, object?> dictionary)
        {
            return dictionary.ToDictionary(
                pair => pair.Key.ToString()!,
                pair => pair.Value?.ToString());
        }

        if (value is List<object?> list)
        {
            return list
                .Select(item => item?.ToString() ?? string.Empty)
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item =>
                {
                    var separator = GetDictionaryEntrySeparator(item, false);
                    return separator < 0
                        ? new KeyValuePair<string, string?>(item, null)
                        : new KeyValuePair<string, string?>(item[..separator], item[(separator + 1)..]);
                })
                .ToDictionary(pair => pair.Key, pair => pair.Value);
        }

        return null;
    }
}
