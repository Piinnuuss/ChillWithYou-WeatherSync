using Bulbul;

namespace MyWeatherSyncMod
{
    public static class WeatherMapper
    {
        /// <summary>
        /// 把 Open-Meteo 的 WMO weather_code 映射为“降水类”窗景。
        /// 返回 null 表示当前没有降水（晴 / 少云 / 阴 / 雾），此时必须关闭所有雨雪窗景。
        ///
        /// WMO 代码速查：
        ///   0  Clear sky            1  Mainly clear        2  Partly cloudy      3  Overcast
        ///  45/48 Fog
        ///  51/53/55 Drizzle        56/57 Freezing drizzle
        ///  61/63/65 Rain           66/67 Freezing rain
        ///  71/73/75 Snow fall      77 Snow grains
        ///  80/81/82 Rain showers
        ///  85/86 Snow showers
        ///  95 Thunderstorm         96/99 Thunderstorm with hail
        /// </summary>
        public static EnvironmentType? GetPrecipitation(int weatherCode)
        {
            switch (weatherCode)
            {
                // ---- 毛毛雨 / 冻雨 ----
                case 51: case 53: case 55:
                case 56: case 57:
                    return EnvironmentType.LightRain;

                // ---- 降雨 / 冻雨 ----
                case 61: case 63: case 65:
                case 66: case 67:
                    return EnvironmentType.HeavyRain;

                // ---- 阵雨（原来 80/81/82 全给了 HeavyRain，这里按强度区分）----
                case 80: return EnvironmentType.LightRain;
                case 81: case 82: return EnvironmentType.HeavyRain;

                // ---- 降雪 / 阵雪 ----
                case 71: case 73: case 75: case 77:
                case 85: case 86:
                    return EnvironmentType.Snow;

                // ---- 雷暴 ----
                case 95: case 96: case 99:
                    return EnvironmentType.ThunderRain;

                // 0/1/2/3（晴~阴）、45/48（雾）以及未知代码一律按“无降水”处理
                default:
                    return null;
            }
        }

        /// <summary>
        /// 该 weather_code 是否属于“阴天/多云/雾”类（真实天空发灰）。
        /// 注意：单独的阴天不应该覆盖用户手动选择的窗景（夜晚、樱花等），
        /// 只用于日志说明；真正的背景切换要配合“自动时间窗景是否开启”一起决定。
        /// </summary>
        public static bool IsCloudy(int weatherCode)
        {
            switch (weatherCode)
            {
                case 1: case 2: case 3:
                case 45: case 48:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>给日志用的可读描述（避免把所有 null 都打印成 "Clear" 造成误判）。</summary>
        public static string Describe(int weatherCode)
        {
            switch (weatherCode)
            {
                case 0: return "Clear";
                case 1: return "MainlyClear";
                case 2: return "PartlyCloudy";
                case 3: return "Overcast";
                case 45: case 48: return "Fog";
                case 51: case 53: case 55: case 56: case 57: return "Drizzle";
                case 61: case 63: case 65: case 66: case 67: return "Rain";
                case 71: case 73: case 75: case 77: return "Snow";
                case 80: case 81: case 82: return "RainShowers";
                case 85: case 86: return "SnowShowers";
                case 95: case 96: case 99: return "Thunderstorm";
                default: return "Unknown(" + weatherCode + ")";
            }
        }

        public static bool IsRain(EnvironmentType type)
        {
            return type == EnvironmentType.LightRain
                || type == EnvironmentType.HeavyRain
                || type == EnvironmentType.ThunderRain;
        }

        public static bool IsSnow(EnvironmentType type)
        {
            return type == EnvironmentType.Snow;
        }

        /// <summary>全部降水类窗景，用于状态查询和清理。</summary>
        public static readonly EnvironmentType[] AllPrecipitation =
        {
            EnvironmentType.LightRain,
            EnvironmentType.HeavyRain,
            EnvironmentType.ThunderRain,
            EnvironmentType.Snow,
        };

        public static EnvironmentType GetTimeEnvironment(TimeOfDay timeOfDay)
        {
            switch (timeOfDay)
            {
                case TimeOfDay.Morning:
                case TimeOfDay.Day:
                case TimeOfDay.Afternoon: return EnvironmentType.Day;
                case TimeOfDay.Dusk: return EnvironmentType.Sunset;
                case TimeOfDay.Night:
                case TimeOfDay.LateNight: return EnvironmentType.Night;
                default: return EnvironmentType.Day;
            }
        }
    }
}
