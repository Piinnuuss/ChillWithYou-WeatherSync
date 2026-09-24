# 更新日志

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
