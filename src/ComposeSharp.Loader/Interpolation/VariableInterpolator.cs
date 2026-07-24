using System.Text.RegularExpressions;

namespace ComposeSharp.Loader.Interpolation;

public static partial class VariableInterpolator
{
    [GeneratedRegex(@"\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:(?<op>:\?|:\+|:-|\+|-|\?)(?<default>[^}]*))?\}")]
    private static partial Regex BracedVariablePattern();

    [GeneratedRegex(@"\$(?<name>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex ShellVariablePattern();

    /// <summary>
    /// Expands Compose-style variables in YAML text.
    /// Process environment variables take precedence over values from the project's <c>.env</c> file.
    /// Per-service <c>env_file</c> entries are container environment inputs and are deliberately not
    /// interpolation sources.
    /// </summary>
    public static string Expand(
        string text,
        IReadOnlyDictionary<string, string> dotenv,
        bool strict = false)
    {
        // Preserve a literal dollar until all interpolation has completed; otherwise $${NAME}
        // would become ${NAME} and be expanded by the next regex pass.
        const string escapedDollar = "\uE000";
        var result = text.Replace("$$", escapedDollar, StringComparison.Ordinal);

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
                ":?" => string.IsNullOrEmpty(value) ? ThrowRequiredVariable(name, defaultValue) : value,
                "?" => value is null ? ThrowRequiredVariable(name, defaultValue) : value,
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

        return result.Replace(escapedDollar, "$", StringComparison.Ordinal);
    }

    private static string ThrowRequiredVariable(string name, string message)
        => throw new InvalidOperationException($"Variable '{name}' is required{(string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}")}");

    private static string? ResolveVariable(string name, IReadOnlyDictionary<string, string> dotenv)
    {
        // This precedence is intentionally centralized so all ${NAME} and $NAME forms behave alike.
        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null) return value;
        return dotenv.TryGetValue(name, out var dotEnvValue) ? dotEnvValue : null;
    }
}
