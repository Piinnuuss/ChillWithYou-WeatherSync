using System;
using Bulbul;
using UnityEngine;
using VContainer;

namespace MyWeatherSyncMod
{
    /// <summary>
    /// 负责把“真实天气”落到游戏的窗景上。
    ///
    /// 关键设计（旧版的两个致命 bug 都在这里）：
    ///
    /// 1. 旧版用一个静态字段 _isPrecipitating 记录“当前是否在下雨”。
    ///    但这个字段只在插件自己调用 EnablePrecipitation() 时才被置为 true。
    ///    如果玩家是在游戏里手动开的雨（插件从未调用过 Enable），它一直是 false，
    ///    于是 DisablePrecipitation() 第一行 `if (!_isPrecipitating) return;`
    ///    直接返回 —— 检测到晴天也永远关不掉雨。
    ///    → 现在改为【实时查询场景真实状态】，不再依赖任何内部缓存。
    ///
    /// 2. 旧版关闭降水时只调用 EnvironmentDataService.SetViewActive（反编译确认它
    ///    仅写存档字段 + SaveEnviromentThrottled()，完全不碰场景 GameObject），
    ///    所以画面上的雨不会停。
    ///    游戏自己的 UI（Bulbul.Mobile.EnvironmentListPresenter.ToggleWindowActive）
    ///    和 Bulbul.WindowBehavior.DeactivateWindowView 都是【两件事一起做】：
    ///        SetViewActive(...)                              // 写存档
    ///        EnvironmentApplicationService.ApplyWindow(...)   // 改场景
    ///    → 现在按同样的方式调用，并补上直控 WindowViewService 的兜底。
    /// </summary>
    public static class EnvironmentApplier
    {
        /// <summary>上一次同步时目标降水类型（null = 无降水），仅用于日志，不参与判断。</summary>
        private static EnvironmentType? _lastTarget;

        /// <summary>
        /// 自动时间窗景是不是【插件为了下雨而关掉的】。
        /// 只在这种情况下才由插件重新打开，避免把玩家自己关掉的设置强行打开。
        /// </summary>
        private static bool _autoTimeDisabledByUs;

        /// <summary>
        /// 把游戏窗景同步到目标状态。
        /// </summary>
        /// <param name="targetPrecipitation">
        /// 目标降水类型；null 表示“当地没有降水”，必须关闭当前所有雨雪窗景。
        /// </param>
        /// <param name="forceCloudy">是否把时间窗景背景强制换成 Cloudy。</param>
        public static void Sync(EnvironmentType? targetPrecipitation, bool forceCloudy)
        {
            var windowService = Resolve<WindowViewService>();
            if (windowService == null)
            {
                Plugin.Log.LogWarning("[Apply] WindowViewService 不可用，跳过本次窗景同步。");
                return;
            }

            // ---- 关键：以场景真实状态为准，而不是插件自己的记忆 ----
            EnvironmentType? actualPrecipitation = QueryActivePrecipitation(windowService);

            bool alreadyCorrect = (!actualPrecipitation.HasValue && !targetPrecipitation.HasValue)
                || (actualPrecipitation.HasValue && targetPrecipitation.HasValue
                    && actualPrecipitation.Value == targetPrecipitation.Value);

            if (alreadyCorrect)
            {
                if (targetPrecipitation.HasValue)
                {
                    RefreshSounds(targetPrecipitation.Value); // 保证音量/静音状态正确
                    RefreshBackground(windowService, forceCloudy, false);
                }
                else if (forceCloudy)
                {
                    // 没有雨但要强制多云（正常不会走到，防御性分支）
                    RefreshBackground(windowService, true, false);
                }
                _lastTarget = targetPrecipitation;
                return;
            }

            if (targetPrecipitation.HasValue)
            {
                Plugin.Log.LogInfo($"[Apply] 激活降水窗景：{actualPrecipitation?.ToString() ?? "无"} → {targetPrecipitation.Value}");
                DisableAllPrecipitation(windowService); // 先彻底清干净，避免雨+雪叠加
                ActivatePrecipitation(windowService, targetPrecipitation.Value, forceCloudy);
            }
            else
            {
                Plugin.Log.LogInfo($"[Apply] 当地无降水，关闭窗景：{actualPrecipitation.Value} → 无");
                DisableAllPrecipitation(windowService);

                // 只有当自动时间是【插件为了下雨关掉的】才重新打开；
                // 玩家自己关掉的保持关闭，只把时间窗景刷成当前时段。
                RefreshBackground(windowService, false, _autoTimeDisabledByUs);
            }

            _lastTarget = targetPrecipitation;
        }

        // ------------------------------------------------------------------
        // 状态查询
        // ------------------------------------------------------------------

        /// <summary>
        /// 查询场景里当前真正激活的降水窗景。
        /// 同时看两个来源，任意一个说“在放”就认为在放（这样即使历史上存档和
        /// 场景已经不一致，也能被检出并清理掉）：
        ///   · WindowViewService.IsActiveWindow  → 直接读 GameObject.activeSelf（画面真相）
        ///   · EnvironmentDataService.IsWindowActive → 存档字段（用户再次进游戏时会恢复它）
        /// </summary>
        private static EnvironmentType? QueryActivePrecipitation(WindowViewService windowService)
        {
            var dataService = Resolve<EnvironmentDataService>();

            foreach (var type in WeatherMapper.AllPrecipitation)
            {
                WindowViewType wvType;
                if (!type.TryConvertToWindowViewType(out wvType))
                    continue;

                bool inScene = false;
                bool inSave = false;

                try { inScene = windowService.IsActiveWindow(wvType); }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] IsActiveWindow({wvType}) 失败: {ex.Message}"); }

                if (dataService != null)
                {
                    try { inSave = dataService.IsWindowActive(wvType); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] IsWindowActive({wvType}) 失败: {ex.Message}"); }
                }

                if (inScene != inSave)
                    Plugin.Log.LogWarning($"[Apply] 状态不一致 {wvType}: 场景={inScene} 存档={inSave}（按“激活”处理并清理）");

                if (inScene || inSave)
                    return type;
            }

            return null;
        }

        // ------------------------------------------------------------------
        // 打开降水
        // ------------------------------------------------------------------

        private static void ActivatePrecipitation(WindowViewService windowService, EnvironmentType weatherType, bool forceCloudy)
        {
            // 降水期间关掉自动时间切换，否则自动时间会把雨景顶掉。
            // 记录“是我们关的”，结束后才由我们负责恢复。
            if (IsAutoTimeEnabled())
            {
                SetAutoTimeSwitch(false);
                _autoTimeDisabledByUs = true;
            }

            WindowViewType wvType;
            if (!weatherType.TryConvertToWindowViewType(out wvType))
            {
                Plugin.Log.LogError($"[Apply] {weatherType} 无法转换为 WindowViewType。");
                return;
            }

            if (!ConfigManager.PreferFullWeather.Value)
            {
                Plugin.Log.LogInfo("[Apply] PreferFullWeather=false，只保留背景与声音，不激活降水窗景视觉。");
                RefreshSounds(weatherType);
                RefreshBackground(windowService, forceCloudy, false);
                return;
            }

            try
            {
                var dataService = Resolve<EnvironmentDataService>();
                if (dataService != null)
                    dataService.SetViewActive(wvType, true);

                var appService = Resolve<EnvironmentApplicationService>();
                if (appService != null)
                    appService.ApplyWindow(wvType, true);
                else
                    windowService.ActivateWindow(wvType); // 兜底

                Plugin.Log.LogInfo($"[Apply] 降水窗景已激活：{wvType}");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Apply] 激活 {wvType} 失败: {ex}");
            }

            RefreshSounds(weatherType);
            RefreshBackground(windowService, forceCloudy, false);
        }

        // ------------------------------------------------------------------
        // 关闭降水 —— 这是旧版完全失效的那段
        // ------------------------------------------------------------------

        /// <summary>关闭全部降水窗景（存档 + 场景两边都清）。</summary>
        private static void DisableAllPrecipitation(WindowViewService windowService)
        {
            var dataService = Resolve<EnvironmentDataService>();
            var appService = Resolve<EnvironmentApplicationService>();

            foreach (var type in WeatherMapper.AllPrecipitation)
            {
                WindowViewType wvType;
                if (!type.TryConvertToWindowViewType(out wvType))
                    continue;

                bool inScene = false;
                bool inSave = false;

                try { inScene = windowService.IsActiveWindow(wvType); } catch { }
                if (dataService != null) { try { inSave = dataService.IsWindowActive(wvType); } catch { } }

                if (!inScene && !inSave)
                    continue;

                // ① 清存档标记（EnvironmentDataService.SetViewActive 只做这件事）
                if (dataService != null)
                {
                    try { dataService.SetViewActive(wvType, false); }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] 清除存档标记 {wvType} 失败: {ex.Message}"); }
                }

                // ② 关掉场景里的雨雪 GameObject（真正的画面开关）
                bool sceneClosed = false;
                if (appService != null)
                {
                    try { appService.ApplyWindow(wvType, false); sceneClosed = true; }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] ApplyWindow(false) 失败: {ex.Message}"); }
                }

                if (!sceneClosed)
                {
                    try { windowService.DeactivateWindow(wvType); sceneClosed = true; }
                    catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] DeactivateWindow 兜底失败: {ex.Message}"); }
                }

                // ③ UI 版控制器（面板打开时存在），保证图标状态同步
                foreach (var ctrl in FindControllers())
                {
                    if (ctrl.EnvironmentType != type) continue;
                    try { ctrl.ChangeWindowView(ChangeType.Deactivate); } catch { }
                }

                Plugin.Log.LogInfo($"[Apply] 已关闭 {wvType}（场景关闭={sceneClosed}）");
            }

            MuteAllPrecipitationSounds();
        }

        // ------------------------------------------------------------------
        // 背景 / 声音
        // ------------------------------------------------------------------

        /// <summary>
        /// 刷新时间窗景背景。
        /// </summary>
        /// <param name="forceCloudy">是否把背景强制换成 Cloudy（仅白天/黄昏下雪时为 true）。</param>
        /// <param name="forceTime">
        /// 是否在关闭自动时间的前提下仍按当前时段刷新一次窗景。
        /// 只有“降水结束、且自动时间是插件为了下雨关掉的”才为 true，
        /// 这样不会把玩家自己关闭的自动时间窗景强行打开。
        /// </param>
        private static void RefreshBackground(WindowViewService windowService, bool forceCloudy, bool forceTime)
        {
            try
            {
                if (!forceCloudy)
                {
                    if (_autoTimeDisabledByUs)
                    {
                        // 是我们为了下雨关掉的 → 由我们恢复
                        SetAutoTimeSwitch(true);
                        _autoTimeDisabledByUs = false;
                        ApplyCurrentTimeWindow(windowService);
                    }
                    else if (forceTime)
                    {
                        // 玩家本就没开自动时间 → 只刷新一次当前时段，不改他的开关
                        ApplyCurrentTimeWindow(windowService);
                    }
                    return;
                }

                if (!IsAutoTimeEnabled())
                {
                    // 玩家关掉了自动时间窗景、手动选了夜晚/樱花等，不要去覆盖他的选择
                    Plugin.Log.LogInfo("[Apply] 自动时间窗景已关闭，保留玩家手动选择的窗景（只下雨）。");
                    return;
                }

                SetAutoTimeSwitch(false);
                _autoTimeDisabledByUs = true;
                windowService.ChangeWeatherAndTime(WindowViewType.Cloudy);
                Plugin.Log.LogInfo("[Apply] 背景已切换为 Cloudy。");
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Apply] 切换背景失败: {ex.Message}");
            }
        }

        private static void ApplyCurrentTimeWindow(WindowViewService windowService)
        {
            try
            {
                var changer = UnityEngine.Object.FindObjectOfType<AutoTimeWindowViewChanger>();
                if (changer != null)
                {
                    changer.ApplyTimeOfDayFromCurrentTime();
                    return;
                }

                var td = SaveDataManager.Instance.AutoTimeWindowChangeData;
                float now = DateTime.Now.Hour + DateTime.Now.Minute / 60f + DateTime.Now.Second / 3600f;
                var settings = new AutoTimeWindowSettings(td.TimeDayStart, td.TimeSunsetStart, td.TimeNightStart, 10);
                windowService.ChangeWeatherAndTime(settings.GetWindowViewTypeFromTime(now));
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Apply] 恢复时间窗景失败: {ex.Message}");
            }
        }

        private static void RefreshSounds(EnvironmentType weatherType)
        {
            if (weatherType == EnvironmentType.Snow)
                return; // 雪没有对应环境音

            AmbientSoundType soundType;
            if (!weatherType.TryConvertToAmbientSoundType(out soundType))
                return;

            var dataService = Resolve<EnvironmentDataService>();
            if (dataService != null)
            {
                try
                {
                    dataService.SetVolume(soundType, 0.5f);
                    dataService.SetMute(soundType, false);
                }
                catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] 设置声音失败: {ex.Message}"); }
            }

            bool applied = false;
            foreach (var ctrl in FindControllers())
            {
                if (ctrl.EnvironmentType != weatherType) continue;
                try
                {
                    ctrl.ChangeVolume(0.5f);
                    ctrl.MuteDeactivate();
                    applied = true;
                }
                catch { }
            }

            Plugin.Log.LogInfo(applied
                ? "[Apply] 环境音已激活（直接控制控制器）。"
                : "[Apply] 未找到匹配的控制器，声音将在面板打开时播放。");
        }

        private static void MuteAllPrecipitationSounds()
        {
            var dataService = Resolve<EnvironmentDataService>();
            var controllers = FindControllers();

            foreach (var type in WeatherMapper.AllPrecipitation)
            {
                AmbientSoundType soundType;
                if (type.TryConvertToAmbientSoundType(out soundType) && dataService != null)
                {
                    try { dataService.SetMute(soundType, true); }
                    catch { }
                }

                foreach (var ctrl in controllers)
                {
                    if (ctrl.EnvironmentType != type) continue;
                    try
                    {
                        ctrl.MuteActivate();
                        ctrl.ChangeVolume(0f);
                    }
                    catch { }
                }
            }
        }

        // ------------------------------------------------------------------
        // 基础设施
        // ------------------------------------------------------------------

        private static EnvironmentController[] FindControllers()
        {
            try { return Resources.FindObjectsOfTypeAll<EnvironmentController>(); }
            catch { return new EnvironmentController[0]; }
        }

        /// <summary>安全解析 VContainer 服务，未注册/容器未就绪时返回 null 而不是抛出。</summary>
        private static T Resolve<T>() where T : class
        {
            try { return RoomLifetimeScope.Resolve<T>(); }
            catch { return null; }
        }

        private static bool IsAutoTimeEnabled()
        {
            try { return SaveDataManager.Instance.AutoTimeWindowChangeData.IsActiveAuto.Value; }
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
                    Plugin.Log.LogInfo($"[Apply] 自动时间窗景 => {enabled}");
                }
            }
            catch (Exception ex) { Plugin.Log.LogWarning($"[Apply] 切换自动时间窗景失败: {ex.Message}"); }
        }
    }
}
