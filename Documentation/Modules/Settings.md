# Settings —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Settings/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Settings/
├── ISettingsStore.cs              # 存储后端接口
├── IAsyncSettingsStore.cs         # 可选：异步存储能力
├── JsonFileStore.cs               # 默认 JSON 文件存储（原子写 + 一代备份）
├── EncryptedSettingsStore.cs      # 加解密装饰器（可叠在任意后端上）
├── ISettingsManager.cs            # 管理器接口
├── SettingsManagerImpl.cs         # 默认实现（internal sealed）
├── SettingsManager.cs             # 全局静态外观
├── SettingRef.cs                  # 字段句柄
├── SettingRefRegistry.cs          # 句柄重放注册表（internal，实例替换时通知订阅者）
├── SettingsOptions.cs             # 选项
├── SettingsEnvelope.cs            # 版本信封（internal）
├── ISettingsMigrator.cs           # 迁移钩子
├── ISettingsValidator.cs          # 载荷校验钩子
├── SettingsAutoSaveTicker.cs      # 自动保存帧驱动器（internal）
├── SettingsPauseNotifier.cs       # 切后台兜底的宿主（internal MonoBehaviour）
├── Messages/
│   └── SettingsChangedMessage.cs  # 变更消息
└── README.md
```

## 沿革与已否决形状

- **README 里曾有两句错话，已订正**：门面**并非**没有同步内容读写（含 `WriteAllBytesAtomic` 在内的同步方法就在门面上，`FileManagerExtensions` 已退为兼容面），也**并非**拿不到一代备份（那是 `IAtomicFileProvider` 契约的一部分）——备份能力的真实限制是「异步形态 + 能力探测」，也就是 README 那三条理由里的第 1 条。所以「`ISettingsStore` 是全同步的、构造必然同步加载」不构成不复用的理由。

## 已评估未采纳与未决

- **没有 `SetInstance` / 工厂**（2026-10-02 全面清点后**裁定不补**）：`SettingsManagerImpl<T>` 是 internal sealed 且门面不提供整体替换——本模块是唯一一个连「换掉管理器行为」都不行的模块（其余模块都有 `SetInstance` 或 provider 等价物）。**不补的理由**：可替换点刻意**下移到** Store / Migrator / Validator（它们才是「会变的地方」，覆盖了加密存储、旧数据迁移、字段校验这些真实需求）；而管理器本身的通知、脏标记与保存时机是**模块语义**，替换它等于换一个模块——与 UI 的排序分层、Update 的切片与节拍同一档（判据见 `Roadmap.md` §四「不把模块内部策略接口化」）。理由已写进 README 的设计理念表（「替换点在后端」行）。
