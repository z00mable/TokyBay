using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using TokyBay;
using TokyBay.Pages;
using TokyBay.Services;

class Program
{
    static async Task Main(string[] args)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();

        var app = serviceProvider.GetRequiredService<Application>();
        await app.RunAsync(args);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        services.AddSingleton<IConfiguration>(config);

        services.AddHttpClient<IHttpService, HttpService>(client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        services.AddSingleton<EscapeCancellableConsole>(sp => new EscapeCancellableConsole(AnsiConsole.Console));
        services.AddSingleton<IAnsiConsole>(sp => sp.GetRequiredService<EscapeCancellableConsole>());

        services.AddSingleton<IIpifyService, IpifyService>();
        services.AddSingleton<IPageService, PageService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddScraperServices(config =>
        {
            config.MaxParallelDownloads = 5;
            config.MaxParallelConversions = 3;
            config.MaxSegmentsPerTrack = 8;
            config.RetryAttempts = 3;
            config.RetryDelayMs = 1000;
        });

        services.AddTransient<MainPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SearchTokybookPage>();
        services.AddTransient<DownloadPage>();

        services.AddSingleton<Application>();
    }
}