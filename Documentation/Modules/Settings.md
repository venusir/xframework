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
├── SettingRefRegistry.cs          # 句柄重放注册表（internal，实例替换时通知订阅者 + 会话代际号）
├── SettingRefSessionCoordinator.cs # 注册表的会话边界协调器（internal，泛型静态表无法统一遍历）
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
- **会话级复位：Settings 是 2026-10-12 那轮七门面处置漏掉的第八个（2026-10-14 补齐）**。判据是同一句原话——`SettingsManager.Initialize<T>` 见 `Managers` 里已有同类型即「告警 + 返回旧实例」，而 `Managers` 只在显式 `Destroy` 里清，**使用方照文档再调一次也无法自救**。同类另两处：重放注册表跨会话累积（第二会话同字段再建句柄 → 重放两次 → 重复日志）、`SettingsPauseNotifier` 计数不归零（`DontDestroyOnLoad` 宿主永不销毁）。发现路径是**步骤 1 的手工验收**（关域重载连跑两遍，第二遍红在重复日志上）——自动化门禁与单会话用例都看不见它。
  **修法选型（两条被否的路，勿重走）**：① **弱引用注册表**——`static readonly` 句柄被静态字段强引用，弱引用对它等于强引用（**救不了 README 推崇的写法**），且重放次数与日志条数随 GC 时序漂移（用例会从「必红」变「随机红绿」）；② **纯清空**——把「可见的重复重放」换成「永久的静默降级」（`Load`/`Reset` 后滑条不动，正是注册表注释自己点名的失败形状）。采纳的是**会话边界清空 + 句柄「用即重登记」**：代际号是「跨会话仍被使用」与「已被丢弃」之间唯一 O(1)、不依赖 GC 的判据；协调器形状照抄 `CollectionPoolManager` 的「静态构造登记清理委托」。

## 已评估未采纳与未决

- **`DefaultPathClaims` 刻意不随会话边界清空**（未决，当前维持）：占用关系对应磁盘文件（清掉反而会放行「B 接管 A 的存档」），且体量有界（每路径一条）——与无界的重放表不同，故不适用同一处置。
- **被丢弃的旧管理器仍被 `Application.wantsToQuit` root**（未决）：开了 `SaveOnQuit` 时，会话边界只丢引用不 `Dispose`（与七门面同形），旧管理器的 `Dispose` 永不调用。属既有先例接受的残留；若要收，须先裁定「会话边界该不该替使用方 `Dispose`」——七门面是同一个问题，别只在此处单独改。
- **没有 `SetInstance` / 工厂**（2026-10-02 全面清点后**裁定不补**）：`SettingsManagerImpl<T>` 是 internal sealed 且门面不提供整体替换——本模块是唯一一个连「换掉管理器行为」都不行的模块（其余模块都有 `SetInstance` 或 provider 等价物）。**不补的理由**：可替换点刻意**下移到** Store / Migrator / Validator（它们才是「会变的地方」，覆盖了加密存储、旧数据迁移、字段校验这些真实需求）；而管理器本身的通知、脏标记与保存时机是**模块语义**，替换它等于换一个模块——与 UI 的排序分层、Update 的切片与节拍同一档（判据见 `Ledger.md` 的「把模块内部策略接口化」条）。理由已写进 README 的设计理念表（「替换点在后端」行）。
