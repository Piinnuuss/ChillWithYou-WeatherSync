using System;
using System.Collections;
using Bulbul;
using UnityEngine;
using UnityEngine.Networking;
using VContainer;

namespace MyWeatherSyncMod
{
    /// <summary>
    /// 天气同步循环。
    ///
    /// 为什么不用 async/await + Task.Delay：
    ///   BepInEx 会在 chainloader 启动完成后销毁插件自己的 GameObject（日志里那句
    ///   "Plugin OnDestroy"），旧版 async void 循环之所以还能跑，只是因为它挂在
    ///   线程池的续体上、脱离了 GameObject 生命周期 —— 属于巧合而非设计。
    ///   一旦依赖 Unity 同步上下文（或像上一版那样在 OnDestroy 里取消 token），
    ///   循环就会在第一帧延迟结束前直接死掉。
    ///
    /// 现在的做法：
    ///   · 把循环挂在自建的 DontDestroyOnLoad GameObject 上，用协程驱动；
    ///   · 所有游戏 API 调用都发生在 Unity 主线程（协程天然保证）；
    ///   · 等待用 WaitForSecondsRealtime，不受 Time.timeScale 影响；
    ///   · 网络用 UnityWebRequest，本身非阻塞，不会卡主线程。
    /// </summary>
    public class WeatherSyncRunner : MonoBehaviour
    {
        private const int StartupDelaySeconds = 10;
        private const int RetryDelaySeconds = 10;
        private const int MinRefreshSeconds = 60;
        private const int NetworkTimeoutSeconds = 15;

        public static WeatherSyncRunner Create()
        {
            var go = new GameObject("WeatherSyncRunner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.hideFlags = HideFlags.HideAndDontSave;
            return go.AddComponent<WeatherSyncRunner>();
        }

        public void Begin()
        {
            StartCoroutine(RunLoop());
        }

        private IEnumerator RunLoop()
        {
            Plugin.Log.LogInfo($"[SyncLoop] 循环已启动，{StartupDelaySeconds}s 后开始检测……");

            yield return new WaitForSecondsRealtime(StartupDelaySeconds);

            while (true)
            {
                bool ready = false;

                try
                {
                    ready = IsEnvironmentReady();
                }
                catch (Exception ex)
                {
                    Plugin.Log.LogError($"[SyncLoop] 环境检测异常: {ex}");
                }

                if (ready)
                {
                    yield return RunOnce();
                }
                else
                {
                    Plugin.Log.LogInfo($"[SyncLoop] VContainer 尚未就绪，{RetryDelaySeconds}s 后重试……");
                }

                int waitSeconds = ready
                    ? Math.Max(ConfigManager.RefreshMinutes.Value, 5) * 60
                    : RetryDelaySeconds;
                if (waitSeconds < MinRefreshSeconds) waitSeconds = MinRefreshSeconds;

                yield return new WaitForSecondsRealtime(waitSeconds);
            }
        }

        private IEnumerator RunOnce()
        {
            var fetcher = CreateFetcher();
            string url = fetcher.BuildUrl();

            string body = null;
            string error = null;

            // 用 using + 手动 MoveNext：走到 finally 时 UnityWebRequest 一定已释放
            using (var req = UnityWebRequest.Get(url))
            {
                req.timeout = NetworkTimeoutSeconds;
                yield return req.SendWebRequest();

                // Unity 2022.3：统一用 result 判断（isNetworkError/isHttpError 已过时）
                bool failed = req.result != UnityWebRequest.Result.Success;
                if (failed)
                    error = $"{req.responseCode} {req.error}";
                else
                    body = req.downloadHandler != null ? req.downloadHandler.text : null;
            }

            if (error != null)
            {
                Plugin.Log.LogWarning($"[Sync] 天气请求失败，本次跳过（保持当前窗景）：{error}");
                yield break;
            }

            WeatherData data;
            try
            {
                data = fetcher.Parse(body);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Sync] 天气数据解析失败，本次跳过：{ex.Message}");
                yield break;
            }

            // ---- 判断（全部在 Unity 主线程上）----
            var weatherEnv = WeatherMapper.GetPrecipitation(data.WeatherCode);
            var timeOfDay = TimeSyncManager.GetTimeOfDay(data.Sunrise, data.Sunset);
            bool isDayPeriod = timeOfDay == TimeOfDay.Morning
                            || timeOfDay == TimeOfDay.Day
                            || timeOfDay == TimeOfDay.Afternoon;

            bool isRain = weatherEnv.HasValue && WeatherMapper.IsRain(weatherEnv.Value);
            bool isSnow = weatherEnv.HasValue && WeatherMapper.IsSnow(weatherEnv.Value);

            // 只在“白天”或“黄昏下雪”时强制多云背景。
            // 这是刻意保留原设计：夜晚不会被换成 Cloudy 白天景，雨雪叠加在夜景之上。
            bool forceCloudy = weatherEnv.HasValue
                            && ConfigManager.EnableTimeSync.Value
                            && (isDayPeriod || (timeOfDay == TimeOfDay.Dusk && isSnow));

            Plugin.Log.LogInfo($"[Sync] Code:{data.WeatherCode} ({WeatherMapper.Describe(data.WeatherCode)}) " +
                               $"Temp:{data.Temperature:F1}°C → 降水:{weatherEnv?.ToString() ?? "none"}");
            Plugin.Log.LogInfo($"[Sync] {WeatherFetcher.DescribeWindow(data)}");
            Plugin.Log.LogInfo($"[Sync] TimeOfDay:{timeOfDay}, forceCloudy:{forceCloudy} " +
                               $"(isDay:{isDayPeriod}, Rain:{isRain}, Snow:{isSnow}, " +
                               $"EnableWeatherSync:{ConfigManager.EnableWeatherSync.Value})");

            try
            {
                if (ConfigManager.EnableWeatherSync.Value)
                    EnvironmentApplier.Sync(weatherEnv, forceCloudy);
                else if (weatherEnv == null)
                    EnvironmentApplier.Sync(null, false); // 功能关闭时也保证不残留雨雪
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Sync] 应用窗景失败: {ex}");
            }

            if (ConfigManager.EnableTimeSync.Value)
            {
                try { UpdateTimeNodes(data.Sunrise, data.Sunset); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Sync] 更新时间节点失败: {ex.Message}"); }
            }

            Plugin.Log.LogInfo("[Sync] Done.");
        }

        private static WeatherFetcher CreateFetcher()
        {
            try
            {
                double lat = ConfigManager.Latitude.Value;
                double lon = ConfigManager.Longitude.Value;
                if (lat == 0.0 && lon == 0.0)
                    lon = WeatherFetcher.DefaultLon; // 首次启动时 lat 为 0，给个合理经度避免落到 (0,0)

                int lookAhead = ConfigManager.WeatherLookAheadHours != null
                    ? ConfigManager.WeatherLookAheadHours.Value
                    : 3;
                double gridRadius = ConfigManager.WeatherGridRadius != null
                    ? ConfigManager.WeatherGridRadius.Value
                    : 0.09;

                return new WeatherFetcher(lat, lon, lookAhead, gridRadius);
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Sync] 读取坐标失败，使用默认值: {ex.Message}");
                return new WeatherFetcher(WeatherFetcher.DefaultLat, WeatherFetcher.DefaultLon);
            }
        }

        /// <summary>房间容器 + 存档是否就绪。只在主线程调用。</summary>
        private static bool IsEnvironmentReady()
        {
            try
            {
                if (RoomLifetimeScope.Resolve<WindowViewService>() == null) return false;
                if (SaveDataManager.Instance == null) return false;
                return true;
            }
            catch { return false; }
        }

        private static void UpdateTimeNodes(DateTime sunrise, DateTime sunset)
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
    }
}
