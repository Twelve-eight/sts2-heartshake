# HeartShake - DEVELOP.md

设计/契约文档. 独立小 mod: 在 Act4Heart (工坊 3747537811) 的尖塔之心 (Corrupt Heart)
战斗中还原一代原版的心跳屏幕震动 + 心跳音效.

## 1. 一代原版行为 (权威证据, 来自 desktop-1.0.jar 反编译)

来源: `com/megacrit/cardcrawl/monsters/ending/CorruptHeart.class` +
`com/megacrit/cardcrawl/helpers/HeartAnimListener.class` +
`images/npcs/heart/skeleton.json`.

- 心脏 spine 动画: `images/npcs/heart/skeleton.json`, 唯一动画 `idle`,
  周期 2.0s, `setAnimation(0, "idle", loop=true)` + `TrackEntry.setTimeScale(1.5f)`
  -> 实际周期 **2.0 / 1.5 = 1.3333s**.
- 动画内事件: `maxbeat` @ t=0.3666, `smallbeat` @ t=0.9333 和 t=1.3.
  `HeartAnimListener.event()` **只响应 maxbeat** (smallbeat 无处理逻辑):
  - 音效: `CardCrawlGame.sound.playAV("HEART_SIMPLE", random(-0.05,0.05), 0.75)`
    -> 文件 `audio/sound/SLS_SFX_HeartBeat_Simple_v1.ogg` (27.5KB).
  - 震动: `CardCrawlGame.screenShake.shake(LOW, SHORT, false)`
    = 强度 20.0f * Settings.scale, 时长 0.3s, 水平 (vertical=false),
    intervalSpeed 0.3.
- 前置条件: `AbstractDungeon.isScreenUp == false` (有屏幕盖住时不触发).
- 死亡时 `die()` 里 removeListener -> 心跳停止.
- **注意**: 心脏没有 update() 重写, 无动态 timeScale. "心跳随玩家 HP 降低而
  加快" 在一代代码中不存在 (启动文档的说法是错的, 已用字节码证据排除).
  心跳恒定 1.3333s/拍直到死亡.

## 2. StS2 / Act4Heart 现状 (反编译 + pck 扫描证据)

- Act4Heart.dll (v1.1.7, Dolso 框架) 注册 `CorruptHeart : MonsterModel`,
  Id.Entry = `CORRUPT_HEART` (StringHelper.Slugify(类名), loc 表
  `CORRUPT_HEART.name` 佐证). 遭遇 `CorruptHeartBoss`, Id = `CORRUPT_HEART_BOSS`.
- StS2 的心脏 spine (pck 内 creature_visuals/corrupt_heart) **无 beat 事件**
  (pck 二进制扫描: maxbeat/smallbeat/HeartBeat 字符串零命中), 所以不能像
  一代那样挂动画事件监听 -> 必须自建计时器.
- BeatOfDeathPower (Act4Heart 移植的死亡节拍) 是打牌触发, 与心跳节奏无关.
- StS2 震动 API: `NGame.Instance.ScreenShake(ShakeStrength, ShakeDuration,
  float degAngle = -1)`. 强度表: VeryWeak=2, Weak=5, Medium=20, Strong=40,
  TooMuch=80; 时长表: Short=0.3s, Normal=0.8s, Long=1.2s (NScreenShake.cs).
  Multiplier 尊重玩家设置 (NScreenshakePaginator.GetShakeMultiplier).
- 音效播放: `NDebugAudioManager.Instance.Play(string, float volume,
  PitchVariance)` - Act4Heart 自己也这么放 SOTE_SFX_FastBlunt_v2.mp3.
- 战斗房间节点: `NCombatRoom` (`MegaCrit.Sts2.Core.Nodes.Rooms`), `_Ready()`
  里 `NGame.Instance.SetScreenShakeTarget(SceneContainer)`. 静态 `Instance`
  属性 = `NRun.Instance?.CombatRoom`.

## 3. 方案

Harmony patch `NCombatRoom._Ready` (postfix): 检查当前遭遇怪物, 若含
`CORRUPT_HEART` 则启动心跳控制器 (Godot Node, 挂到 NCombatRoom 子节点):

- 每 1.3333s 一次: `NGame.Instance?.ScreenShake(ShakeStrength.Weak,
  ShakeDuration.Short)` + 心跳音效.
- 强度选择: 一代 LOW=20/100 (最大 HIGH), StS2 Weak=5/80. 比例 0.20 vs
  0.0625. 若按比例 StS2 应选 VeryWeak(2/80=0.025)~Weak(5/80) 之间. 取
  Weak: 一代心跳是"持续整个战斗的背景脉冲", 太弱会没存在感, 一代 LOW
  实际观感偏弱但每 1.3s 一次累计明显. 默认 Weak, 可配置.
- 音效: 打包 SLS_SFX_HeartBeat_Simple_v1.ogg (一代原版文件, CCP 对 StS1
  mod 生态一贯宽松; 我们只在自己 mod 内播放, 不修改上游) 到 HeartShake.pck.
  实现方式(已定案, 见 DEVLOG Session 2): ResourceLoader 看不到 pck 内的裸
  ogg, 因此改用 FileAccess.GetFileAsBytes 读原始字节 + AudioStreamOggVorbis
  .LoadFromBuffer, 播放节点是自建 AudioStreamPlayer(Bus "SFX").
  NDebugAudioManager 未使用 - 上面那条 [INFERENCE] 与其 fallback 方案在实机
  上就是最终实现. 音效可由配置开关关闭.
- 生命周期: 控制器 node 监听战斗结束 (心脏死/玩家死/房间退出). 简化:
  node 挂在 NCombatRoom 下, 房间销毁时自动一起销毁, `_ExitTree` 停止.
  心脏死亡即时停: 轮询检查 creature.IsDead (1.33s 一拍频率够低, 每拍检查
  心脏是否已死, 死了不再震动; node 仍随房间销毁).
- 屏幕盖住检查: 一代 isScreenUp 检查 - StS2 对应物不明确, 简化为不检查
  (震动只影响 SceneContainer 位置, 无 UI 破坏风险).

## 4. 可选依赖 (AFTP 模式)

csproj: `<A4HDll>G:/steam/steamapps/workshop/content/2868840/3747537811/Act4Heart.dll</A4HDll>`
- `Condition="Exists('$(A4HDll)')"` 才引用 + Publicize + 条件编译 patch 文件.
- 无引用时不 patch 任何东西, mod 空转 (log 一行).
- 实际识别用字符串 `CORRUPT_HEART` (不需要类型引用), 所以 patch 文件甚至
  可以不 using Act4Heart - 但保留 A4H 引用以便未来 hook 其类型. 简化决策:
  **不引用 A4H dll**, 纯字符串识别. Act4Heart 不在时 patch 照常无害运行
  (永远匹配不到 CORRUPT_HEART). 这比条件编译更简单更稳.
  - 修正: manifest.json 里声明依赖 id "Act4Heart" 让 loader 保证加载顺序
    (BaseLib 模板 manifest 支持 dependencies 数组; 格式已实现并随 v0.1.0 发布,
    见 mod/HeartShake.json 的 BaseLib>=3.4.5 + Act4Heart>=1.1.7).

## 5. 交付物

- repo `G:/omp works/sts2-heartshake/`: mod/ (csproj+代码+json), DEVELOP.md,
  DEVLOG.md, workshop/ (staging + VDF + 描述).
- 部署 `G:/steam/steamapps/common/Slay the Spire 2/mods/HeartShake/`.
- GitHub: `https://github.com/Twelve-eight/sts2-heartshake.git`.

## 6. 验证

- 构建 0 错误, PCK packed, mods/ mtime 更新.
- pck 字节级抽查: 心跳 ogg 内嵌.
- 实机心脏战视觉效果: 用户已双确认 (DEVLOG Session 2: "震动/音效现已双确认").
