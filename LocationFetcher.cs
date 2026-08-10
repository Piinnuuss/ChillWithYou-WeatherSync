using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MyWeatherSyncMod
{
    public static class LocationFetcher
    {
        private static readonly HttpClient Client = new HttpClient();

        public class LocationResult
        {
            public double Latitude;
            public double Longitude;
            public bool Success;
        }

        public static async Task<LocationResult> FetchAsync()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

                // 使用 ip-api.com（免费版仅支持 HTTP，不支持 HTTPS）
                var json = await Client.GetStringAsync("http://ip-api.com/json/?fields=lat,lon");
                var obj = JObject.Parse(json);

                if (obj["lat"] != null && obj["lon"] != null)
                {
                    return new LocationResult
                    {
                        Latitude = obj["lat"].Value<double>(),
                        Longitude = obj["lon"].Value<double>(),
                        Success = true
                    };
                }

                return new LocationResult { Success = false };
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Auto-location failed: {ex.Message}");
                return new LocationResult { Success = false };
            }
        }
    }
}