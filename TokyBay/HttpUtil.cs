namespace TokyBay
{
    public static class HttpUtil
    {
        private static readonly HttpClient httpClient = new();

        public static async Task<HttpResponseMessage> GetAsync(string url)
        {
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
        }

        public static async Task<HttpResponseMessage> GetTracksAsync(string url, string audioBookId, string streamToken, string trackSrc)
        {
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Add("x-audiobook-id", audioBookId);
                request.Headers.Add("x-stream-token", streamToken);
                request.Headers.Add("x-track-src", trackSrc);
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
        }

        public static async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, url);
                request.Content = content;
                return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            }
        }
    }
}