## Unity 开发规范 & 通用规则 (Unity Development Standards)

# 项目上下文与规范
- Unity 版本: 2022.3 LTS 或更新版本。
- 脚本运行时: .NET Standard 2.1。
- API 兼容级别: .NET Framework。
- 项目模板: 3D 核心或 URP/HDRP，有特殊需求时说明。

# 总体原则
- **原子化:** 每次只专注于完成一个逻辑明确、可独立验证的编码任务。
- **版本控制:** 所有生成的代码都应易于审查和通过 Git 进行原子提交。
- **MCP优先:** 如果 Unity MCP 可用，任何对 Unity Editor 中 GameObject、组件、属性的增删改操作，应优先通过发送 MCP 指令来完成。
- **测试驱动:** 完成逻辑后，应提供简单的测试步骤（如在 `Update` 中添加 `Debug.Log`，或建议添加 `[UnityTest]` 单元测试）。
- **避免臆测:** 如果需求不明确，或者对某个 Unity API 的行为不确定，请先提问，而不是猜测。

# C# 编码规范
- 遵循 Unity 官方 C# 代码规范，并提供具体示例。
- **命名约定:**
    - 公共字段、序列化字段 (`[SerializeField]`) 和属性使用 PascalCase，例如 `PlayerHealth`, `moveSpeed`。
    - 私有字段使用 camelCase，并以下划线开头，例如 `_rb`, `_animator`。
    - 方法、枚举、结构体和类使用 PascalCase，例如 `CalculateDamage()`。
    - 常量使用 PascalCase，例如 `MaxPlayerCount`。
- **代码结构:**
    - 文件顶部统一导入必要的命名空间 (`using UnityEngine;`, `using System.Collections;` 等)。
    - 在脚本开头定义公共变量，然后是私有变量，接着是 `Awake`, `Start`, `Update` 等 Unity 生命周期方法，最后是自定义方法。
- **代码风格:**
    - 使用大括号 `{}`，并将左括号放在新的一行。
    - 始终使用 `#region` 来分组代码块，例如 `#region Public Variables`, `#region Lifecycle Methods`。
    - 使用 `[SerializedField]` 代替 `public` 暴露私有字段，以保持封装性。
    - 避免在 `Update` 方法中进行昂贵的操作，如 `Camera.main` 或 `FindObjectOfType`，应使用缓存引用。
- **游戏对象查找:** 优先使用 `GetComponent`, `GetComponentInChildren`, `Transform.Find` 等方法，避免使用 `GameObject.Find` 和 `SendMessage`。