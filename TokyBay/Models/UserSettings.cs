namespace TokyBay.Models
{
    public class UserSettings
    {
        public string DownloadPath { get; set; } = string.Empty;
        public bool ConvertMp3ToM4b { get; set; } = false;
        public bool DeleteMp3AfterDownload { get; set; } = false;
        public string FFmpegDirectory { get; set; } = string.Empty;
    }
}
