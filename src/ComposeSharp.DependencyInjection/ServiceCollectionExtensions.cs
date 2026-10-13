// Rider inspection rationale: This public member is part of a package contract and is intended for downstream callers.
// ReSharper disable UnusedMember.Global
// Rider inspection rationale: This public type is part of the package contract and is intended for downstream callers.
// ReSharper disable UnusedType.Global
using ComposeSharp.Api;
using ComposeSharp.Engine;
using Microsoft.Extensions.DependencyInjection;

namespace ComposeSharp.DependencyInjection;

/// <summary>Dependency-injection registration for ComposeSharp services.</summary>
public static class ServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IComposeService"/> as a singleton with default settings.</summary>
    public static IServiceCollection AddComposeSharp(this IServiceCollection services)
    {
        // Simple path: singleton registration with the parameterless implementation.
        services.AddSingleton<IComposeService>(_ => new ComposeService());
        return services;
    }

    /// <summary>Registers <see cref="IComposeService"/> as a singleton with caller-supplied defaults.</summary>
    public static IServiceCollection AddComposeSharp(this IServiceCollection services, Action<ComposeSharpOptions> configure)
    {
        // Build and register the options instance so consumers can resolve the configured defaults.
        var options = new ComposeSharpOptions();
        configure(options);
        services.AddSingleton(options);
        // Keep the same explicit construction path as the parameterless registration overload.
        services.AddSingleton<IComposeService>(_ => new ComposeService());
        return services;
    }
}

/// <summary>Default settings applied when resolving ComposeSharp services.</summary>
public sealed class ComposeSharpOptions
{
    /// <summary>Default Docker daemon endpoint used when a project context does not specify one.</summary>
    public string? DefaultSocketPath { get; init; }

    /// <summary>Default project name used when a project context does not specify one.</summary>
    public string? DefaultProjectName { get; init; }
}
// ReSharper restore UnusedType.Global
// ReSharper restore UnusedMember.Global
