# Input —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Input/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Input/
├── IInputProvider.cs                 # 输入提供者接口（核心抽象）
├── IRebindingOperation.cs            # 交互式按键重绑定操作句柄接口
├── InputManager.cs                   # 全局静态外观（静态类，直接调用）
├── InputBindingInfo.cs               # 绑定信息结构体（UI 按键提示用）
├── InputDeviceType.cs                # 输入设备类型枚举
├── GamepadType.cs                    # 手柄类型枚举
├── Messages/                         # 消息定义
│   ├── DeviceConnectedMessage.cs
│   ├── DeviceDisconnectedMessage.cs
│   └── GamepadTypeChangedMessage.cs
├── Default/
│   ├── InputSystemProvider.cs        # 基于 Unity Input System 的默认实现
│   └── SystemRebindingOperation.cs   # Unity Input System 的 IRebindingOperation 实现
└── README.md
```

## 模块版本记录

本表是**模块独立成包时代的本地版本号**（1.0.0–2.4.0），与包 `CHANGELOG.md` 的语义化版本（`0.1.0` / `0.2.0`）**不是同一套**，不要互相映射。这些条目在 CHANGELOG 里基本没有对应记录，保留是为了不丢失本模块的时间线。

| 版本  | 说明                                                                                                                                                                         |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.4.0 | 响应式订阅内部实现更换为自研帧脉冲引擎（移除 R3 依赖）；公共 API 与语义不变（首次必过、相同值去重）                                                                           |
| 2.3.0 | 新增响应式输入订阅 API：`ObservePressed`、`ObserveReleased`、`ObserveHeld`、`ObservePressDuration`、`ObserveVector2`、`ObserveFloat`、`ObserveVector2Raw`、`ObserveFloatRaw` |
| 2.2.0 | 新增 `IRebindingOperation` 接口与 `StartRebinding` 交互式按键重绑定 API；`GetBindings` 过滤复合绑定并填充 `IsOverridden`；新增 `SystemRebindingOperation` 实现               |
| 2.1.0 | 新增运行时绑定 API：`GetBindingDisplayString`、`GetBindings`、`SaveBindingOverrides`、`LoadBindingOverrides`、`ResetBindingOverrides`、`ResetAllBindingOverrides`            |
| 2.0.0 | **破坏性重构**：移除 PlayerInputState 和 InputActions，改为纯字符串 API；所有游戏须自行封装输入                                                                              |
| 1.0.0 | 初始版本                                                                                                                                                                     |

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过，零高危。已修项见 `CHANGELOG`。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层）：

- **不给 `Observe*` 的闭包做池化**：分配发生在订阅期（低频），每帧路径只有一次 `_framePulse.Emit(int)`；
  为它引入池会把句柄生命周期复杂化。C1 类判据的前提是「频度」，这里不成立。
- **不把 `StartRebinding` 的 `null` 拆成「未初始化」与「id 不存在」两种信号**：文档只承诺了后者；拆开要动
  返回值形状（公开 API），而两条路径的调用方处理相同（都该当失败处理）。
- **不给 `Destroy()` 加「退订本模块所有订阅」**：`_framePulse` 是模块级共享流，退订它会连带清掉其它
  订阅者；「句柄归订阅方」是 UI 已定的仓内语义，补文档即可（已补进 README「已知限制」）。
- **不加「接口成员与门面一一对应」的反射断言**：门面**有意**比接口多 16 个成员（`IsInitialized` /
  `Provider` / `SetProvider` / `Initialize(IInputProvider)` / `Destroy` / 8 个 `Observe*` / 3 个 `Subscribe`），
  那条断言会把模块自有能力判成缺陷。守卫只断言「接口声明的成员都有同名转发」。
- **不改 `InputSystemProvider.Initialize(asset)` 里那处预防性调序的行为面**：`SwitchActionMap("Player")`
  挪到设备订阅之前是纯防御（见 `CHANGELOG`），**今天构造不出它抛异常的路径**，故没有为它写用例。

**未决**：

- **跨播放会话的会话级复位**（本轮唯一留给下一轮的中危项）：见 README「已知限制」第 1 条——关闭域重载时
  第二个播放会话帧脉冲静默失效。根因跨模块（`UpdateManager.OnQuitting` 释放调度器 + 门面 `_initialized`
  无会话级复位），修法待设计。**2026-10-01 已实测复现机制**，配方与结论见本节末尾。
- **`StartRebinding` 两因同值**（未初始化 / id 不存在都返回 `null`）：见上。

## 会话级复位：复现配方与实测结论（2026-10-01）

**结论：机制已实测复现，不再是推理。** 且在**单个播放会话内**即可确定性重现——不依赖编辑器是否关闭域重载。

**为什么单会话等价**：缺陷需要两个条件同时成立——「静态状态跨会话存活」与「`Initialize()` 因 `_initialized`
为真而早退」。在单会话内手动调 `UpdateManager.OnQuitting()`（`internal`，测试经 `InternalsVisibleTo` 可见）
即可造出后者：调度器被拆了，而门面的静态字段仍是原值。机制上等价，但确定、可重复。

**复现配方**（探针是一次性的，跑完即删——见下方「为什么不留红的用例」）：

```
① InputManager.Initialize();     // 帧驱动进调度器：UpdateManager.TotalCount == 1
② UpdateManager.OnQuitting();    // 模拟退出拆除 → TotalCount == 0
③ UpdateManager.AutoInit();      // 模拟第二个会话开始（SubsystemRegistration 必重建）→ 新调度器是空的
④ InputManager.Initialize();     // 告警「called more than once」后早退
⑤ UpdateManager.TotalCount       // 实测 **0**，而它应当是 1 —— 缺陷在此
```

实测输出 `Expected: 1, But was: 0`。前三步的前提断言全部通过，所以红点精确落在第 ⑤ 步，而不是「环境不对」。

**两道锁各自都足以造成它**，修的时候两个都要动：

1. `InputManager.Initialize()` 因 `_initialized` 仍为真而早退（`Runtime/Input/InputManager.cs:70-74`）；
2. 即便它不早退，`RegisterTicker()` 的 `if (_ticker != null) return;`（同文件 `:174`）也会拦掉重建
   —— `_ticker` 同样是跨会话存活的静态字段。

**顺带确认的一条**：`InputManager.Destroy()`（同文件 `:129-140`）会同时复位 `_initialized` 与 `_ticker`，
所以**调用方只要调过 `Destroy` 就自愈**。缺陷恰恰在于正常游戏不会调它——`Destroy` 的语义是「释放并重建」，
而游戏在启动时调一次 `Initialize` 之后就不再碰了。

**验收标准**：关闭域重载连跑两遍 PlayMode 全量。注意这条**一个 App 生命周期事件源通不过**
（理由见 `Documentation/Roadmap.md` §三）——缺的不是「事件」，是「谁在会话开始时把静态状态复位」。

**为什么不留一个红的用例**：门禁是「全量 0 失败」，而永久红的用例会训练人忽略红色——本仓在 File / Save
那里吃过这个亏（同一失败消息连红 7 次，被当成环境噪音，见 `ModuleAudit.md` D3）。**下一轮修它时写
「改前必红」的回归用例，那才是对的时刻**；本节的配方足够照着重建。

**同族与其它落点**：

- `AssetManager` / `ConfigManager` / `LocalizationManager` / `FileManager` 同一根因（重复 `Initialize()`
  变 no-op、使用方无法自救），修的时候一并裁定。
- **`Runtime/Asset/InstanceTracker.cs:33` 是一处未登记的落点**：它用**静态构造函数**订阅
  `Application.quitting`，而 `AutoInitTests` 的族清单只扫 `RuntimeInitializeOnLoadMethod` ——它扫不到。
  那处注释写的是「Editor 退出播放模式时由域重载自动重置」，正属「关闭域重载时跨会话」的那一类。
- Update 侧**不在**同族名单里：它是被踩过并已修好的那一半（`OnQuitting` 可重建 + `AutoInit` 幂等重建），
  `UpdateManagerTests.OnQuitting_ThenAutoInit_RebuildsScheduler` 钉着它的自愈。本缺陷是「**客户端**没自愈」。
