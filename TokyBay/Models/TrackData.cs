namespace TokyBay.Models
{
    public abstract class TrackData
    {
        public string TrackTitle { get; set; } = string.Empty;
        public string SanitizedTitle { get; set; } = string.Empty;
        public int TrackNumber { get; set; }
        public int TotalTracks { get; set; }
    }
}
