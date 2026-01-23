namespace TokyBay.Services
{
    public class HttpService(HttpClient httpClient) : IHttpService
    {
        private readonly HttpClient _httpClient = httpClient;

        public Task<HttpResponseMessage> GetAsync(string url)
        {
            return GetAsync(url, []);
        }

        public async Task<HttpResponseMessage> GetAsync(string url, Dictionary<string, string> headers)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            foreach (var header in headers)
            {
                request.Headers.Add(header.Key, header.Value);
            }
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }

        public async Task<HttpResponseMessage> PostAsync(string url, HttpContent content)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Content = content;
            return await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        }
    }
}