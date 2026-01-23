using Spectre.Console;

namespace TokyBay.Services
{
    public class PageService(EscapeCancellableConsole console) : IPageService
    {
        private readonly EscapeCancellableConsole _console = console;

        public void DisplayHeader()
        {
            _console.Write(
                new FigletText(FigletFont.Load("Bulbhead.flf"), "TokyBay")
                    .LeftJustified()
                    .Color(Color.Red));
            _console.WriteLine();
        }

        public async Task<(string? value, bool cancelled)> DisplayPromptAsync(string prompt, string[] options)
        {
            _console.Clear();
            DisplayHeader();
            return await DisplayPartialPromptAsync(prompt, options);
        }

        public async Task<(string? value, bool cancelled)> DisplayPartialPromptAsync(string prompt, string[] options)
        {
            try
            {
                var result = await _console.PromptAsync(
                    new SelectionPrompt<string>()
                        .Title($"[grey]{prompt}[/]")
                        .PageSize(20)
                        .MoreChoicesText("[grey](Move up and down to reveal more titles)[/]")
                        .AddChoices(options)
                );
                return (result, false);
            }
            catch (OperationCanceledException)
            {
                _console.ResetCancellationToken();
                return (default, true);
            }
        }

        public async Task<(T? value, bool cancelled)> DisplayAskAsync<T>(string prompt)
        {
            try
            {
                var result = await _console.AskAsync<T>(prompt);
                return (result, false);
            }
            catch (OperationCanceledException)
            {
                _console.ResetCancellationToken();
                return (default, true);
            }
        }
    }
}