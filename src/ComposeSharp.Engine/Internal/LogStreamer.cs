using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using ComposeSharp.Api;
using Docker.DotNet;
using Docker.DotNet.Models;

namespace ComposeSharp.Engine.Internal;

internal sealed class LogStreamer
{
    public async Task<string> ReadLogsAsync(DockerClient client, string containerId, string tail, bool follow, CancellationToken ct)
    {
        using var stream = await client.Containers.GetContainerLogsAsync(containerId, false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = follow, Tail = tail }, ct);
        var (stdout, stderr) = await stream.ReadOutputToEndAsync(ct);
        return stdout + stderr;
    }

    public async Task LogsAsync(DockerClient client, string projectName, ComposeLogsOptions options, ILogConsumer? consumer, CancellationToken ct)
    {
        var containers = await GetLogContainersAsync(client, projectName, options.Services, ct);

        if (consumer is null)
        {
            foreach (var container in containers)
            {
                var text = await ReadLogsAsync(client, container.ID, options.Tail, false, ct);
                Console.Write(text);
            }
            return;
        }

        var tasks = containers.Select(container => Task.Run(async () =>
        {
            try
            {
                var svcName = container.Labels != null && container.Labels.TryGetValue(ComposeConstants.ServiceLabel, out var svc) ? svc : container.ID[..12];
                await foreach (var line in StreamLogLinesAsync(client, container.ID, options.Tail, ct))
                    consumer.OnLog(svcName, line, false);
                consumer.OnLogComplete(svcName);
            }
            catch (OperationCanceledException) { }
        }, ct)).ToArray();

        await Task.WhenAll(tasks);
    }

    public async IAsyncEnumerable<string> StreamLogLinesAsync(DockerClient client, string containerId, string tail,
        [EnumeratorCancellation] CancellationToken ct)
    {
        using var stream = await client.Containers.GetContainerLogsAsync(containerId, false,
            new ContainerLogsParameters { ShowStdout = true, ShowStderr = true, Follow = true, Tail = tail }, ct);

        var buffer = new byte[8192];
        var pending = new StringBuilder();
        while (true)
        {
            var result = await stream.ReadOutputAsync(buffer, 0, buffer.Length, ct);
            if (result.EOF)
            {
                if (pending.Length > 0) yield return pending.ToString();
                yield break;
            }
            if (result.Count <= 0) continue;

            pending.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            while (TryTakeLine(pending, out var line))
                yield return line;
        }
    }

    private static async Task<IReadOnlyList<ContainerListResponse>> GetLogContainersAsync(
        DockerClient client, string projectName, IReadOnlyList<string>? services, CancellationToken ct)
    {
        if (services is { Count: > 0 })
        {
            var all = new List<ContainerListResponse>();
            foreach (var svc in services)
            {
                var containers = await client.Containers.ListContainersAsync(new ContainersListParameters
                {
                    All = true,
                    Filters = LabelHelper.ServiceLabelFilter(projectName, svc)
                }, ct);
                all.AddRange(containers);
            }
            return all;
        }

        return (await client.Containers.ListContainersAsync(new ContainersListParameters
        {
            All = true,
            Filters = LabelHelper.ProjectLabelFilter(projectName)
        }, ct)).ToList();
    }

    private static bool TryTakeLine(StringBuilder buffer, out string line)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (buffer[i] != '\n') continue;
            var length = i > 0 && buffer[i - 1] == '\r' ? i - 1 : i;
            line = buffer.ToString(0, length);
            buffer.Remove(0, i + 1);
            return true;
        }
        line = string.Empty;
        return false;
    }
}
