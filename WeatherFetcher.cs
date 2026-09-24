using System;
using System.Globalization;
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

    /// <summary>
    /// 纯粹的 URL 构造 + JSON 解析，没有任何 Unity / 网络依赖。
    /// 实际请求由 WeatherSyncRunner 用 UnityWebRequest 协程发起（不阻塞主线程）。
    /// </summary>
    public class WeatherFetcher
    {
        public const double DefaultLat = 31.3093;
        public const double DefaultLon = 120.6020;

        public readonly double Latitude;
        public readonly double Longitude;

        public WeatherFetcher(double lat, double lon)
        {
            // 防止配置被写成 0,0（几内亚湾）或非法值
            Latitude = (lat >= -90.0 && lat <= 90.0 && lat != 0.0) ? lat : DefaultLat;
            Longitude = (lon >= -180.0 && lon <= 180.0 && lon != 0.0) ? lon : DefaultLon;
        }

        public string BuildUrl()
        {
            return "https://api.open-meteo.com/v1/forecast?" +
                   "latitude=" + Latitude.ToString("F4", CultureInfo.InvariantCulture) +
                   "&longitude=" + Longitude.ToString("F4", CultureInfo.InvariantCulture) +
                   "&current=weather_code,temperature_2m" +
                   "&daily=sunrise,sunset&timezone=auto";
        }

        /// <summary>解析 Open-Meteo 的 JSON。字段缺失时抛异常，由调用方记录并跳过本轮。</summary>
        public WeatherData Parse(string json)
        {
            var root = JObject.Parse(json);

            var current = root["current"];
            var daily = root["daily"];
            if (current == null || daily == null)
                throw new InvalidOperationException("Open-Meteo 响应缺少 'current'/'daily' 字段。");

            var sunriseArr = daily["sunrise"] as JArray;
            var sunsetArr = daily["sunset"] as JArray;
            if (sunriseArr == null || sunsetArr == null || sunriseArr.Count == 0 || sunsetArr.Count == 0)
                throw new InvalidOperationException("Open-Meteo 响应缺少 sunrise/sunset。");

            return new WeatherData
            {
                WeatherCode = current["weather_code"].Value<int>(),
                Temperature = current["temperature_2m"].Value<float>(),
                Sunrise = DateTime.Parse(sunriseArr[0].Value<string>(), CultureInfo.InvariantCulture),
                Sunset = DateTime.Parse(sunsetArr[0].Value<string>(), CultureInfo.InvariantCulture),
            };
        }
    }
}
