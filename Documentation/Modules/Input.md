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
