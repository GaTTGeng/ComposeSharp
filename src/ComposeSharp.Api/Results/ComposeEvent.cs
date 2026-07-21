namespace ComposeSharp.Api;

public sealed record ComposeEvent
{
    public required DateTime Timestamp { get; init; }
    public required string Type { get; init; }
    public required string Action { get; init; }
    public required string ID { get; init; }
    public string? Service { get; init; }
    public string? Container { get; init; }
    public IReadOnlyDictionary<string, string>? Attributes { get; init; }

    public override string ToString()
    {
        var ts = Timestamp.ToString("yyyy-MM-dd HH:mm:ss.ffffff");
        var attrs = Attributes is { Count: > 0 }
            ? $" ({string.Join(", ", Attributes.Select(kv => $"{kv.Key}={kv.Value}"))})"
            : "";
        return $"{ts} {Type} {Action} {ID}{attrs}";
    }
}
