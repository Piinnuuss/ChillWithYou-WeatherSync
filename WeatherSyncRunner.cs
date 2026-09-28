using System;
using System.Collections;
using System.Collections.Generic;
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

            // ---- 每次启动都重新解析位置 ----
            yield return ResolveLocation();

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

        // ------------------------------------------------------------------
        // 位置解析：城市名优先，IP 多源兜底
        // ------------------------------------------------------------------

        /// <summary>
        /// 每次启动都会跑一遍（不是只跑一次）。
        /// 优先级：配置的 City（地理编码，精确）> 多源 IP 定位 > 沿用配置里的坐标。
        /// </summary>
        private IEnumerator ResolveLocation()
        {
            string city = null;
            try { city = ConfigManager.City != null ? ConfigManager.City.Value : null; }
            catch { }
            if (city != null) city = city.Trim();

            // ---- 1. 城市名优先（最准） ----
            if (!string.IsNullOrEmpty(city))
            {
                var byCity = new LocationResult();
                yield return FetchCity(city, byCity);
                if (byCity.Success)
                {
                    ConfigManager.Latitude.Value = Math.Round(byCity.Latitude, 4);
                    ConfigManager.Longitude.Value = Math.Round(byCity.Longitude, 4);
                    ConfigManager.Save();
                    LogCityResolved(city, byCity);
                    yield break;
                }
                Plugin.Log.LogWarning($"[Location] 城市名 \"{city}\" 解析失败，退回 IP 定位。");
            }

            // ---- 2. IP 定位兜底 ----
            bool autoLocate = true;
            try { autoLocate = ConfigManager.AutoLocate == null || ConfigManager.AutoLocate.Value; }
            catch { }

            if (!autoLocate)
            {
                Plugin.Log.LogInfo("[Location] AutoLocate=false 且未配置 City，沿用配置中的坐标。");
                yield break;
            }

            var byIp = new LocationResult();
            yield return FetchIp(byIp);

            if (byIp.Success)
            {
                ApplyLocation(byIp, byIp.Source);
                Plugin.Log.LogInfo(
                    "[Location] 提示：IP 定位在移动/共享宽带下可能落到邻近城市。" +
                    "想要精确可在地图复制坐标填 Latitude/Longitude，" +
                    "或在配置的 City 里填城市名。");
            }
            else
            {
                Plugin.Log.LogWarning("[Location] 所有 IP 服务都失败，沿用配置中的坐标。");
            }
        }

        private void ApplyLocation(LocationResult loc, string source)
        {
            ConfigManager.Latitude.Value = Math.Round(loc.Latitude, 4);
            ConfigManager.Longitude.Value = Math.Round(loc.Longitude, 4);
            ConfigManager.Save();

            var place = string.IsNullOrEmpty(loc.PlaceName) ? "" : $" [{loc.PlaceName}]";
            Plugin.Log.LogInfo(
                $"[Location] 已定位{place}：{ConfigManager.Latitude.Value:F4}, " +
                $"{ConfigManager.Longitude.Value:F4}（来源：{source}）");
        }

        /// <summary>
        /// 城市名解析成功后的日志。
        /// 地名用英文名（与你填写的名字一致）+ 中文省/市层级，
        /// 例如填 Jiangyin 会显示 [Jiangyin（中国 江苏 无锡市）] ——
        /// 因为 Open-Meteo 的中文地名是「澄江」（江阴市区街道名），直接显示会让人困惑。
        /// </summary>
        private void LogCityResolved(string typed, LocationResult loc)
        {
            var display = loc.BuildDisplay();
            if (string.IsNullOrEmpty(display)) display = loc.PlaceName;

            Plugin.Log.LogInfo(
                $"[Location] 已定位 [{display}]：{ConfigManager.Latitude.Value:F4}, " +
                $"{ConfigManager.Longitude.Value:F4}（来源：城市名 \"{typed}\"）");
        }

        /// <summary>
        /// 用 Open-Meteo 地理编码按城市名取精确坐标。
        /// 发两个请求：默认（英文名，保证地名与你填的一致）+ language=zh（拿中文省/市层级）。
        /// 中文版失败不影响主流程。
        /// </summary>
        private IEnumerator FetchCity(string city, LocationResult result)
        {
            // ---- 主请求：默认语言，英文地名 ----
            string bodyEn = null;
            using (var req = UnityWebRequest.Get(LocationFetcher.BuildCityUrlEnglish(city)))
            {
                req.timeout = NetworkTimeoutSeconds;
                yield return req.SendWebRequest();
                if (req.result == UnityWebRequest.Result.Success && req.downloadHandler != null)
                    bodyEn = req.downloadHandler.text;
                else
                    Plugin.Log.LogWarning($"[Location] 城市名查询失败：{req.responseCode} {req.error}");
            }
            if (bodyEn == null) yield break;

            string nameEn; double lat, lon;
            if (!LocationFetcher.TryParseCityName(bodyEn, out nameEn, out lat, out lon))
                yield break; // 无结果 -> 交给调用方回退到 IP

            result.Latitude = lat;
            result.Longitude = lon;
            result.PlaceName = nameEn;
            result.Success = true;
            result.Source = "城市名 geocoding";

            // ---- 附加请求：中文层级，仅用于日志展示 ----
            using (var req2 = UnityWebRequest.Get(LocationFetcher.BuildCityUrl(city)))
            {
                req2.timeout = NetworkTimeoutSeconds;
                yield return req2.SendWebRequest();
                if (req2.result == UnityWebRequest.Result.Success && req2.downloadHandler != null)
                {
                    string a1, a2;
                    if (LocationFetcher.TryParseCityChineseAdmins(req2.downloadHandler.text, out a1, out a2))
                    {
                        result.Admin1 = a1;
                        result.Admin2 = a2;
                    }
                }
            }
        }

        /// <summary>依次尝试多个 IP 服务，返回第一个成功的结果（数值重复的会被忽略）。</summary>
        private IEnumerator FetchIp(LocationResult result)
        {
            var candidates = new List<KeyValuePair<string, LocationResult>>();

            foreach (var ep in LocationFetcher.IpEndpoints)
            {
                string body = null;
                using (var req = UnityWebRequest.Get(ep.Url))
                {
                    req.timeout = NetworkTimeoutSeconds;
                    yield return req.SendWebRequest();
                    if (req.result == UnityWebRequest.Result.Success && req.downloadHandler != null)
                        body = req.downloadHandler.text;
                }

                if (body == null)
                {
                    Plugin.Log.LogInfo($"[Location] IP 服务 {ep.Name} 不可用，尝试下一个。");
                    continue;
                }

                LocationResult parsed;
                if (ep.TryParse(body, out parsed))
                {
                    Plugin.Log.LogInfo(
                        $"[Location] IP 服务 {ep.Name} -> {parsed.Latitude:F4}, " +
                        $"{parsed.Longitude:F4}{(string.IsNullOrEmpty(parsed.PlaceName) ? "" : " " + parsed.PlaceName)}");
                    candidates.Add(new KeyValuePair<string, LocationResult>(ep.Name, parsed));
                }
                else
                {
                    Plugin.Log.LogInfo($"[Location] IP 服务 {ep.Name} 返回无效数据，尝试下一个。");
                }
            }

            LocationResult best;
            string note;
            if (LocationFetcher.SelectBestIpResult(candidates, out best, out note))
            {
                if (!string.IsNullOrEmpty(note)) Plugin.Log.LogInfo($"[Location] {note}");
                result.Latitude = best.Latitude;
                result.Longitude = best.Longitude;
                result.PlaceName = best.PlaceName;
                result.Source = best.Source;
                result.Success = true;
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
            // 这是刻意保留原设计：夜晚不被换成 Cloudy 白天景，雨雪叠加在夜景之上。
            bool isDuskCloudy = timeOfDay == TimeOfDay.Dusk && isSnow;
            bool forceCloudy = weatherEnv.HasValue
                            && ConfigManager.EnableTimeSync.Value
                            && (isDayPeriod || isDuskCloudy);

            Plugin.Log.LogInfo($"[Sync] Code:{data.WeatherCode} ({WeatherMapper.Describe(data.WeatherCode)}) " +
                               $"Temp:{data.Temperature:F1}°C → 降水:{weatherEnv?.ToString() ?? "none"}");
            Plugin.Log.LogInfo($"[Sync] {WeatherFetcher.DescribeWindow(data)}");
            Plugin.Log.LogInfo($"[Sync] TimeOfDay:{timeOfDay}, forceCloudy:{forceCloudy} " +
                               $"(isDay:{isDayPeriod}, Rain:{isRain}, Snow:{isSnow}, " +
                               $"EnableWeatherSync:{ConfigManager.EnableWeatherSync.Value})");

            // 降水但没切多云时，说清是“时段原因”还是“功能关闭”
            if (weatherEnv.HasValue && !forceCloudy)
            {
                if (!ConfigManager.EnableTimeSync.Value)
                    Plugin.Log.LogInfo($"[Sync] 降水保持 {timeOfDay} 窗景：EnableTimeSync=false，不做背景转换。");
                else if (!isDayPeriod && !isDuskCloudy)
                    Plugin.Log.LogInfo($"[Sync] 降水保持 {timeOfDay} 窗景：仅白天（或黄昏下雪）才转为多云，夜晚/傍晚维持原窗景。");
            }

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
