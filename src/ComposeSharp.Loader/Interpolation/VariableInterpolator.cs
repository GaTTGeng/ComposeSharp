// Rider inspection rationale: TryGetValue is retained because the lookup and fallback decision are intentionally explicit.
// ReSharper disable CanSimplifyDictionaryTryGetValueWithGetValueOrDefault
// Rider inspection rationale: The current branch shape is retained to keep interpolation behavior easy to follow.
// ReSharper disable MergeConditionalExpression
// Rider inspection rationale: This parameter is part of a public interface contract and is consumed by implementations or callers outside this solution.
// ReSharper disable UnusedParameter.Global
using System.Text.RegularExpressions;

namespace ComposeSharp.Loader.Interpolation;

/// <summary>
/// Compose-style variable interpolation for raw YAML text, applied before parsing.
/// Supports <c>$NAME</c>, <c>${NAME}</c>, <c>$$</c> escaping, and the Compose default
/// operators (<c>:-</c>, <c>-</c>, <c>:?</c>, <c>?</c>, <c>:+</c>, <c>+</c>).
/// </summary>
public static partial class VariableInterpolator
{
    [GeneratedRegex(@"\$\$|\$\{(?<name>[A-Za-z_][A-Za-z0-9_]*)(?:(?<op>:\?|:\+|:-|\+|-|\?)(?<default>[^}]*))?\}|\$(?<shellName>[A-Za-z_][A-Za-z0-9_]*)")]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Expands Compose-style variables in YAML text.
    /// Process environment variables take precedence over values from the project's <c>.env</c> file.
    /// Per-service <c>env_file</c> entries are container environment inputs and are deliberately not
    /// interpolation sources.
    /// </summary>
    /// <param name="text">Raw YAML text to expand.</param>
    /// <param name="dotenv">Values from the project's <c>.env</c> file, used only when the process environment has no value.</param>
    /// <param name="strict">Reserved for stricter unresolved-variable behavior; currently has no effect.</param>
    public static string Expand(
        string text,
        IReadOnlyDictionary<string, string> dotenv,
        bool strict = false)
    {
        return Expand(text, dotenv, strict, environmentOverrides: null);
    }

    internal static string Expand(
        string text,
        IReadOnlyDictionary<string, string> dotenv,
        bool strict,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        return VariablePattern().Replace(text, match =>
        {
            // $$ escapes to a literal dollar; bare $NAME resolves without any default operator.
            if (match.Value == "$$") return "$";

            if (match.Groups["shellName"].Success)
                return ResolveVariable(match.Groups["shellName"].Value, dotenv, environmentOverrides) ?? "";

            var name = match.Groups["name"].Value;
            var op = match.Groups["op"].Success ? match.Groups["op"].Value : "";
            var defaultValue = match.Groups["default"].Success ? match.Groups["default"].Value : "";

            var value = ResolveVariable(name, dotenv, environmentOverrides);

            // Colon operators treat empty string as unset; bare operators only treat missing as unset.
            // Alternate/default values are expanded recursively so nested ${} inside them works.
            return op switch
            {
                ":-" => string.IsNullOrEmpty(value) ? Expand(defaultValue, dotenv, strict, environmentOverrides) : value,
                "-" => value is null ? Expand(defaultValue, dotenv, strict, environmentOverrides) : value,
                ":?" => string.IsNullOrEmpty(value) ? ThrowRequiredVariable(name, defaultValue) : value,
                "?" => value is null ? ThrowRequiredVariable(name, defaultValue) : value,
                ":+" => string.IsNullOrEmpty(value) ? "" : Expand(defaultValue, dotenv, strict, environmentOverrides),
                "+" => value is null ? "" : Expand(defaultValue, dotenv, strict, environmentOverrides),
                _ => value ?? ""
            };
        });
    }

    private static string ThrowRequiredVariable(string name, string message)
        => throw new InvalidOperationException($"Variable '{name}' is required{(string.IsNullOrWhiteSpace(message) ? string.Empty : $": {message}")}");

    private static string? ResolveVariable(
        string name,
        IReadOnlyDictionary<string, string> dotenv,
        IReadOnlyDictionary<string, string>? environmentOverrides)
    {
        // Per-call values replace process environment values, preserving MergeWithEnv precedence without global mutation.
        if (TryGetEnvironmentOverride(environmentOverrides, name, out var overrideValue))
            return overrideValue;

        var value = Environment.GetEnvironmentVariable(name);
        if (value is not null) return value;
        return dotenv.TryGetValue(name, out var dotEnvValue) ? dotEnvValue : null;
    }

    private static bool TryGetEnvironmentOverride(
        IReadOnlyDictionary<string, string>? environmentOverrides,
        string name,
        out string value)
    {
        if (environmentOverrides is not null && environmentOverrides.TryGetValue(name, out value!))
            return true;

        if (OperatingSystem.IsWindows() && environmentOverrides is not null)
        {
            foreach (var (key, candidate) in environmentOverrides)
            {
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = candidate;
                    return true;
                }
            }
        }

        value = null!;
        return false;
    }
}
// ReSharper restore UnusedParameter.Global
// ReSharper restore MergeConditionalExpression
// ReSharper restore CanSimplifyDictionaryTryGetValueWithGetValueOrDefault
