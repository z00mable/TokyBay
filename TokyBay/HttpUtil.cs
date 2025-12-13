namespace TokyBay
{
    public static class HttpUtil
    {
        private static readonly HttpClient httpClient = new();

        public static async Task<HttpResponseMessage> GetAsync(string url)
        {
            httpClient.DefaultRequestHeaders.Clear();
            return await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }

        public static async Task<HttpResponseMessage> GetTracksAsync(string url, string audioBookId, string streamToken, string trackSrc)
        {
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("x-audiobook-id", audioBookId);
            httpClient.DefaultRequestHeaders.Add("x-stream-token", streamToken);
            httpClient.DefaultRequestHeaders.Add("x-track-src", trackSrc);
            return await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }

        public static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            httpClient.DefaultRequestHeaders.Clear();
            return await httpClient.PostAsync(url, content);
        }
    }
}
