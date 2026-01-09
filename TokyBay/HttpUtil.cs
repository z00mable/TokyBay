namespace TokyBay
{
    public static class HttpUtil
    {
        private static readonly HttpClient httpClient = new();

        public static async Task<HttpResponseMessage> GetAsync(string url)
        {
            return await GetAsync(url, []);
        }

        public static async Task<HttpResponseMessage> GetAsync(string url, Dictionary<string, string> headers)
        {
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                foreach (var header in headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }

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