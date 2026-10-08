namespace ComposeSharp.Loader.Models;

/// <summary>
/// Shape of a top-level Compose network definition.
/// The loader currently exposes project networks as name lists on <see cref="ComposeProject"/>;
/// this record captures the richer Compose shape for inspection but is not populated yet.
/// </summary>
/// <param name="Name">Network key from the top-level <c>networks</c> mapping.</param>
/// <param name="External">When true, the network is expected to pre-exist outside the project.</param>
/// <param name="Driver">Network driver name; not applied by the engine.</param>
/// <param name="Internal">Restricts external route access when set.</param>
/// <param name="Attachable">Allows standalone containers to attach when set.</param>
/// <param name="Labels">Network labels; not applied by the engine.</param>
/// <param name="DriverOpts">Driver-specific options; not applied by the engine.</param>
/// <param name="Ipam">IPAM driver and subnet configuration; not applied by the engine.</param>
/// <param name="EnableIpv6">Enables IPv6 when set.</param>
/// <param name="Scope">Network scope hint; not applied by the engine.</param>
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

/// <summary>IPAM configuration block of a top-level network definition.</summary>
/// <param name="Driver">IPAM driver name.</param>
/// <param name="Config">Per-subnet IPAM entries.</param>
public sealed record IpamConfig(
    string? Driver,
    IReadOnlyList<IpamSubnetConfig>? Config);

/// <summary>One IPAM subnet entry.</summary>
/// <param name="Subnet">CIDR subnet string.</param>
/// <param name="Gateway">Optional gateway address.</param>
/// <param name="IpRange">Optional allocatable IP range.</param>
public sealed record IpamSubnetConfig(
    string? Subnet,
    string? Gateway,
    string? IpRange);
