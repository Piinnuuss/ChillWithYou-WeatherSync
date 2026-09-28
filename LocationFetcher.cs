using System;
using System.Text;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace MyWeatherSyncMod
{
    public class LocationResult
    {
        public double Latitude;
        public double Longitude;
        public bool Success;

        /// <summary>定位来源，用于日志（例如 "城市名 geocoding" / "IP:ipapi.is"）。</summary>
        public string Source = "";

        /// <summary>地名（城市名解析时优先填英文名，保证与用户填写的名字一致）。</summary>
        public string PlaceName = "";

        /// <summary>省 / 州（中文，仅城市名解析时有）。</summary>
        public string Admin1 = "";

        /// <summary>地级市（中文，仅城市名解析时有）。</summary>
        public string Admin2 = "";

        /// <summary>国家（中文，仅城市名解析时有）。</summary>
        public string Country = "";

        /// <summary>组装成 "[Jiangyin（中国 江苏 无锡市）]" 这样的展示串。</summary>
        public string BuildDisplay()
        {
            if (string.IsNullOrEmpty(PlaceName)) return "";

            var where = new List<string>();
            if (!string.IsNullOrEmpty(Country)) where.Add(Country);
            if (!string.IsNullOrEmpty(Admin1)) where.Add(Admin1);
            if (!string.IsNullOrEmpty(Admin2) && Admin2 != PlaceName && Admin2 != Admin1)
                where.Add(Admin2);

            return where.Count > 0
                ? PlaceName + "（" + string.Join(" ", where.ToArray()) + "）"
                : PlaceName;
        }
    }

    /// <summary>
    /// 位置解析。优先用配置里的城市名做地理编码，失败才退回 IP 定位。
    ///
    /// 【为什么需要城市名】
    ///   实测（江苏江阴，中国移动宽带，IP 183.211.133.86）：
    ///     ip-api.com  -> 南京 32.0607,118.7630   偏差约 150km
    ///     ipinfo.io   -> 上海 31.2222,121.4581   偏差约 130km
    ///     ipapi.is    -> 无锡 31.5694,120.2888   偏差约  40km
    ///   三家给出三个城市 —— 运营商共享出口 IP 的定位天生不可靠，
    ///   再怎么换服务也只能做到"附近城市"。
    ///   而 Open-Meteo 自己的地理编码按城市名查询是精确的：
    ///     "Jiangyin" -> 澄江 31.91102,120.26302
    ///
    /// 【多源 IP 兜底】
    ///   IP 服务之间差异很大，逐个尝试并取第一个成功的结果；
    ///   数值完全相同的（同一上游数据源）会被去重，并在日志里标出。
    ///
    /// 网络请求由 WeatherSyncRunner 用 UnityWebRequest 协程发起，不阻塞主线程。
    /// </summary>
    public static class LocationFetcher
    {
        public class Endpoint
        {
            public string Name;
            public string Url;
            public Func<JObject, double?> GetLat;
            public Func<JObject, double?> GetLon;
            public Func<JObject, string> GetPlace;

            /// <summary>返回 null 表示该响应不是有效的位置数据（含 status:fail）。</summary>
            public bool TryParse(string json, out LocationResult result)
            {
                result = null;
                JObject obj;
                try { obj = JObject.Parse(json); }
                catch { return false; }

                // ip-api 用 status:fail 表示失败，此时 lat/lon 仍为 0 —— 必须排除
                var status = obj["status"];
                if (status != null && status.Type == JTokenType.String &&
                    status.Value<string>() == "fail")
                    return false;

                var lat = GetLat(obj);
                var lon = GetLon(obj);
                if (lat == null || lon == null) return false;
                if (lat.Value < -90 || lat.Value > 90 || lon.Value < -180 || lon.Value > 180)
                    return false;
                if (lat.Value == 0.0 && lon.Value == 0.0) return false;

                string place = "";
                try { place = GetPlace != null ? GetPlace(obj) : ""; } catch { }

                result = new LocationResult
                {
                    Latitude = lat.Value,
                    Longitude = lon.Value,
                    Success = true,
                    PlaceName = place ?? "",
                };
                return true;
            }
        }

        private static double? D(JToken t)
        {
            if (t == null || t.Type == JTokenType.Null) return null;
            try { return t.Value<double>(); } catch { return null; }
        }

        public static readonly Endpoint[] IpEndpoints =
        {
            // ipapi.is: 实测在中国移动网络下给到"附近城市"最接近的一个（无锡 vs 江阴 ~40km），
            // 字段是根级 lat/lon（不是嵌套的 location 对象）。
            new Endpoint {
                Name = "ipapi.is",
                Url  = "https://api.ipapi.is/",
                GetLat = o => D(o["lat"]),
                GetLon = o => D(o["lon"]),
                GetPlace = o => { var c = o["city"]; return c != null ? c.Value<string>() : ""; },
            },
            new Endpoint {
                Name = "ipwho.is",
                Url  = "https://ipwho.is/",
                GetLat = o => D(o["latitude"]),
                GetLon = o => D(o["longitude"]),
                GetPlace = o => { var c = o["city"]; return c != null ? c.Value<string>() : ""; },
            },
            new Endpoint {
                Name = "ip-api.com",
                Url  = "http://ip-api.com/json/?fields=status,lat,lon,city",
                GetLat = o => D(o["lat"]),
                GetLon = o => D(o["lon"]),
                GetPlace = o => { var c = o["city"]; return c != null ? c.Value<string>() : ""; },
            },
        };

        /// <summary>IP 服务的基础地址（供插件配置项展示用）。</summary>
        public static string DescribeEndpoints()
        {
            var sb = new StringBuilder();
            foreach (var e in IpEndpoints)
            {
                if (sb.Length > 0) sb.Append(" -> ");
                sb.Append(e.Name);
            }
            return sb.ToString();
        }

        /// <summary>按城市名解析精确坐标（Open-Meteo 地理编码）。中文版：层级用中文。</summary>
        public static string BuildCityUrl(string city)
        {
            return "https://geocoding-api.open-meteo.com/v1/search?name=" +
                   UnityWebRequest.EscapeURL(city) + "&count=1&language=zh&format=json";
        }

        /// <summary>英文版：地名用英文，避免用中文名显示时对不上用户填的名字。</summary>
        public static string BuildCityUrlEnglish(string city)
        {
            return "https://geocoding-api.open-meteo.com/v1/search?name=" +
                   UnityWebRequest.EscapeURL(city) + "&count=1&format=json";
        }

        /// <summary>只取第一条结果的英文地名与坐标。</summary>
        public static bool TryParseCityName(string json, out string name, out double lat, out double lon)
        {
            name = "";
            lat = 0;
            lon = 0;
            try
            {
                var obj = JObject.Parse(json);
                var results = obj["results"] as JArray;
                if (results == null || results.Count == 0) return false;

                var first = results[0];
                var la = D(first["latitude"]);
                var lo = D(first["longitude"]);
                if (la == null || lo == null) return false;

                name = first["name"] != null ? first["name"].Value<string>() : "";
                lat = la.Value;
                lon = lo.Value;
                return true;
            }
            catch { return false; }
        }

        /// <summary>只取第一条结果的中文层级（省 / 地级市）。</summary>
        public static bool TryParseCityChineseAdmins(string json, out string admin1, out string admin2)
        {
            admin1 = "";
            admin2 = "";
            try
            {
                var obj = JObject.Parse(json);
                var results = obj["results"] as JArray;
                if (results == null || results.Count == 0) return false;

                var first = results[0];
                admin1 = first["admin1"] != null ? first["admin1"].Value<string>() : "";
                admin2 = first["admin2"] != null ? first["admin2"].Value<string>() : "";
                return true;
            }
            catch { return false; }
        }

        public static bool TryParseCity(string json, out LocationResult result)
        {
            result = null;
            try
            {
                var obj = JObject.Parse(json);
                var results = obj["results"] as JArray;
                if (results == null || results.Count == 0) return false;

                var first = results[0];
                var lat = D(first["latitude"]);
                var lon = D(first["longitude"]);
                if (lat == null || lon == null) return false;

                var nameZh = first["name"] != null ? first["name"].Value<string>() : "";
                var admin1 = first["admin1"] != null ? first["admin1"].Value<string>() : "";
                var admin2 = first["admin2"] != null ? first["admin2"].Value<string>() : "";
                var country = first["country"] != null ? first["country"].Value<string>() : "";

                // language=zh 时 name 可能是下一级地名（如 Jiangyin -> 「澄江」），
                // 这里保留它作为回退；调用方通常会用英文版覆盖成"和你填的名字一致"的地名。
                result = new LocationResult
                {
                    Latitude = lat.Value,
                    Longitude = lon.Value,
                    Success = true,
                    Source = "城市名 geocoding",
                    PlaceName = nameZh,
                };
                result.Admin1 = admin1;
                result.Admin2 = admin2;
                result.Country = country;
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// 依次尝试各 IP 服务，返回第一个成功的结果。
        /// 同时报告是否与其他服务结果重复（同一数据源）。
        /// </summary>
        public static bool SelectBestIpResult(
            List<KeyValuePair<string, LocationResult>> candidates,
            out LocationResult best, out string note)
        {
            best = null;
            note = "";

            var seen = new List<string>();
            foreach (var kv in candidates)
            {
                var r = kv.Value;
                if (r == null || !r.Success) continue;

                string key = r.Latitude.ToString("F3") + "," + r.Longitude.ToString("F3");
                if (seen.Contains(key))
                {
                    note += $"{kv.Key} 与前面结果相同({key})，忽略；";
                    continue;
                }
                seen.Add(key);

                if (best == null)
                {
                    best = r;
                    best.Source = "IP:" + kv.Key;
                }
            }

            if (best == null && seen.Count > 0)
                note += "所有 IP 服务返回同一坐标；";

            return best != null;
        }
    }
}
