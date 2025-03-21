
using Microsoft.Extensions.Configuration;
using Spectre.Console;
using System.Text.Json;
using TokyBay.Models;

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
                var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                UserSettings.DownloadPath = userPath;
                await PersistSettings();
            }

            await Task.CompletedTask;
        }

        public static async Task PersistSettings()
        {
            var jsonObject = new
            {
                UserSettings = UserSettings
            };

            var userSettings = JsonSerializer.Serialize(jsonObject, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync("appsettings.json", userSettings);
        }

        public static async Task GetSettings()
        {
            var exit = false;
            while (!exit)
            {
                AnsiConsole.Clear();
                AnsiConsole.MarkupLine("[blue italic]Welcome to TokyBay[/]");
                AnsiConsole.WriteLine();
                AnsiConsole.MarkupLine($"Current download directory: [green]{UserSettings.DownloadPath}[/]");
                AnsiConsole.WriteLine();

                var choice = AnsiConsole.Prompt(
                    new SelectionPrompt<string>()
                        .Title("[grey]Settings Menu:[/]")
                        .AddChoices(new[] { "Change download directory", "Exit" })
                );

                switch (choice)
                {
                    case "Change download directory":
                        var newPath = AnsiConsole.Ask<string>("Enter new download directory:");
                        if (!string.IsNullOrWhiteSpace(newPath))
                        {
                            UserSettings.DownloadPath = newPath;
                            await PersistSettings();
                            AnsiConsole.MarkupLine("[green]Download directory updated.[/]");
                            await Task.Delay(1000);
                        }
                        break;
                    case "Exit":
                        exit = true;
                        break;
                }
            }

            await Task.CompletedTask;
        }
    }
}
