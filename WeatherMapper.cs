using Bulbul;

namespace MyWeatherSyncMod
{
    public static class WeatherMapper
    {
        /// <summary>根据 WMO 天气码返回游戏天气环境（无降水时返回 null）</summary>
        public static EnvironmentType? GetWeatherEnvironment(int weatherCode)
        {
            switch (weatherCode)
            {
                case 0: return null;                    // 晴
                case 1: case 2: case 3: return null;                    // 多云（只换背景，不加天气）
                case 51: case 53: case 55: return EnvironmentType.LightRain;
                case 61: case 63: case 65: return EnvironmentType.HeavyRain;
                case 71: case 73: case 75: return EnvironmentType.Snow;
                case 80: case 81: case 82: return EnvironmentType.HeavyRain; // 阵雨当大雨
                case 85: case 86: return EnvironmentType.Snow;      // 阵雪当雪
                case 95: case 96: case 99: return EnvironmentType.ThunderRain;
                default: return null;
            }
        }

        /// <summary>是否需要强制多云背景（雨、雪、阴天）</summary>
        public static bool ShouldForceCloudy(int weatherCode)
        {
            // 除了晴天(0)外，其他天气都最好用多云背景
            return weatherCode != 0;
        }

        /// <summary>根据时段返回时间背景（仅在无降水时使用）</summary>
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