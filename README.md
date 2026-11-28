# HeartShake（尖塔之心心跳震动 / Corrupt Heart Heartbeat）

在 [Act 4 Heart](https://steamcommunity.com/sharedfiles/filedetails/?id=3747537811) 模组的尖塔之心（Corrupt Heart）战斗中，还原《杀戮尖塔 1》原版的心跳脉冲震动与音效，并提供可选的“死亡节拍由奥斯提（Osty）承受”开关。

> **English**: Restores the Slay the Spire 1 Corrupt Heart heartbeat screen shake and sound (every 1.3333s) in the Act4Heart fight, plus an optional toggle to redirect Beat of Death self-damage to Osty while Osty is alive.

- **Current Version**: `0.1.3`（rebuilt & verified against Slay the Spire 2 **`v0.114.0`**, `min_game_version: 0.109.0`）
- **Steam Workshop**: [3799286717](https://steamcommunity.com/sharedfiles/filedetails/?id=3799286717)
- **Dependencies**:
  - [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127) `>= 3.4.5`
  - [Act 4 Heart](https://steamcommunity.com/sharedfiles/filedetails/?id=3747537811) `>= 1.1.7`

## 特性 / Features

- **原版参数还原**：恒定 `1.3333s` 节拍、对应原版 `LOW / SHORT` 档屏幕震动（遵循游戏震动强度设置）、心跳音效音量 `0.75`（音高随机抖动 `-0.05 ~ +0.05`），心脏阵亡或离开战斗房间时立即停止并释放播放器。
- **可选死亡节拍拦截（默认关闭）**：开启 `BeatOfDeathTargetsOsty` 后，奥斯提存活时由奥斯提承受死亡节拍出牌扣血，阵亡或未召唤时回落玩家。
- **分组开关**：`设置 → Mod 设置 → HeartShake` 提供总开关、音效开关与奥斯提承伤开关。

## 安装 / Installation

- **创意工坊**：订阅 [HeartShake](https://steamcommunity.com/sharedfiles/filedetails/?id=3799286717)、[Act 4 Heart](https://steamcommunity.com/sharedfiles/filedetails/?id=3747537811) 与 [BaseLib](https://steamcommunity.com/sharedfiles/filedetails/?id=3737335127)。
- **手动安装**：从本仓库 [Releases](https://github.com/Twelve-eight/sts2-heartshake/releases) 下载压缩包，解压至 `<Slay the Spire 2>/mods/`，得到 `mods/HeartShake/{HeartShake.dll,HeartShake.pck,HeartShake.json}`（勿与工坊版同时启用）。

## 从源码构建 / Building from source

```bash
dotnet build mod/HeartShake.csproj -c Release \
  -p:Sts2Path="<path-to-Slay-the-Spire-2>" \
  -p:CopyToModsFolderOnBuild=false
```

详细设计与验证记录见 [DEVELOP.md](DEVELOP.md) 与 [DEVLOG.md](DEVLOG.md)。
