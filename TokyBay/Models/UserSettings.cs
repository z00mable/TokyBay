namespace TokyBay.Models
{
    public class UserSettings
    {
        public string DownloadPath { get; set; } = string.Empty;
        public string FFmpegDirectory { get; set; } = string.Empty;
        public bool ConvertToMp3 { get; set; } = false;
        public bool ConvertToM4b { get; set; } = false;
    }
}
