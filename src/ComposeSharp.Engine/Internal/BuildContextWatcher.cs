using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComposeSharp.Api;

namespace ComposeSharp.Engine.Internal;

/// <summary>Observes selected build directories and reports their file changes continuously.</summary>
internal static class BuildContextWatcher
{
    internal sealed record Registration(string ServiceName, string Directory);

    internal static async IAsyncEnumerable<WatchEvent> ObserveAsync(
        IReadOnlyList<Registration> registrations,
        Action? onReady = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registrations);
        cancellationToken.ThrowIfCancellationRequested();

        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var contexts = new Dictionary<string, List<string>>(comparer);
        foreach (var registration in registrations)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.ServiceName);
            ArgumentException.ThrowIfNullOrWhiteSpace(registration.Directory);
            string directory;
            try
            {
                directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(registration.Directory));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new ArgumentException(
                    $"Invalid build context for service '{registration.ServiceName}': '{registration.Directory}'.",
                    nameof(registrations), exception);
            }

            if (!System.IO.Directory.Exists(directory))
                throw new DirectoryNotFoundException($"Build context for service '{registration.ServiceName}' does not exist: '{directory}'.");

            if (!contexts.TryGetValue(directory, out var services))
                contexts.Add(directory, services = []);
            if (!services.Contains(registration.ServiceName, StringComparer.Ordinal))
                services.Add(registration.ServiceName);
        }

        foreach (var services in contexts.Values)
            services.Sort(StringComparer.Ordinal);

        if (contexts.Count == 0)
            yield break;

        var channel = Channel.CreateBounded<WatchEvent>(new BoundedChannelOptions(1024)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });
        var watchers = new List<FileSystemWatcher>(contexts.Count);
        try
        {
            foreach (var (directory, services) in contexts)
            {
                var reportGate = new object();
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.CreationTime
                };

                void Report(string path)
                {
                    lock (reportGate)
                    {
                        if (cancellationToken.IsCancellationRequested) return;
                        foreach (var service in services)
                        {
                            if (!channel.Writer.TryWrite(new WatchEvent { ServiceName = service, Action = "rebuild", Path = path }))
                            {
                                channel.Writer.TryComplete(new IOException(
                                    $"Build context watcher event buffer overflowed for '{directory}'."));
                                break;
                            }
                        }
                    }
                }

                watcher.Changed += (_, args) => Report(args.FullPath);
                watcher.Created += (_, args) => Report(args.FullPath);
                watcher.Deleted += (_, args) => Report(args.FullPath);
                watcher.Renamed += (_, args) => Report(args.FullPath);
                watcher.Error += (_, args) => channel.Writer.TryComplete(args.GetException()
                    ?? new IOException($"Build context watcher failed for '{directory}'."));
                watchers.Add(watcher);
            }

            // Attach every handler before enabling any watcher so all contexts are active together.
            foreach (var watcher in watchers)
                watcher.EnableRaisingEvents = true;
            onReady?.Invoke();

            await foreach (var change in channel.Reader.ReadAllAsync(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return change;
            }
        }
        finally
        {
            foreach (var watcher in watchers)
                watcher.Dispose();
        }
    }
}
