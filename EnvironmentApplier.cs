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
            if (weatherType.HasValue)
                EnablePrecipitation(weatherType.Value, forceCloudy);
            else
                DisablePrecipitation();
        }

        private static void EnablePrecipitation(EnvironmentType weatherType, bool forceCloudy)
        {
            if (forceCloudy)
            {
                if (!_isPrecipitating)
                {
                    SetAutoTimeSwitch(false);
                    Plugin.Log.LogInfo("Auto time switch disabled for daytime precipitation.");
                }

                try
                {
                    RoomLifetimeScope.Resolve<WindowViewService>().ChangeWeatherAndTime(WindowViewType.Cloudy);
                    Plugin.Log.LogInfo("Time background set to Cloudy.");
                }
                catch (Exception ex) { Plugin.Log.LogError($"Set Cloudy failed: {ex.Message}"); }
            }
            else
            {
                if (_isPrecipitating || !IsAutoTimeEnabled())
                {
                    SetAutoTimeSwitch(true);
                    ApplyCurrentTimeWindow();
                    Plugin.Log.LogInfo("Time background restored to current time period.");
                }
            }

            // 天气窗景（雨/雪视觉）
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

            // 声音激活
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
            ApplyCurrentTimeWindow();
            MuteAllPrecipitationSounds();
            DeactivateAllWeatherWindows();

            Plugin.Log.LogInfo("Precipitation disabled, time background restored.");
            _isPrecipitating = false;
        }

        private static void ApplyCurrentTimeWindow()
        {
            try
            {
                var changer = UnityEngine.Object.FindObjectOfType<AutoTimeWindowViewChanger>();
                if (changer != null)
                    changer.ApplyTimeOfDayFromCurrentTime();
                else
                {
                    var saveData = SaveDataManager.Instance;
                    var td = saveData.AutoTimeWindowChangeData;
                    float now = DateTime.Now.Hour + DateTime.Now.Minute / 60f + DateTime.Now.Second / 3600f;
                    var settings = new AutoTimeWindowSettings(td.TimeDayStart, td.TimeSunsetStart, td.TimeNightStart, 10);
                    var wvType = settings.GetWindowViewTypeFromTime(now);
                    RoomLifetimeScope.Resolve<WindowViewService>().ChangeWeatherAndTime(wvType);
                }
            }
            catch (Exception ex) { Plugin.Log.LogError($"Apply current time window failed: {ex.Message}"); }
        }

        private static bool IsAutoTimeEnabled()
        {
            try
            {
                return SaveDataManager.Instance.AutoTimeWindowChangeData.IsActiveAuto.Value;
            }
            catch { return false; }
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
            var data = RoomLifetimeScope.Resolve<EnvironmentDataService>();
            var controllers = Resources.FindObjectsOfTypeAll<EnvironmentController>();

            foreach (var t in types)
            {
                AmbientSoundType st;
                if (t.TryConvertToAmbientSoundType(out st))
                {
                    try { data.SetMute(st, true); } catch { }
                }

                // 同时直接操作匹配的控制器，确保声音立即停止
                foreach (var ctrl in controllers)
                {
                    if (ctrl.EnvironmentType == t)
                    {
                        try
                        {
                            ctrl.MuteActivate();
                            ctrl.ChangeVolume(0f);
                        }
                        catch { }
                    }
                }
            }
        }

        private static void DeactivateAllWeatherWindows()
        {
            var types = new[] { EnvironmentType.LightRain, EnvironmentType.HeavyRain, EnvironmentType.ThunderRain, EnvironmentType.Snow };
            var data = RoomLifetimeScope.Resolve<EnvironmentDataService>();
            var controllers = Resources.FindObjectsOfTypeAll<EnvironmentController>();

            foreach (var t in types)
            {
                WindowViewType wvt;
                if (t.TryConvertToWindowViewType(out wvt))
                {
                    try { data.SetViewActive(wvt, false); } catch { }
                }

                // 直接操作匹配的控制器，强制关闭窗景视觉
                foreach (var ctrl in controllers)
                {
                    if (ctrl.EnvironmentType == t)
                    {
                        try
                        {
                            ctrl.ChangeWindowView(ChangeType.Deactivate);
                        }
                        catch { }
                    }
                }
            }
        }
    }
}