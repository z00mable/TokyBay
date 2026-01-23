namespace TokyBay.Models
{
    public class StreamingAudiobookMetadata : AudiobookMetadata
    {
        public List<TrackInfo> Tracks { get; set; } = new();
        public string StreamToken { get; set; } = string.Empty;
        public string AudioBookId { get; set; } = string.Empty;
    }
}
