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
        public static ConfigEntry<string> City;
        public static ConfigEntry<bool> PreferFullWeather;
        public static ConfigEntry<int> WeatherLookAheadHours;
        public static ConfigEntry<double> WeatherGridRadius;

        /// <summary>
        /// 内部状态：下雨期间插件临时接管了窗景，需要知道"原来该不该开自动时间"。
        /// 持久化是为了避免游戏在降雨期间被强制结束/崩溃后，无法还原玩家设置。
        /// 不是给用户改的选项。
        /// </summary>
        public static ConfigEntry<bool> WeatherTookOverWindow;
        public static ConfigEntry<bool> AutoTimeWasOnBeforeRain;

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
                "Automatically detect location using IP. Disable to use manual coordinates. " +
                "NOTE: IP-based location is often wrong on carrier/mobile networks " +
                "(it may resolve to a neighbouring city). Setting City below is more accurate.");
            City = cfg.Bind("Location", "City", "",
                "RECOMMENDED. Your city name, e.g. \"Jiangyin\" or \"江阴\". " +
                "Resolved to exact coordinates via Open-Meteo geocoding and takes priority " +
                "over AutoLocate. Leave empty to fall back to IP-based detection.");
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

            // 内部状态，不是用户选项
            WeatherTookOverWindow = cfg.Bind("Internal", "WeatherTookOverWindow", false,
                "INTERNAL STATE - do not edit. True while the plugin has taken over the window " +
                "view because of rain/snow, and must hand it back when precipitation ends.");
            AutoTimeWasOnBeforeRain = cfg.Bind("Internal", "AutoTimeWasOnBeforeRain", true,
                "INTERNAL STATE - do not edit. Remembered value of the game's 'auto time window' " +
                "switch, so it can be restored exactly as the player had it.");
        }

        /// <summary>把当前配置写回磁盘（自动定位成功后调用）。</summary>
        public static void Save()
        {
            if (_config != null)
                _config.Save();
        }
    }
}
