using ComposeSharp.Engine.Internal;

namespace ComposeSharp.Tests;

public sealed class WaitResultCollectorTests
{
    [Fact]
    public async Task CollectAsync_CollectsConcurrentExitCodesAndAggregatesIndependentlyOfCompletionOrder()
    {
        var containerIds = new[] { "container-c", "container-a", "container-b" };
        var completions = containerIds.ToDictionary(
            containerId => containerId,
            _ => new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously),
            StringComparer.Ordinal);
        var startedCount = 0;
        var allStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var pendingResult = WaitResultCollector.CollectAsync(containerIds, containerId =>
        {
            if (Interlocked.Increment(ref startedCount) == containerIds.Length)
                allStarted.SetResult();
            return completions[containerId].Task;
        });

        await allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
        completions["container-b"].SetResult(9);
        completions["container-a"].SetResult(3);
        completions["container-c"].SetResult(7);

        var result = await pendingResult.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(3, result.ExitCodes.Count);
        Assert.Equal(7, result.ExitCodes["container-c"]);
        Assert.Equal(3, result.ExitCodes["container-a"]);
        Assert.Equal(9, result.ExitCodes["container-b"]);
        Assert.Equal(3, result.Code);
    }
}
