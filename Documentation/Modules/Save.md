# Save —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Save/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Save/
├── ISaveManager.cs        # 接口 + 工厂委托
├── SaveManager.cs         # 静态门面
├── SaveManagerImpl.cs     # 默认实现
├── SaveMeta.cs            # 存档元数据（含侧车字段与校验和）
├── SaveOptions.cs         # 初始化选项（格式版本、加密）
├── SaveReport.cs          # 进度载荷
├── SaveLoadResult.cs      # 加载状态与结果
├── SavePathUtility.cs     # 路径构造、槽位解析与标识校验
├── SaveIntegrity.cs       # 载荷校验和（FNV-1a 64）
├── SaveBootstrapStage.cs  # 引导阶段：初始化门面 + 跑一次启动恢复扫描
└── README.md
```
