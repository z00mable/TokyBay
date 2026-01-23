namespace TokyBay.Models
{
    public class SegmentedTrackData : TrackData
    {
        public string TempFolder { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public List<string> TsSegments { get; set; } = new();
    }
}
