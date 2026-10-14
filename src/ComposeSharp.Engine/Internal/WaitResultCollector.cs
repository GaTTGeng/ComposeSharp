using ComposeSharp.Api;

namespace ComposeSharp.Engine.Internal;

internal static class WaitResultCollector
{
    public static async Task<WaitResult> CollectAsync(IEnumerable<string> containerIds, Func<string, Task<int>> waitAsync)
    {
        var tasks = containerIds.Select(async containerId =>
            (ContainerId: containerId, ExitCode: await waitAsync(containerId))).ToArray();
        var results = await Task.WhenAll(tasks);
        var exitCodes = results.ToDictionary(result => result.ContainerId, result => result.ExitCode, StringComparer.Ordinal);
        var code = results
            .OrderBy(result => result.ContainerId, StringComparer.Ordinal)
            .Select(result => result.ExitCode)
            .FirstOrDefault(exitCode => exitCode != 0);

        return new WaitResult { ExitCodes = exitCodes, Code = code };
    }
}
