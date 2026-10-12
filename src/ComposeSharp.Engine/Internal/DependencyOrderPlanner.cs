using ComposeSharp.Loader.Models;

namespace ComposeSharp.Engine.Internal;

/// <summary>Plans a stable order for starting services after their dependencies.</summary>
internal static class DependencyOrderPlanner
{
    /// <summary>
    /// Returns selected services and their transitive dependencies in stable topological order.
    /// Service names are matched without regard to case.
    /// </summary>
    /// <param name="services">Services in source order.</param>
    /// <param name="requestedServices">Requested service names, or null to select every service.</param>
    /// <exception cref="InvalidOperationException">A requested service or dependency is missing, names are duplicated, or dependencies contain a cycle.</exception>
    public static IReadOnlyList<ServiceDefinition> Plan(
        IReadOnlyList<ServiceDefinition> services,
        IReadOnlyList<string>? requestedServices = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var byName = new Dictionary<string, ServiceDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            if (!byName.TryAdd(service.Name, service))
                throw new InvalidOperationException($"Service name '{service.Name}' is duplicated (service names are case-insensitive).");
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (requestedServices is null)
        {
            selected.UnionWith(byName.Keys);
        }
        else
        {
            foreach (var requested in requestedServices)
            {
                if (!byName.ContainsKey(requested))
                    throw new InvalidOperationException($"Requested service '{requested}' does not exist.");
                selected.Add(requested);
            }
        }

        // Expand only the requested closure, so unrelated invalid services do not block a subset operation.
        var pending = new Stack<string>(selected);
        while (pending.TryPop(out var name))
        {
            var service = byName[name];
            foreach (var dependency in service.DependsOn)
            {
                if (!byName.ContainsKey(dependency))
                    throw new InvalidOperationException($"Service '{service.Name}' depends on missing service '{dependency}'.");
                if (selected.Add(dependency)) pending.Push(dependency);
            }
        }

        var remaining = services.Where(service => selected.Contains(service.Name)).ToList();
        var orderedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<ServiceDefinition>(remaining.Count);
        while (remaining.Count > 0)
        {
            // Scanning source order on each pass keeps independent peers in document order.
            var ready = remaining.Where(service => service.DependsOn.All(orderedNames.Contains)).ToList();
            if (ready.Count == 0)
            {
                var cycle = FindCycle(remaining, byName);
                throw new InvalidOperationException($"Service dependency cycle detected: {string.Join(" -> ", cycle)}.");
            }

            foreach (var service in ready)
            {
                result.Add(service);
                orderedNames.Add(service.Name);
                remaining.Remove(service);
            }
        }

        return result;
    }

    private static IReadOnlyList<string> FindCycle(
        IReadOnlyList<ServiceDefinition> remaining,
        IReadOnlyDictionary<string, ServiceDefinition> byName)
    {
        var remainingNames = remaining.Select(service => service.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var state = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var stack = new List<string>();

        foreach (var service in remaining)
        {
            var cycle = Visit(service.Name);
            if (cycle is not null) return cycle;
        }

        return remaining.Select(service => service.Name).ToArray();

        IReadOnlyList<string>? Visit(string name)
        {
            state[name] = 1;
            stack.Add(name);
            foreach (var dependency in byName[name].DependsOn)
            {
                if (!remainingNames.Contains(dependency)) continue;
                if (state.GetValueOrDefault(dependency) == 1)
                {
                    var start = stack.FindIndex(item => StringComparer.OrdinalIgnoreCase.Equals(item, dependency));
                    return stack.Skip(start).Append(dependency).ToArray();
                }
                if (!state.ContainsKey(dependency))
                {
                    var cycle = Visit(dependency);
                    if (cycle is not null) return cycle;
                }
            }
            stack.RemoveAt(stack.Count - 1);
            state[name] = 2;
            return null;
        }
    }
}
