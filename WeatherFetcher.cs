using System;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MyWeatherSyncMod
{
    public class WeatherData
    {
        public int WeatherCode;
        public float Temperature;
        public DateTime Sunrise;
        public DateTime Sunset;
    }

    public class WeatherFetcher
    {
        private readonly double _lat, _lon;
        private static readonly HttpClient Client;

        static WeatherFetcher()
        {
            // ✅ 强制启用 TLS 1.2，解决 SecureChannelFailure 错误
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            Client = new HttpClient();
        }

        public WeatherFetcher(double lat, double lon)
        {
            _lat = lat;
            _lon = lon;
        }

        public async Task<WeatherData> FetchAsync()
        {
            var url = $"https://api.open-meteo.com/v1/forecast?" +
                      $"latitude={_lat}&longitude={_lon}" +
                      "&current=weather_code,temperature_2m" +
                      "&daily=sunrise,sunset&timezone=auto";

            var response = await Client.GetStringAsync(url);
            var json = JObject.Parse(response);

            return new WeatherData
            {
                WeatherCode = json["current"]["weather_code"].Value<int>(),
                Temperature = json["current"]["temperature_2m"].Value<float>(),
                Sunrise = DateTime.Parse(json["daily"]["sunrise"][0].Value<string>()),
                Sunset = DateTime.Parse(json["daily"]["sunset"][0].Value<string>())
            };
        }
    }
}