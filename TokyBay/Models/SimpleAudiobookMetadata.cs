namespace TokyBay.Models
{
    public class SimpleAudiobookMetadata : AudiobookMetadata
    {
        public List<string> ChapterUrls { get; set; } = new();
    }
}
