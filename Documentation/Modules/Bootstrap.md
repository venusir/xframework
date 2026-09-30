# Bootstrap —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Bootstrap/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 沿革与已否决形状

- **反向清理原先散落在各引导节点的 `OnDestroy` 中**；现由 `Bootstrap.Shutdown` 按登记顺序逆序统一完成。
- **Phase 1 / 2 曾是 Lock / Message 的相位**：二者改为 `[RuntimeInitializeOnLoadMethod]` 自管理生命周期后被移出，这两个值随之空闲。
