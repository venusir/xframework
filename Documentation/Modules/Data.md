# Data —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Data/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
XData/
├── IDataBlock.cs             # 数据块接口定义
├── IDataManager.cs           # 服务接口
├── DataManager.cs            # 静态门面
├── DataManagerImpl.cs        # 内部实现
├── DataBootstrapStage.cs     # 引导阶段：初始化门面（Phase 3）
├── DataException.cs          # 异常类型
├── DataSnapshot.cs           # 存档快照数据结构
└── README.md
```
