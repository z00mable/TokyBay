using Newtonsoft.Json.Linq;

namespace TokyBay.Services
{
    public interface IIpifyService
    {
        Task<JObject> GetUserIdentityAsync();
    }
}
