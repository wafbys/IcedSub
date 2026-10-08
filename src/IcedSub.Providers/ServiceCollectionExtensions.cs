using IcedSub.Core.Abstractions;
using IcedSub.Core.Models;
using IcedSub.Providers.Assrt;
using IcedSub.Providers.OpenSubtitles;
using Microsoft.Extensions.DependencyInjection;

namespace IcedSub.Providers;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddIcedSubProviders(
        this IServiceCollection services,
        AppSettings settings)
    {
        services.AddSingleton(settings);
        services.AddSingleton(settings.OpenSubtitles);
        services.AddSingleton(settings.Assrt);

        services.AddHttpClient<OpenSubtitlesProvider>((sp, client) =>
        {
            var s = sp.GetRequiredService<OpenSubtitlesSettings>();
            client.BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", s.UserAgent);
            if (!string.IsNullOrWhiteSpace(s.ApiKey))
                client.DefaultRequestHeaders.TryAddWithoutValidation("Api-Key", s.ApiKey);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddHttpClient<AssrtProvider>((sp, client) =>
        {
            var s = sp.GetRequiredService<AssrtSettings>();
            client.BaseAddress = new Uri(s.BaseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "IcedSub/0.1 (subtitle client; +https://assrt.net)");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        });

        services.AddTransient<ISubtitleProvider>(sp => sp.GetRequiredService<OpenSubtitlesProvider>());
        services.AddTransient<ISubtitleProvider>(sp => sp.GetRequiredService<AssrtProvider>());

        return services;
    }
}
