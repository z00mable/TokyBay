namespace TokyBay.Models
{
    public class DownloadedTrack
    {
        public string TempFolder { get; set; } = string.Empty;
        public string TrackTitle { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public string SanitizedTitle { get; set; } = string.Empty;
        public List<string> TsSegments { get; set; } = new();
        public int TrackNumber { get; set; }
        public int TotalTracks { get; set; }
    }
}