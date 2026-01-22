namespace TokyBay.Models
{
    public class AudiobookData
    {
        public string AudioBookId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public List<string> ChapterUrls { get; set; } = new();
    }
}
