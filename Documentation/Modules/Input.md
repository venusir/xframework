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
  无会话级复位），修法待设计。
- **`StartRebinding` 两因同值**（未初始化 / id 不存在都返回 `null`）：见上。
