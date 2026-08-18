# XFramework 项目规则

## 项目上下文与规范

- Unity 版本: 2022.3 LTS 或更新版本。
- 脚本运行时: .NET Standard 2.1。
- API 兼容级别: .NET Framework。
- 项目模板: 3D 核心或 URP/HDRP，有特殊需求时说明。

### 总体原则
- **原子化:** 每次只专注于完成一个逻辑明确、可独立验证的编码任务。
- **版本控制:** 所有生成的代码都应易于审查和通过 Git 进行原子提交。
- **MCP优先:** 如果 Unity MCP 可用，任何对 Unity Editor 中 GameObject、组件、属性的增删改操作，应优先通过发送 MCP 指令来完成。
- **测试驱动:** 完成逻辑后，应提供简单的测试步骤（如在 `Update` 中添加 `Debug.Log`，或建议添加 `[UnityTest]` 单元测试）。
- **避免臆测:** 如果需求不明确，或者对某个 Unity API 的行为不确定，请先提问，而不是猜测。

### C# 编码规范
- 遵循 Unity 官方 C# 代码规范，并提供具体示例。
- **命名约定:**
    - 公共字段、序列化字段 (`[SerializeField]`) 和属性使用 PascalCase，例如 `PlayerHealth`, `MoveSpeed`。
    - 私有字段使用 camelCase，并以下划线开头，例如 `_rb`, `_animator`。
    - 方法、枚举、结构体和类使用 PascalCase，例如 `CalculateDamage()`。
    - 常量使用 PascalCase，例如 `MaxPlayerCount`。
- **代码结构:**
    - 文件顶部统一导入必要的命名空间 (`using UnityEngine;`, `using System.Collections;` 等)。
    - 在脚本开头定义公共变量，然后是私有变量，接着是 `Awake`, `Start`, `Update` 等 Unity 生命周期方法，最后是自定义方法。
- **代码风格:**
    - 使用大括号 `{}`，并将左括号放在新的一行。
    - 始终使用 `#region` 来分组代码块，例如 `#region Public Variables`, `#region Lifecycle Methods`。
    - 使用 `[SerializeField]` 代替 `public` 暴露私有字段，以保持封装性。
    - 避免在 `Update` 方法中进行昂贵的操作，如 `Camera.main` 或 `FindObjectOfType`，应使用缓存引用。
- **游戏对象查找:** 优先使用 `GetComponent`, `GetComponentInChildren`, `Transform.Find` 等方法，避免使用 `GameObject.Find` 和 `SendMessage`。

## 核心开发原则

- **单一职责:** 每一个 C# 脚本应只负责游戏中的一个核心功能。例如，健康的角色有 `Health` 组件来控制生命值，`PlayerController` 组件来控制移动，`Weapon` 组件来控制武器。
- **组合优于继承:** 推荐使用组件（Component）组合的方式来构建游戏对象（GameObject），而不是创建很深的继承结构。
- **性能优先:**
    - 在 `Update` 方法中，考虑使用 `Input.GetButton` 等输入判断，但要避免每帧进行复杂计算。
    - 缓存频繁使用的组件引用，避免重复 `GetComponent`。
    - 对于频繁创建和销毁的对象（如子弹、粒子效果），**必须**使用对象池（Object Pooling）。
    - 对于仅在特定条件下运行的 `Update`，可通过 `enabled` 属性来启用/禁用此脚本来节省性能。
- **潜在问题预判:**
    - **空引用异常:** 在访问一个组件之前，应检查它是否已被正确赋值，即 `if (_rb != null)`。
    - **数值溢出:** 注意 `int` 或 `float` 在循环中的累加，特别是协程中的 `while` 循环，确保有明确的退出条件。
    - **内存泄漏:** 创建 `Texture2D`, `Mesh`, `Material` 等非托管资源时，应在对象销毁或场景切换时调用 `Destroy()` 或 `Resources.UnloadUnusedAssets()`。

## 文件结构与资产组织

项目的文件夹组织遵循 Unity 最佳实践:

- **Scripts/**: 存放所有 C# 脚本，按功能模块划分子文件夹，如 `Scripts/Player`, `Scripts/UI`, `Scripts/Managers`。
- **Prefabs/**: 存放所有预制体，并按照功能分类。
- **Scenes/**: 存放游戏场景文件，如 `Scenes/MainMenu.unity`, `Scenes/Gameplay.unity`。
- **Art/**: 存放美术资产，包括 `Animations`, `Materials`, `Models`, `Textures` 等。
- **Audio/**: 存放音频文件。
- **Resources/**: 存放需要通过 `Resources.Load` 动态加载的资源。
- **Editor/**: 存放编辑器扩展脚本。

### 资产命名规范:
- 所有资产遵循 PascalCase 并使用描述性名称。
- 纹理: `TX_Name_UsageType.ext`，例如 `TX_Player_Diffuse.png`。
- 材质: `M_Name.ext`，例如 `M_PlayerMat.mat`。
- 模型: `MOD_Name.ext`，例如 `MOD_Player.fbx`。
- 预制体: `PF_Name.ext`，例如 `PF_Player.prefab`。
- 脚本: `CS_Name.cs`，例如 `CS_PlayerController.cs`。
- 动画控制器: `AC_Name.controller`，例如 `AC_Player.controller`。
- 动画片段: `AN_Name.anim`，例如 `AN_Walk.anim`。

## Unity API 使用指南

- **Vector3:** 尽量避免每帧在 `Update` 中 `new Vector3(...)`，应缓存一个 `Vector3` 变量并修改其 `x`, `y`, `z` 属性，或使用 `Vector3.right`, `up`, `forward` 等。
- **Transform:** 优先使用 `transform.SetParent(parent)` 并明确 `worldPositionStays` 参数，而不是直接赋值 `transform.parent`。
- **Instantiate 和 Destroy:** 在循环中频繁调用 `Instantiate` 和 `Destroy` 会导致性能下降，应使用对象池。
- **协程:** 建议使用 `StartCoroutine` 开始协程，并在适当时候调用 `StopCoroutine` 或 `StopAllCoroutines`。如果需要在多个生命周期事件中调用，最好保存 `Coroutine` 变量引用。
- **Input:**
    - 在 `Update` 中使用 `Input.GetButtonDown`, `GetButtonUp` 处理一次性事件。
    - 在 `Update` 中使用 `Input.GetAxis` 获取平滑输入值（如移动），不要与 `*Down` 混用。
    - 对于新项目，推荐使用 **Input System Package**，而非旧的 `Input` 类。

## Unity MCP 交互规则

> 注:以下规则在配置了 Unity MCP 服务器后生效（项目当前未配置 `.mcp.json`）。

- **优先使用 MCP 进行场景操作:**
    - 创建/删除 GameObject: 应使用 MCP 发送 `CreateGameObject`, `DestroyGameObject` 指令。
    - 添加/移除组件: 应使用 MCP 发送 `AddComponent`, `RemoveComponent` 指令。
    - 修改组件属性: 应使用 MCP 发送 `SetComponentProperty` 指令。
    - 执行 Unity Editor 菜单项: 如 `GameObject/...` 等命令，应使用 MCP 的 `ExecuteMenuItem` 指令。
- **等待 MCP 反馈:** 在发送一个 MCP 指令后，请等待 Unity MCP 插件的回复确认操作已完成，再继续发送下一个指令。
- **复杂操作的分解:** 一个复杂的场景构建任务（如创建一整个关卡），应分解为原子步骤，每一步都通过 MCP 执行并获得反馈。
- **操作失败处理:** 如果 MCP 返回错误（如找不到 GameObject 或组件），请描述错误并停止当前任务，请求用户介入或提供更精确的标识符。

## 沟通与工作流

- **任务规划:** 在开始编码前，先**在 Plan Mode** 下与用户确认需求细节和理解是否正确。
- **代码审查:** 生成任何代码修改后，都应主动请求用户审查更改。
- **提交信息:** 在生成代码后，如果用户要求提交到 Git，应生成清晰、原子的 Git 提交信息，类型和主题用英文，描述可用中文。
- **解释决策:** 对于复杂的、可能影响性能或设计的选择（如使用了 `Singleton` 模式），必须在代码注释或对话中解释其理由。
- **询问澄清:** 如果用户需求不明确，或者设计决策与现有代码库模式不符，请在操作前**提问**。

## 自定义项目规则

- 这是一个Unity插件项目，希望其他游戏可直接引入此项目使用
- **插件依赖:** 添加插件时，如果添加的Git URL到添加插件的package.json依赖时，第三方应用此package会报错，请在readme里面引导第三方手动安装git url依赖
- **避免GC:** 实现代码过程应该尽量避免GC，以及装箱，拆箱
- **避免反射** 请尽量避免使用反射方案
- **目录规则:** 在规划新模块时，先确定是否应该放到节点树里面，如果在节点树下，目录放到节点目录里面，否则放到与节点目录平级
- **非节点树模块** 非节点树模块尽量以静态类+接口扩展的方式实现
- **Git Commit提交及推送** 除类名或者成员等关键字外，请使用中文，不要在完成任务后自动提交，推送
- **框架结构** 静态服务+内部实现+扩展接口提供服务 节点树处理GamPlay 并由节点树启动静态服务初始化 静态服务不能对节点树对象产生依赖
