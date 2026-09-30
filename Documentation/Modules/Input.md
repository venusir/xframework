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
