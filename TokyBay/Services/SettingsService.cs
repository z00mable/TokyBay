using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Text.Json;
using TokyBay.Models;
using Xabe.FFmpeg.Downloader;

namespace TokyBay.Services
{
    public class SettingsService(IAnsiConsole console) : ISettingsService
    {
        private readonly IAnsiConsole _console = console;
        private UserSettings _userSettings = new UserSettings();

        public UserSettings GetSettings() => _userSettings;

        public async Task InitializeAsync(IConfiguration config)
        {
            config.GetSection("UserSettings").Bind(_userSettings);
            if (string.IsNullOrEmpty(_userSettings.DownloadPath))
            {
                var windowsMusicFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? "\\Music"
                    : string.Empty;
                var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + windowsMusicFolder;
                _userSettings.DownloadPath = userPath;
                await PersistSettingsAsync();
            }
        }

        public async Task UpdateDownloadPathAsync(string path)
        {
            _userSettings.DownloadPath = path;
            await PersistSettingsAsync();
        }

        public async Task EnsureFFmpegAsync()
        {
            if (ExistsFFmpegFile(_userSettings.FFmpegDirectory))
            {
                if (string.IsNullOrWhiteSpace(_userSettings.FFmpegDirectory))
                {
                    _userSettings.FFmpegDirectory = Directory.GetCurrentDirectory();
                }

                return;
            }

            await _console.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("[green]Downloading FFmpeg files...[/]", async ctx =>
                {
                    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                });

            _userSettings.FFmpegDirectory = Directory.GetCurrentDirectory();

            _console.MarkupLine($"[green]FFmpeg successfully downloaded to:[/] {_userSettings.FFmpegDirectory}");
            await Task.Delay(1000);
        }

        public async Task PersistSettingsAsync()
        {
            var userSettings = JsonSerializer.Serialize(
                new { UserSettings = _userSettings },
                new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync("appsettings.json", userSettings);
        }

        public async Task ToggleConvertToMp3Async()
        {
            _userSettings.ConvertToMp3 = !_userSettings.ConvertToMp3;
            await PersistSettingsAsync();
        }

        public async Task ToggleConvertToM4bAsync()
        {
            _userSettings.ConvertToM4b = !_userSettings.ConvertToM4b;
            await PersistSettingsAsync();
        }

        public async Task UpdateFFmpegDirectoryAsync(string path)
        {
            if (ExistsFFmpegFile(path))
            {
                _userSettings.FFmpegDirectory = path;
                await PersistSettingsAsync();
            }
            else
            {
                throw new DirectoryNotFoundException("FFmpeg not found in directory");
            }
        }

        private static bool ExistsFFmpegFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var ffmpegExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? "ffmpeg.exe"
                : "ffmpeg";
            var ffmpegExecutablePath = Path.Combine(path, ffmpegExecutableName);
            return File.Exists(ffmpegExecutablePath);
        }
    }
}