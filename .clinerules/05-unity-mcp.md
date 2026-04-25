## Unity MCP 交互规则 (Unity MCP Interaction Rules)

- **优先使用 MCP 进行场景操作:**
    - 创建/删除 GameObject: 应使用 MCP 发送 `CreateGameObject`, `DestroyGameObject` 指令。
    - 添加/移除组件: 应使用 MCP 发送 `AddComponent`, `RemoveComponent` 指令。
    - 修改组件属性: 应使用 MCP 发送 `SetComponentProperty` 指令。
    - 执行 Unity Editor 菜单项: 如 `GameObject/...` 等命令，应使用 MCP 的 `ExecuteMenuItem` 指令。
- **等待 MCP 反馈:** 在发送一个 MCP 指令后，请等待 Unity MCP 插件的回复确认操作已完成，再继续发送下一个指令。
- **复杂操作的分解:** 一个复杂的场景构建任务（如创建一整个关卡），应分解为原子步骤，每一步都通过 MCP 执行并获得反馈。
- **操作失败处理:** 如果 MCP 返回错误（如找不到 GameObject 或组件），请描述错误并停止当前任务，请求用户介入或提供更精确的标识符。