namespace ComposeSharp.Api;

public sealed record WaitResult
{
    public IReadOnlyDictionary<string, int> ExitCodes { get; init; } = new Dictionary<string, int>();
    public int Code { get; init; }
}
