using Bulbul;

namespace MyWeatherSyncMod
{
    public static class WeatherMapper
    {
        /// <summary>
        /// 把 Open-Meteo 的 WMO weather_code 映射为“降水类”窗景。
        /// 返回 null 表示当前没有降水（晴 / 少云 / 阴 / 雾），此时必须关闭所有雨雪窗景。
        ///
        /// 【重要】游戏里雨的窗景只有 3 个（已由 WindowViewService 反编译确认）：
        ///     WindowViewType 6 = LightRain   → 小雨
        ///     WindowViewType 7 = HeavyRain   → 雨（游戏没有单独的“大雨/暴雨”窗景）
        ///     WindowViewType 8 = ThunderRain → 雷雨
        ///   所以 WMO 的 6 档雨强必须【归并】进这 3 个桶，
        ///   “大雨(65)”“暴雨(82)”都进 HeavyRain，不会各自变成一个窗景。
        ///
        /// 归并规则：
        ///   小雨  : 51/53/55 毛毛雨、56/57 冻雨、80 小阵雨
        ///   雨    : 61/63/65 雨（含大雨）、66/67 冻雨、81/82 阵雨（含暴雨）
        ///   雷雨  : 95/96/99 雷暴
        ///   雪    : 71/73/75/77 降雪、85/86 阵雪
        ///   无降水: 0/1/2/3 晴~阴、45/48 雾
        ///
        /// WMO 代码速查：
        ///   0  Clear sky            1  Mainly clear        2  Partly cloudy      3  Overcast
        ///  45/48 Fog
        ///  51/53/55 Drizzle        56/57 Freezing drizzle
        ///  61/63/65 Rain (61 小 / 63 中 / 65 大)
        ///  66/67 Freezing rain
        ///  71/73/75 Snow fall      77 Snow grains
        ///  80/81/82 Rain showers (80 小 / 81 中 / 82 暴)
        ///  85/86 Snow showers
        ///  95 Thunderstorm         96/99 Thunderstorm with hail
        /// </summary>
        public static EnvironmentType? GetPrecipitation(int weatherCode)
        {
            switch (weatherCode)
            {
                // ---- 小雨：毛毛雨 / 冻雨 / 小阵雨 ----
                case 51: case 53: case 55:
                case 56: case 57:
                case 80:
                    return EnvironmentType.LightRain;

                // ---- 雨：降雨(含大雨) / 冻雨 / 阵雨(含暴雨) ----
                case 61: case 63: case 65:
                case 66: case 67:
                case 81: case 82:
                    return EnvironmentType.HeavyRain;

                // ---- 雪：降雪 / 阵雪 ----
                case 71: case 73: case 75: case 77:
                case 85: case 86:
                    return EnvironmentType.Snow;

                // ---- 雷雨：雷暴 ----
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

        /// <summary>
        /// 给日志用的可读描述。直接标出归并后的游戏窗景，
        /// 这样日志里出现“雨”时能一眼看出它是哪几档 WMO 合并来的。
        /// </summary>
        public static string Describe(int weatherCode)
        {
            switch (weatherCode)
            {
                case 0: return "Clear";
                case 1: return "MainlyClear";
                case 2: return "PartlyCloudy";
                case 3: return "Overcast";
                case 45: case 48: return "Fog";

                // 小雨
                case 51: return "Drizzle(小雨)";
                case 53: return "Drizzle(小雨)";
                case 55: return "Drizzle(小雨)";
                case 56: return "FreezingDrizzle(小雨)";
                case 57: return "FreezingDrizzle(小雨)";
                case 80: return "RainShowers-slight(小雨)";

                // 雨（含大雨/暴雨，全部归到同一个窗景）
                case 61: return "Rain-slight(雨)";
                case 63: return "Rain-moderate(雨)";
                case 65: return "Rain-heavy(雨)";
                case 66: return "FreezingRain(雨)";
                case 67: return "FreezingRain(雨)";
                case 81: return "RainShowers-moderate(雨)";
                case 82: return "RainShowers-violent(雨)";

                // 雪
                case 71: return "Snow-slight(雪)";
                case 73: return "Snow-moderate(雪)";
                case 75: return "Snow-heavy(雪)";
                case 77: return "SnowGrains(雪)";
                case 85: return "SnowShowers(雪)";
                case 86: return "SnowShowers(雪)";

                // 雷雨
                case 95: return "Thunderstorm(雷雨)";
                case 96: return "Thunderstorm-hail(雷雨)";
                case 99: return "Thunderstorm-hail(雷雨)";

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
    }
}
