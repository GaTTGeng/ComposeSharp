namespace ComposeSharp.Engine.Internal;

/// <summary>Derives a project status from the states returned for its labeled containers.</summary>
internal static class StackStatus
{
    public static string FromContainerStates(IEnumerable<string?> states)
    {
        var values = states.ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace))
            return "unknown";

        var normalized = values.Select(state => state!.Trim().ToLowerInvariant()).ToArray();
        if (normalized.Any(state => state is not ("running" or "exited" or "dead")))
            return "unknown";
        if (normalized.All(state => state == "running"))
            return "running";
        if (normalized.All(state => state is "exited" or "dead"))
            return "exited";
        return "partial";
    }
}
