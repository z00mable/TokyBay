using Microsoft.Extensions.DependencyInjection;
using TokyBay.Scraper;
using TokyBay.Scraper.Abstractions;
using TokyBay.Scraper.Configuration;
using TokyBay.Scraper.Strategies;
using TokyBay.Services;

namespace TokyBay
{
    public static class ScraperServiceExtensions
    {
        public static IServiceCollection AddScraperServices(
            this IServiceCollection services,
            Action<ScraperConfig>? configureOptions = null)
        {
            var config = new ScraperConfig();
            configureOptions?.Invoke(config);
            services.AddSingleton(config);

            services.AddTransient<IScraperStrategy, TokybookStrategy>();
            services.AddTransient<IScraperStrategy, ZAudiobooksStrategy>();

            services.AddSingleton<ScraperFactory>();
            services.AddSingleton<DownloadService>();

            return services;
        }
    }
}