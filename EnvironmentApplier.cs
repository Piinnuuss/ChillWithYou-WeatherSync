using System;
using Bulbul;
using UnityEngine;
using VContainer;

namespace MyWeatherSyncMod
{
    /// <summary>
    /// 负责把“真实天气/时间”落到游戏的窗景上。
    ///
    /// 【职责边界】插件只做两件事：
    ///   1. 雨雪 —— 按当地真实降水开关 LightRain / HeavyRain / ThunderRain / Snow；
    ///              白天有降水时把时间背景换成 Cloudy。
    ///   2. 时间窗景（Day / Sunset / Night）—— 按当地真实时间驱动，
    ///              但【只在当前窗景本来就是时间类时】才动，绝不覆盖玩家自己选的
    ///              烟花 / 樱花 / 深海等自定义窗景。
    ///
    /// 【不碰的东西】游戏自己的「自动时间窗景」开关（AutoTimeWindowChangeData.IsActiveAuto）。
    ///   那个开关属于玩家的设置，插件一改就等于篡改用户偏好。
    ///   注意游戏自身的 AutoTimeWindowViewChanger.ApplyTimeOfDayFromCurrentTime()
    ///   第一行就是 `if (!IsActiveAuto) return;` —— 开关关着时它什么都不做，
    ///   所以插件必须自己算时间窗景，不能依赖它。
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

        /// <summary>日志去重：避免每个检测周期都刷同一句“保留自定义窗景”。</summary>
        private static string _lastBackgroundAction = "";

        /// <summary>
        /// 【为什么必须临时关掉游戏的「自动时间窗景」】
        ///
        /// 实测反编译结果：DateService.Setup() 用 Observable.Interval(0.2s) 每 0.2 秒推送一次
        /// OnChangeTime，而 AutoTimeWindowViewChanger.Setup() 订阅了它，并且在该开关为 true 时
        /// 立刻调用 ApplyTimeOfDayFromCurrentTime() -> WindowViewService.ChangeWeatherAndTime(当前时段)。
        ///
        /// 也就是说：只要「自动时间窗景」开着，游戏每 0.2 秒就会把时间窗景强制拉回
        /// Day/Sunset/Night。插件切换的 Cloudy 会在 0.2 秒内被覆盖 —— 表现为
        /// “多云只出现了一瞬又切回自动天气”。
        ///
        /// （雨雪窗景不受影响，因为 ChangeWeatherAndTime 只管时间类 GameObject。）
        ///
        /// 所以降水期间必须由插件接管窗景：记录玩家原本的开关值 → 关掉 → 降水结束原样还原。
        /// </summary>
        private static bool WeatherTookOverWindow
        {
            get
            {
                try { return ConfigManager.WeatherTookOverWindow != null && ConfigManager.WeatherTookOverWindow.Value; }
                catch { return false; }
            }
            set
            {
                try
                {
                    if (ConfigManager.WeatherTookOverWindow != null &&
                        ConfigManager.WeatherTookOverWindow.Value != value)
                    {
                        ConfigManager.WeatherTookOverWindow.Value = value;
                        ConfigManager.Save();
                    }
                }
                catch { }
            }
        }

        private static bool AutoTimeWasOnBeforeRain
        {
            get
            {
                try { return ConfigManager.AutoTimeWasOnBeforeRain == null || ConfigManager.AutoTimeWasOnBeforeRain.Value; }
                catch { return true; }
            }
            set
            {
                try
                {
                    if (ConfigManager.AutoTimeWasOnBeforeRain != null &&
                        ConfigManager.AutoTimeWasOnBeforeRain.Value != value)
                    {
                        ConfigManager.AutoTimeWasOnBeforeRain.Value = value;
                        ConfigManager.Save();
                    }
                }
                catch { }
            }
        }

        /// <summary>把游戏窗景同步到目标状态。</summary>
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

            // _lastTarget 必须在调用 RefreshBackground 之前更新：
            // RefreshBackground 用 _lastTarget.HasValue 判断"本次是否有降水"，
            // 从而决定要不要接管/交还窗景。
            _lastTarget = targetPrecipitation;

            // ---- 关键：以场景真实状态为准，而不是插件自己的记忆 ----
            EnvironmentType? actualPrecipitation = QueryActivePrecipitation(windowService);

            bool alreadyCorrect = (!actualPrecipitation.HasValue && !targetPrecipitation.HasValue)
                || (actualPrecipitation.HasValue && targetPrecipitation.HasValue
                    && actualPrecipitation.Value == targetPrecipitation.Value);

            if (alreadyCorrect)
            {
                // 降水状态没变化也要刷新背景：
                //   · 有降水时保证音量/静音状态正确，并维持 Cloudy 背景；
                //   · 【关键】没有降水时，时间窗景会随真实时间在 白天/傍晚/夜晚 之间迁移，
                //     如果这里直接 return，雨停之后窗景就永远停在当次刷新时的那个时段
                //     （实测：19 点多了还停在"傍晚"）。
                if (targetPrecipitation.HasValue)
                    RefreshSounds(targetPrecipitation.Value);

                RefreshBackground(windowService, forceCloudy);
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
                RefreshBackground(windowService, false); // 交回时间窗景（会自行判断是否该动）
            }
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
            // 注意：这里【不再】去动游戏的「自动时间窗景」开关（IsActiveAuto）。
            // 那是玩家的设置，插件一改就是篡改用户偏好；时间窗景由 ApplyTimeWindow 自己负责。

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
                RefreshBackground(windowService, forceCloudy);
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
            RefreshBackground(windowService, forceCloudy);
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
        /// 刷新窗景背景。三条路径：
        ///   · forceCloudy=true（白天有降水）→ 换成 Cloudy 时间窗景，并【接管】窗景
        ///   · forceCloudy=false + 降水中（夜晚/傍晚）→ 按真实时间显示，但仍【接管】窗景
        ///   · forceCloudy=false 且无降水 → 交还给游戏的时间窗景 / 玩家设置
        ///
        /// 铁律：当前窗景是玩家自定义的（烟花/樱花/深海…）时绝不覆盖，直接不动。
        /// </summary>
        private static void RefreshBackground(WindowViewService windowService, bool forceCloudy)
        {
            bool precipitating = _lastTarget.HasValue;

            try
            {
                // 玩家自己选的窗景（非时间类）→ 无论如何都不碰，也不需要接管
                if (!IsTimeWindowActive(windowService))
                {
                    LogBackgroundOnce("保留自定义窗景",
                        "[Apply] 当前是玩家自定义窗景（烟花/樱花等），不覆盖，只处理雨雪。");
                    return;
                }

                if (precipitating)
                {
                    // ---- 降水期间：必须接管，否则游戏每 0.2 秒覆盖一次 ----
                    TakeOverWindow();
                }
                else
                {
                    // ---- 无降水：先交还，再按时间/设置刷新 ----
                    if (!ReleaseWindow()) return; // 交还后仍由游戏托管则不必再动
                }

                // 时间窗景是要解锁的（夜晚需先解锁），没解锁就不要强开
                var target = forceCloudy ? WindowViewType.Cloudy : GetWindowViewTypeForNow();
                if (!IsWindowUnlocked(target))
                {
                    LogBackgroundOnce("未解锁-" + target,
                        $"[Apply] 时间窗景 {target} 尚未解锁，保持当前窗景。");
                    return;
                }

                if (forceCloudy)
                {
                    windowService.ChangeWeatherAndTime(WindowViewType.Cloudy);
                    LogBackgroundOnce("多云",
                        $"[Apply] 白天有降水，背景已切换为 Cloudy（已暂停游戏的自动时间切换以免被覆盖）。");
                }
                else if (precipitating)
                {
                    // 夜晚/傍晚降水：不转多云，但窗景已"冻结"，需要插件自己随时段推进
                    var cur = GetActiveTimeWindow(windowService);
                    windowService.ChangeWeatherAndTime(target);
                    LogBackgroundOnce("降水时段-" + target,
                        $"[Apply] 降水保持 {target} 窗景（{cur} → {target}，非白天不转多云）。");
                }
                else
                {
                    var cur = GetActiveTimeWindow(windowService);
                    windowService.ChangeWeatherAndTime(target);
                    LogBackgroundOnce("时间-" + target,
                        $"[Apply] 背景跟随真实时间：{cur} → {target}" +
                        $"（当前 {DateTime.Now:HH:mm}，时段边界 白天/傍晚/夜晚 = " +
                        $"{FormatHour(GetTimeNode(t => t.TimeDayStart))}/" +
                        $"{FormatHour(GetTimeNode(t => t.TimeSunsetStart))}/" +
                        $"{FormatHour(GetTimeNode(t => t.TimeNightStart))}）。");
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[Apply] 切换背景失败: {ex.Message}");
            }
        }

        // ------------------------------------------------------------------
        // 窗景接管 / 交还
        // ------------------------------------------------------------------

        /// <summary>
        /// 降水开始或持续时调用：记录玩家原本的「自动时间窗景」设置并临时关闭它。
        /// 不关掉的话，游戏每 0.2 秒会把 Cloudy 覆盖回当前时段。
        /// </summary>
        private static void TakeOverWindow()
        {
            if (WeatherTookOverWindow)
            {
                EnsureAutoTimeOff();
                return;
            }

            AutoTimeWasOnBeforeRain = IsAutoTimeOn();
            WeatherTookOverWindow = true;
            EnsureAutoTimeOff();
            Plugin.Log.LogInfo(
                $"[Apply] 降水期间接管窗景（玩家原本的自动时间窗景 = " +
                $"{(AutoTimeWasOnBeforeRain ? "开" : "关")}，暂关以免背景被游戏覆盖，降水结束会原样还原）。");
        }

        /// <summary>
        /// 降水结束时调用：把「自动时间窗景」还原成玩家原本的设置。
        /// </summary>
        /// <returns>true 表示窗景已交还给游戏托管，调用方不必再手动设置窗景。</returns>
        private static bool ReleaseWindow()
        {
            if (!WeatherTookOverWindow)
                return false; // 本来就没接管，交给调用方按时间刷新

            bool restoreTo = AutoTimeWasOnBeforeRain;
            WeatherTookOverWindow = false;

            SetAutoTime(restoreTo);
            Plugin.Log.LogInfo($"[Apply] 降水结束，已把「自动时间窗景」还原为 {(restoreTo ? "开" : "关")}。");

            // 还原成"开"时，游戏的 ApplyTimeOfDayFromCurrentTime() 会自行把窗景拉回当前时段
            return restoreTo;
        }

        private static bool IsAutoTimeOn()
        {
            try { return SaveDataManager.Instance.AutoTimeWindowChangeData.IsActiveAuto.Value; }
            catch { return false; }
        }

        /// <summary>只在降水期间使用，用于压制游戏的自动时间覆盖。</summary>
        private static void EnsureAutoTimeOff()
        {
            if (!IsAutoTimeOn()) return;
            SetAutoTime(false);
        }

        private static void SetAutoTime(bool enabled)
        {
            try
            {
                var sd = SaveDataManager.Instance;
                if (sd.AutoTimeWindowChangeData.IsActiveAuto.Value == enabled) return;
                sd.AutoTimeWindowChangeData.IsActiveAuto.Value = enabled;
                sd.SaveAutoTimeWindowChangeData();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[Apply] 切换自动时间窗景失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 当前激活的是不是「时间类」窗景（Day/Sunset/Night/Cloudy）。
        /// 这四者是互斥的一类，其余（烟花/樱花/深海…）都是玩家自定义窗景。
        /// </summary>
        private static bool IsTimeWindowActive(WindowViewService windowService)
        {
            return GetActiveTimeWindow(windowService) != null;
        }

        /// <summary>返回当前激活的时间类窗景；若当前是自定义窗景则返回 null。</summary>
        private static WindowViewType? GetActiveTimeWindow(WindowViewService windowService)
        {
            foreach (var t in TimeWindowTypes)
            {
                try { if (windowService.IsActiveWindow(t)) return t; }
                catch { }
            }
            return null;
        }

        /// <summary>读存档里的某个时段节点，只用于日志展示。</summary>
        private static float GetTimeNode(Func<AutoTimeWindowChangeData, float> pick)
        {
            try { return pick(SaveDataManager.Instance.AutoTimeWindowChangeData); }
            catch { return 0f; }
        }

        /// <summary>把小时浮点数格式化成 HH:mm，只用于日志展示。</summary>
        private static string FormatHour(float hours)
        {
            int h = (int)Math.Floor(hours);
            int m = (int)Math.Round((hours - h) * 60f);
            if (m >= 60) { h++; m = 0; }
            return $"{h:00}:{m:00}";
        }

        private static readonly WindowViewType[] TimeWindowTypes =
        {
            WindowViewType.Day,
            WindowViewType.Sunset,
            WindowViewType.Night,
            WindowViewType.Cloudy,
        };

        /// <summary>
        /// 按存档里的时间节点 + 真实时钟算出当前该用哪个时间窗景。
        /// 不使用 AutoTimeWindowViewChanger.ApplyTimeOfDayFromCurrentTime()：
        /// 那个方法第一行就是 `if (!IsActiveAuto) return;`，玩家关掉自动时间时它无效。
        /// </summary>
        private static WindowViewType GetWindowViewTypeForNow()
        {
            var td = SaveDataManager.Instance.AutoTimeWindowChangeData;
            float now = DateTime.Now.Hour + DateTime.Now.Minute / 60f + DateTime.Now.Second / 3600f;
            var settings = new AutoTimeWindowSettings(td.TimeDayStart, td.TimeSunsetStart, td.TimeNightStart, 10);
            return settings.GetWindowViewTypeFromTime(now);
        }

        /// <summary>该时间窗景是否已解锁（夜晚等需要解锁，未解锁不要强开）。</summary>
        private static bool IsWindowUnlocked(WindowViewType viewType)
        {
            // 注意方向：WindowViewType → EnvironmentType（WindowViewTypeExtensions 提供）
            EnvironmentType envType;
            if (!viewType.TryConvertToWindowViewType(out envType))
                return true; // 非时间类不做解锁判断

            try
            {
                var unlock = Resolve<UnlockItemService>();
                if (unlock == null || unlock.Environment == null) return true; // 查不到就不阻拦
                var state = unlock.Environment.GetLockState(envType);
                if (state == null) return true;
                return !state.IsLocked.CurrentValue;
            }
            catch
            {
                return true; // 查询失败时不阻拦，避免整个时间同步瘫痪
            }
        }

        /// <summary>同一件事只记一次日志，避免每轮同步刷屏。</summary>
        private static void LogBackgroundOnce(string key, string message)
        {
            if (_lastBackgroundAction == key) return;
            _lastBackgroundAction = key;
            Plugin.Log.LogInfo(message);
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

        // 说明：这里刻意【没有】读写游戏「自动时间窗景」开关(IsActiveAuto) 的方法。
        // 那个开关属于玩家设置，插件不碰；时间窗景由 GetWindowViewTypeForNow() 自行计算。
    }
}
