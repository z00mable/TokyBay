namespace TokyBay.Services
{
    public interface IHttpService
    {
        Task<HttpResponseMessage> GetAsync(string url);
        Task<HttpResponseMessage> GetAsync(string url, Dictionary<string, string> headers);
        Task<HttpResponseMessage> PostAsync(string url, HttpContent content);
    }
}
