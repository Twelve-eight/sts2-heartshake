# DEVLOG - sts2-heartshake

## Session 1 - 2026-09-11: HeartShake v0.1.0 从零到部署

任务来源: `.tmp/BOOTSTRAP-heartshake.md`. 在 Act4Heart (工坊 3747537811, v1.1.7)
的尖塔之心战斗中还原一代原版心跳屏幕震动 + 音效.

### 研究结论 (全部有证据)

1. **启动文档的一个错误已纠正**: "心跳节拍随玩家 HP 降低而加快" 在一代
   代码中不存在. 证据: `CorruptHeart.class` 无 update() 重写, 无动态
   timeScale 调用; 心跳由 spine 动画事件驱动, 恒定速率.
2. **StS1 权威行为** (desktop-1.0.jar 反编译):
   - idle 动画 2.0s, `setTimeScale(1.5f)` -> 周期 1.3333s.
   - 事件: maxbeat@0.3666 (唯一被 HeartAnimListener 处理的),
     smallbeat@0.9333/1.3 (无处理逻辑).
   - maxbeat -> `SoundMaster.playAV("HEART_SIMPLE", random(-0.05,0.05), 0.75)`
     + `ScreenShake.shake(LOW, SHORT, false)`.
   - LOW=20.0f*Settings.scale, SHORT=0.3s, 水平.
   - 前置: `isScreenUp == false`; die() 里 removeListener.
   - 音频文件: `audio/sound/SLS_SFX_HeartBeat_Simple_v1.ogg` (27.5KB).
3. **StS2 侧**:
   - Act4Heart.dll (Dolso 框架): CorruptHeart : MonsterModel,
     Id.Entry = `CORRUPT_HEART` (Slugify), 遭遇 `CORRUPT_HEART_BOSS`.
     pck 的 spine 无 beat 事件 (二进制扫描零命中) -> 必须自建计时器.
   - 引擎震动 API: `NGame.Instance?.ScreenShake(ShakeStrength, ShakeDuration)`;
     Weak=5/Short=0.3s (NScreenShake.cs 强度表 2/5/20/40/80).
   - NDebugAudioManager.Play 只管 res://debug_audio/ (TmpSfx.GetPath),
     不适用于 mod 自带音频 -> 自建 AudioStreamPlayer (Bus "SFX").
   - NCombatRoom._Ready: ActiveCombat 模式下 CreateEnemyNodes 后
     _creatureNodes 完整; CreatureNodes -> Entity.Monster.Id.Entry 可比对.
   - Creature.IsDead / NCreature.Entity / MonsterModel 都是 public, 无需
     Publicize sts2 之外的东西.

### 实现

- 仓库 `G:/omp works/sts2-heartshake/`, mod 骨架复制 mpconfigsync 模板.
- `HeartShakeCode/MainFile.cs`: BaseLib config 注册 + SimpleLoc + PatchAll.
- `HeartShakeCode/HeartShakeConfig.cs`: EnableHeartShake / EnableHeartSound.
- `HeartShakeCode/HeartBeatNode.cs`: Godot Node, _Process 计时 1.3333s,
  每拍 ScreenShake(Weak, Short) + AudioStreamPlayer 播
  res://HeartShake/heartbeat.ogg (vol 0.75, pitch jitter ±0.05).
  心脏 IsDead -> SetProcess(false). Node 挂 NCombatRoom 下随房间销毁.
- `HeartShakeCode/Patches/CombatRoomReadyPatch.cs`: Harmony postfix
  NCombatRoom._Ready, Mode==ActiveCombat 且怪物含 CORRUPT_HEART 才启动.
  纯字符串识别, 无 Act4Heart 编译依赖.
- 资产: StS1 原版 ogg 复制为 `HeartShake/heartbeat.ogg` 进 pck.
- 本地化: eng/zhs settings_ui.json (HEARTSHAKE- 前缀).

### 构建与验证 (2026-09-11 02:55)

- `dotnet build` 0 错误 0 警告, PCK packed, 部署 mods/HeartShake/
  (dll 14336B / json / pck 29139B / pdb).
- pck 字节级: heartbeat.ogg 原样内嵌 offset 128 唯一; 路径
  `HeartShake/heartbeat.ogg` 与 `res://HeartShake/heartbeat.ogg` 吻合;
  localization json x2 内嵌.
- dll 反编译自检: patch 目标/ID 比对/震动参数(ShakeStrength)2=(ShakeDuration)1
  = Weak/Short 全部正确.
- **未自动验证 (移交用户)**: 实机进 Act4Heart 心脏战观察每 1.33s 弱震 +
  心跳声; ResourceLoader 运行时加载 mod pck 内 ogg 的实际播放效果.

### 发布状态

- GitHub: https://github.com/Twelve-eight/sts2-heartshake (已建并推送, 4711937 + 979a1f2).
- 工坊: staging + VDF + 专用推送脚本 workshop/heartshake-push.ps1 已备;
  发布等用户最终确认后走 `.tmp/STEAMCMD-ACCESS.md` 流程 (码前零准备纪律).

## Session 2 - 2026-09-11: 音效修复实测通过 + 心跳/BGM 同步性研究

### 音效修复 (commit 979a1f2, 用户实测: 有心跳声)

用户报告震动正常无声音 -> godot.log 定位: `ResourceLoader.Exists` 对 pck 内
原始 ogg 返回 false (quick packer 无 .import 元数据). 修复:
`Godot.FileAccess.GetFileAsBytes` 读原始字节 +
`AudioStreamOggVorbis.LoadFromBuffer` 运行时构造流; 失败只报一次
(_beatLoadFailed), 不再每拍重试刷日志. 震动/音效现已双确认.

### 心跳是否对齐 BGM 第三拍 - 结论: 否 (三方证据闭环)

用户提问 -> 研究产出已固化到
`sts2-spire1/research/sts1-kb/mechanics/heartbeat.md` (R01-R07,
mechanics 索引规则数 285 -> 292):

1. **代码层 [高]**: TempMusic/MainMusic/MusicMaster 零 setPosition, BGM 恒从
   头播, 无相位输出; 心跳走独立 spine 动画计时, 两系统零通信.
2. **音频分析 [中]**: STS_Boss4_v6.ogg 谱流+comb-filter ->
   ~157.2 BPM (拍 0.3817s, 分段稳定性 10 窗 9 窗 157.0-157.5);
   心跳 1.3333s = 3.493 拍, 不可通约, 相位持续漂移.
   工具链: pip --target G:/omp works/.tmp/pylibs (numpy+soundfile),
   libsndfile 原生解码 ogg, 不写 C 盘.
3. **用户一代实机听感**: 大多数心跳落在两拍之间, 少部分偶合踩拍 - 与
   3.493 拍漂移特征吻合 (连续 6-7 个心跳内相位仅移动 ~5ms/拍, 段落内听感
   "几乎踩拍", 跨段后滑走).

HeartShake 立场: 不做拍对齐 (忠实原版解耦行为), 见 heartbeat.md R07 基线表.

## Session 3 - 2026-09-11: 创意工坊首发成功

- 用户自行执行 heartbeat-push.ps1 (码前零准备流程): PublishFileID **3799286717**,
  Committing update..Success.
- VDF publishedfileid 已回填固化, 后续更新直接重复同一条推送命令即可.
- 工坊页: https://steamcommunity.com/sharedfiles/filedetails/?id=3799286717

## Session 4 - 2026-09-12: BeatOfDeath 改由奥斯提承受 + 本地化键修复

### 用户请求
心脏战斗中出牌受伤(死亡节拍)不会被奥斯提拦截 -> 加一个选项控制.

### 根因
Act4Heart.BeatOfDeathPower.AfterCardPlayed (`G:/omp works/.tmp/a4h-src/Act4Heart.decompiled.cs:859-867`)
打 `(ValueProp)4 = Unpowered` 伤害. Osty 的 DieForYouPower
(`MegaCrit.Sts2.Core.Models.Powers/DieForYouPower.cs:15-20`) 只拦截
`IsPoweredAttack()` (= `Move && !Unpowered`), 所以 4 永远不会被拦截.

### 实现
- `Patches/BeatOfDeathRedirectPatch.cs`: 无 Act4Heart 编译依赖,
  `AccessTools.TypeByName("Act4Heart.BeatOfDeathPower")` 运行时查类型,
  prefix 在 `AfterCardPlayed` 上. 选项关闭或 Osty 已死/未召唤时直接返回 `true`
  (走原版). 选项开启且 Osty 存活时: 复现 `Flash()` + `NDebugAudioManager.Play`,
  然后 `CreatureCmd.Damage(..., owner.Osty, ..., (ValueProp)4, ...)` 并短路原方法.
  保留 `Unpowered` props => 不改变格挡/力量语义.
- `HeartShakeConfig.cs`: `BeatOfDeathTargetsOsty` 默认 `false` (保持原版行为).

### 顺带发现的已发布缺陷: 本地化键 mismatch
BaseLib `SimpleModConfig` 取标签键 = `{ModPrefix}{StringHelper.Slugify(propertyName)}.title`.
`Slugify` 对 camelCase 的规则是 `([A-Za-z0-9]|\G(?!^))([A-Z])` -> `"$1_$2"`,
所以 `EnableHeartShake` -> `ENABLE_HEART_SHAKE`.
现有键 `HEARTSHAKE-ENABLEHEARTSHAKE.title` 与之不匹配 => 设置页显示原始属性名,
hover tip 因 `... .hover.desc not found` 被跳过. 已修正两个旧键, 新增一个键,
均通过真实 .NET 正则验证.

### 构建
0 警告 / 0 错误, PCK packed.

### 未实机验证
死亡节拍改由奥斯提承受的效果需在 Act4Heart 心脏战中, 使用死灵绑定者且召唤了
奥斯提, 并开启选项后才能验证. 代码路径: 前缀命中 -> Osty 存活 -> `Flash` +
`Play` + `CreatureCmd.Damage(..., Osty, ...)`. 若 Act4Heart 未装则补丁静默跳过.

---

## 2026-09-12 (夜) astra-advice 项 8 修复: 类型命名空间 (主会话单线)

- **缺陷**: 死亡节拍补丁按 `Act4Heart.BeatOfDeathPower` 查找, 实机工坊 DLL 里
  该类在 `Act4Heart.Powers` 子命名空间 (证据 heart-type-identities.txt) →
  TypeByName 解析 null → 新功能永远静默 skipped。
- **修复**: 先按验证过的全名 `Act4Heart.Powers.BeatOfDeathPower` 查找, 保留旧
  扁平名作回退 (兼容旧版 Act4Heart); skip 日志口径同步更正。
- **验证**: 隔离构建 0 错误, 已部署实机。已确认的音效/震动补丁未触碰。
  实机验收: 装有 Act4Heart + 开关开启 → 死亡节拍伤害转嫁给 Osty。

## 2026-09-14 astra 第三轮审查交接记录

第三轮隔离构建 exit 0, 0 warnings. 当前源代码首选 `Act4Heart.Powers.BeatOfDeathPower`, 但本轮未对当前 HeartShake DLL 做目标安装或 Act4Heart 战斗. 仍需验证开关, Osty 状态, 格挡/无形/过量伤害, 双端配置和退出生命周期.

证据索引: `G:\\omp works\\astra-advice-evidence\\2026-09-14\\handoff-state.json`. 当前 advice 修改未提交, 未操作游戏或部署.
