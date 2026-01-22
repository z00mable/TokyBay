using Microsoft.Extensions.Configuration;
using TokyBay.Pages;
using TokyBay.Services;

namespace TokyBay
{
    public class Application(
    IConfiguration config,
    ISettingsService settingsService,
    MainPage mainPage)
    {
        private readonly IConfiguration _config = config;
        private readonly ISettingsService _settingsService = settingsService;
        private readonly MainPage _mainPage = mainPage;

        public async Task RunAsync(string[] args)
        {
            await _settingsService.InitializeAsync(_config);

            for (int i = 0; i < args.Length; i++)
            {
                if ((args[i] == "-d" || args[i] == "--directory") && i + 1 < args.Length)
                {
                    var currentSettings = _settingsService.GetSettings();
                    if (args[i + 1] != currentSettings.DownloadPath)
                    {
                        await _settingsService.UpdateDownloadPathAsync(args[i + 1]);
                    }
                }
            }

            await _settingsService.EnsureFFmpegAsync();
            await _mainPage.ShowAsync();
        }
    }
}
