using BepInEx;
using BepInEx.Logging;
using Bulbul;
using System;
using System.Threading.Tasks;
using UnityEngine;
using VContainer;

namespace MyWeatherSyncMod
{
    [BepInPlugin("com.yourname.weathersync", "Real-Time Weather Sync", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private static WeatherFetcher _weatherFetcher;
        private static bool _loopStarted;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("=== AWAKE START ===");

            ConfigManager.Init(Config);
            _weatherFetcher = new WeatherFetcher(
                ConfigManager.Latitude.Value,
                ConfigManager.Longitude.Value
            );

            // 自动定位并更新配置
            if (ConfigManager.AutoLocate.Value)
                _ = TryAutoLocateAsync();

            if (!_loopStarted)
            {
                _loopStarted = true;
                SyncLoop();
                Log.LogInfo("Main-thread sync loop started.");
            }
            Log.LogInfo("=== AWAKE END ===");
        }

        private static async Task TryAutoLocateAsync()
        {
            var loc = await LocationFetcher.FetchAsync();
            if (loc.Success)
            {
                ConfigManager.Latitude.Value = loc.Latitude;
                ConfigManager.Longitude.Value = loc.Longitude;
                ConfigManager.Config.Save();
                _weatherFetcher = new WeatherFetcher(loc.Latitude, loc.Longitude);
                Log.LogInfo($"Auto-located: {loc.Latitude:F4}, {loc.Longitude:F4}");
            }
            else
            {
                Log.LogWarning("Could not auto-locate, using default/manual coordinates.");
            }
        }

        private static bool IsEnvironmentReady()
        {
            try
            {
                RoomLifetimeScope.Resolve<WindowViewService>();
                return true;
            }
            catch { return false; }
        }

        private static async void SyncLoop()
        {
            await Task.Delay(10000);
            Log.LogInfo("[SyncLoop] Detection loop started...");

            while (true)
            {
                try
                {
                    if (IsEnvironmentReady())
                    {
                        await SyncWeatherAsync();
                    }
                    else
                    {
                        Log.LogInfo("[SyncLoop] VContainer not ready (retry in 10s)");
                    }
                }
                catch (Exception ex) { Log.LogError($"[SyncLoop] Error: {ex}"); }

                int delayMs = IsEnvironmentReady()
                    ? Math.Max(ConfigManager.RefreshMinutes.Value, 5) * 60 * 1000
                    : 10000;
                await Task.Delay(delayMs);
            }
        }

        private static async Task SyncWeatherAsync()
        {
            var data = await _weatherFetcher.FetchAsync();
            var weatherEnv = WeatherMapper.GetWeatherEnvironment(data.WeatherCode);
            var now = DateTime.Now;

            // 判断当前时段
            var timeOfDay = TimeSyncManager.GetTimeOfDay(data.Sunrise, data.Sunset);
            bool isDayPeriod = timeOfDay == TimeOfDay.Morning || timeOfDay == TimeOfDay.Day || timeOfDay == TimeOfDay.Afternoon;
            bool isDusk = timeOfDay == TimeOfDay.Dusk;

            // 判断天气类型
            bool isRain = weatherEnv.HasValue && (WeatherMapper.IsRain(weatherEnv.Value));
            bool isSnow = weatherEnv.HasValue && WeatherMapper.IsSnow(weatherEnv.Value);

            // 决定是否强制多云背景
            bool forceCloudy = false;
            if (isDayPeriod && (isRain || isSnow)) forceCloudy = true;
            else if (isDusk && isSnow) forceCloudy = true;
            // 夜晚从不强制多云

            Log.LogInfo($"[Sync] Code:{data.WeatherCode} Temp:{data.Temperature}°C → Weather:{weatherEnv?.ToString() ?? "Clear"}");
            Log.LogInfo($"[Sync] TimeOfDay:{timeOfDay}, forceCloudy:{forceCloudy} (isDay:{isDayPeriod}, isDusk:{isDusk}, Rain:{isRain}, Snow:{isSnow})");

            // 应用天气（根据 forceCloudy 决定是否多云背景）
            if (ConfigManager.EnableWeatherSync.Value)
                EnvironmentApplier.ApplyPrecipitation(weatherEnv, forceCloudy);

            // 仅当不强制多云时（晴天、黄昏/夜晚下雨等），更新日出日落时间节点
            if (ConfigManager.EnableTimeSync.Value && !forceCloudy)
                UpdateTimeNodes(data.Sunrise, data.Sunset);

            Log.LogInfo("[Sync] Done.");
        }

        private static void UpdateTimeNodes(DateTime sunrise, DateTime sunset)
        {
            try
            {
                var saveData = SaveDataManager.Instance;
                var timeData = saveData.AutoTimeWindowChangeData;

                float dayStart = (float)sunrise.Hour + (float)sunrise.Minute / 60f;
                float sunsetStart = (float)sunset.Hour + (float)sunset.Minute / 60f;
                float nightStart = sunsetStart + 0.5f;
                if (nightStart >= 24f) nightStart -= 24f;

                timeData.TimeDayStart = dayStart;
                timeData.TimeSunsetStart = sunsetStart;
                timeData.TimeNightStart = nightStart;
                saveData.SaveAutoTimeWindowChangeData();
                Plugin.Log.LogInfo($"Time nodes updated: Day {dayStart:F2}h, Sunset {sunsetStart:F2}h, Night {nightStart:F2}h.");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"Update time nodes failed: {ex.Message}");
            }
        }

        private void OnDestroy() => Log.LogInfo("Plugin OnDestroy – loop continues.");
    }
}