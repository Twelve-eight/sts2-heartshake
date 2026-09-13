## 第三轮复审 (2026-09-14)

当前隔离构建 exit 0, 0 warning/0 error. 当前源代码已用 `Act4Heart.Powers.BeatOfDeathPower` 作为首选类型名并保留旧名回退;本轮没有加载当前 HeartShake DLL 挂载目标,也没有真实 Act4Heart 战斗.

HEART-1 的类型身份修复仍只能算源码/历史日志支持. 仍未覆盖开关开关, Osty 存在/缺席/死亡,格挡/无形/过量伤害,双端配置一致性,单次 CardPlay 和房间退出节点生命周期. 不把构建或旧 `redirect patch applied` 日志升级成伤害目标已验证.

# Astra advice - HeartShake

日期: 2026-09-12. 主会话单线评估. 原有心跳震动/声音有用户确认, 这条证据保留. 新死亡节拍转给 Osty 的功能是另一条路径, 不能继承旧功能的已验证状态.

本轮副本构建 0 警告/0 错误. 从当前工坊 Act4Heart.dll 实际列举类型, 未启动游戏.

## P1 HEART-1: 死亡节拍补丁查错完整类型名

位置: `mod/HeartShakeCode/Patches/BeatOfDeathRedirectPatch.cs:25-31`.

代码查 `Act4Heart.BeatOfDeathPower`.

当前实际 DLL 存在的是 `Act4Heart.Powers.BeatOfDeathPower`. 工坊路径 `G:/steam/steamapps/workshop/content/2868840/3747537811/Act4Heart.dll`, SHA256 见证据包 binary-inputs.json. 反编译原 hook 也在 namespace Act4Heart.Powers.

结果: targetType=null, 返回 skipped, 配置开关即使打开也没有重定向. 这是类型身份错误, 不是需要用户反复打心脏来确定的偶然问题.

建议匹配正确完整类型+方法签名, 安装后以 Harmony.GetPatchInfo 验证该 MethodInfo 上确有 prefix. optional 缺席与已安装但目标漂移要区分日志/状态. 不用模糊全程序集按短类名抓第一个来掩盖版本耦合.

验收: 当前 A4H 已装 -> patch attached; A4H 真缺席 -> 明确 dormant; 方法签名改变 -> 明确 incompatible, 不称 initialized=成功重定向.

## P2 HEART-2: 打开后的目标语义需独立验收

当前 prefix 直接把原 ValueProp.Unpowered 的伤害目标换成 owner.Osty. 它没有使用 DieForYouPower 的 powered-attack 拦截规则. 这是一个新规则, 不只是修复原版漏拦截.

需检验:

- 默认关闭时原方法完全保持.
- 打牌者无 Osty/未召唤/已死, 原玩家受伤路径保持.
- 有存活 Osty 时只让该玩家的 Osty 受伤, 不使用队友宠物.
- Osty 格挡, Intangible, 伤害超过剩余生命, Osty 在效果链中死亡, 原玩家格挡分别如何作用.
- 是否需要溢出伤害给玩家, 由现有产品约定决定; 不凭直觉新增分摊.
- 双端配置一致, 同一次 CardPlay 只执行一次. 一端开启一端关闭就是玩法分歧, affects_gameplay=true 只声明类别, 不同步配置.

构建成功不覆盖这些分支. 先修类型名, 再实际打一张牌确认, 不直接发布新版.

## P2 HEART-3: 心跳总开关只在 Attach 时读

位置: `HeartBeatNode.cs:53-62,73-95`.

EnableHeartShake=false 只阻止新建 node. 已在心脏战中关闭, _Process 仍震动; EnableHeartSound 却每拍读取, 两个开关行为不一致. 如果开关承诺实时关闭, 应在运行 node 每拍/每帧遵守, 或明确下一场才生效.

生命周期建议保持简单: 一个 room 一个 node, 心脏死/房间销毁停止; 不把播放器挂 NGame 后忘记房间退出的声音尾部. 神经式防重试/复杂音频管理不必要, 先验证节点数不累积和暂停/失败/退出.

## 不应再传播的说明偏差

- 原版固定 2/1.5 秒一拍, 不是低血加速; 原证据有效.
- 文档关于按震动最大值比例选 Weak 的算式不一致, 且实际实现在屏幕方向/首拍时点/遮罩期间触发上并非原版逐帧复刻. 用户确认观感良好可以保留, 不再称数学精确还原.
- ResourceLoader.Exists 不适合裸 ogg 的历史修复是有实机证据的, 不要改回旧路径只为统一 API.
- MainFile 把 PatchAll 与 Apply redirect 包同一个 try, 任一普通 patch 失败也会跳过 redirect. 若要失败隔离, 按能力分别安装并验证.

## 接手顺序

1. HEART-1 精确类型/签名与挂载证明.
2. HEART-2 一场实际战斗矩阵, 记录伤害目标/HP/格挡, 不只截设置开关.
3. HEART-3 live toggle/节点生命周期.
4. 更新版本/文档需要用户决定; 本轮没有 Steam 发布授权.

证据: `../astra-advice-evidence/2026-09-12/heart-type-identities.txt`, `binary-inputs.json`, `build-results.json`. 检查时 DEVLOG.md 有别人的未提交修改, 本轮保留, 不纳入建议提交.

## 附录: 先证明能力可达, 再证明观感与数值

通用流程见 [总建议附录](../astra-advice.md).

1. 把 "mod loaded", "依赖已加载", "目标类型存在", "patch attached", "该次 CardPlay 进入 prefix", "Osty 实际受伤" 分开记录. 前一项不自动证明后一项.
2. 用真缺依赖与目标名写错作对照. 两者都能返回 null, 但一个是合法 dormant, 一个是实现故障; 不用同一条 skipped 日志消除区别.
3. 把有状态对象放到生命周期中测: Attach 前关闭, 已 Attach 后关闭, 房间退出, 心脏死亡, 重进房间. 只读配置属性不能证明已存在 node 停止工作.
4. 对重定向明确是改目标还是复用原拦截协议. owner/Osty/队友/Osty 溢出伤害的关系从契约推导, 不因 "让宠物挡" 就自行新增规则.

最短复验: 正确 DLL 类型身份 -> 实际挂载 -> 开关开/关的同一张牌 -> 无 Osty 的对照 -> 退出房间后无残留 node/节拍. 已确认的旧音效观感不重做无关检查, 新玩法路径单独证明.
