using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using TokyBay;
using TokyBay.Pages;
using TokyBay.Scraper;
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

        services.AddSingleton<IDownloaderService, DownloaderService>();
        services.AddSingleton<IIpifyService, IpifyService>();
        services.AddSingleton<IPageService, PageService>();
        services.AddSingleton<ISettingsService, SettingsService>();

        services.AddTransient<Tokybook>();
        services.AddTransient<ZAudiobooks>();

        services.AddTransient<MainPage>();
        services.AddTransient<SettingsPage>();
        services.AddTransient<SearchTokybookPage>();
        services.AddTransient<DownloadPage>();

        services.AddSingleton<Application>();
    }
}