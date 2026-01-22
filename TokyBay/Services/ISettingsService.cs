using Microsoft.Extensions.Configuration;
using TokyBay.Models;

namespace TokyBay.Services
{
    public interface ISettingsService
    {
        UserSettings GetSettings();
        Task InitializeAsync(IConfiguration config);
        Task UpdateDownloadPathAsync(string path);
        Task EnsureFFmpegAsync();
        Task PersistSettingsAsync();
        Task ToggleConvertToMp3Async();
        Task ToggleConvertToM4bAsync();
        Task UpdateFFmpegDirectoryAsync(string path);
    }
}