using Spectre.Console;
using Spectre.Console.Rendering;

namespace TokyBay
{
    public class EscapeCancellableConsole : IAnsiConsole
    {
        private readonly IAnsiConsole console;
        private readonly object lockObj = new();
        private CancellationTokenSource _cts = null!;
        private EscapeCancellableInput _input = null!;

        public EscapeCancellableConsole(IAnsiConsole console)
        {
            this.console = console;
            ResetCancellationToken();
        }

        public CancellationTokenSource EscapeCancellationTokenSource
        {
            get { lock (lockObj) { return _cts; } }
        }

        public IAnsiConsoleInput Input
        {
            get { lock (lockObj) { return _input; } }
        }

        public void ResetCancellationToken()
        {
            lock (lockObj)
            {
                _cts?.Dispose();
                _cts = new CancellationTokenSource();
                _input = new EscapeCancellableInput(console.Input, _cts);
            }
        }

        public Profile Profile => console.Profile;
        public IAnsiConsoleCursor Cursor => console.Cursor;
        public IExclusivityMode ExclusivityMode => console.ExclusivityMode;
        public RenderPipeline Pipeline => console.Pipeline;

        public void Clear(bool home) => console.Clear(home);
        public void Write(IRenderable renderable) => console.Write(renderable);

        public Task<T> PromptAsync<T>(IPrompt<T> prompt, CancellationToken cancellationToken = default)
            => AnsiConsoleExtensions.PromptAsync(this, prompt, GetMergedCancellationToken(cancellationToken));

        public Task<T> AskAsync<T>(string prompt, CancellationToken cancellationToken = default)
            => AnsiConsoleExtensions.AskAsync<T>(this, prompt, GetMergedCancellationToken(cancellationToken));

        CancellationToken GetMergedCancellationToken(CancellationToken cancellationToken)
        {
            var escapeCts = EscapeCancellationTokenSource;
            return cancellationToken == CancellationToken.None || cancellationToken == escapeCts.Token
                ? escapeCts.Token
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, escapeCts.Token).Token;
        }
    }

    public class EscapeCancellableInput(IAnsiConsoleInput originalInput, CancellationTokenSource cts) : IAnsiConsoleInput
    {
        public bool IsKeyAvailable() => originalInput.IsKeyAvailable();

        public ConsoleKeyInfo? ReadKey(bool intercept)
        {
            var key = originalInput.ReadKey(intercept);
            if (key?.Key == ConsoleKey.Escape)
            {
                cts.Cancel();
            }

            return key;
        }

        public async Task<ConsoleKeyInfo?> ReadKeyAsync(bool intercept, CancellationToken cancellationToken)
        {
            var key = await originalInput.ReadKeyAsync(intercept, cancellationToken);
            if (key?.Key == ConsoleKey.Escape)
            {
                await cts.CancelAsync();
            }

            return key;
        }
    }
}