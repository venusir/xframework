## 核心开发原则 (Core Principles)

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