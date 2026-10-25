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

## 决策台账

- 已做：**PauseNotifier 代际、目录创建与静默回默认**（2026-10-19 · R3-SET-004/005/006，本轮修复）——`SettingsPauseNotifier` 计数无代际、旧会话 `Release` 可串到新会话；`JsonFileStore.Save` 的目录创建与 `ToJson` 在 try 外；加密装饰器对「文件存在但不是密文外壳」静默回默认。修法：代际 token；两段纳入受保护范围；`Data` 为空且文件存在时 Warning。回归三例。
- 已做：**默认路径占用与写入口主线程断言**（2026-10-19 · R3-SET-002/003，本轮修复）——已用显式路径初始化后默认路径 `Initialize<T>()` 仍写 `DefaultPathClaims`；`Apply`/`Save`/`SaveAsync` 无 README 承诺的 Editor-only 断言。修法：只在确实创建实例时占用；三个写入口补守卫；回归 `SettingsManagerFacadeTests` 两例。
- 已做：**加密装饰器同步降级在调用线程序列化**（2026-10-19 · R3-SET-001，回归修复）——内层同步时 `SaveAsync` 把 `Save(settings)` 整段丢线程池，活对象在池线程 `JsonUtility.ToJson`；改为先 `ToCipherBlob` 再只把 IO 交线程池；回归 `EncryptedSettingsStoreTests.SaveAsync_SyncInner_EncryptsOnCallingThread`。
- 未做：**`DefaultPathClaims` 刻意不随会话边界清空（当前维持）**（2026-10-14 · 会话级复位轮）——占用关系对应磁盘文件（清掉反而会放行「B 接管 A 的存档」），且体量有界（每路径一条）——与无界的重放表不同，故不适用同一处置。
- 不做：**被丢弃的旧管理器仍被 `Application.wantsToQuit` root**（2026-10-14 · 会话级复位轮；2026-10-19 R3-ADJ-09 裁定为不做）——开了 `SaveOnQuit` 时，会话边界只丢引用不 `Dispose`（与七门面同形），旧管理器的 `Dispose` 永不调用。与 2026-10-18 框架级裁定「会话边界只丢引用、不替使用方 Dispose」一致，`SaveOnQuit` 下是可接受残留。重开判据：框架级会话边界裁定被推翻。
- 已做：**`Application.wantsToQuit` 的编辑器探针**（2026-10-20 · R3-PRB-01，承接 R2 SET-012）——实测（Unity 6000.4.5f1）：退出播放模式时 `wantsToQuit` 先于 `quitting` 触发，`wantsToQuit` 返回 `false` 不取消退出（返回值被忽略）；关闭编辑器时两者都不触发。三处文案已按实测订正（`SettingsOptions.cs`、模块 README、`CHANGELOG`），测试注释同步。
- 不做：**没有 `SetInstance` / 工厂**（2026-10-02 · 全面清点后裁定不补）——`SettingsManagerImpl<T>` 是 internal sealed 且门面不提供整体替换——本模块是唯一一个连「换掉管理器行为」都不行的模块（其余模块都有 `SetInstance` 或 provider 等价物）。**不补的理由**：可替换点刻意**下移到** Store / Migrator / Validator（它们才是「会变的地方」，覆盖了加密存储、旧数据迁移、字段校验这些真实需求）；而管理器本身的通知、脏标记与保存时机是**模块语义**，替换它等于换一个模块——与 UI 的排序分层、Update 的切片与节拍同一档（判据见 `Ledger.md` 的「把模块内部策略接口化」条）。理由已写进 README 的设计理念表（「替换点在后端」行）。无重开判据。
- 已做：**加密装饰器实现内层异步能力**（2026-10-17 · `d078656`，SET-001）——`EncryptedSettingsStore` 实现 `IAsyncSettingsStore`：内层异步则转发（加解密在调用线程完成），否则自身按管理器同款线程池降级；公开 API 源兼容新增已获批准并更新基线。
- 不做：**会话边界不替使用方清句柄订阅流**（2026-10-18 · 裁定 + 文档订正）——会话边界只清重放登记表，不清句柄的订阅流；句柄在第二会话被使用即重新入表并重放给会话 1 的旧订阅者（SET-004），README 的「旧订阅者不会复活」已订正为如实描述。要避免旧回调请重新 `Ref` + `Subscribe`，或在边界前释放旧订阅。无重开判据。


- 已做：**SET-015 编译/运行探针与修复提示**（2026-10-18 · 独立工程 dotnet build + `SettingsManagerFacadeTests.Ref_FieldTypeMismatch_ThrowsWithRepairHint`）——`Initialize<T>(null)` 在 `(string, Func<T>, SettingsOptions)` 与 `(Func<T>, SettingsOptions)` 之间 CS0121（探针复现），XML 与 README 写明用命名实参或显式强转；`Ref<T,TField>` 字段类型不匹配时原抛 BCL 裸 `ArgumentException`，已包装为 `[SettingsManager]` 前缀 + 修复提示，不改签名。
- 已做：**加密读侧解析切回主线程**（2026-10-25 · `c95b784d`，R4-SET-001，本轮修复）——内层同步时整段 `Load<T>` 在池线程（含 `JsonUtility.FromJson`），与 README 示例相反。修法：拆出 `DecryptToJson`（池线程 IO + 解密）与主线程解析；内层异步也显式 `SwitchToMainThread`，切回后解析前检查取消。回归 `EncryptedSettingsStoreTests.LoadAsync_SyncInner_DecryptsOnPoolAndParsesOnMainThread`（改前 `OnAfterDeserialize` 在池线程）。
