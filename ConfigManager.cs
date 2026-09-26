using BepInEx.Configuration;

namespace MyWeatherSyncMod
{
    public static class ConfigManager
    {
        private static ConfigFile _config;

        public static ConfigEntry<bool> EnableWeatherSync;
        public static ConfigEntry<bool> EnableTimeSync;
        public static ConfigEntry<double> Latitude;
        public static ConfigEntry<double> Longitude;
        public static ConfigEntry<int> RefreshMinutes;
        public static ConfigEntry<bool> AutoLocate;
        public static ConfigEntry<bool> PreferFullWeather;
        public static ConfigEntry<int> WeatherLookAheadHours;
        public static ConfigEntry<double> WeatherGridRadius;

        public static void Init(ConfigFile cfg)
        {
            _config = cfg;

            EnableWeatherSync = cfg.Bind("General", "EnableWeatherSync", true,
                "Enable real-time weather synchronization");
            EnableTimeSync = cfg.Bind("General", "EnableTimeSync", true,
                "Enable day/night cycle based on local time");
            PreferFullWeather = cfg.Bind("General", "PreferFullWeather", true,
                "If true, rain/snow window visuals will be activated (replaces custom window views). If false, only cloudy background + sound.");
            Latitude = cfg.Bind("Location", "Latitude", WeatherFetcher.DefaultLat,
                "Your latitude (-90 to 90)");
            Longitude = cfg.Bind("Location", "Longitude", WeatherFetcher.DefaultLon,
                "Your longitude (-180 to 180)");
            AutoLocate = cfg.Bind("Location", "AutoLocate", true,
                "Automatically detect location using IP. Disable to use manual coordinates.");
            RefreshMinutes = cfg.Bind("Update", "RefreshMinutes", 30,
                "Weather refresh interval in minutes (minimum 5)");
            WeatherLookAheadHours = cfg.Bind("Update", "WeatherLookAheadHours", 3,
                new ConfigDescription(
                    "How many hours ahead to look when deciding whether it is raining. " +
                    "Prevents missing intermittent rain that falls between two samples. " +
                    "0 = judge only by the current hour. Range 0-24.",
                    new AcceptableValueRange<int>(0, 24)));
            WeatherGridRadius = cfg.Bind("Update", "WeatherGridRadius", 0.09,
                new ConfigDescription(
                    "Radius (in degrees) of the sampling grid around your coordinate. " +
                    "0.09 deg is about 10km. Compensates for IP-based location error; " +
                    "rain is treated as active if ANY point in the grid reports it. " +
                    "0 = query the single coordinate only.",
                    new AcceptableValueRange<double>(0.0, 1.0)));
        }

        /// <summary>把当前配置写回磁盘（自动定位成功后调用）。</summary>
        public static void Save()
        {
            if (_config != null)
                _config.Save();
        }
    }
}
