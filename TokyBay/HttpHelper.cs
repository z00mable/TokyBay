using System.Net;

namespace TokyBay
{
    public static class HttpHelper
    {
        private static readonly HttpClient httpClient = new HttpClient();

        public static async Task<string> GetHtmlAsync(string url)
        {
            HttpResponseMessage response = await GetHttpResponseAsync(url);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return string.Empty;
            }

            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public static async Task<HttpResponseMessage> GetHttpResponseAsync(string url)
        {
            return await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        }
    }
}
