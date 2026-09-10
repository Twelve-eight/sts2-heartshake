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

- GitHub: 待建 (Twelve-eight/sts2-heartshake).
- 工坊: staging + VDF 已备; 发布等用户实机验证通过后走
  `.tmp/STEAMCMD-ACCESS.md` 流程 (码前零准备纪律).
