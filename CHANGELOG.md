# 更新日志

## v1.3.1

### 变更

- **插件不再读写游戏自己的「自动时间窗景」开关**（`AutoTimeWindowChangeData.IsActiveAuto`）。
  那个开关属于玩家设置，插件改动它就是篡改用户偏好。

  之所以要改：游戏自身的 `AutoTimeWindowViewChanger.ApplyTimeOfDayFromCurrentTime()`
  第一行就是 `if (!IsActiveAuto) return;` —— 玩家关掉该开关时它完全无效。
  旧实现为了绕开这一点去改开关，方向错了。

  现在插件改为**自己按真实时钟计算**该用哪个时间窗景
  （`SaveDataManager.AutoTimeWindowChangeData` 的时间节点 + `DateTime.Now`），
  因此无论玩家那个开关是开是关，时间窗景都会跟随真实时间。
  实测审计：编译产物中对 `IsActiveAuto` 的读写次数为 **0**。

  ⇒ 上一版为持久化"插件关过自动时间"而加的配置项
  `[Internal] AutoTimeDisabledByPlugin` 随之废弃并删除（该机制已不需要）。

### 新增

- **保护玩家自定义窗景**：每次同步先查询当前激活的是不是时间类窗景
  （Day / Sunset / Night / Cloudy）。如果不是（烟花 / 樱花 / 深海等玩家自选窗景），
  插件只处理雨雪与环境音，**完全不碰背景**。
- **不强开未解锁的窗景**：夜晚等时间窗景需要解锁，切换前用
  `UnlockEnvironment.GetLockState(EnvironmentType).IsLocked` 检查。
  查询失败时返回"不阻拦"，避免整个时间同步因一次异常而瘫痪。
- 背景切换日志做了去重（`LogBackgroundOnce`），避免每个检测周期重复刷屏。

### 移除

- 死代码：`WeatherMapper.GetTimeEnvironment()`（自 `forceCloudy` 重构后已无调用方）。

## v1.3.0

### 修复

- **修复「当地明明有雨，插件却判定为晴天」**。根因是天气判定方式太脆弱，有两处：

  1. **只看瞬时快照**。原实现读 Open-Meteo 的 `current.weather_code`，那是某一刻的值。
     雨是间歇性的，而插件每 30 分钟才采样一次，很容易正好落在两场雨的间隙里。
     实测 12:48 时 `current.weather_code=3`（阴）、`precipitation=0.00mm`，
     但 `hourly` 显示 15:00 起有降水，当天 `daily.weather_code=95`（雷暴）、
     累计降水 8.9mm —— 只看瞬时值必然判成晴天。
     现改为在 `hourly` 序列上取窗口（**前 1 小时 ~ 后 N 小时**），
     窗口内任意一小时满足「降水量 ≥ 0.1mm」或「降水概率 ≥ 60%」即视为降水。

  2. **在错误的坐标上查天气**。IP 定位有误差，实测插件拿到的
     `31.3093, 120.6020` 与江阴市中心 `31.92, 120.28` 相差约 70km，
     同一时刻气温 `31.5°C` vs `26.1°C`、降水 `0.00mm` vs `0.70mm`。
     现改为在坐标周围取 **3×3 采样网格**（Open-Meteo 支持单次请求多坐标，
     仍然只发一个请求），任一点判定为降水即按降水处理。

### 新增配置

- `WeatherLookAheadHours`（默认 `3`）：判定窗口往后看几小时，`0` = 只看当前小时。
  调大更"预报式"（提前开雨景），调小更严格实时。
- `WeatherGridRadius`（默认 `0.09`，约 10km）：采样网格半径，`0` = 只查中心点。
  用于补偿 IP 定位误差。

### 变更

- 日志新增判定依据：`9点网格 | 当前小时 #N 12时*code/mm/prob%`，
  `*` 标记当前小时，可直接看出结论来自哪个采样点的哪个小时。
- 气温改取**离请求坐标最近的采样点**（Open-Meteo 会吸附到最近网格，返回顺序不保证）。

## v1.2.1

### 修复

- **夜晚不再被强制多云**。v1.2.0 里 `forceCloudy` 只判断「有降水」，导致雨夜会把夜晚窗景
  换成白天多云。现已恢复原有设计：仅**白天**（早晨/白天/下午）有雨雪、或**黄昏下雪**时才切多云。
- **不再强行打开玩家关闭的「自动时间窗景」**。原实现在降水结束时会无条件调用
  `SetAutoTimeSwitch(true)`。现在只有插件自己为了下雨而关闭的，才会由插件恢复。

## v1.2.0

### 修复

- **同步循环彻底重写为 Unity 协程**，挂在自建的 `DontDestroyOnLoad` GameObject 上。
  v1.1.0 的 `async Task` + `CancellationTokenSource` 写法会在 BepInEx 销毁插件对象时
  立刻取消循环，导致检测在第一次 10 秒延迟前就死掉，日志停在 `Plugin OnDestroy`。
- **网络请求改用 `UnityWebRequest`**，不再阻塞 Unity 主线程（原来每次请求都会卡顿一下）。

## v1.1.0

### 修复

- **修复「当地晴天但游戏里的雨关不掉」的致命 bug**（两个独立原因）：
  1. `DisablePrecipitation()` 开头有 `if (!_isPrecipitating) return;`，而这个静态字段只在
     插件自己开雨时才为 `true`。玩家在游戏里手动开的雨，插件从没开过，字段一直是 `false`，
     于是检测到晴天也会在这一行直接返回，后续代码一行都不执行。
     现改为**实时查询场景真实状态**，不再依赖任何内部缓存。
  2. 关闭降水时只调用了 `EnvironmentDataService.SetViewActive()`。经反编译确认该函数
     **仅写存档字段 + 存档**，完全不碰场景 GameObject，所以画面上雨不会停。
     现按游戏自身 UI 的做法同时调用 `EnvironmentApplicationService.ApplyWindow()`，
     并补上直控 `WindowViewService.DeactivateWindow()` 的兜底。
- **补全 WMO 天气代码**：新增 56/57/66/67（冻雨）、77（米雪）、45/48（雾）；
  阵雨 80 按小雨、81/82 按大雨区分。
- **修正日志误导**：原来所有无降水代码都打印成 `Clear`，`code 3`（阴天）看起来也像晴天。
  现在打印真实描述，如 `Code:3 (Overcast)`。
- **请求失败不再影响游戏**：网络异常时只记录警告并跳过本轮，不动当前窗景。
- **坐标保护**：`0,0` 或非法经纬度回退到默认值。
- 自动定位改由协程轮询，带超时，不再依赖 `async void`。
