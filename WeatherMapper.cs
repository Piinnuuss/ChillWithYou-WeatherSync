using Bulbul;

namespace MyWeatherSyncMod
{
    public static class WeatherMapper
    {
        public static EnvironmentType? GetWeatherEnvironment(int weatherCode)
        {
            switch (weatherCode)
            {
                case 0: return null;
                case 1: case 2: case 3: return null; // 多云/阴（只换背景，不加天气）
                case 51: case 53: case 55: return EnvironmentType.LightRain;
                case 61: case 63: case 65: return EnvironmentType.HeavyRain;
                case 71: case 73: case 75: return EnvironmentType.Snow;
                case 80: case 81: case 82: return EnvironmentType.HeavyRain;
                case 85: case 86: return EnvironmentType.Snow;
                case 95: case 96: case 99: return EnvironmentType.ThunderRain;
                default: return null;
            }
        }

        public static bool IsRain(EnvironmentType type)
        {
            return type == EnvironmentType.LightRain || type == EnvironmentType.HeavyRain || type == EnvironmentType.ThunderRain;
        }

        public static bool IsSnow(EnvironmentType type)
        {
            return type == EnvironmentType.Snow;
        }

        /// <summary>是否需要强制多云背景</summary>
        public static bool ShouldForceCloudy(int weatherCode)
        {
            return weatherCode != 0;
        }

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