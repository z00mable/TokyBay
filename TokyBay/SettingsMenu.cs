using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Runtime.InteropServices;
using System.Text.Json;
using TokyBay.Models;
using Xabe.FFmpeg.Downloader;

namespace TokyBay
{
    public static class SettingsMenu
    {
        public static UserSettings UserSettings { get; set; } = new UserSettings();

        public static async Task SetSettings(IConfiguration config)
        {
            config.GetSection("UserSettings").Bind(UserSettings);

            if (string.IsNullOrEmpty(UserSettings.DownloadPath))
            {
                var windowsMusicFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\Music" : string.Empty;
                var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + windowsMusicFolder;
                UserSettings.DownloadPath = userPath;
                await PersistSettings();
            }

            await Task.CompletedTask;
        }

        public static async Task DownloadFFmpeg()
        {
            if (ExistsFFmpegFile(UserSettings.FFmpegDirectory))
            {
                if (string.IsNullOrWhiteSpace(UserSettings.FFmpegDirectory))
                {
                    UserSettings.FFmpegDirectory = Directory.GetCurrentDirectory();
                }

                return;
            }

            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue bold"))
                .StartAsync("[green]Downloading FFmpeg files...[/]", async ctx =>
                {
                    await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                });

            UserSettings.FFmpegDirectory = Directory.GetCurrentDirectory();

            AnsiConsole.MarkupLine($"[green]'FFmpeg successfully downloaded to:[/] {UserSettings.FFmpegDirectory}");
            await Task.Delay(1000);
        }

        public static async Task PersistSettings()
        {
            var userSettings = JsonSerializer.Serialize(new { UserSettings }, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync("appsettings.json", userSettings);
        }

        public static async Task GetSettings()
        {
            const string changeDownload = "Change download directory";
            const string changeFFmpegd = "Change FFmpeg directory";
            const string toggleMp3 = "Toggle 'Download files as mp3'";
            const string toggleM4b = "Toggle 'Download files as m4b'";
            const string exit = "Exit";

            var exitPressed = false;

            while (!exitPressed)
            {
                AnsiConsole.Clear();
                Constants.ShowHeader();
                ShowCurrentSettings();
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .AddChoices(new[]
                        {
                            changeDownload,
                            changeFFmpegd,
                            toggleMp3,
                            toggleM4b,
                            exit
                        })
                );

                switch (choice)
                {
                    case changeDownload:
                        await ChangeDownloadDirectory();
                        break;
                    case changeFFmpegd:
                        await ChangeFfmpegDirectory();
                        break;
                    case toggleMp3:
                        await DownloadAsMp3();
                        break;
                    case toggleM4b:
                        await DownloadAsM4b();
                        break;
                    case exit:
                        exitPressed = true;
                        break;
                }
            }
            await Task.CompletedTask;
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

            AnsiConsole.Write(panel);
        }

        private static async Task ChangeDownloadDirectory()
        {
            var newPath = AnsiConsole.Ask<string>("Enter new download directory:");
            if (!string.IsNullOrWhiteSpace(newPath) && Path.Exists(newPath))
            {
                UserSettings.DownloadPath = newPath;
                await PersistSettings();
                AnsiConsole.MarkupLine("[green]Download directory updated.[/]");
                await Task.Delay(1000);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]Directory does not exist.[/]");
                await Task.Delay(1000);
            }
        }

        private static async Task ChangeFfmpegDirectory()
        {
            var newFFmpegPath = AnsiConsole.Ask<string>("Enter FFmpeg directory path:");
            if (!string.IsNullOrWhiteSpace(newFFmpegPath) && ExistsFFmpegFile(newFFmpegPath))
            {
                UserSettings.FFmpegDirectory = newFFmpegPath;
                await PersistSettings();
                AnsiConsole.MarkupLine("[green]FFmpeg directory updated.[/]");
                await Task.Delay(1000);
            }
            else
            {
                AnsiConsole.MarkupLine("[red]No FFmpeg found in directory.[/]");
                await Task.Delay(1000);
            }
        }

        private static async Task DownloadAsMp3()
        {
            UserSettings.ConvertToMp3 = !UserSettings.ConvertToMp3;
            await PersistSettings();
            AnsiConsole.MarkupLine("[green]'Download files as mp3' updated.[/]");
            await Task.Delay(1000);
        }

        private static async Task DownloadAsM4b()
        {
            UserSettings.ConvertToM4b = !UserSettings.ConvertToM4b;
            await PersistSettings();
            AnsiConsole.MarkupLine("[green]'Download files as m4b' updated.[/]");
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
