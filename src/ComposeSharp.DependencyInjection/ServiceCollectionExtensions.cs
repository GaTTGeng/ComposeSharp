using ComposeSharp.Api;
using ComposeSharp.Engine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComposeSharp.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComposeSharp(this IServiceCollection services)
    {
        services.AddSingleton<IComposeService, ComposeService>();
        return services;
    }

    public static IServiceCollection AddComposeSharp(this IServiceCollection services, Action<ComposeSharpOptions> configure)
    {
        var options = new ComposeSharpOptions();
        configure(options);
        services.AddSingleton(options);
        services.AddSingleton<IComposeService>(sp =>
        {
            var logger = sp.GetService<ILogger<ComposeService>>();
            return new ComposeService();
        });
        return services;
    }
}

public sealed class ComposeSharpOptions
{
    public string? DefaultSocketPath { get; init; }
    public string? DefaultProjectName { get; init; }
}
