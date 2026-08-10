using System;
using UnityEngine;
using Bulbul;
using VContainer;

namespace MyWeatherSyncMod
{
    public static class EnvironmentApplier
    {
        private static bool _isPrecipitating;

        public static void ApplyPrecipitation(EnvironmentType? weatherType, bool forceCloudy)
        {
            if (forceCloudy && weatherType.HasValue)
                EnablePrecipitation(weatherType.Value);
            else
                DisablePrecipitation();
        }

        private static void EnablePrecipitation(EnvironmentType weatherType)
        {
            if (!_isPrecipitating)
            {
                SetAutoTimeSwitch(false);
                Plugin.Log.LogInfo("Auto time switch disabled for precipitation.");
            }

            // 1. 多云背景
            try
            {
                RoomLifetimeScope.Resolve<WindowViewService>().ChangeWeatherAndTime(WindowViewType.Cloudy);
                Plugin.Log.LogInfo("Time background set to Cloudy.");
            }
            catch (Exception ex) { Plugin.Log.LogError($"Set Cloudy failed: {ex.Message}"); }

            // 2. 天气窗景（雨/雪视觉）
            if (ConfigManager.PreferFullWeather.Value &&
                weatherType.TryConvertToWindowViewType(out WindowViewType wvType))
            {
                try
                {
                    RoomLifetimeScope.Resolve<EnvironmentDataService>().SetViewActive(wvType, true);
                    var controllers = Resources.FindObjectsOfTypeAll<EnvironmentController>();
                    foreach (var ctrl in controllers)
                    {
                        try { ctrl.ApplyWindowBySaveData(); } catch { }
                    }
                    Plugin.Log.LogInfo($"Weather window view applied ({controllers.Length} controllers).");
                }
                catch (Exception ex) { Plugin.Log.LogError($"Weather window failed: {ex.Message}"); }
            }

            // 3. 声音激活（直接通过控制器驱动，确保立即出声）
            if (weatherType != EnvironmentType.Snow &&
                weatherType.TryConvertToAmbientSoundType(out AmbientSoundType asType))
            {
                var dataService = RoomLifetimeScope.Resolve<EnvironmentDataService>();
                dataService.SetVolume(asType, 0.5f);
                dataService.SetMute(asType, false);

                var controllers = Resources.FindObjectsOfTypeAll<EnvironmentController>();
                bool audioApplied = false;
                foreach (var ctrl in controllers)
                {
                    if (ctrl.EnvironmentType == weatherType)
                    {
                        try
                        {
                            ctrl.ChangeVolume(0.5f);
                            ctrl.MuteDeactivate();
                            audioApplied = true;
                        }
                        catch { }
                    }
                }

                if (audioApplied)
                    Plugin.Log.LogInfo($"Rain sound activated via controllers.");
                else
                    Plugin.Log.LogInfo("No matching controller for sound; will play when panel opens.");
            }

            _isPrecipitating = true;
        }

        private static void DisablePrecipitation()
        {
            if (!_isPrecipitating) return;
            SetAutoTimeSwitch(true);
            MuteAllPrecipitationSounds();
            DeactivateAllWeatherWindows();

            try
            {
                var changer = UnityEngine.Object.FindObjectOfType<AutoTimeWindowViewChanger>();
                if (changer != null) changer.ApplyTimeOfDayFromCurrentTime();
                else
                {
                    var saveData = SaveDataManager.Instance;
                    var td = saveData.AutoTimeWindowChangeData;
                    float now = DateTime.Now.Hour + DateTime.Now.Minute / 60f + DateTime.Now.Second / 3600f;
                    var settings = new AutoTimeWindowSettings(td.TimeDayStart, td.TimeSunsetStart, td.TimeNightStart, 10);
                    var wvType = settings.GetWindowViewTypeFromTime(now);
                    RoomLifetimeScope.Resolve<WindowViewService>().ChangeWeatherAndTime(wvType);
                }
                Plugin.Log.LogInfo("Time background restored.");
            }
            catch (Exception ex) { Plugin.Log.LogError($"Restore failed: {ex.Message}"); }

            _isPrecipitating = false;
        }

        private static void SetAutoTimeSwitch(bool enabled)
        {
            try
            {
                var sd = SaveDataManager.Instance;
                if (sd.AutoTimeWindowChangeData.IsActiveAuto.Value != enabled)
                {
                    sd.AutoTimeWindowChangeData.IsActiveAuto.Value = enabled;
                    sd.SaveAutoTimeWindowChangeData();
                }
            }
            catch { }
        }

        private static void MuteAllPrecipitationSounds()
        {
            var types = new[] { EnvironmentType.LightRain, EnvironmentType.HeavyRain, EnvironmentType.ThunderRain };
            try
            {
                var data = RoomLifetimeScope.Resolve<EnvironmentDataService>();
                foreach (var t in types)
                {
                    AmbientSoundType st;
                    if (t.TryConvertToAmbientSoundType(out st))
                        try { data.SetMute(st, true); } catch { }
                }
            }
            catch { }
        }

        private static void DeactivateAllWeatherWindows()
        {
            var types = new[] { EnvironmentType.LightRain, EnvironmentType.HeavyRain, EnvironmentType.ThunderRain, EnvironmentType.Snow };
            try
            {
                var data = RoomLifetimeScope.Resolve<EnvironmentDataService>();
                foreach (var t in types)
                {
                    WindowViewType wvt;
                    if (t.TryConvertToWindowViewType(out wvt))
                        try { data.SetViewActive(wvt, false); } catch { }
                }
            }
            catch { }
        }
    }
}