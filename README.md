# 🌦️ Chill With You - Real-Time Weather Sync

自动将《放松时光：与你共享 Lo-Fi 故事》的游戏环境同步为你当地的真实天气和时间。

## 功能

- ☀️ 根据真实日出日落时间自动切换白天/黄昏/夜晚
- 🌧️ 根据真实天气（雨/雪/阴/雷）自动切换游戏环境
- 📍 自动定位（通过 IP），无需手动填经纬度
- 🔊 雨声音量自动设为 50%
- 🎨 不破坏用户自定义窗景（樱花/烟花等）

## 安装

1. 安装 [BepInEx 5.4.23](https://github.com/BepInEx/BepInEx/releases) 到游戏根目录
2. 下载本插件的 `MyWeatherSyncMod.dll` 和 `Newtonsoft.Json.dll`
3. 放入 `BepInEx/plugins/` 目录
4. 启动游戏，插件会自动同步天气

## 配置

配置文件位于 `BepInEx/config/com.yourname.weathersync.cfg`

| 设置 | 说明 | 默认值 |
|------|------|--------|
| EnableWeatherSync | 是否同步真实天气 | true |
| EnableTimeSync | 是否同步昼夜 | true |
| RefreshMinutes | 刷新间隔（分钟） | 30 |
| PreferFullWeather | 是否激活雨窗景视觉 | true |
| AutoLocate | 是否自动定位 | true |

## 许可证

MIT License