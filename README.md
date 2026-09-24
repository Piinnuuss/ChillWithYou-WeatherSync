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

> ⚠️ 坐标为 `0,0` 或超出范围时会被替换为默认值，避免请求落到几内亚湾。

---

## 🌤️ 天气映射规则

使用 WMO weather code（Open-Meteo 标准）：

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

### 背景切换规则

降水发生时，**仅在以下情况**把时间窗景背景换成多云：

- 白天（早晨 / 白天 / 下午）有雨或雪
- 黄昏且下雪

**夜晚永远不强制多云** —— 雨雪直接叠加在夜景之上，保留原版夜晚窗景。

如果你自己关闭了「自动时间窗景」，插件不会去覆盖你的手动选择，只负责下雨。

---

## 🔍 日志与排错

日志位置：`Chill With You/BepInEx/LogOutput.log`

### 正常启动的样子

```
[Info :Real-Time Weather Sync] Loading [Real-Time Weather Sync 1.2.1]
[Info :Real-Time Weather Sync] === AWAKE START ===
[Info :Real-Time Weather Sync] [SyncLoop] 循环已启动，10s 后开始检测……
[Info :Real-Time Weather Sync] Auto-located: 31.3093, 120.6020
[Info :Real-Time Weather Sync] [Sync] Code:2 (PartlyCloudy) Temp:26.1°C → 降水:none
[Info :Real-Time Weather Sync] [Sync] TimeOfDay:Night, forceCloudy:False (...)
[Info :Real-Time Weather Sync] [Apply] 当地无降水，关闭窗景：HeavyRain → 无
[Info :Real-Time Weather Sync] [Apply] 已关闭 HeavyRain（场景关闭=True）
[Info :Real-Time Weather Sync] [Sync] Done.
```

### 常见问题

| 现象 | 原因 / 处理 |
|------|------------|
| 日志里版本号不是最新的 | DLL 没替换成功。**游戏运行时会锁定 DLL，必须先完全退出游戏再覆盖** |
| 没有 `[SyncLoop] 循环已启动` | 插件没加载成功，检查 BepInEx 版本与 DLL 是否放在 `plugins/` |
| 一直刷 `VContainer 尚未就绪` | 还没进入房间场景，属正常；进入后会自行开始检测 |
| `[Sync] 天气请求失败，本次跳过` | 网络问题或 Open-Meteo 不可达，会保持当前窗景，下一轮重试 |
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

---

## 📄 许可证

MIT License
