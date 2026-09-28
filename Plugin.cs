using BepInEx;
using BepInEx.Logging;

namespace MyWeatherSyncMod
{
    [BepInPlugin("com.yourname.weathersync", "Real-Time Weather Sync", "1.4.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo("=== AWAKE START ===");

            ConfigManager.Init(Config);

            // 同步循环 + 位置解析都跑在自建的 DontDestroyOnLoad GameObject 上，
            // 不依赖本插件的生命周期（BepInEx 启动完成后会销毁本对象）。
            // 位置解析由 WeatherSyncRunner.ResolveLocation() 在每轮启动时执行。
            var runner = WeatherSyncRunner.Create();
            runner.Begin();

            Log.LogInfo("=== AWAKE END ===");
        }
    }
}
