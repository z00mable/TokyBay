namespace TokyBay.Services
{
    public interface IPageService
    {
        void DisplayHeader();
        Task<(string? value, bool cancelled)> DisplayPromptAsync(string prompt, string[] options);
        Task<(string? value, bool cancelled)> DisplayPartialPromptAsync(string prompt, string[] options);
        Task<(T? value, bool cancelled)> DisplayAskAsync<T>(string prompt);
    }
}
