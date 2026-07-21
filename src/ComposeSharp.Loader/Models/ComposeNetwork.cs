namespace ComposeSharp.Loader.Models;

public sealed record ComposeNetwork(
    string Name,
    bool External,
    string? Driver,
    bool? Internal,
    bool? Attachable,
    IReadOnlyDictionary<string, string>? Labels,
    IReadOnlyDictionary<string, string>? DriverOpts,
    IpamConfig? Ipam,
    bool? EnableIpv6,
    string? Scope);

public sealed record IpamConfig(
    string? Driver,
    IReadOnlyList<IpamSubnetConfig>? Config);

public sealed record IpamSubnetConfig(
    string? Subnet,
    string? Gateway,
    string? IpRange);
