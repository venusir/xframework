## Unity API 使用指南 (Unity API Usage Guide)

- **Vector3:** 尽量避免每帧在 `Update` 中 `new Vector3(...)`，应缓存一个 `Vector3` 变量并修改其 `x`, `y`, `z` 属性，或使用 `Vector3.right`, `up`, `forward` 等。
- **Transform:** 优先使用 `transform.SetParent(parent)` 并明确 `worldPositionStays` 参数，而不是直接赋值 `transform.parent`。
- **Instantiate 和 Destroy:** 在循环中频繁调用 `Instantiate` 和 `Destroy` 会导致性能下降，应使用对象池。
- **协程:** 建议使用 `StartCoroutine` 开始协程，并在适当时候调用 `StopCoroutine` 或 `StopAllCoroutines`。如果需要在多个生命周期事件中调用，最好保存 `Coroutine` 变量引用。
- **Input:**
    - 在 `Update` 中使用 `Input.GetButtonDown`, `GetButtonUp` 处理一次性事件。
    - 在 `Update` 中使用 `Input.GetAxis` 获取平滑输入值（如移动），不要与 `*Down` 混用。
    - 对于新项目，推荐使用 **Input System Package**，而非旧的 `Input` 类。