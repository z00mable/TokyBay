using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Text.Json;
using TokyBay.Models;
using Xabe.FFmpeg.Downloader;

namespace TokyBay.Pages
{
    public static class SettingsPage
    {
        public static UserSettings UserSettings { get; set; } = new UserSettings();

        public static async Task ShowAsync()
        {
            const string changeDownload = "Change download directory";
            const string changeFFmpegd = "Change FFmpeg directory";
            const string toggleMp3 = "Toggle 'Download files as mp3'";
            const string toggleM4b = "Toggle 'Download files as m4b'";
            const string exit = "Exit";

            string[] options = { changeDownload, changeFFmpegd, toggleMp3, toggleM4b, exit };

            var exitPressed = false;

            while (!exitPressed)
            {
                Program.CustomAnsiConsole.Clear();
                MenuHandler.ShowHeader();
                ShowCurrentSettings();
                Program.CustomAnsiConsole.WriteLine();
                var selection = await MenuHandler.DisplayPartialPromptAsync(string.Empty, options);
                if (selection == null)
                {
                    return;
                }

                switch (selection)
                {
                    case changeDownload:
                        await ChangeDownloadDirectoryAsync();
                        break;
                    case changeFFmpegd:
                        await ChangeFfmpegDirectoryAsync();
                        break;
                    case toggleMp3:
                        await DownloadAsMp3Async();
                        break;
                    case toggleM4b:
                        await DownloadAsM4bAsync();
                        break;
                    case exit:
                        exitPressed = true;
                        break;
                }
            }

            await Task.CompletedTask;
        }

        public static async Task SetSettingsAsync(IConfiguration config)
        {
            config.GetSection("UserSettings").Bind(UserSettings);

            if (string.IsNullOrEmpty(UserSettings.DownloadPath))
            {
                var windowsMusicFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\Music" : string.Empty;
                var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + windowsMusicFolder;
                UserSettings.DownloadPath = userPath;
                await PersistSettingsAsync();
            }

            await Task.CompletedTask;
        }

        public static async Task DownloadFFmpegAsync()
        {
            if (ExistsFFmpegFile(UserSettings.FFmpegDirectory))
            {
                if (string.IsNullOrWhiteSpace(UserSettings.FFmpegDirectory))
                {
                    UserSettings.FFmpegDirectory = Directory.GetCurrentDirectory();
                }

                return;
            }

            await Program.CustomAnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("[green]Downloading FFmpeg files...[/]", async ctx =>
                {
                    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                });

            UserSettings.FFmpegDirectory = Directory.GetCurrentDirectory();

            Program.CustomAnsiConsole.MarkupLine($"[green]'FFmpeg successfully downloaded to:[/] {UserSettings.FFmpegDirectory}");
            await Task.Delay(1000);
        }

        public static async Task PersistSettingsAsync()
        {
            var userSettings = JsonSerializer.Serialize(new { UserSettings }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync("appsettings.json", userSettings);
        }

        private static void ShowCurrentSettings()
        {
            var table = new Table { Border = TableBorder.Rounded };
            table.AddColumn(new TableColumn("[yellow]Setting[/]"));
            table.AddColumn(new TableColumn("[yellow]Value[/]"));
            table.AddRow("Download directory", $"[green]{UserSettings.DownloadPath}[/]");
            table.AddRow("FFmpeg directory", $"[green]{UserSettings.FFmpegDirectory}[/]");
            table.AddRow("Download files as mp3", $"[green]{UserSettings.ConvertToMp3}[/]");
            table.AddRow("Download files as m4b", $"[green]{UserSettings.ConvertToM4b}[/]");
            var panel = new Panel(table)
            {
                Header = new PanelHeader("[yellow] Current Settings [/]"),
                Border = BoxBorder.Double
            };

            Program.CustomAnsiConsole.Write(panel);
        }

        private static async Task ChangeDownloadDirectoryAsync()
        {
            var (newPath, cancelled) = await MenuHandler.DisplayAskAsync<string>("Enter new download directory:");
            if (cancelled)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(newPath) && Path.Exists(newPath))
            {
                UserSettings.DownloadPath = newPath;
                await PersistSettingsAsync();
                Program.CustomAnsiConsole.MarkupLine("[green]Download directory updated.[/]");
                await Task.Delay(1000);
            }
            else
            {
                Program.CustomAnsiConsole.MarkupLine("[red]Directory does not exist.[/]");
                await Task.Delay(1000);
            }
        }

        private static async Task ChangeFfmpegDirectoryAsync()
        {
            var (newFFmpegPath, cancelled) = await MenuHandler.DisplayAskAsync<string>("Enter new download directory:");
            if (cancelled)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(newFFmpegPath) && ExistsFFmpegFile(newFFmpegPath))
            {
                UserSettings.FFmpegDirectory = newFFmpegPath;
                await PersistSettingsAsync();
                Program.CustomAnsiConsole.MarkupLine("[green]FFmpeg directory updated.[/]");
                await Task.Delay(1000);
            }
            else
            {
                Program.CustomAnsiConsole.MarkupLine("[red]No FFmpeg found in directory.[/]");
                await Task.Delay(1000);
            }
        }

        private static async Task DownloadAsMp3Async()
        {
            UserSettings.ConvertToMp3 = !UserSettings.ConvertToMp3;
            await PersistSettingsAsync();
            Program.CustomAnsiConsole.MarkupLine("[green]'Download files as mp3' updated.[/]");
            await Task.Delay(1000);
        }

        private static async Task DownloadAsM4bAsync()
        {
            UserSettings.ConvertToM4b = !UserSettings.ConvertToM4b;
            await PersistSettingsAsync();
            Program.CustomAnsiConsole.MarkupLine("[green]'Download files as m4b' updated.[/]");
            await Task.Delay(1000);
        }

        private static bool ExistsFFmpegFile(string path)
        {
            var ffmpegExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
            var ffmpegExecutablePath = Path.Combine(path, ffmpegExecutableName);
            return File.Exists(ffmpegExecutablePath);
        }
    }
}
