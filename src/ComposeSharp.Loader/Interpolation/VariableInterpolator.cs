using System.Text.RegularExpressions;

namespace ComposeSharp.Loader.Interpolation;

public static partial class VariableInterpolator
{
    [GeneratedRegex(@"\$\$\{(?<name>[^}]+)\}")]
    private static partial Regex EscapedDollarPattern();

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:(?<op>:\?|:\+|:-|\+|-|\?)(?<default>[^}]*))?\}")]
    private static partial Regex BracedVariablePattern();

    [GeneratedRegex(@"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ShellVariablePattern();

    public static string Expand(string text, IReadOnlyDictionary<string, string> dotenv, bool strict = false)
    {
        var result = EscapedDollarPattern().Replace(text, m => "${" + m.Groups["name"].Value + "}");

        result = BracedVariablePattern().Replace(result, match =>
        {
            var name = match.Groups["name"].Value;
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : "";
            var defaultValue = match.Groups["default"].Success ? match.Groups["default"].Value : "";

            var value = ResolveVariable(name, dotenv);

            return op switch
            {
                ":-" => string.IsNullOrEmpty(value) ? defaultValue : value,
                "-" => value is null ? defaultValue : value,
                ":?" => string.IsNullOrEmpty(value) ? throw new InvalidOperationException($"Variable '{name}' is not set: {defaultValue}") : value,
                "?" => value is null ? throw new InvalidOperationException($"Variable '{name}' is not set: {defaultValue}") : value,
                ":+" => string.IsNullOrEmpty(value) ? "" : defaultValue,
                "+" => value is null ? "" : defaultValue,
                _ => value ?? ""
            };
        });

        result = ShellVariablePattern().Replace(result, match =>
        {
            var name = match.Groups["name"].Value;
            return ResolveVariable(name, dotenv) ?? "";
        });

        return result;
    }

    private static string? ResolveVariable(string name, IReadOnlyDictionary<string, string> dotenv)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null) return value;
        return dotenv.TryGetValue(name, out var dotEnvValue) ? dotEnvValue : null;
    }
}
