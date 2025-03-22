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

            if (!string.IsNullOrEmpty(UserSettings.FFmpegDirectory))
            {
                if (!ExistsFFmpegFile(UserSettings.FFmpegDirectory))
                {
                    UserSettings.FFmpegDirectory = string.Empty;
                    UserSettings.ConvertMp3ToM4b = false;
                    UserSettings.DeleteMp3AfterDownload = false;
                    await PersistSettings();
                }
            }

            if (string.IsNullOrEmpty(UserSettings.DownloadPath))
            {
                var windowsMusicFolder = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "\\Music" : string.Empty;
                var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + windowsMusicFolder;
                UserSettings.DownloadPath = userPath;
                await PersistSettings();
            }

            await Task.CompletedTask;
        }

        public static async Task PersistSettings()
        {
            var jsonObject = new { UserSettings = UserSettings };
            var userSettings = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync("appsettings.json", userSettings);
        }

        public static async Task GetSettings()
        {
            var exit = false;
            while (!exit)
            {
                AnsiConsole.Clear();
                Constants.ShowHeader();
                ShowCurrentSettings();
                AnsiConsole.WriteLine();
                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .AddChoices(new[]
                        {
                            "Change download directory",
                            "Set FFmpeg directory",
                            "Toggle 'Convert mp3 to m4b after download'",
                            "Toggle 'Delete mp3 after download'",
                            "Exit"
                        })
                );

                switch (choice)
                {
                    case "Change download directory":
                        await ChangeDownloadDirectory();
                        break;
                    case "Set FFmpeg directory":
                        await SetFfmpegDirectory();
                        break;
                    case "Toggle 'Convert mp3 to m4b after download'":
                        await ConvertMp3ToM4b();
                        break;
                    case "Toggle 'Delete mp3 after download'":
                        await DeleteMp3AfterDownload();
                        break;
                    case "Exit":
                        exit = true;
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
            table.AddRow("Convert mp3 to m4b after download", $"[green]{UserSettings.ConvertMp3ToM4b}[/]");
            table.AddRow("Delete mp3 after download", $"[green]{UserSettings.DeleteMp3AfterDownload}[/]");
            var panel = new Panel(table)
            {
                Header = new PanelHeader("[yellow]Current Settings[/]"),
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

        private static async Task SetFfmpegDirectory()
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

        private static async Task ConvertMp3ToM4b()
        {
            UserSettings.ConvertMp3ToM4b = !UserSettings.ConvertMp3ToM4b;
            if (UserSettings.ConvertMp3ToM4b)
            {
                await AnsiConsole.Status()
                    .SpinnerStyle(Style.Parse("blue bold"))
                    .StartAsync("[green]Downloading FFmpeg files...[/]", async ctx =>
                    {
                        await FFmpegDownloader.GetLatestVersion(FFmpegVersion.Official);
                    });

                UserSettings.FFmpegDirectory = Directory.GetCurrentDirectory();
            }
            else
            {
                UserSettings.DeleteMp3AfterDownload = false;
            }

            await PersistSettings();
            AnsiConsole.MarkupLine("[green]'Convert mp3 to m4b after download' updated.[/]");
            await Task.Delay(1000);
        }

        private static async Task DeleteMp3AfterDownload()
        {
            if (!UserSettings.ConvertMp3ToM4b)
            {
                AnsiConsole.MarkupLine("[red]You cannot change 'Delete mp3 after download' because conversion is disabled.[/]");
                await Task.Delay(1500);
            }
            else
            {
                UserSettings.DeleteMp3AfterDownload = !UserSettings.DeleteMp3AfterDownload;
                await PersistSettings();
                AnsiConsole.MarkupLine("[green]'Delete mp3 after download' updated.[/]");
                await Task.Delay(1000);
            }
        }

        private static bool ExistsFFmpegFile(string path)
        {
            var ffmpegExecutableName = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
            var ffmpegExecutablePath = Path.Combine(path, ffmpegExecutableName);
            return File.Exists(ffmpegExecutablePath);
        }
    }
}
