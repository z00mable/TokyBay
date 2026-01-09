using Microsoft.Extensions.Configuration;
using Spectre.Console;
using TokyBay;
using TokyBay.Pages;

static class Program
{
    public static EscapeCancellableConsole CustomAnsiConsole = new EscapeCancellableConsole(AnsiConsole.Console);

    static async Task Main(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        await SettingsPage.SetSettingsAsync(config);

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-d" || args[i] == "--directory") && i + 1 < args.Length)
            {
                if (args[i + 1] != SettingsPage.UserSettings.DownloadPath)
                {
                    SettingsPage.UserSettings.DownloadPath = args[i + 1];
                    await SettingsPage.PersistSettingsAsync();
                }
            }
        }

        await SettingsPage.DownloadFFmpegAsync();

        await MainPage.ShowAsync();
    }
}