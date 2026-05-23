# XInput —— 纯字符串驱动的输入系统

## 概述

XInput 是一个**解耦**的输入抽象层，不依赖任何特定的游戏类型或动作名称。

- **纯字符串 API**：所有输入查询以 `string` 为参数（如 `"Jump"`, `"Move"`），框架不预设任何动作。
- **多实现切换**：默认基于 Unity Input System，可切换为 Rewired 等任何底层实现。
- **自动设备检测**：支持键盘/鼠标、手柄、触摸的自动识别，以及手柄类型（Xbox/PS4/PS5/Switch）检测。
- **零 GC 分配**：方法直接基于字符串缓存字典，无装箱，无额外堆分配。

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

## 快速开始

### 1. 输入动作资源

确保项目中存在 `InputSystem_Actions.inputactions` 并放入 `Resources/` 文件夹。
在 `.inputactions` 中自由定义你的 Action（如 `Jump`, `Move`, `Fire`, `Interact` 等），
**不需要与框架中的任何常量对应**。

### 2. 初始化

```csharp
using XFramework.XInput;

void Awake()
{
    InputManager.Initialize();  // 默认使用 InputSystemProvider
    // 或注入自定义实现：
    // InputManager.Initialize(new MyCustomProvider());
}

void Update()
{
    InputManager.Tick();  // 每帧调用
}

void OnDestroy()
{
    InputManager.Destroy();
}
```

### 3. 读取输入（纯字符串 API）

```csharp
// 按钮事件
if (InputManager.WasPressedThisFrame("Jump"))
    Debug.Log("按下跳跃");

if (InputManager.WasReleasedThisFrame("Interact"))
    Debug.Log("释放交互");

if (InputManager.IsPressed("Sprint"))
    playerSpeed *= 1.5f;

// 值输入（平滑后）
Vector2 move = InputManager.ReadVector2("Move");
float throttle = InputManager.ReadFloat("Throttle");

// 原始值（不平滑，适合走相机等）
Vector2 lookRaw = InputManager.ReadVector2Raw("Look");

// 长按时长
float pressDuration = InputManager.GetButtonPressDuration("Jump");
```

### 4. ActionMap 切换

```csharp
// 切换到 UI 模式（禁用所有其他 ActionMap）
InputManager.SwitchActionMap("UI");

// 返回游戏模式
InputManager.SwitchActionMap("Player");

// 叠加启用（不禁用其他）
InputManager.EnableActionMap("Debug");

// 禁用指定
InputManager.DisableActionMap("UI");
```

### 5. 设备信息

```csharp
// 当前活跃设备类型
InputDeviceType deviceType = InputManager.LastActiveDeviceType;  // KeyboardMouse / Gamepad / Touch / None

// 当前手柄类型
GamepadType gamepadType = InputManager.ActiveGamepadType;  // Xbox / PS4 / PS5 / SwitchPro / Generic / None

```

### 6. 手柄振动

```csharp
// 持续振动
InputManager.SetVibration(0, 0.5f, 0.5f, 2.0f);

// 手动停止
InputManager.StopVibration(0);

// 停止所有手柄振动
InputManager.StopAllVibration();
```

### 7. 按键绑定（UI 提示 & 持久化）

```csharp
// 获取当前设备对应的按键显示名（如键盘返回 "W"，手柄返回 "X 按钮"）
string displayName = InputManager.GetBindingDisplayString("Jump");
uiPromptText.text = displayName;

// 获取某个动作的所有绑定信息（用于按键设置 UI）
IReadOnlyList<InputBindingInfo> bindings = InputManager.GetBindings("Jump");
foreach (var b in bindings)
{
    Debug.Log($"绑定: {b.DisplayName} | 设备组: {b.Group} | 复合: {b.IsComposite}");
}

// 保存自定义按键设置到 PlayerPrefs
string overridesJson = InputManager.SaveBindingOverrides();
PlayerPrefs.SetString("InputOverrides", overridesJson);

// 下次启动时恢复
string saved = PlayerPrefs.GetString("InputOverrides", "");
InputManager.LoadBindingOverrides(saved);

// 重置按键
InputManager.ResetBindingOverrides("Jump");   // 重置单个
InputManager.ResetAllBindingOverrides();      // 重置所有
```

### 8. 交互式按键重绑定（按键设置 UI）

> GetBindings 现在会自动过滤复合绑定（如 WASD 组合），只返回最终可绑定的普通绑定项。
> 每个 InputBindingInfo 现在包含 `IsOverridden` 字段，UI 可据此显示"重置为默认"按钮。

```csharp
// 获取某个动作的所有可绑定项（已过滤复合绑定）
IReadOnlyList<InputBindingInfo> bindings = InputManager.GetBindings("Jump");
foreach (var b in bindings)
{
    // b.Id            — 绑定唯一标识
    // b.DisplayName   — 当前人类可读名称（如 "W"、"X 按钮"）
    // b.Group         — 设备分组（"Keyboard&Mouse"、"Gamepad"）
    // b.IsOverridden  — 用户是否已覆盖此绑定（UI 可据此显示"重置"按钮）

    Debug.Log($"{b.DisplayName} ({b.Group}) 覆盖:{b.IsOverridden}");
}

// 开始重新绑定：用户选择一个绑定项后，调用 StartRebinding
var rebindOp = InputManager.StartRebinding("Jump", bindings[0].Id);

// 绑定完成事件
rebindOp.OnCompleted += (newBinding) =>
{
    Debug.Log($"新按键: {newBinding.DisplayName}");

    // 保存到 PlayerPrefs
    string overridesJson = InputManager.SaveBindingOverrides();
    PlayerPrefs.SetString("InputOverrides", overridesJson);
};

// 绑定取消事件（如用户按了 Esc）
rebindOp.OnCancelled += () =>
{
    Debug.Log("重绑定已取消");
};

// 实时预览按键名（可选）
rebindOp.OnPotentialMatch += (keyName) =>
{
    waitingPromptText.text = $"请按下新按键... 当前检测: {keyName}";
};

// 可在任意时刻取消
// rebindOp.Cancel();
```

### 9. 响应式订阅（可选）

> 替代轮询 `Update()` 的声明式输入方式。所有 `ObserveXxx` 方法返回 `IDisposable`，传入 `this` 可自动随组件销毁取消订阅。
> 公共 API 零依赖 R3 — 调用方不需要了解任何响应式库。

#### 按钮事件

```csharp
using XFramework.XInput;
using UnityEngine;

public class PlayerController : MonoBehaviour
{
    void Awake()
    {
        // 按下事件（替代 WasPressedThisFrame 轮询）
        InputManager.ObservePressed("Jump", OnJump, this);
        InputManager.ObserveReleased("Guard", OnUnguard, this);

        // 按住状态变化（true/false 切换时回调）
        InputManager.ObserveHeld("Sprint", held => playerSpeed = held ? sprintSpeed : normalSpeed, this);

        // 持续按下时长（秒，仅值变化时回调）
        InputManager.ObservePressDuration("Charge", duration => chargeBar.fillAmount = duration / maxCharge, this);
    }

    void OnJump() => Debug.Log("Jump!");
    void OnUnguard() => isGuarding = false;
}
```

#### 值输入

```csharp
void Awake()
{
    // Vector2 轴（如移动摇杆），仅值变化时回调，内置 DistinctUntilChanged
    InputManager.ObserveVector2("Move", v => transform.Translate(v * speed * Time.deltaTime), this);

    // float 轴（如扳机键），值变化时回调
    InputManager.ObserveFloat("Throttle", f => enginePower = f, this);

    // 原始值（不平滑），适合相机等场景
    InputManager.ObserveVector2Raw("Look", delta => camera.Rotate(delta), this);
}
```

#### 手动管理生命周期

```csharp
// 不传 context 时，需要手动保存返回的 IDisposable 并自行 Dispose
IDisposable sub = InputManager.ObservePressed("Fire", OnFire);
// 稍后取消：
sub.Dispose();
```

#### 与轮询模式的对比

| 轮询模式                         | 响应式订阅                            |
| -------------------------------- | ------------------------------------- |
| `void Update()` 中每帧 `if` 判断 | `Awake` 中一行注册，回调自动触发      |
| 需要手动管理状态（如上一帧值）   | 内置 `DistinctUntilChanged`，自动去重 |
| 需要记得在 `OnDestroy` 中清理    | 传入 `this` 自动绑定生命周期          |

两种模式可混合使用，不影响现有代码。

## 自定义输入封装（第三方游戏必须做）

由于框架不定义任何动作名，每个游戏应**自行封装**专属的输入类。

### 示例：动作游戏角色输入

```csharp
namespace MyGame.Input
{
    // 定义你游戏的动作字符串常量
    public static class GameActions
    {
        public const string Move = "Move";
        public const string Look = "Look";
        public const string Jump = "Jump";
        public const string Attack = "Attack";
        public const string Dodge = "Dodge";
        public const string Sprint = "Sprint";
        public const string Interact = "Interact";
    }

    public struct PlayerInputData
    {
        public UnityEngine.Vector2 Move;
        public UnityEngine.Vector2 Look;
        public bool JumpDown;
        public bool AttackDown;
        public bool DodgeDown;
        public bool SprintHeld;
        public bool InteractDown;
    }

    public static class PlayerInputReader
    {
        public static PlayerInputData Read()
        {
            return new PlayerInputData
            {
                Move    = XFramework.XInput.InputManager.ReadVector2(GameActions.Move),
                Look    = XFramework.XInput.InputManager.ReadVector2(GameActions.Look),
                JumpDown     = XFramework.XInput.InputManager.WasPressedThisFrame(GameActions.Jump),
                AttackDown   = XFramework.XInput.InputManager.WasPressedThisFrame(GameActions.Attack),
                DodgeDown    = XFramework.XInput.InputManager.WasPressedThisFrame(GameActions.Dodge),
                SprintHeld   = XFramework.XInput.InputManager.IsPressed(GameActions.Sprint),
                InteractDown = XFramework.XInput.InputManager.WasPressedThisFrame(GameActions.Interact),
            };
        }
    }
}
```

### 示例：RTS 游戏输入封装

```csharp
namespace MyRTSGame.Input
{
    public static class RTSActions
    {
        public const string CameraMove = "CameraMove";
        public const string CameraZoom = "CameraZoom";
        public const string CameraRotate = "CameraRotate";
        public const string Select = "Select";
        public const string BoxSelect = "BoxSelect";
        public const string Command = "Command";
        public const string CycleUnitGroup = "CycleUnitGroup";
    }

    public struct RTSInputData
    {
        public UnityEngine.Vector2 CameraPan;
        public float CameraZoom;
        public float CameraRotation;
        public bool SelectDown;
        public bool CommandDown;
        public bool CycleUnitGroupDown;
    }

    public static class RTSInputReader
    {
        public static RTSInputData Read()
        {
            return new RTSInputData
            {
                CameraPan     = XFramework.XInput.InputManager.ReadVector2(RTSActions.CameraMove),
                CameraZoom    = XFramework.XInput.InputManager.ReadFloat(RTSActions.CameraZoom),
                CameraRotation = XFramework.XInput.InputManager.ReadFloat(RTSActions.CameraRotate),
                SelectDown    = XFramework.XInput.InputManager.WasPressedThisFrame(RTSActions.Select),
                CommandDown   = XFramework.XInput.InputManager.WasPressedThisFrame(RTSActions.Command),
                CycleUnitGroupDown = XFramework.XInput.InputManager.WasPressedThisFrame(RTSActions.CycleUnitGroup),
            };
        }
    }
}
```

### 示例：UI 输入封装

```csharp
namespace MyGame.Input
{
    public static class UIActions
    {
        public const string Navigate = "Navigate";
        public const string Submit = "Submit";
        public const string Cancel = "Cancel";
        public const string TabLeft = "TabLeft";
        public const string TabRight = "TabRight";
    }

    public struct UIInputData
    {
        public UnityEngine.Vector2 Navigate;
        public bool SubmitPressed;
        public bool CancelPressed;
        public bool TabLeftPressed;
        public bool TabRightPressed;
    }

    public static class UIInputReader
    {
        public static UIInputData Read()
        {
            return new UIInputData
            {
                Navigate      = XFramework.XInput.InputManager.ReadVector2(UIActions.Navigate),
                SubmitPressed = XFramework.XInput.InputManager.WasPressedThisFrame(UIActions.Submit),
                CancelPressed = XFramework.XInput.InputManager.WasPressedThisFrame(UIActions.Cancel),
                TabLeftPressed  = XFramework.XInput.InputManager.WasPressedThisFrame(UIActions.TabLeft),
                TabRightPressed = XFramework.XInput.InputManager.WasPressedThisFrame(UIActions.TabRight),
            };
        }
    }
}
```

## 关键原理

### 字符串查找与缓存

`InputSystemProvider` 内部维护一个 `Dictionary<string, InputAction>` 缓存，**首次查找后缓存** `InputAction` 引用，后续调用直接命中缓存，零开销。

### GC 友好

- 字符串常量 `"Jump"` 等为编译时常量，不产生 GC。
- 缓存字典仅在首次访问时分配一次。
- 所有方法的输入和输出都是原生类型（bool, float, Vector2），无装箱。

### ActionMap 管理

- `SwitchActionMap` = 禁用所有 + 启用指定（互斥模式）
- `EnableActionMap` = 叠加启用（不冲突的操作模式并存，如 Gameplay + Debug）

## 扩展：自定义 IInputProvider（如 Rewired）

实现 `IInputProvider` 接口即可切换底层：

```csharp
public class RewiredProvider : IInputProvider
{
    private Rewired.Player _player;

    public void Initialize()
    {
        // Rewired 初始化逻辑
    }

    public bool WasPressedThisFrame(string action, uint playerId = 0)
    {
        return _player.GetButtonDown(action);
    }

    // ... 实现其他接口方法
}
```

## 依赖

- Unity 2022.3 LTS 或更新版本
- Unity Input System Package（用于默认实现）
  ```json
  {
    "com.unity.inputsystem": "1.7.0"
  }
  ```
- 可选：Rewired（如需自定义实现）

## 版本记录

| 版本  | 说明                                                                                                                                                                         |
| ----- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 2.3.0 | 新增响应式输入订阅 API：`ObservePressed`、`ObserveReleased`、`ObserveHeld`、`ObservePressDuration`、`ObserveVector2`、`ObserveFloat`、`ObserveVector2Raw`、`ObserveFloatRaw` |
| 2.2.0 | 新增 `IRebindingOperation` 接口与 `StartRebinding` 交互式按键重绑定 API；`GetBindings` 过滤复合绑定并填充 `IsOverridden`；新增 `SystemRebindingOperation` 实现               |
| 2.1.0 | 新增运行时绑定 API：`GetBindingDisplayString`、`GetBindings`、`SaveBindingOverrides`、`LoadBindingOverrides`、`ResetBindingOverrides`、`ResetAllBindingOverrides`            |
| 2.0.0 | **破坏性重构**：移除 PlayerInputState 和 InputActions，改为纯字符串 API；所有游戏须自行封装输入                                                                              |
| 1.0.0 | 初始版本                                                                                                                                                                     |
