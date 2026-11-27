using System.Globalization;
using System.Net;
using ComposeSharp.Loader.Models;

namespace ComposeSharp.Loader;

internal sealed class PortParseException(string path, string message) : Exception(message)
{
    internal string Path { get; } = path;
}

internal static class ComposePortParser
{
    internal static IReadOnlyList<ComposePort> Parse(object? value)
    {
        string? hostIp = null;
        string? published = null;
        string? name = null;
        string target;
        var protocol = "tcp";

        if (value is Dictionary<object, object?> map)
        {
            foreach (var (key, field) in map)
            {
                var path = $".{key}";
                if (field is null || field is Dictionary<object, object?> or List<object?>)
                    throw new PortParseException(path, "expected a scalar value.");
                var text = field.ToString()!;
                switch (key.ToString())
                {
                    case "target": break;
                    case "published": published = text; break;
                    case "host_ip": hostIp = text; break;
                    case "protocol": protocol = text; break;
                    case "name": name = text; break;
                    default: throw new PortParseException(path, "this port option is not supported by the engine.");
                }
            }
            if (!map.TryGetValue("target", out var targetValue) || targetValue is null)
                throw new PortParseException(".target", "a target port is required.");
            target = targetValue.ToString()!;
        }
        else if (value is not null && value is not List<object?>)
        {
            var text = value.ToString()!;
            var slash = text.LastIndexOf('/');
            if (slash >= 0)
            {
                protocol = text[(slash + 1)..];
                text = text[..slash];
            }
            var parts = text.Split(':');
            target = parts[^1];
            if (parts is [.., { Length: > 0 } hostPort, _]) published = hostPort;
            if (parts.Length >= 3)
            {
                hostIp = string.Join(":", parts[..^2]);
                if (hostIp.StartsWith('[') != hostIp.EndsWith(']'))
                    throw new PortParseException("", "IPv6 address brackets must match.");
                hostIp = hostIp.Trim('[', ']');
            }
        }
        else
        {
            throw new PortParseException("", "expected a short port mapping or a long port mapping.");
        }

        if (protocol is not ("tcp" or "udp" or "sctp"))
            throw new PortParseException(".protocol", "expected tcp, udp or sctp.");
        if (hostIp is not null)
        {
            if (!IPAddress.TryParse(hostIp, out var address))
                throw new PortParseException(".host_ip", "expected an IPv4 or IPv6 address.");
            hostIp = address.ToString();
        }
        var (firstTarget, lastTarget) = ParseRange(target, ".target");
        var firstHost = 0;
        var lastHost = 0;
        if (published == "0") published = null;
        if (published is not null)
            (firstHost, lastHost) = ParseRange(published, ".published");
        if (published is not null && firstTarget != lastTarget && lastTarget - firstTarget != lastHost - firstHost)
            throw new PortParseException(".published", "host and target ranges must have equal lengths.");

        var result = new List<ComposePort>();
        for (var port = firstTarget; port <= lastTarget; port++)
        {
            var host = published is null ? null : firstTarget == lastTarget && firstHost != lastHost
                ? published : (firstHost + port - firstTarget).ToString(CultureInfo.InvariantCulture);
            result.Add(new ComposePort(host, $"{port}/{protocol}", protocol) { HostIp = hostIp, Name = name });
        }
        return result;
    }

    internal static bool Conflicts(IEnumerable<ComposePort> previous, ComposePort port)
    {
        if (port.HostPort is null || port.HostPort.Contains('-')) return false;
        return previous.Any(other =>
        {
            if (other.HostPort != port.HostPort || other.Protocol != port.Protocol) return false;
            if (other.HostIp == port.HostIp && other.ContainerPort == port.ContainerPort) return false;
            if (other.HostIp is null || port.HostIp is null) return true;
            var left = IPAddress.Parse(other.HostIp);
            var right = IPAddress.Parse(port.HostIp);
            return left.Equals(right) || left.AddressFamily == right.AddressFamily &&
                (left.Equals(IPAddress.Any) || left.Equals(IPAddress.IPv6Any) ||
                 right.Equals(IPAddress.Any) || right.Equals(IPAddress.IPv6Any));
        });
    }

    private static (int First, int Last) ParseRange(string text, string path)
    {
        var parts = text.Split('-');
        if (parts.Length is < 1 or > 2 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
            first is < 1 or > 65535)
            throw new PortParseException(path, "expected a port or an ascending range between 1 and 65535.");
        var last = first;
        if (parts.Length == 2 && (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out last) ||
                                  last < first || last > 65535))
            throw new PortParseException(path, "expected a port or an ascending range between 1 and 65535.");
        return (first, last);
    }
}
