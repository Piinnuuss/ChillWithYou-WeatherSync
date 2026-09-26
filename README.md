# 🌦️ Chill With You - Real-Time Weather Sync

自动把《放松时光：与你共享 Lo-Fi 故事》(Chill With You) 的游戏环境同步为你所在地的**真实天气与真实时间**。

- 天气数据：[Open-Meteo](https://open-meteo.com/)（免费、无需 API Key）
- 定位数据：[ip-api.com](http://ip-api.com/)（可关闭，改用手动经纬度）
- 运行环境：BepInEx 5.4.x + Unity 2022.3

---

## ✨ 功能

| 功能 | 说明 |
|------|------|
| 🌧️ 真实天气同步 | 当地在下雨/下雪/打雷时，自动打开对应的雨雪窗景与环境音 |
| ☀️ 晴天自动关闭 | 当地没有降水时，**自动关闭**游戏里正开着的雨雪窗景（含手动开启的） |
| 🌅 真实日出日落 | 用当地真实 sunrise/sunset 更新游戏的时间窗景节点 |
| 📍 IP 自动定位 | 启动时自动获取经纬度并写回配置，也可手动指定 |
| 🔊 环境音 50% | 雨声自动设为 50% 音量并取消静音 |
| 🎨 尊重自定义窗景 | 晴天/阴天不会覆盖你手动选的窗景（夜晚、樱花、烟花等） |

---

## 📦 安装

1. 安装 [BepInEx 5.4.x](https://github.com/BepInEx/BepInEx/releases)（x64）到游戏根目录
2. 下载 `MyWeatherSyncMod.dll` 与 `Newtonsoft.Json.dll`
3. 放入 `Chill With You/BepInEx/plugins/` 目录
4. 启动游戏，插件会在 **10 秒后**开始首次检测

> `Newtonsoft.Json.dll` 如果游戏目录里已经存在（其他插件带的），可以不放。

---

## ⚙️ 配置

配置文件：`BepInEx/config/com.yourname.weathersync.cfg`（首次启动后生成）

### `[General]`

| 设置 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `EnableWeatherSync` | bool | `true` | 是否同步真实天气 |
| `EnableTimeSync` | bool | `true` | 是否同步昼夜与时间窗景背景 |
| `PreferFullWeather` | bool | `true` | `true` 时激活雨雪窗景视觉；`false` 则只保留多云背景与环境音 |

### `[Location]`

| 设置 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `AutoLocate` | bool | `true` | 用 IP 自动定位。关闭后使用下面的手动坐标 |
| `Latitude` | double | `31.3093` | 纬度（−90 ~ 90），自动定位成功后会写回这里 |
| `Longitude` | double | `120.6020` | 经度（−180 ~ 180） |

### `[Update]`

| 设置 | 类型 | 默认值 | 说明 |
|------|------|--------|------|
| `RefreshMinutes` | int | `30` | 检测间隔（分钟）。程序内部下限为 5 分钟 |
| `WeatherLookAheadHours` | int | `3` | 判定「是否在下雨」时往后看几小时（0–24），见下节 |
| `WeatherGridRadius` | double | `0.09` | 采样网格半径（度），`0.09` ≈ 10km，见下节 |

> ⚠️ 坐标为 `0,0` 或超出范围时会被替换为默认值，避免请求落到几内亚湾。

---

## 🌤️ 天气判定逻辑

### 为什么不用 `current.weather_code`

早期版本直接读 Open-Meteo 的 `current`（瞬时快照），有两个实测出来的问题：

**问题 1：瞬时值会漏判间歇性降雨**

雨是断断续续的，而插件每 30 分钟才采样一次，很容易正好落在两场雨的间隙里。
实测 12:48 时 `current.weather_code=3`（阴）、`precipitation=0.00mm`，
但 `hourly` 显示 15:00 起有降水 —— 只看瞬时值必然判成晴天。

→ **改为在 `hourly` 序列上取窗口：前 1 小时 ~ 后 `WeatherLookAheadHours` 小时**。
窗口内任意一小时满足「降水量 ≥ 0.1mm」或「降水概率 ≥ 60%」即视为降水。

**问题 2：IP 定位的坐标本身不准**

实测插件通过 IP 拿到的 `31.3093, 120.6020` 与江阴市中心 `31.92, 120.28` 相差约 70km，
同一时刻气温 `31.5°C` vs `26.1°C`、降水 `0.00mm` vs `0.70mm`
—— 在错误的点上查天气，后面判得再对也是错的。

→ **改为在坐标周围取 3×3 网格**（Open-Meteo 支持一次请求多个坐标，仍然只发一个请求），
任意一点判定为降水即按降水处理。气温取离请求坐标最近的采样点。

### 参数怎么调

| 想要的效果 | 怎么调 |
|-----------|--------|
| 更早开启雨景（预报式，提前知道要下雨） | 调大 `WeatherLookAheadHours`（如 `6`） |
| 只在真正下雨时才开雨景（严格实时） | 调小 `WeatherLookAheadHours`（`0` = 只看当前小时） |
| IP 定位偏差大、想让判定更宽松 | 调大 `WeatherGridRadius`（如 `0.2` ≈ 22km） |
| 已有精确坐标、不想被周边天气影响 | `WeatherGridRadius = 0`，并手动填精确经纬度 |

> 💡 如果你的 IP 定位明显偏离实际位置，**最有效的做法是直接手动填经纬度**
> （地图上右键即可复制），再把 `AutoLocate` 设为 `false`。

### WMO 天气码映射

| WMO 代码 | 含义 | 游戏内表现 |
|----------|------|-----------|
| `0` | 晴 | 无降水 |
| `1` `2` `3` | 少云 / 多云 / 阴 | 无降水（不覆盖你的窗景） |
| `45` `48` | 雾 | 无降水 |
| `51` `53` `55` `56` `57` | 毛毛雨 / 冻雨 | 小雨 |
| `61` `63` `65` `66` `67` | 降雨 / 冻雨 | 大雨 |
| `80` | 阵雨（弱） | 小雨 |
| `81` `82` | 阵雨（中/强） | 大雨 |
| `71` `73` `75` `77` `85` `86` | 降雪 / 阵雪 | 雪 |
| `95` `96` `99` | 雷暴 | 雷雨 |

> 游戏里雨的窗景只有 3 个（`LightRain` / `HeavyRain` / `ThunderRain`），
> 所以 WMO 的 6 档雨强会**归并**进这 3 个桶：大雨(65)、暴雨(82) 都归到「雨」。

### 背景切换规则

**① 时间窗景跟随真实时间**

插件按存档里的时间节点（`TimeDayStart` / `TimeSunsetStart` / `TimeNightStart`）
加上真实时钟，算出当前该用哪个时间窗景（Day / Sunset / Night），每轮同步刷新。

> 插件**不读写游戏自己的「自动时间窗景」开关**（`IsActiveAuto`）—— 那是你的设置。
> 因为游戏自带的 `ApplyTimeOfDayFromCurrentTime()` 在该开关关闭时会直接 return，
> 插件改为自己计算，所以无论那个开关开着还是关着，时间窗景都会跟随真实时间。

**② 白天降水 → 多云背景**

- 白天（早晨 / 白天 / 下午）有雨或雪 → 背景换成 `Cloudy`
- 黄昏且下雪 → 换成 `Cloudy`
- **夜晚永远不换成多云** —— 雨雪直接叠加在夜景之上

降水结束后，背景自动切回真实时间对应的窗景。

**③ 绝不覆盖你的自定义窗景**

插件每次同步先查询当前激活的是不是时间类窗景。如果是**烟花 / 樱花 / 深海**等
你自己选的窗景，插件**只处理雨雪与环境音，完全不碰背景**。

**④ 不强开未解锁的窗景**

夜晚等时间窗景需要解锁，未解锁时保持当前窗景并记录日志。

---

## 🔍 日志与排错

日志位置：`Chill With You/BepInEx/LogOutput.log`

### 正常启动的样子

```
[Info :Real-Time Weather Sync] Loading [Real-Time Weather Sync 1.3.1]
[Info :Real-Time Weather Sync] === AWAKE START ===
[Info :Real-Time Weather Sync] [SyncLoop] 循环已启动，10s 后开始检测……
[Info :Real-Time Weather Sync] Auto-located: 31.3093, 120.6020
[Info :Real-Time Weather Sync] [Sync] Code:95 (Thunderstorm) Temp:31.5°C → 降水:ThunderRain
[Info :Real-Time Weather Sync] [Sync] 9点网格 | 当前小时 #5 12时*3/0mm/4% | ...
[Info :Real-Time Weather Sync] [Sync] TimeOfDay:Day, forceCloudy:True, isDay:True
[Info :Real-Time Weather Sync] [Apply] 降水窗景已激活：ThunderRain
[Info :Real-Time Weather Sync] [Apply] 白天有降水，背景已切换为 Cloudy。
[Info :Real-Time Weather Sync] [Sync] Done.
```

关键诊断信息有两处：

- `9点网格 | 当前小时 #N 12时*code/mm/prob%` —— `*` 标记当前小时，`#N` 是采样点序号
  （`#5` 是中心点），可以直接看出判定依据来自哪个点的哪个小时。
- `[Apply]` 开头的行 —— 说明本次对窗景做了什么。常见的有：

| 日志 | 含义 |
|------|------|
| `背景按真实时间切换为 Day/Sunset/Night。` | 时间窗景跟随真实时间 |
| `白天有降水，背景已切换为 Cloudy。` | 白天降水 → 多云 |
| `当前是玩家自定义窗景（烟花/樱花等），不覆盖，只处理雨雪。` | 保护你的自定义窗景 |
| `时间窗景 xxx 尚未解锁，保持当前窗景。` | 该窗景未解锁，不强开 |

### 常见问题

| 现象 | 原因 / 处理 |
|------|------------|
| 日志里版本号不是最新的 | DLL 没替换成功。**游戏运行时会锁定 DLL，必须先完全退出游戏再覆盖** |
| 没有 `[SyncLoop] 循环已启动` | 插件没加载成功，检查 BepInEx 版本与 DLL 是否放在 `plugins/` |
| 一直刷 `VContainer 尚未就绪` | 还没进入房间场景，属正常；进入后会自行开始检测 |
| `[Sync] 天气请求失败，本次跳过` | 网络问题或 Open-Meteo 不可达，会保持当前窗景，下一轮重试 |
| 天气跟实际不符 | 先看日志里的 `Auto-located` 坐标是否接近你的真实位置，偏差大就手动填经纬度并关掉 `AutoLocate` |
| 判定太保守/太激进 | 调 `WeatherLookAheadHours`（时间窗口）和 `WeatherGridRadius`（空间范围） |
| 窗景不跟时间走 | 看是否有 `保留自定义窗景` —— 说明当前是樱花/烟花等，插件按设计不覆盖。切回时间窗景即可恢复 |
| `已关闭 X（场景关闭=False）` | 场景侧关闭失败，请附日志提 issue |
| `状态不一致 X: 场景=True 存档=False` | 检测到历史遗留的不一致状态，插件会自动清理，属正常自愈 |

---

## 🔨 从源码构建

需要 Visual Studio 2022/2026（含 .NET Framework 4.7.2 目标包）。

项目 `MyWeatherSyncMod.csproj` 通过相对路径引用游戏程序集：

```
..\..\..\SteamLibrary\steamapps\common\Chill with You Lo-Fi Story\...
```

**如果你的游戏不在 `D:\SteamLibrary\...`，需要改这些 `HintPath`。**

编译（Release）：

```powershell
& "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" `
  "MyWeatherSyncMod.csproj" /p:Configuration=Release
```

产物：`bin/Release/MyWeatherSyncMod.dll` → 复制到 `BepInEx/plugins/`

### 依赖的游戏程序集

`Assembly-CSharp.dll`、`VContainer.dll`、`R3.dll`、`R3.Unity.dll`、`FastEnum.dll`、
`UnityEngine.CoreModule.dll`、`UnityEngine.UnityWebRequestModule.dll`

---

## 🧩 实现要点

给想改代码的人留的笔记：

- **同步循环用 Unity 协程**，跑在自建的 `DontDestroyOnLoad` GameObject 上。
  BepInEx 会在 chainloader 启动完成后销毁插件自身对象，所以循环**不能**依赖插件生命周期，
  也不要用 `async void + Task.Delay`（会因同步上下文/销毁而静默失联）。
- **网络用 `UnityWebRequest`**，非阻塞，不卡主线程。
- **开关窗景必须同时做两件事**（游戏 UI 也是这么做的）：
  `EnvironmentDataService.SetViewActive()` 只写存档，
  `EnvironmentApplicationService.ApplyWindow()` 才真正改场景 GameObject。
  只调前者的话画面上雨不会停。
- **判断当前是否有雨靠实时查询**（`WindowViewService.IsActiveWindow` +
  `EnvironmentDataService.IsWindowActive`），不要用插件自己缓存的布尔值 ——
  玩家手动开的雨，缓存里是不知道的。
- **`forceCloudy` 只在白天/黄昏下雪生效**，夜晚保持夜景。
- **天气判定不要只看瞬时值**。`current.weather_code` 是某一刻的快照，
  间歇性降雨会被完全漏掉；要在 `hourly` 上取时间窗口。
  同理，IP 定位给的坐标有几十公里误差，单点查询可能查到完全不同的天气。
- **Open-Meteo 支持一次请求多个坐标**（`latitude=a,b,c&longitude=x,y,z`），
  此时响应是**数组**而不是对象，且返回顺序**不保证** ——
  需要哪个点的数据要按经纬度距离匹配，不能写死索引。
- **不要去改游戏自己的「自动时间窗景」开关**（`AutoTimeWindowChangeData.IsActiveAuto`）。
  那是玩家设置。而 `AutoTimeWindowViewChanger.ApplyTimeOfDayFromCurrentTime()` 第一行就是
  `if (!IsActiveAuto) return;` —— 开关关着时它无效，所以插件必须自己按真实时钟算窗景。
- **改背景前先确认当前是不是时间类窗景**（Day/Sunset/Night/Cloudy 四者互斥）。
  玩家自选的烟花/樱花等不能被覆盖。
- **时间窗景需要解锁**（夜晚等），切换前用
  `UnlockEnvironment.GetLockState(EnvironmentType).IsLocked` 检查，未解锁不要强开。

---

## 📄 许可证

MIT License
