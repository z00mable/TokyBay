using Newtonsoft.Json.Linq;
using Spectre.Console;

namespace TokyBay
{
    public static class TokybookApiHandler
    {
        private const string IpifyUrl = "https://api.ipify.org?format=json";

        public static async Task<JObject> GetUserIdentity()
        {
            try
            {
                var response = await HttpUtil.GetAsync(IpifyUrl);
                var json = await response.Content.ReadAsStringAsync();
                var data = JObject.Parse(json);

                return new JObject
                {
                    ["ipAddress"] = data["ip"]?.ToString(),
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/143.0.0.0 Safari/537.36",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error getting IP: {ex.Message}[/]");
                return new JObject
                {
                    ["ipAddress"] = "0.0.0.0",
                    ["userAgent"] = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36",
                    ["timestamp"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                };
            }
        }
    }
}
