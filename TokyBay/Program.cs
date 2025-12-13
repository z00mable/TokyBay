using Microsoft.Extensions.Configuration;
using TokyBay;

class Program
{
    static async Task Main(string[] args)
    {
        IConfiguration config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();

        await SettingsMenu.SetSettings(config);

        for (int i = 0; i < args.Length; i++)
        {
            if ((args[i] == "-d" || args[i] == "--directory") && i + 1 < args.Length)
            {
                if (args[i + 1] != SettingsMenu.UserSettings.DownloadPath)
                {
                    SettingsMenu.UserSettings.DownloadPath = args[i + 1];
                    await SettingsMenu.PersistSettings();
                }
            }
        }

        await SettingsMenu.DownloadFFmpeg();

        await MenuHandler.ShowMainMenu();
    }
}