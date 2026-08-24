namespace ComposeSharp.Loader;

/// <summary>
/// Describes invalid Compose input with its source and YAML location.
/// </summary>
public sealed class ComposeValidationException : InvalidOperationException
{
    public ComposeValidationException(
        string sourceFile,
        string propertyPath,
        string message,
        string? serviceName = null,
        long? line = null,
        long? column = null,
        Exception? innerException = null)
        : base(FormatMessage(sourceFile, propertyPath, message, serviceName, line, column), innerException)
    {
        SourceFile = sourceFile;
        PropertyPath = propertyPath;
        ServiceName = serviceName;
        Line = line;
        Column = column;
    }

    public string SourceFile { get; }

    public string PropertyPath { get; }

    public string? ServiceName { get; }

    public long? Line { get; }

    public long? Column { get; }

    private static string FormatMessage(
        string sourceFile,
        string propertyPath,
        string message,
        string? serviceName,
        long? line,
        long? column)
    {
        var service = serviceName is null ? string.Empty : $", service '{serviceName}'";
        var location = line is null
            ? string.Empty
            : $", line {line}{(column is null ? string.Empty : $", column {column}")}";
        return $"Invalid Compose input in '{sourceFile}' at '{propertyPath}'{service}{location}: {message}";
    }
}
