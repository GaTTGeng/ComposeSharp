namespace ComposeSharp.Api;

public sealed record ExecResult
{
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public bool Succeeded => ExitCode == 0;
}
