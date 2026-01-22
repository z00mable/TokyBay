using Spectre.Console;
using TokyBay.Models;
using TokyBay.Services;

namespace TokyBay.Pages
{
    public class SettingsPage(
        IAnsiConsole console,
        IPageService pageService,
        ISettingsService settingsService)
    {
        private readonly IAnsiConsole _console = console;
        private readonly IPageService _pageService = pageService;
        private readonly ISettingsService _settingsService = settingsService;
        private readonly UserSettings _settings = settingsService.GetSettings();

        public async Task ShowAsync()
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
                _console.Clear();
                _pageService.DisplayHeader();
                ShowCurrentSettings();
                _console.WriteLine();

                var (selection, cancelled) = await _pageService.DisplayPartialPromptAsync(string.Empty, options);
                if (cancelled || string.IsNullOrWhiteSpace(selection))
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
        }

        private void ShowCurrentSettings()
        {
            var table = new Table { Border = TableBorder.Rounded };
            table.AddColumn(new TableColumn("[yellow]Setting[/]"));
            table.AddColumn(new TableColumn("[yellow]Value[/]"));
            table.AddRow("Download directory", $"[green]{_settings.DownloadPath}[/]");
            table.AddRow("FFmpeg directory", $"[green]{_settings.FFmpegDirectory}[/]");
            table.AddRow("Download files as mp3", $"[green]{_settings.ConvertToMp3}[/]");
            table.AddRow("Download files as m4b", $"[green]{_settings.ConvertToM4b}[/]");

            var panel = new Panel(table)
            {
                Header = new PanelHeader("[yellow] Current Settings [/]"),
                Border = BoxBorder.Double
            };

            _console.Write(panel);
        }

        private async Task ChangeDownloadDirectoryAsync()
        {
            var (newPath, cancelled) = await _pageService.DisplayAskAsync<string>("Enter new download directory:");
            if (cancelled)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(newPath) && Path.Exists(newPath))
            {
                await _settingsService.UpdateDownloadPathAsync(newPath);
                _console.MarkupLine("[green]Download directory updated.[/]");
                await Task.Delay(1000);
            }
            else
            {
                _console.MarkupLine("[red]Directory does not exist.[/]");
                await Task.Delay(1000);
            }
        }

        private async Task ChangeFfmpegDirectoryAsync()
        {
            var (newFFmpegPath, cancelled) = await _pageService.DisplayAskAsync<string>("Enter new FFmpeg directory:");
            if (cancelled)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(newFFmpegPath))
            {
                try
                {
                    await _settingsService.UpdateFFmpegDirectoryAsync(newFFmpegPath);
                    _console.MarkupLine("[green]FFmpeg directory updated.[/]");
                    await Task.Delay(1000);
                }
                catch (DirectoryNotFoundException)
                {
                    _console.MarkupLine("[red]No FFmpeg found in directory.[/]");
                    await Task.Delay(1000);
                }
            }
        }

        private async Task DownloadAsMp3Async()
        {
            await _settingsService.ToggleConvertToMp3Async();
            _console.MarkupLine("[green]'Download files as mp3' updated.[/]");
            await Task.Delay(1000);
        }

        private async Task DownloadAsM4bAsync()
        {
            await _settingsService.ToggleConvertToM4bAsync();
            _console.MarkupLine("[green]'Download files as m4b' updated.[/]");
            await Task.Delay(1000);
        }
    }
}