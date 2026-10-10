using ComposeSharp.Api;
using ComposeSharp.Engine;
using ComposeSharp.Engine.Internal;

namespace ComposeSharp.Tests;

public sealed class BuildContextWatcherTests
{
    [Fact]
    public async Task ObserveAsync_WithNoBuildContextsCompletesImmediately()
    {
        await using var changes = BuildContextWatcher.ObserveAsync([]).GetAsyncEnumerator();
        Assert.False(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task ObserveAsync_WatchesAllContextsAndFansOutSharedChangesContinuously()
    {
        var root = CreateTempDirectory();
        var shared = Directory.CreateDirectory(Path.Combine(root, "shared")).FullName;
        var separate = Directory.CreateDirectory(Path.Combine(root, "separate")).FullName;
        try
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var changes = BuildContextWatcher.ObserveAsync(
                [new("zeta", shared), new("other", separate), new("alpha", shared)],
                () => ready.SetResult()).GetAsyncEnumerator();

            var first = changes.MoveNextAsync().AsTask();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var otherPath = Directory.CreateDirectory(Path.Combine(separate, "first")).FullName;
            Assert.True(await first.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("other", changes.Current.ServiceName);
            Assert.Equal(otherPath, changes.Current.Path);
            Assert.Equal("rebuild", changes.Current.Action);

            var sharedPath = Directory.CreateDirectory(Path.Combine(shared, "second")).FullName;
            Assert.True(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("alpha", changes.Current.ServiceName);
            Assert.Equal(sharedPath, changes.Current.Path);
            Assert.True(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("zeta", changes.Current.ServiceName);
            Assert.Equal(sharedPath, changes.Current.Path);

            var nextPath = Directory.CreateDirectory(Path.Combine(shared, "third")).FullName;
            Assert.True(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("alpha", changes.Current.ServiceName);
            Assert.Equal(nextPath, changes.Current.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ObserveAsync_CancellationStopsWithoutReportingAChange()
    {
        var root = CreateTempDirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var changes = BuildContextWatcher.ObserveAsync(
                [new("app", root)], () => ready.SetResult(), cancellation.Token).GetAsyncEnumerator();

            var pending = changes.MoveNextAsync().AsTask();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));
            cancellation.Cancel();
            var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal(cancellation.Token, exception.CancellationToken);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("change")]
    [InlineData("delete")]
    [InlineData("rename")]
    public async Task ObserveAsync_ReportsExistingEntryChanges(string operation)
    {
        var root = CreateTempDirectory();
        var original = Path.Combine(root, "original.txt");
        File.WriteAllText(original, "before");
        try
        {
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            await using var changes = BuildContextWatcher.ObserveAsync(
                [new("app", root)], () => ready.SetResult()).GetAsyncEnumerator();
            var pending = changes.MoveNextAsync().AsTask();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(10));

            var expectedPath = original;
            switch (operation)
            {
                case "change":
                    File.AppendAllText(original, "after");
                    break;
                case "delete":
                    File.Delete(original);
                    break;
                case "rename":
                    expectedPath = Path.Combine(root, "renamed.txt");
                    File.Move(original, expectedPath);
                    break;
            }

            Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.Equal("app", changes.Current.ServiceName);
            Assert.Equal(expectedPath, changes.Current.Path);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ObserveAsync_RejectsMissingContextBeforeStartingAnyWatcher()
    {
        var root = CreateTempDirectory();
        try
        {
            var missing = Path.Combine(root, "missing");
            await using var changes = BuildContextWatcher.ObserveAsync(
                [new("valid", root), new("broken", missing)]).GetAsyncEnumerator();
            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => changes.MoveNextAsync().AsTask());
            Assert.Contains("broken", exception.Message);
            Assert.Contains(missing, exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_RejectsUnknownRequestedService()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteCompose(root, """
                services:
                  app:
                    image: example/app
                """);
            await using var changes = new ComposeService().WatchAsync(
                CreateContext(root), new ComposeWatchOptions { Services = ["missing"] }).GetAsyncEnumerator();
            var exception = await Assert.ThrowsAsync<ArgumentException>(() => changes.MoveNextAsync().AsTask());
            Assert.Contains("missing", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_EmptyServiceSelectionCompletes()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteCompose(root, """
                services:
                  app:
                    build: ./missing
                """);
            await using var changes = new ComposeService().WatchAsync(
                CreateContext(root), new ComposeWatchOptions { Services = [] }).GetAsyncEnumerator();
            Assert.False(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_IgnoresInactiveProfileContextButExplicitSelectionIncludesIt()
    {
        var root = CreateTempDirectory();
        try
        {
            WriteCompose(root, """
                services:
                  app:
                    image: example/app
                  debug:
                    build: ./missing
                    profiles: [debug]
                """);
            var service = new ComposeService();
            await using (var changes = service.WatchAsync(CreateContext(root)).GetAsyncEnumerator())
                Assert.False(await changes.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)));

            await using var explicitChanges = service.WatchAsync(
                CreateContext(root), new ComposeWatchOptions { Services = ["debug"] }).GetAsyncEnumerator();
            var exception = await Assert.ThrowsAsync<DirectoryNotFoundException>(
                () => explicitChanges.MoveNextAsync().AsTask());
            Assert.Contains("debug", exception.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_PruneIsRejectedBeforeLoadingProject()
    {
        var root = CreateTempDirectory();
        try
        {
            await using var changes = new ComposeService().WatchAsync(
                CreateContext(root), new ComposeWatchOptions { Prune = true }).GetAsyncEnumerator();
            await Assert.ThrowsAsync<NotSupportedException>(() => changes.MoveNextAsync().AsTask());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_PreCancelledTokenThrowsBeforeLoadingProject()
    {
        var root = CreateTempDirectory();
        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            await using var changes = new ComposeService().WatchAsync(
                CreateContext(root), cancellationToken: cancellation.Token).GetAsyncEnumerator();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => changes.MoveNextAsync().AsTask());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WatchAsync_NoUpAndQuietObserveWithoutDockerConnection()
    {
        var root = CreateTempDirectory();
        try
        {
            var buildContext = Directory.CreateDirectory(Path.Combine(root, "app")).FullName;
            WriteCompose(root, """
                services:
                  app:
                    build: ./app
                """);
            var context = CreateContext(root) with { SocketPath = "invalid://unreachable" };
            using var cancellation = new CancellationTokenSource();
            await using var changes = new ComposeService().WatchAsync(context,
                new ComposeWatchOptions { NoUp = true, Quiet = true }, cancellation.Token).GetAsyncEnumerator();
            try
            {
                // Starting enumeration registers the watcher synchronously, avoiding a startup race.
                var pending = changes.MoveNextAsync().AsTask();
                var changedPath = Directory.CreateDirectory(Path.Combine(buildContext, "changed")).FullName;
                Assert.True(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
                Assert.Equal("app", changes.Current.ServiceName);
                Assert.Equal("rebuild", changes.Current.Action);
                Assert.Equal(changedPath, changes.Current.Path);
            }
            finally
            {
                cancellation.Cancel();
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static ComposeProjectContext CreateContext(string root) => new()
    {
        ProjectName = "watch-tests",
        WorkingDirectory = root,
        ComposeFileName = "compose.yaml"
    };

    private static void WriteCompose(string root, string contents) =>
        File.WriteAllText(Path.Combine(root, "compose.yaml"), contents);

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"compose-watch-{Guid.NewGuid():N}");
        return Directory.CreateDirectory(path).FullName;
    }
}
