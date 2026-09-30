# Localization —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Localization/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Localization/
├── ILocalizationManager.cs        # 本地化管理器公共接口
├── LocalizationManager.cs         # 静态外观（全局入口）
├── LocalizationManagerImpl.cs     # 默认实现（LRU 缓存）
├── LocalizationBootstrapStage.cs  # 引导阶段（IBootstrapStage，Phase 90）
├── LanguageChangedMessage.cs      # 语言切换消息（readonly struct，无装箱）
└── LanguageAssetLoader.cs          # 语言数据异步加载器（内部）
```
