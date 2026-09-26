using BepInEx;
using BepInEx.Logging;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace MyWeatherSyncMod
{
    [BepInPlugin("com.yourname.weathersync", "Real-Time Weather Sync", "1.3.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("=== AWAKE START ===");

            ConfigManager.Init(Config);

            // 同步循环跑在自建的 DontDestroyOnLoad GameObject 上，
            // 不依赖本插件的生命周期（BepInEx 启动完成后会销毁本对象）。
            var runner = WeatherSyncRunner.Create();
            runner.Begin();
            runner.StartCoroutine(AutoLocateThenNotify());

            Log.LogInfo("=== AWAKE END ===");
        }

        /// <summary>
        /// IP 自动定位。定位结果写回配置，下一轮同步会读到新坐标。
        /// 在独立 GameObject 上跑协程，避免被插件对象的销毁打断。
        /// </summary>
        private static System.Collections.IEnumerator AutoLocateThenNotify()
        {
            if (!ConfigManager.AutoLocate.Value)
                yield break;

            var task = LocationFetcher.FetchAsync();

            float waited = 0f;
            while (!task.IsCompleted && waited < 20f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            if (!task.IsCompleted)
            {
                Log.LogWarning("Auto-locate 超时，沿用配置中的坐标。");
                yield break;
            }

            if (task.IsFaulted || task.Result == null || !task.Result.Success)
            {
                Log.LogWarning("Could not auto-locate, using default/manual coordinates.");
                yield break;
            }

            var loc = task.Result;
            ConfigManager.Latitude.Value = loc.Latitude;
            ConfigManager.Longitude.Value = loc.Longitude;
            ConfigManager.Save();
            Log.LogInfo($"Auto-located: {loc.Latitude:F4}, {loc.Longitude:F4}");
        }
    }
}
