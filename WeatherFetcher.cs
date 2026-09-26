using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MyWeatherSyncMod
{
    public class WeatherData
    {
        /// <summary>用于映射窗景的天气码（经窗口 + 网格判定，0 = 确认无降水）。</summary>
        public int WeatherCode;

        public float Temperature;
        public DateTime Sunrise;
        public DateTime Sunset;

        /// <summary>判定所依据的采样点/小时摘要，用于日志追溯。</summary>
        public string SampledHours = "";

        /// <summary>本次参与判定的采样点数量。</summary>
        public int SamplePointCount;

        /// <summary>判定窗口内的最大单点降水量（mm）。</summary>
        public double WindowPrecipitation;

        /// <summary>判定窗口内的最大降水概率（%），无数据时为 -1。</summary>
        public int WindowPrecipProbability = -1;

        /// <summary>窗口内是否出现降水（由降水量或概率判定）。</summary>
        public bool WindowHasPrecipitation;
    }

    /// <summary>
    /// URL 构造 + JSON 解析 + 天气判定，不含任何 Unity / 网络依赖。
    /// 实际请求由 WeatherSyncRunner 用 UnityWebRequest 协程发起。
    ///
    /// 为什么不直接用 current.weather_code（两个原因，都实测过）：
    ///
    /// 1. current 是「瞬时快照」。雨是间歇性的，而插件默认每 30 分钟才采样一次，
    ///    很容易正好落在两场雨的间隙里。实测 12:48 时 current.weather_code=3(阴)、
    ///    precipitation=0，但 hourly 显示 15:00 起有降水 —— 只看瞬时值必然漏判。
    ///    → 改为在 hourly 序列上取窗口（前 1 小时 ~ 后 N 小时）。
    ///
    /// 2. IP 定位的坐标本身有误差。实测插件拿到的 31.3093,120.6020 与江阴市中心
    ///    31.92,120.28 相差约 70km，同一时刻气温 31.5°C vs 26.1°C、降水 0.00mm vs 0.70mm
    ///    —— 在错的点上查天气，再怎么判也是错的。
    ///    → 改为在坐标周围取 3×3 网格（Open-Meteo 支持一次请求多个坐标），
    ///      任一点判定为降水即按降水处理。温度仍取中心点。
    /// </summary>
    public class WeatherFetcher
    {
        public const double DefaultLat = 31.3093;
        public const double DefaultLon = 120.6020;

        /// <summary>判定窗口：当前小时往前取几小时（回看，避免雨刚停就立刻关掉）。</summary>
        public const int LookBackHours = 1;

        /// <summary>窗口内单小时降水量达到该值即视为「这一小时在下」。</summary>
        private const double WetHourMm = 0.1;

        /// <summary>降水概率达到该值即视为「大概率在下」。</summary>
        private const int HighProbability = 60;

        public readonly double Latitude;
        public readonly double Longitude;
        public readonly int LookAheadHours;

        /// <summary>采样网格半径（度）。0.09° ≈ 10km。设为 0 则只查中心点。</summary>
        public readonly double GridRadius;

        public WeatherFetcher(double lat, double lon, int lookAheadHours = 3, double gridRadius = 0.09)
        {
            // 防止配置被写成 0,0（几内亚湾）或非法值
            Latitude = (lat >= -90.0 && lat <= 90.0 && lat != 0.0) ? lat : DefaultLat;
            Longitude = (lon >= -180.0 && lon <= 180.0 && lon != 0.0) ? lon : DefaultLon;
            LookAheadHours = Math.Max(0, Math.Min(lookAheadHours, 24));
            GridRadius = Math.Max(0.0, Math.Min(gridRadius, 1.0));
        }

        /// <summary>中心点 + 周围 8 点；GridRadius=0 时只有中心点。</summary>
        public List<double[]> BuildSamplePoints()
        {
            var pts = new List<double[]>();
            if (GridRadius <= 0.0)
            {
                pts.Add(new[] { Latitude, Longitude });
                return pts;
            }

            // 经度方向按 cos(lat) 校正，保证网格在东西向也是等距的
            double dLon = GridRadius / Math.Max(0.2, Math.Cos(Latitude * Math.PI / 180.0));
            foreach (double dLat in new[] { -GridRadius, 0.0, GridRadius })
                foreach (double dLonOff in new[] { -dLon, 0.0, dLon })
                {
                    double la = Math.Max(-90.0, Math.Min(90.0, Latitude + dLat));
                    double lo = Longitude + dLonOff;
                    if (lo > 180.0) lo -= 360.0;
                    if (lo < -180.0) lo += 360.0;
                    pts.Add(new[] { la, lo });
                }
            return pts;
        }

        public string BuildUrl()
        {
            var pts = BuildSamplePoints();
            var lats = new List<string>();
            var lons = new List<string>();
            foreach (var p in pts)
            {
                lats.Add(p[0].ToString("F4", CultureInfo.InvariantCulture));
                lons.Add(p[1].ToString("F4", CultureInfo.InvariantCulture));
            }

            // 不用 past_days：否则 daily.sunrise[0] 会变成昨天
            return "https://api.open-meteo.com/v1/forecast?" +
                   "latitude=" + string.Join(",", lats.ToArray()) +
                   "&longitude=" + string.Join(",", lons.ToArray()) +
                   "&current=temperature_2m" +
                   "&hourly=weather_code,precipitation,precipitation_probability" +
                   "&daily=sunrise,sunset" +
                   "&timezone=auto&forecast_days=3";
        }

        /// <summary>解析 Open-Meteo 的 JSON。字段缺失时抛异常，由调用方记录并跳过本轮。</summary>
        public WeatherData Parse(string json)
        {
            var parsed = JToken.Parse(json);
            var points = parsed as JArray;
            if (points == null)
            {
                // 只查了一个坐标时，Open-Meteo 返回对象而不是数组
                var single = new JArray();
                single.Add(parsed);
                points = single;
            }
            if (points.Count == 0)
                throw new InvalidOperationException("Open-Meteo 返回了空的坐标列表。");

            var data = new WeatherData { SamplePointCount = points.Count };

            // 温度取「离请求坐标最近的那个采样点」——Open-Meteo 会吸附到最近的网格，
            // 且返回顺序不保证，所以按距离匹配而不是写死索引。
            var center = FindClosestPoint(points);
            if (center == null)
                throw new InvalidOperationException("Open-Meteo 返回的采样点无法解析。");

            var daily = center["daily"];
            if (daily == null)
                throw new InvalidOperationException("Open-Meteo 响应缺少 'daily' 字段。");
            var sunriseArr = daily["sunrise"] as JArray;
            var sunsetArr = daily["sunset"] as JArray;
            if (sunriseArr == null || sunsetArr == null || sunriseArr.Count == 0 || sunsetArr.Count == 0)
                throw new InvalidOperationException("Open-Meteo 响应缺少 sunrise/sunset。");
            data.Sunrise = DateTime.Parse(sunriseArr[0].Value<string>(), CultureInfo.InvariantCulture);
            data.Sunset = DateTime.Parse(sunsetArr[0].Value<string>(), CultureInfo.InvariantCulture);

            var cur = center["current"];
            if (cur != null && cur["temperature_2m"] != null)
                data.Temperature = cur["temperature_2m"].Value<float>();

            // timezone=auto 返回的时间字符串就是当地时间，直接与本地"当前小时"对齐
            var now = DateTime.Now;
            var currentHour = new DateTime(now.Year, now.Month, now.Day, now.Hour, 0, 0);
            var from = currentHour.AddHours(-LookBackHours);
            var to = currentHour.AddHours(LookAheadHours);

            int worstCode = -1;
            bool anyPrecip = false;
            double maxWindowPrecip = 0.0;
            int maxProb = -1;
            int matched = 0;
            var sampled = new List<string>();

            foreach (var pt in points)
            {
                var hourly = pt["hourly"] as JObject;
                if (hourly == null) continue;

                var times = hourly["time"] as JArray;
                var codes = hourly["weather_code"] as JArray;
                if (times == null || codes == null) continue;

                var precip = hourly["precipitation"] as JArray;
                var probs = hourly["precipitation_probability"] as JArray;

                double pointPrecip = 0.0;
                int pointProb = -1;
                var pointHours = new List<string>();

                for (int i = 0; i < times.Count && i < codes.Count; i++)
                {
                    DateTime t;
                    if (!DateTime.TryParse(times[i].Value<string>(), CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out t))
                        continue;
                    if (t < from || t > to) continue;

                    matched++;

                    int code = (codes[i] != null && codes[i].Type != JTokenType.Null)
                        ? codes[i].Value<int>() : -1;

                    double mm = 0.0;
                    if (precip != null && i < precip.Count && precip[i].Type != JTokenType.Null)
                        mm = precip[i].Value<double>();

                    int prob = -1;
                    if (probs != null && i < probs.Count && probs[i].Type != JTokenType.Null)
                        prob = probs[i].Value<int>();

                    pointPrecip += mm;
                    if (prob > pointProb) pointProb = prob;
                    if (prob > maxProb) maxProb = prob;

                    // 任一点、任一小时满足条件即视为在下
                    if (mm >= WetHourMm || (prob >= 0 && prob >= HighProbability))
                        anyPrecip = true;

                    if (code >= 0 && code > worstCode) worstCode = code;

                    if (t == currentHour)
                        pointHours.Add($"{t:HH时}*{code}/{mm:0.#}mm/{prob}%");
                }

                if (pointPrecip > maxWindowPrecip) maxWindowPrecip = pointPrecip;

                if (pointHours.Count > 0)
                    sampled.Add($"#{sampled.Count + 1} " + string.Join(" ", pointHours.ToArray()));
            }

            if (matched == 0)
                throw new InvalidOperationException(
                    $"hourly 数据中找不到当前小时 {currentHour:yyyy-MM-ddTHH:mm}（共 {points.Count} 个采样点）。");

            data.SampledHours = string.Join(" | ", sampled.ToArray());
            data.WindowPrecipitation = maxWindowPrecip;
            data.WindowPrecipProbability = maxProb;
            data.WindowHasPrecipitation = anyPrecip;
            data.WeatherCode = Classify(worstCode, anyPrecip, maxWindowPrecip);

            return data;
        }

        /// <summary>找出离请求坐标最近的采样点（用于取温度，避免被网格角落的极值影响）。</summary>
        private JToken FindClosestPoint(JArray points)
        {
            JToken best = null;
            double bestDist = double.MaxValue;

            foreach (var pt in points)
            {
                var laTok = pt["latitude"];
                var loTok = pt["longitude"];
                if (laTok == null || loTok == null) continue;

                double la = laTok.Value<double>();
                double lo = loTok.Value<double>();

                // 网格尺度很小，用平面近似足够
                double dLat = la - Latitude;
                double dLon = (lo - Longitude) * Math.Cos(Latitude * Math.PI / 180.0);
                double dist = dLat * dLat + dLon * dLon;

                if (dist < bestDist) { bestDist = dist; best = pt; }
            }

            return best;
        }

        /// <summary>综合判定：天气码 或 降水量/概率 任一指向降水，就按降水处理。</summary>
        private static int Classify(int worstCode, bool anyPrecip, double windowPrecip)
        {
            if (worstCode >= 0 && WeatherMapper.GetPrecipitation(worstCode).HasValue)
                return worstCode;

            if (!anyPrecip)
                return 0;

            // 天气码说没有降水，但降水量/概率指向降水 → 按降水量给一个保守的雨类
            return windowPrecip >= 2.0 ? 63 : 51;
        }

        /// <summary>给日志用的可读摘要。</summary>
        public static string DescribeWindow(WeatherData d)
        {
            var prob = d.WindowPrecipProbability >= 0 ? d.WindowPrecipProbability + "%" : "n/a";
            return $"{d.SamplePointCount}点网格 | 当前小时 {d.SampledHours} | " +
                   $"最大降水 {d.WindowPrecipitation:0.#}mm 概率 {prob}";
        }
    }
}
