# Bootstrap —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Bootstrap/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 沿革与已否决形状

- **反向清理原先散落在各引导节点的 `OnDestroy` 中**；现由 `Bootstrap.Shutdown` 按登记顺序逆序统一完成。
- **Phase 1 / 2 曾是 Lock / Message 的相位**：二者改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期后被移出，这两个值随之空闲。

## 已评估未采纳与未决

（2026-10-01 一轮：「如何保证框架加载完成才执行 Update」的定向审计 + 门控方向裁定。该轮全部改动是**文档**，未碰任何行为。）

**已评估未采纳**：

- **GameLauncher 自动门控加载期派发**（`Awake` 门控 → `RunAsync` 完成/失败后 `finally` 恢复）：不采纳。会给 Bootstrap 引入到 Update 的新依赖，让可选启动器变成「有主张」；与节点系统删除时**刻意解除 GameLauncher ↔ Update 耦合**的沿革相反（GameLauncher 曾直接驱动 Update，后改为 Update 自注入）；且失败/中断路径存在「忘了恢复即整机静默冻结」的闩锁形状——Update 模块 2026-09 刚清除过同类故障。
- **Bootstrap 就绪句柄**（`IsCompleted` / 可 await 的完成信号）：不采纳。使用方在 `await RunAsync()` 后自行注册即为正解；新增静态完成信号要额外定义失败/取消/重复 `RunAsync` 的语义，收益不抵 API 面。若有第三方真实诉求可重开。

**未决**：

- **GameLauncher 启动途中销毁与在途 `RunAsync` 交叠**（推理，未实测）：`Start` 里 `RunAsync()` 未传 `destroyCancellationToken`，`OnDestroy` 无条件 `Shutdown()`——加载中途销毁时，清理会与在途启动交叠（最坏一条「启动失败」错误日志）。GameLauncher 是 `DontDestroyOnLoad`、正常仅在退出时销毁，影响面小；修法（传令牌 / Shutdown 前取消）未评估。
