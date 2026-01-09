using Spectre.Console;

namespace TokyBay.Pages
{
    public static class MenuHandler
    {
        public static void ShowHeader()
        {
            Program.CustomAnsiConsole.Write(
                new FigletText(FigletFont.Load("Bulbhead.flf"), "TokyBay")
                    .LeftJustified()
                    .Color(Color.Red));
            Program.CustomAnsiConsole.WriteLine();
        }

        public static async Task<string?> DisplayPromptAsync(string prompt, string[] options)
        {
            Program.CustomAnsiConsole.Clear();
            ShowHeader();
            return await DisplayPartialPromptAsync(prompt, options);
        }

        public static async Task<string?> DisplayPartialPromptAsync(string prompt, string[] options)
        {
            try
            {
                return await Program.CustomAnsiConsole.PromptAsync(
                    new SelectionPrompt<string>()
                        .Title($"[grey]{prompt}[/]")
                        .PageSize(20)
                        .MoreChoicesText("[grey](Move up and down to reveal more titles)[/]")
                        .AddChoices(options)
                );
            }
            catch (OperationCanceledException)
            {
                Program.CustomAnsiConsole.ResetCancellationToken();
                return null;
            }
            
        }

        public static async Task<(T? value, bool cancelled)> DisplayAskAsync<T>(string prompt)
        {
            try
            {
                var result = await Program.CustomAnsiConsole.AskAsync<T>(prompt);
                return (result, false);
            }
            catch (OperationCanceledException)
            {
                Program.CustomAnsiConsole.ResetCancellationToken();
                return (default, true);
            }
        }
    }
}
