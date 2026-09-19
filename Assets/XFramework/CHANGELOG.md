# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- **启动引导模块 `XBootstrap`**：`IBootstrapStage`（= Pipeline 的 `IPhaseStage` + 同步 `Shutdown`）与 `Bootstrap` 静态门面（`Register` 显式登记 / `RunAsync` 相位装配运行 / `Shutdown` 逆序清理）。框架内置的引导阶段归位到各自模块——`AssetBootstrapStage` 落 `Runtime/Asset/`，Data / Save / Localization 同理，框架不再在自己的目录里装着别人的服务。两点相对旧启动路径的实质改进：`RunAsync` 首次提供 `CancellationToken`（旧 `StartupAsync` 不可取消），且失败与取消会**抛出**而非只留日志（旧实现只订阅 `OnProgressUpdate`、从不订阅 `OnFailed` / `OnCancelled`，阶段失败时调用方无从感知）。登记按**实例**去重而非按类型——登记表是「初始化步骤列表」，参数化的阶段用同一类型登记多次是合法的；`RegisterDefaults()` 例外，它按类型跳过已存在的内置阶段，故可重复调用

- **File 原子替换与一代备份**：改用 `File.Replace` 一步完成「替换正式文件 + 保留一代 `.bak`」，平台不支持时降级为三步移动。原实现以「删正式文件 → Move」实现替换，两步之间进程被杀即为「旧档已删、新档还在 `.tmp`」——从上层看是存档凭空消失且无从恢复。两条路径都维持「任意时刻至少一份完整副本」的不变式
- **File 目录枚举可选能力**：新增 `IDirectoryProvider.GetDirectoriesAsync` 与 `FileManager.GetDirectoriesAsync`。`IFileProvider` 只有非递归的 `GetFilesAsync`，上层无法发现「存在但当前没有文件的目录」；按 `IAtomicFileProvider` 的既有模式做成可选能力，不给 `IFileProvider` 增加成员，第三方 Provider 实现零影响。装饰器 `CryptoFileProvider` 同步透传——装饰器漏实现可选能力会把底层 Provider 的能力遮蔽掉
- **File 加密按域限定**：`SetCryptoProvider(provider, FileDomain?)`。原实现把整个 Provider 包成加密装饰器、即加密全部域，从存档侧接线会静默连带加密 AppData / Cache
- **Data 快照应用上报失败块数（破坏性）**：`IDataManager.ApplySnapshot` 由 `void` 改为返回未能恢复的数据块数量。此前单块恢复失败只记 warning 后吞掉、方法正常返回，调用方无从判断本次加载是否**完整**——「部分块未恢复」等价于内存里少了一半数据
- **Save 元数据侧车与校验和**：槽位旁新增 `slot_N.save.meta`（元数据 + FNV-1a 64 校验和），使枚举不必为拿元数据而读入并反序列化整个载荷。侧车缺失或不可解析时回退全量解析并重建；**校验和不符时刻意不重建**——那是「载荷可能已损坏」的信号，重建等于把它抹掉，应留给加载侧走备份回退。定位写明：只发现损坏，不提供防篡改
- **Save 加载结果枚举与备份回退**：新增 `SaveLoadStatus` / `SaveLoadResult` / `TryLoadAsync`，把「槽位不存在」「文件损坏」这类预期内失败以状态回报而非抛异常——存档界面需要区分这些情况，做成异常会迫使调用方用 try 表达正常分支。主文件不可用时自动回退到一代备份并回报 `LoadedFromBackup`
- **Save 快照版本门禁**：`SaveOptions.CurrentVersion` / `SetCurrentVersion`。保存时写入快照，加载时高于当前客户端则整份拒绝（`VersionTooNew`）、低于则正常加载并回报 `Migrated`。此前只有 Data 侧的按块门禁，而它是「跳过该块」——结果是新格式存档被半加载（一半新数据、一半空着），比干净地拒绝危险得多
- **Save 启动恢复扫描**：`SaveBootstrapNode` 初始化后扫描存档目录，用一代备份还原丢失的载荷、清理崩溃残留、重建缺失侧车
- **Save 槽位复制与移动**：`CopySlotAsync` / `MoveSlotAsync`，基于 provider 读写原语实现，所有存储后端与加密层都成立；目标侧车按目标槽位重建（照抄源侧车会让元数据把目标报成源槽位）
- **Save 进度上报**：多步操作（枚举槽位、批量删除）支持 `IProgress<SaveReport>`，按框架惯例以重载而非改签名新增
- **Save 加密接线**：`SaveOptions.CryptoProvider` 非空时只对 `FileDomain.SaveData` 域接线加密
- **Asset 低内存自动回收**：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动清池并卸载全部包中未使用资源
- **Asset 批量加载**：`AssetManager.LoadAllAsync<T>` 按序返回句柄数组，单项失败为 default 句柄，取消时自动释放已完成项
- **异步发布 `PublishAsync`**：同步投递先行的基础上等待全部异步处理器完成，`MessagePublishStrategy` 可选 Parallel（默认）/ Sequential；`Publish` 仍以 fire-and-forget 触发异步订阅
- **缓冲通道淘汰**：`EvictBufferedChannel`（类型级 / 按键）、`EvictBufferedChannels`、`TrimEmptyChannels`；订阅清零的通道自动回收，缓冲通道需显式淘汰（保留「订阅前发布可重放」语义）
- **按 Key 跨消息类型淘汰**：`EvictBufferedChannels<TKey>(key)` 一次淘汰该 Key 在**所有**消息类型下的缓冲通道。此前淘汰只能按消息类型进行，实体销毁处要为其参与的每种带 Key 消息各写一次调用，且每新增一种消息都得回去补一处——漏掉的那处即「复用同 Id 的新实体重放到旧值」的静默事故。新 API 与实体的生命周期同形，README「内存管理」的推荐写法已同步改为按 Key 淘汰
- **运行统计**：`MessageManager.GetStats()` / `GetChannelStats<T>()` 返回 `readonly struct` 快照（通道数、订阅数、缓冲通道数、发布/请求次数等），零分配，用于排查订阅泄漏与缓冲内存驻留
- **过滤器管理**：`RemoveFilter` / `ClearFilters`
- **请求处理器注销**：`Unregister<TRequest, TResponse>`
- **请求-响应补全**：`HasHandler<TRequest>()` 探测响应方是否已注册；`TryRequestAsync<TRequest, TResponse>` 在一次调用内完成「查 + 发」，未注册处理器时返回 `(false, default)` 而不抛异常。模块定位是跨模块解耦，请求方本就不该假定响应方已就绪，此前只能靠 try/catch 接 `InvalidOperationException`——既是用异常做控制流，也无法与处理器内部抛出的同类型异常区分。`false` 只表示未注册，处理器自身的异常照常上抛
- **节点订阅扩展**：`NodeExtensions.SubscribeAsync(this BaseNode, ...)`；`Subscribe` 去掉 `where TMessage : class` 约束，框架内置 struct 消息现可用
- **节点缓冲订阅**：`NodeExtensions.SubscribeBuffered<TMessage>(this BaseNode, handler)`，订阅即收到最近一条并自动绑定节点生命周期。刻意只提供这一个缓冲重载——带 Key 与带过滤条件的变体一旦同时存在，`(TKey, Action<TMessage>)` 会捕获本意为过滤器的委托实参，使该调用退化为 CS0121 或被静默解析为「把谓词当 Key」

- **Settings 字段句柄 `SettingRef`**：把设置对象里的单个字段包装成响应式视图，实现 `IReactiveProperty<T>`，故 UI 模块现成的 `BindToSlider` / `BindToText` 等绑定扩展方法可直接使用。刻意**不缓存设置实例**——每次读写都解析当前实例，因此 `Load`/`Reset`/`Apply` 换实例后自动跟随、无需重新绑定（若让设置字段直接声明为 `ReactiveProperty<T>`，则每次换实例都要重绑、且旧对象上的订阅全部失活）。去重基准取 POCO 的实时值而非缓存，故无陈旧锚点。句柄**不实现 `IDisposable`**：它通常声明为静态字段并活到进程结束，带释放语义会让「面板关闭时 Dispose 掉 ViewModel」这类正常操作连带废掉它。选择器必须以字段结尾——属性不被 `JsonUtility` 序列化，用它做设置项会「改了但没存」，`Ref` 直接拒绝并说明原因
- **Settings 异步持久化**：`SaveAsync` / `LoadAsync` 与可选能力接口 `IAsyncSettingsStore`。此前模块只有同步 API，设置面板保存会阻塞主线程做文件 IO，而它是框架内唯一同步落盘的持久化实现。未实现该接口的存储后端不必改动：管理器做能力探测、把同步调用整体挪到线程池，语义与同步版完全一致（与 FileManager 对 `IAtomicFileProvider` 的处理同构）。能力接口必须带 `ExistsAsync`——管理器靠它区分「有持久化数据」与「根本没有数据」，缺了它异步加载会与同步加载产生不同的默认值
- **Settings 格式版本与迁移**：`SettingsOptions.CurrentVersion` + `ISettingsMigrator<T>`。README 一直声称「JSON 天然支持版本迁移」而代码零实现，字段改名或结构调整后旧文件会被静默按新结构解析、错位而不报错。版本与载荷一起落盘成信封（`{"Version":1,"Data":{…}}`）——`ISettingsStore` 只有泛型的 Load/Save、没有版本通道，把版本处理下推进 store 会让每个第三方 store 都得实现一遍。做成**可选**：默认 `CurrentVersion = 0` 时不启用，落盘仍是设置对象本身，保持「JSON 干净」这一卖点。加载时版本高于本版本则整份拒绝（按旧结构解析只会得到静默错位的设置）、低于则调用迁移器、缺迁移器同样回退默认值
- **Settings 脏标记与可选自动保存**：`IsDirty` / `MarkDirty`，以及 `AutoSave` / `AutoSaveDelay` / `SaveOnQuit`（默认全关，保持「调用方显式保存」的既有取舍）。脏标记用**变更计数而非布尔标志**：`Save` 开始时取快照、结束后把已提交档位设为快照值，这样「保存过程中用户继续拖动滑条」产生的改动不会被吞掉——布尔标志会在保存结束时无条件清除，把那次改动丢掉。自动保存是**去抖而非节流**：等待窗口从最后一次改动起算，拖动滑条期间一次都不写盘、松手静默后写一次；若做成节流，一次三秒的拖动会写六次。关闭时不注册任何帧回调，默认路径零开销
- **Settings 原子写入与一代备份**：改用 `FilePathUtility.ReplaceFileAtomically`（与 Save/File 模块同一套替换原语）。原先 `File.WriteAllText` 直接覆盖正式文件，写到一半崩溃就留下截断 JSON，配合「解析失败即抛异常」的行为足以让玩家此后每次启动都崩
- **UI 绑定接收者放宽到 `IReactiveProperty<T>`**：`UIBinder` 的 7 个绑定方法与 `UIPanelBinding` / `UIPanelBase` / `ViewModelBase.CreateReadOnlyProperty` / `ReadOnlyReactiveProperty.Select` 的接收者由具体类放宽到接口（对调用方源码兼容）。此前绑定 API 只吃具体类型，任何非框架实现都无法接入。必须是**替换**而非新增重载——两版并存时具体类更精确、永远胜出，接口版会沦为死代码
- **Update 三个派发时机**：新增 `ILateUpdateable` / `IFixedUpdateable` 与 `UpdateManager.RegisterLate` / `RegisterFixed`，并把生命周期回调抽成三者共用的 `IUpdateLifecycle`（`IUpdateable` 继承它，既有实现零改动）。每时机一套独立调度器（各自的桶、帧计数、切片相位、暂停状态），因为共用一个实例会让两种时机的切片相位互相干扰。固定步长时机的时间基准是 `Time.fixedTime`——那里的档位以**固定步**计（默认 0.02s 一步），因此它没有时间轴参数（Unity 固定步本就随 `timeScale` 停摆）、也不能由变步长时钟驱动
- **Update 双时间轴与暂停**：新增 `UpdateClock`（time + unscaledTime + isPaused）与 `UpdateTimeMode`（Scaled / Unscaled），桶按「时间轴 × LOD」二维组织。需要「暂停期间仍运行」的逻辑（暂停菜单、UI 动画、手柄振动到期）终于有正规表达方式——此前生产代码里已经出现绕过（`InputSystemProvider` 自己读 `Time.unscaledTime`）。配套 `Pause` / `Resume` / `IsPaused`；节点树侧可经 `IUpdateTimeMode` 声明自己的轴
- **Update PlayerLoop 自驱动**：驱动注入 `Update` / `PreLateUpdate` / `FixedUpdate` 三个阶段，不再要求场景里存在 `GameLauncher` 或任何 MonoBehaviour；`IsDrivingPlayerLoop` 可查询注入状态，注入失败打 `LogWarning`（门面是宽容语义、不会抛异常，不留痕的话故障表现只是「静止」）。注入基于 `GetCurrentPlayerLoop` 且只插入不替换，因此与 UniTask 等同样靠注入工作的库共存（有断言钉住）
- **Update 补齐长周期档位**：`UpdateLOD` 增加 `Tier6`（约 1067ms / 64 个固定步）与 `Tier7`（约 2133ms / 128 个固定步）。原阶梯封顶在 `Tier5`，而它在 30fps 下是 1.07 秒、144fps 下只有 222ms——「每秒醒一次」这类需求此前无法表达，只能自己在节点里按 `deltaTime` 累加。档位是 2 的幂的等比梯子，桶数组尺寸随 `UpdateLOD.Max` 自动推导，故补齐只是加枚举成员

- **UI 面板级 LOD 与失焦剔除**：`UIViewBase.UpdateLod`（Inspector 可配、运行时热改即重排）。用不到每帧的面板（倒计时、进度插值）声明较低档位即可降耗；被覆盖而失焦的面板完全不派发。按档位懒注册驱动器——Tier0 复用常驻的那个，其余档位桶由空转非空时才注册、重新变空即注销，故初始化后调度器里仍只有一个条目。`IsPaused` 此前是无人消费的死字段（`OnBlur` 置位、从无读者），至此才第一次生效
- **UI 查询 API**：`OpenCount` / `IsAnyOpen` / `GetTopPanel` / `Panels`（活视图，读取零分配）/ `CopyPanels(buffer)` / `CopyPanelsInLayer`。此前查询只能逐类型试 `IsOpen<T>()`，「现在开着哪些面板」「哪个在最前面」无从回答，除非调用方自己维护一份账。`CopyPanels` 为零分配主入口，由调用方持有缓冲区
- **UI 遮罩引用计数**：`ShowMask` 返回 `UIMaskHandle`（`readonly struct`，与 `LockHandle` 同惯例），配 `UIMaskStyle`。多系统各自需要遮罩时（弹窗 + 引导 + 网络等待），先结束的那个不再把别人的遮罩一起关掉。支持 `owner` 面板联动——面板关闭时自动释放它开的遮罩。`UIMaskStyle` 刻意不用哨兵值：层级 0 是合法层级，全透明遮罩（挡输入但不显示）也是合法需求
- **UI 层级 API 可达**：`SetLayerVisibility` / `SetLayerInteractive` 此前只存在于内部实现上，**连 `IUIManager` 都没有**，而门面既无转发也无实例属性——第三方虽能在文档里读到它们，实际完全不可达。现补进接口并由门面转发，另暴露 `UIRoot`
- **UI 诊断**：`UIStateSnapshot`（`readonly struct`，零分配）+ `DumpState()` + `Tools/XFramework/UI State` 窗口。用于回答「面板是不是漏关了」「遮罩为什么还亮着」「哪个打开卡在半路」这类只能翻运行时状态才能定位的问题。窗口数据现取自显示栈与在途表，不是另记一份账
- **UI 生命周期钩子 `OnPause` / `OnResume`**：把 `OnBlur` 原先承担的两件事正交拆开——交互维度（`OnFocus`/`OnBlur` 管 `IsFocused` + Raycaster）与更新维度（`OnPause`/`OnResume` 管 `IsPaused`）。于是「失焦但仍要按低频更新」这类需求终于可表达，且被覆盖的面板仍会收到语言切换等回调（否则 Pop 回来会看到旧语言）
- **UI 双向绑定**：新增能力接口 `IReactivePropertyWriter<T>`（继承 `IReactiveProperty<T>`，只读接口本身不变），`ReactiveProperty<T>` 与 `SettingRef<T,TField>` 各自实现，故任何第三方实现都能接入。`UIBinder.BindTwoWay` 支持 Slider / Toggle，内部以重入守卫阻断回灌，并在目标规范化写入值（取整、钳制）时回填控件。约定式绑定补上 `sld_` / `tgl_`（此前 `UIPanelBinding` 缓存了这两类组件却无人消费），`btn_` 走新增的 `BindClick`
- **UI 安全区适配 `UISafeArea`**：把 RectTransform 收缩到 `Screen.safeArea`。推荐挂在 UIRoot 上——层级容器都是它的子节点，一处生效即全局生效
- **UI 订阅归口 `UIViewBase.Track(IDisposable)`**：面板与 HUD 都是回池而非销毁，挂在 `OnDestroy` 上的释放永不触发。这是「订阅随视图生命周期释放」的唯一出口，`BindToLocalizedText` 已归口进来
- **UI 面板资源真释放**：`UnloadPanelAssetAsync(assetPath)`，配套 Asset 侧新增能力接口 `IAssetPoolController`（`ClearPool` / `ClearAllPools`）。此前 `UnloadAsset` 声称「释放内存」而只清一个记账字典——查证后确认这不是实现偷懒而是能力缺口：`DestroyAllPooledInstances` 是 private，且回池时实例保留 `AssetHandle` 保活资源，于是池里只要有一个闲置实例，该预制体的引用计数就不会归零。能力接口与 `IAssetManager` 分开，故不破坏第三方自定义实现
- **UI 排序空间单点定义 `UISorting` / `UILayers`**：所有 `sortingOrder` 取值一律由此推导，`UISortingTests.EveryBandValue_SurvivesCanvasRoundTrip` 会把越界取值当场拦住

### Changed

- **销毁令牌迁入 Message 并真正接上订阅绑定（破坏性）**：`IDestroyCancellationToken` 从 `XNode.BaseNode` 迁到 `XMessage`，`MessageManager.TryBindToDestroy` 增加对它的识别分支。此前 `MessageManager` 的文档承诺「其他生命周期类型由 Core 层扩展方法负责桥接」，而**那段代码从未存在**——`TryBindToDestroy` 实际只有 `is MonoBehaviour` 一个分支，非 MonoBehaviour 对象的同步订阅根本没有自动退订途径，只能自己持有返回的 `IDisposable`。迁移后实现该接口即可自动退订

#### 破坏性变更迁移表（UI 模块）

| 旧写法 | 新写法 |
| --- | --- |
| `UIManager.HasPrevious` | `UIManager.CanGoBack` |
| `UIManager.BackToAsync<T>()` | `UIManager.PopToAsync<T>()`（另新增 `PopToRootAsync()`） |
| `UIManager.ShowTip(text, cfg)` | `await UIManager.ShowTipAsync(text, cfg)`（或 `.Forget()`） |
| `UIManager.ShowHud<T>(...)` | `UIManager.ShowHudAsync<T>(...)` |
| `UIManager.UnloadAsset<T>()` | `UIManager.ForgetPreload<T>()` |
| `UIManager.ClearAssetCache()` | `UIManager.ClearPreloads()` |
| `UIManager.GetTopSortingOrder(layer)` | 已删除：它对外算错了（忽略了层偏移），且与 `GetNextSortingOrder` 口径分裂。排序值现由 `UISorting` 按栈位推导 |
| `protected internal override void OnUpdate()` | `protected internal override void OnUpdate(float deltaTime, float time)` |
| `CloseAllAsync` 之后世界空间 HUD 与在播 Tip 仍在 | 现在会一并回收。要保留请在关闭后自行重挂；遮罩不受影响，仍需显式 `HideMask()` |

> 门面一律扁平：上表之外没有别的改名。`UIManager` 的每个成员都与 `IUIManager` 同名，两条路径（静态门面 / 注入实例）用的是同一套名字。

- **Update 档位类型更名为 `UpdateTier`（破坏性）**：`UpdateLOD` 更名为 `UpdateTier`，公开参数 `initialLOD` 更名为 `initialTier`；枚举成员 `Tier0..Tier7` 与 `Max`、枚举值、行为均不变。旧名有三处问题：本仓 3 字母缩写在标识符中一律 PascalCase（`JsonUtility` / `CsvLoader` / `UIHudItem`），只有 LOD 全大写，且已与 UI 模块的 `UpdateLod` 分裂成只差大小写的两种拼法；LOD 在 Unity 语境里指网格/贴图细节层级，与本模块「档位不是精度，是采样间隔」的语义相冲。类型名自 0.2.0 起即公开（当时成员为 `Frame1..Frame16`），故属破坏性变更。UI 模块自己那套 `Lod` 拼写（`UIViewBase.UpdateLod` / `LodDriver` / `LodDemandChanged` 等）一并统一到 `Tier`——那批 API 与其他面板级 LOD 特性同为未发布内容，不构成破坏性变更

- **Update 顺序参数 `depth` 更名为 `order`（破坏性：仅参数名）**：语义与行为零变化（仍是桶内按序插入、同值按注册先后），改的是名字与文档。旧名说的是「节点在**树**中的深度」，而节点系统已在本批次移除；公开文档还写着「数值越小越先执行」，可桶内下标同时决定切片相位——切片档里它并不表达「本帧谁先跑」，只表达排在哪一相位。新名与重写的 doc 一并说清这双重作用

#### 破坏性变更迁移表（Update 模块）

| 旧写法 | 新写法 |
| --- | --- |
| `UpdateLOD` | `UpdateTier` |
| `Register(..., initialLOD: lod)`（`RegisterLate` / `RegisterFixed` 同） | `initialTier: tier` |
| `UpdateLOD.Tier3` 等枚举成员 | 不变（成员名与枚举值都没动） |
| `Register(node, depth: 0, ...)`（`RegisterLate` / `RegisterFixed` 同） | `Register(node, order: 0, ...)` |

- **Update 补写派发次序契约与边界声明（纯文档）**：README 新增「派发次序」与「分组怎么做」两节，写明一帧内的确定性次序（时间轴 → 档位 → 桶内 `order` → 时机），并给出反向声明——这些次序只保证**可复现**、不保证**可依赖**（跨档位的节点本就不在同一帧派发，需要「A 之后才做 B」请走事件）；「设计原则」补一条**不做并行**（派发是主线程同步回调、没有等待点，异步/分帧由节点自理，通用并行编排归 Pipeline）。分组则明确不提供 API，只列三种做法与代价——组一旦进入框架就要回答生命周期与嵌套，那是被删掉的节点系统。此前这些边界只散落在代码与讨论里，第三方读者只能靠猜

- **UI `OnUpdate` 携带 `deltaTime`/`time`（破坏性）**：面板可声明较低档位而被降频派发（Tier3 约 133ms 一次）。若面板继续用 `Time.deltaTime` 做积分，每 133ms 只前进一帧的量——**慢 8 倍**。故 `deltaTime` 必须由派发方给出，取值是「距上次派发」的间隔。这是 LOD 的正确性前提，不只是风格统一。门面无参 `Update()` 一并删除：它给不出正确的 `deltaTime`
- **UI 导航栈统一入栈（破坏性）**：显示栈改存实例，`OpenAsync` 与 `PushAsync` 都入栈，`HasPrevious` → `CanGoBack`，`BackToAsync<T>` → `PopToAsync<T>`。此前只有 `PushAsync` 入栈，于是「Open 开主界面 + Push 开二级页」之后栈深恒为 1，`PopAsync`、`HasPrevious`、遮罩点击关闭会同时失效——而那恰是最常见的用法组合
- **UI 排序空间重做（破坏性，且修一处长期潜伏的正确性缺陷）**：`Canvas.sortingOrder` 名义上是 `int`，运行时却只保留 **16 位有符号**范围，超出会被静默截断回绕（实测 `100001 → -31071`、`500000 → -24288`）。既有方案 `layer × 1000` 因此只要层号 ≥ 33 就全盘失效，而框架推荐的层恰好越界：层 200 实存 `+3393`、层 300 实存 `-27679`，于是 **Top 层实际渲染在 Popup 之下**、遮罩也不在预期位置。之所以从未暴露，是因为此前没有任何用例断言过 `sortingOrder`。现全部取值经 `UISorting` 推导并落在 int16 内，另加一条把每个保留带取值写进 Canvas 再读回的守卫用例
- **UI `OnBlur` 不再置 `IsPaused`（破坏性）**：改由 `OnPause` / `OnResume` 承担。第三方若在重写的 `OnBlur` 里读 `IsPaused`，会看到旧值
- **UI `ShowTipAsync` 的完成时机变化（破坏性）**：从「播放结束」变为「已创建并开始播放」。Tip 的播放改由统一帧通路推进，故无从等待播完；需要知道何时播完请按 `TipConfig.Duration` 自行计时
- **UI `UIDefaultController` 默认静默（破坏性）**：它每次拦截都打日志，等于每开一个面板五条带字符串插值的日志。需要观察生命周期流程时用 `new UIDefaultController(verbose: true)`
- **UI 异步 API 全面接入取消令牌（破坏性）**：`IUIManager` 全部异步成员、`IUIController` 五个回调、`IUiHudProvider.AttachAsync`、`IUITipProvider` 均新增 `CancellationToken`（一律收盘）。两条提交点语义已写进文档：打开以「实例化返回」为提交点、关闭以「`OnBeforeCloseAsync` 放行」为提交点，之后一律走完——「已从集合摘除但未回池」的半关状态比「取消失效」难排查得多
- **UI `IUITipProvider` 与 `IUiHudProvider` 同形（破坏性）**：新增 `Update(deltaTime, time)` 与 `DetachAll()`
- **UI provider 归属下沉（破坏性）**：`_tipProvider` / `_hudProvider` 由门面静态字段移入 `UIManagerImpl`，`IUIManager` 相应新增五个成员。门面由此退化为纯转发——此前它要自己管懒初始化、还要回头摸 `_instance.UIRoot` 才能给 provider 挂根节点
- **UI `UnloadAsset` / `ClearAssetCache` 正名（破坏性）**：改为 `ForgetPreload` / `ClearPreloads`，并把真实作用写进文档——**只清记账，不卸载资源**。真释放见新增的 `UnloadPanelAssetAsync`

- **Save 契约重整（破坏性）**：`ISaveManager` 全面收敛——异步成员统一 `Async` 后缀（此前同一文件里两种读法，`SaveAsync` 保留而 `GetSlotMetas`/`DeleteAllSlots` 丢弃）；玩家隔离 API（`SetCurrentPlayer` / `ClearCurrentPlayer` / `CurrentPlayerId` / `GetAllPlayerIdsAsync` / `DeletePlayerAsync`）由实现类内部成员提升到接口，消除门面里靠 `is` 降级访问、自定义实现静默空返回的问题；去掉同步成员（`DeleteSlot` / `SlotExists`）——同步 IO 在主线程执行，而 Console 等平台的 Provider 可能是数百毫秒的平台 SDK 调用
- **Save 存档 IO 后切回主线程**：Provider 的 IO 用 `RunOnThreadPool(configureAwait: false)` 完成时停留在子线程，此前 `SaveManagerImpl` 中所有 IO `await` 之后的代码——含 `DataManager.ApplySnapshot` 与第三方 `IDataBlock` 回调——都在线程池上执行，既是「Unity API 只能在主线程调用」违规，也是与主线程读写共享状态的真实竞态。代价：这些方法依赖 PlayerLoop 泵，**禁止在主线程同步阻塞等待**（`.GetAwaiter().GetResult()` 会死锁）
- **Save 序列化移出主线程**：载荷的序列化与反序列化改在池线程上执行（`CreateSnapshot` / `ApplySnapshot` 必须留主线程，它们要遍历并回调第三方数据块）。固有代价：目标类型的字段初始化器与构造器会在后台线程执行，第三方数据类若在其中访问 UnityEngine 对象会抛异常
- **Save 校验前移**：槽位号与 playerId 的校验集中到 `SavePathUtility` 并在入口快速失败。此前校验只在构造路径时生效——`SetCurrentPlayer("../../outside")` 会先污染玩家上下文、直到下一次保存才失败（那时快照已生成、脏标记已清空）；`SetCurrentPlayer(".")` 更能穿过路径沙箱把存档写进域根、绕开玩家隔离，而 `DeletePlayer(".")` 会删掉域根下全部存档
- **Save 写操作门禁**：`IsBusy` 改为 `Interlocked` 原子占位并覆盖全部写操作（保存/加载/删除/复制/移动）。此前删除类操作不受约束，可在「已写 `.tmp`、尚未替换」的窗口里把目标文件抽走；且普通 `bool` 字段跨线程无可见性保证。读操作刻意不进保护（替换是原子的，读只会看到完整的旧文件或新文件），该取舍已写入注释
- **Save 枚举结果确定化**：`GetSlotMetasAsync` 结果按槽位号升序排序（此前是文件系统顺序，跨平台不确定）；损坏槽位带 `SaveMeta.isCorrupted` 标记保留在列表中，不再从列表消失——消失会与 `SlotExistsAsync` 返回 true 自相矛盾，界面既看不到也删不掉它
- **Asset 并发初始化修复**：`InitializeAsync` 并发调用共享同一初始化任务（门面 + 实现层），`Destroy()`/`SetInstance()` 使在途初始化结果作废
- **未初始化异常消息统一**：各模块 guard 消息改为中文 + `[模块]` 前缀 + 修复提示；`DataException` 改为继承 `InvalidOperationException`
- **Message 异步订阅签名变更（破坏性）**：`SubscribeAsync` 处理器由 `Func<TMessage, UniTask>` 改为 `Func<TMessage, CancellationToken, UniTask>`；处理器收到的令牌即订阅自身令牌，退订会取消在途 await
- **Message 请求处理器签名变更（破坏性）**：`Register` 处理器由 `Func<TRequest, UniTask<TResponse>>` 改为 `Func<TRequest, CancellationToken, UniTask<TResponse>>`；`RequestAsync` 补可选 `CancellationToken`（令牌语义见下条）
- **Message `RequestAsync` 取消语义变更（破坏性）**：调用方令牌此前只原样转发给处理器、**不**中断本次等待——处理器若不响应令牌，调用方将永久挂起。现对齐 `PublishAsync`：令牌一方面仍原样转发给处理器，另一方面用于取消本次等待，取消时抛 `OperationCanceledException` 但不中断已启动的处理器。破坏性在于「传入已取消的令牌仍能正常拿到响应」这一写法不再成立，对应的既有测试已改写为用令牌同一性验证转发，并新增「在途取消」用例锁定新语义
- **Message 异步处理器执行时机变更**：异步订阅不再与同步订阅者同链交错，改为排在同步投递之后独立派发
- **Message 缓冲通道结构**：`MessageBroker` 的 6 个业务字典合并为「通道对象」两表（`MessageChannel<TMessage>` + `KeyedChannelStore<TKey,TMessage>`），键值键改由 `(消息类型, Key 类型)` 复合而成
- **Message 淘汰即回收**：`EvictBufferedChannel` / `EvictBufferedChannels` 丢弃重放缓存后顺带回收因此变空的通道与存储表项，不再残留「已可回收但仍在表中」的空壳；`TrimEmptyChannels` 退居兜底与诊断手段，常规路径下返回 0。返回值语义不变（只计淘汰数，不含回收数）。同时补文档：带 Key 的淘汰是语义要求而非可选优化——漏淘汰会把上一个同 Id 实体的旧值重放给新订阅者
- **节点订阅解析变更**：`BaseNode` 实现 `IMessagePublisher`/`IMessageSubscriber`，`NodeExtensions.Subscribe` 改以 `BaseNode` 为接收者以消除与 `MessageManager.Subscribe(this IMessageSubscriber, ...)` 的重载二义
- **Message 统计字段更名（破坏性）**：`MessageBusStats.MessageTypeCount` 改为 `ChannelStoreCount`。它的值本就是「类型通道表项数 + 键值通道表项数」，同一消息类型既有类型通道又配了键值通道（或配多种 Key 类型）会各占一项而重复计数——原名在说谎，而统计接口的用途正是排查泄漏，误导性命名会直接浪费排查时间。`ToString()` 的标签也由「类型」改为「通道表」。无行为变化，仅更名
- **Message 接口可见性收敛**：`IMessageBroker` 由 `public` 改为 `internal`——它只有一个 `Clear()` 成员，实现类 `MessageBroker` 与实例替换点 `MessageManager.SetInstance` 均为 internal，对外既无法实现也无处注入，公开它只会得到一个不可用的契约。`IMessagePublisher`/`IMessageSubscriber` 保持公开（它们是扩展方法的接收者类型，`BaseNode` 实现了二者）。外部代码本就无法取得 `IMessageBroker` 实例，实际无可观察影响
- **Reactive 消息总线内部去 R3 化结构**：订阅直落自研事件流，移除内部 ObservableSignal 包装层与信号缓存；公共 API 与行为语义不变（投递顺序、缓冲保留、completed 语义保持）
- **Reactive 清理**：裁撤 Signal/ISignal/IReadonlySignal 死代码（公开类型名中 Signal 术语退场），ReactiveProperty 接入自足 `IReactiveProperty<T>` 接口链；注释与文档同步去 R3 叙述
- **模块拆分**：消息总线与事件流引擎迁入新 `Runtime/Message/`（命名空间 `XFramework.XMessage`），Reactive 仅保留响应式属性；公共类型名不变，使用方仅需改 using（编译期破坏性变更）
- **引擎去 Rx 命名**：Subject→EventStream、ReplaySubject→BufferedEventStream、AnonymousDisposable→ActionDisposable；Unit 删除（InputManager 帧脉冲改发 `Time.frameCount`）；事件引擎日志前缀定稿 `[Message]`

- **Settings 响应式契约重整（破坏性）**：移除 `ObserveField`，`Observe` 改为「订阅即回调当前对象」。`ObserveField` 用 `EqualityComparer<TField>.Default` 去重，对引用类型字段即为引用相等，`s => s.audio` 这类选择器在子对象内容变化时被静默吞掉且无文档；字段级能力改由 `SettingRef` 以**字段身份**路由，该缺陷随之消失，不必再修。两级分工至此明确：字段值变化归句柄、对象被整体替换归 `Observe`。此次变更还让 `[0.2.0]` 那条「订阅立即回调」的说明从与测试断言相反变为准确
- **Settings 实现类可见性收紧**：`SettingsManagerImpl<T>` 由 `public` 降为 `internal sealed`，对齐门面模板「公开面只留接口」。公开面从未暴露过该具体类型——`Initialize` 返回的一直是 `ISettingsManager<T>`，故实际无可观察影响
- **Settings 存储替换与释放语义收紧**：`Store` setter 明确为「只换后端、不迁移数据」并在替换时打 LogWarning（刻意不做隐式重新加载——那会静默丢掉内存中尚未 Save 的修改，是更难查的故障）；`Load` 防御 store 违约返回 `null`；`Save(null)` 由静默 return 改为抛 `ArgumentNullException`，与 `Apply(null)` 对齐；`Dispose` 后除自身外所有公开成员抛 `ObjectDisposedException`
- **Update 派发期间的操作改为帧末生效**：`Disable` / `Enable` 原先在派发期就地改活表，而遍历是「读 `entries[i]` → 回调 → 写回 `entries[i]`」，回调里改动下标更小的元素后写回会覆盖顶替上来的节点整条条目（对方永久停更）；现与注册/注销统一走操作队列，按调用顺序在帧末应用。代价是派发期发起的 `OnDisable`/`OnEnable` 推迟到帧末——不破坏契约（不会出现「`OnDisable` 之后又 `OnUpdate`」），且迭代外调用仍立即生效
- **Update `Unregister` 等门面形参放宽到 `IUpdateLifecycle`**：三个时机各有自己的接口，注销/启用/禁用/查询不再以 `IUpdateable` 为形参；门面把请求转发给各时机，只有持有该对象的那套会动作，因此不必维护「节点属于哪个时机」的映射表（那是第二份真相，漏同步即幽灵条目）
- **Update 切片改为步长切片**：原实现按连续下标区间划分，`count` 不是 `sliceCount` 整数倍时尾部切片会被夹空、前面的切片超载（17 个条目 8 个切片派发成 3,3,3,3,3,2,0,0，最后两帧白跑一遍循环）；改为「本帧只处理下标 ≡ sliceIndex (mod sliceCount) 的条目」，每帧派发量只差 1
- **Update 重复注册去重**：同一对象重复注册改为「重新注册」（先摘旧条目再按新 LOD/Depth 插入）。此前会产生两条条目、每帧被派发两次——树里挂了两个 `UpdateNode` 即触发，且单值桶索引表达不了「两条条目分处两个桶」，注销时的「删净」语义会漏删
- **Update 可见性收敛（破坏性）**：`UpdateScheduler` 由 `public` 改为 `internal sealed`，对齐「实现类默认 internal sealed」的框架约定；第三方若直接 `new UpdateScheduler()` 会编译不过，但正因它的内部结构仍在演进，收口可避免依赖上内部细节
- **UI 每帧通路并入统一调度**：面板 / HUD 的每帧更新原由场景里的 `UIRootNode.Update` 驱动，那条通路既不在 LOD 调度里、也不受 `Pause` 约束（暂停游戏时面板照跑），还要求场景里必须存在 `UIRootNode`。现由 `UIManager.Initialize` 注册进 `UpdateManager`；面板 `OnUpdate` 的时机随之从「场景 MonoBehaviour.Update」变为「注入的 Update 系统内」，并从此可被 LOD 降频与统一暂停
- **Update LOD 档位改名（破坏性）**：`UpdateLOD.Frame1/2/4/8/16/32` 改为 `Tier0..Tier5`，枚举值与行为均不变（节拍基准的改动见下条）。旧名把「每 N 帧」这一实现细节写进了标识符，而档位的真正含义（周期）属于模块约定——做成序数名后，调整节拍基准时不必再改一次名。迁移按 `Frame(2^k) → Tier(k)` 机械替换（`Frame1`→`Tier0`、`Frame8`→`Tier3`、`Frame32`→`Tier5`），第三方只需改标识符
- **Update LOD 节拍由帧数改为时间（破坏性）**：切片节拍原先每帧推进一格，故第 k 档的周期是 2^k **帧**、随帧率缩放（`Tier3` 在 30fps 下 267ms、144fps 下 56ms，跨度 4.8 倍）。现按各轴自己的时间推进（一格 = 1/60 秒），周期与帧率无关。**固定步轴除外**：`Time.fixedTime` 每步恰好等长、本无漂移可修，那里保持每步一格，档位含义是「每 2^k 个固定步」。每帧最多补 3 格——上限决定「周期精确」能覆盖到多慢的帧（余量恒小于一格，故帧长不足 N 格时该补的格数不超过 N），取 3 即帧长 50ms（约 20fps）以内精确；补不上时丢弃**整格**债务而非累积到后续帧，卡顿不会滚雪球。切片算法本身、每帧派发量上限、`deltaTime` 语义均未变，高帧率下只是多出「本帧不推进」的空帧。副作用：`timeScale = 0.5` 时的节流强度不再与 `timeScale` 无关——逻辑轴按逻辑时间计拍，墙钟周期随之翻倍（这正是「逻辑时间轴」的应有之义，但推翻了旧文档的说法）

- **UI `CloseAllAsync` 一并回收 HUD 与在播 Tip（行为变更）**：HUD 与 Tip 不是面板，但与面板共享同一个 UIRoot 与生命周期——「全部关闭」对调用方（切场景、回标题）而言就是「界面清空」，只关面板会让世界空间 HUD 与飘着的 Tip 继续留在场景里。实现里那句「已由门面处理」的注释与 README 的承诺都写着这个行为，此前并不存在。**遮罩刻意不在其列**：它是引用计数句柄，强制清掉会让别的系统手里的句柄凭空失效，要收需显式 `HideMask()`
- **UI `SetLayerVisibility` 记住期望值（行为变更）**：层容器是「该层第一次开面板」时才创建的，原实现只对已存在的容器生效，于是「先隐藏、后开面板」会让隐藏被悄悄撤销。现补一份与 `SetLayerInteractive` 对称的记忆，并在容器创建时立即应用
- **UI `UIManager.Subscribe` 的 `context` 放宽到任意生命周期对象（破坏性：签名）**：形参类型由 `MonoBehaviour` 改为 `object`，实现 `IDestroyCancellationToken` 的普通 C# 对象（ViewModel / Model）从此可用这个归口入口；两者皆非时不绑定并打一条告警。**源码兼容**（`MonoBehaviour` 实参照旧可传），但签名不同，二进制层面需重编。绑定逻辑一并改为复用 `MessageManager` 的同一份实现——此前三个重载各自手写了一遍，比底层弱三处：每次订阅分配一个闭包、令牌已取消时仍去注册、且只认 `MonoBehaviour`

### Removed

- **删除节点系统（破坏性）**：整个 `XFramework.XNode` 模块——`BaseNode` / `ParentNode` / `EntityNode` / `ContainerNode` / `DictionaryNode` / `RootNode` / `LeafNode`、`NodeFactory` / `NodePool` / `NodeExtensions` / `NodeUtility`、`StartupExtensions` / `StartupStages` / `AssetExtensions`，以及 `Node/Bootstrap/` 下的四个引导节点。节点系统同时承担了四件事：框架的启动路径、一套 GamePlay 架构、一个服务定位器、更新调度的桥接；只有最后一项是基础框架层该管的，而它已由 Bootstrap 模块覆盖。本项目定位是基础框架层，不该预设 GamePlay 架构。连带影响：① `GameLauncher` 改由 `Bootstrap` 驱动并移入 `Runtime/Bootstrap/`（命名空间 `XFramework.XNode` → `XFramework.XBootstrap`），仍是**可选**入口，场景里没有它也照常运转；② `UpdateManagerExtensions`（`ResolveTimeMode` / `RegisterUpdate` / `UnregisterUpdate`，接收者均为 `BaseNode`）与 `LocalizationBootstrapNode` 一并移除，Update 模块从此与节点系统无关；③ `IUpdateTimeMode` 保留但不再有自动轴发现——注册时请显式传 `timeMode:`；④ `AssetExtensions` 的 12 个方法全部是 `AssetManager` 静态方法的纯转发（`self` 参数从未被使用，其中 `PreloadAssetsAsync` 还比门面版少了 `progress` 参数），直接调用 `AssetManager` 即可

### Fixed

- **File 域根在子线程解析，启动恢复扫描必崩**：`DesktopFileProvider` 的 `GetFilesAsync` / `GetDirectoriesAsync` 把 `GetPhysicalPath(domain, null)`（→ `Application.persistentDataPath`）放在了 `UniTask.RunOnThreadPool` 的委托**内部**，而 `RunOnThreadPool(configureAwait: false)` 会把池线程留给调用方续体——`SaveManagerImpl.RecoverAsync` 的线程契约偏偏是「全程不切回主线程」。于是**每次启动**的恢复扫描必然在池线程上读 Unity API 而抛 `UnityException`；即便从主线程调用，只要目标目录存在也照样抛（委托内那行不在提前返回的路径上）。现改为**域根在主线程解析一次并缓存**（`DesktopFileProvider.PrimeRoots`，由 `FileManager.Initialize` 与编辑器的 `InitializeOnLoadMethod` / 运行时的 `RuntimeInitializeOnLoadMethod` 各调一次），`GetPhysicalPath` 退化为纯字符串运算，两处 `rootDir` 取值一并提到委托外。`SaveManagerImpl.RecoverAsync` 的「不切回主线程」契约至此才真正成立，Save 侧一行未改
- **为什么此前没被单测抓到（同批补上）**：8 个 File/Save fixture 全部注入 `TempFileProvider`——它在构造函数里就把根路径定死，**结构性绕开 Unity API**，于是「域根解析是否碰了 Unity API」这一整类缺陷在替身上不可见。唯一用真实 `DesktopFileProvider` 的 EditMode 用例因此独自红了 7 次（失败消息逐字相同），还被当成了环境噪音。新增 `DesktopFileProviderThreadTests`：主线程与池线程两条路径各锁一次，改前 0/4 通过、改后 4/4

- **Save `LoadAsync` 静默清空内存数据**：内容为合法 JSON 但不是存档的文件（如 `{"foo":1}`）同样能反序列化出快照，其数据块列表取到的是字段初始化器给的空列表而非 `null`，`ApplySnapshot` 会先清空全部数据块、再因列表为空直接返回——玩家的内存数据被零警告清空；传字面量 `null` 则先清空全部数据块再抛 NRE。现引入结构校验（非 null + 含数据块列表 + 版本号 ≥ 1）后才允许应用
- **Save `GetAllPlayerIds` 恒返回空数组**：原实现靠扫描非递归 `GetFilesAsync` 的返回路径、用分隔符切出玩家目录前缀，而根目录返回的路径永不含分隔符，该分支自诞生起从未成立，且无测试覆盖
- **Save 按玩家查询会串档**：`GetPlayerSlotMetas` 在 `await` 期间把全局玩家上下文临时改成目标玩家，且该路径不设门禁——窗口内发起的保存会写进被查询者的目录，而 `SetCurrentPlayer` 的 busy 守卫对此无效（它读的正是未被置位的 `IsBusy`）。现改为玩家上下文只作默认值、实际路径由参数决定
- **Save 数据块恢复失败被静默吞掉**：`ApplySnapshot` 会跳过恢复失败的块并正常返回，而 Save 侧据此回报 `Loaded`——内存里少了一整个块的数据却报告成功，玩家看到的是半个游戏加一行控制台 warning。现由 Data 侧上报失败块数，Save 侧据此判定加载不完整、回滚内存并回报 `Corrupt`
- **Save 加载失败无回滚**：应用快照失败后内存已被清空且不可恢复，现先留回滚点、失败时尽力恢复（回滚本身失败也必须记 Error 而不能静默，否则会留下「以为已回滚、实则是半清空」的状态）。已知局限：`CreateSnapshot` 会清空脏标记，故回滚恢复得了数据、恢复不了脏标记集合
- **Reactive 带过滤订阅的过滤条件抛异常不再击穿派发**：记 Error 日志、该订阅本条不收、订阅保留、其余订阅者照常收到（含缓冲重放路径）
- **BufferedEventStream 方法隐藏改为 override**：此前经 `IDisposable` 接口释放会落到基类槽位，缓存不清空，已释放的流仍向新订阅者重放陈旧值；同时修复「已完成（`OnCompleted`）后再 `OnNext` 会写回缓存并重放」的 completed 语义违背
- **键值通道的 Key 类型隔离**：同一消息类型配不同 Key 类型（如 `Publish("k", 1)` 与 `Publish(1, 1)`）此前会强转到先创建者并抛 `InvalidCastException`
- **缓冲通道内存无界增长**：新增显式淘汰 API；`EvictBufferedChannels<T>` 按消息类型跨全部 Key 类型淘汰
- **订阅回池的重复归还**：淘汰缓冲流后陈旧句柄的 `Dispose` 会二次回池，同一节点被发放两次将形成 `node.Next` 自环并令派发快照死循环
- **`SubscribeAsync` 已取消令牌留下空通道**：四个重载此前先建通道再判令牌（实参先于被调方法求值），令牌已取消时通道（键值版连整条存储表项）被建出却没有订阅者来触发回收，只剩 `TrimEmptyChannels` 兜底；现将令牌守卫置于建通道之前，判空仍先于判令牌（保证 `null` 处理器照常抛 `ArgumentNullException`）
- **异步订阅令牌竞态下通道永久不可回收**：`AsyncSubscription` 构造期间向已取消令牌 `Register` 会同步内联触发退订，而该项尚未入表，退订落空、不回调空通知；此时若仍入表，登记表永远非空使 `IsReclaimable` 恒为 false，自动回收与 `TrimEmptyChannels` 共用该谓词而双双失效，只能等 `Clear()`。现改为已退订的登记项不入表（该窗口仅在跨线程 `Cancel` 时可达，属防御性加固）
- **节点消息订阅补全**：`NodeExtensions.Subscribe` 的 `where TMessage : class` 约束使框架内置 struct 消息全部编译不过，且与 `MessageManager.Subscribe` 同形导致重载二义；`BaseNode` 实现消息标记接口后二者同时解决
- **订阅与过滤器 API 判空**：此前 `null` 过滤器要到派发时才 NRE 并被 `try/catch` 吞成日志（静默失效且订阅保留）
- **异步与键值重载的二义**：键值 `PublishAsync` 的 `strategy` 取消默认值，避免两参调用 `(消息, 策略)` 与 `(Key, 消息)` 无法裁决
- **文档修正**：UI README 引用了不存在的 `MessageManager.Unsubscribe`；`IMessageSubscriber` 扩展文档夸大了生命周期绑定范围（实际仅 `MonoBehaviour`）；`Documentation/XFramework.md` 的 Node README 链接漂移

- **Settings `Destroy` 后永久不可用**：`_destroyed` 是单向闩锁——`Destroy` 置位后 `Initialize` 开头即抛 `ObjectDisposedException`，而该异常消息偏偏写着「SettingsManager 已被销毁，请重新调用 Initialize」，照着做会再抛一次。关闭 Domain Reload 时静态字段不复位，缺陷还会跨 Play 会话残留。现改为复位而非闩锁，与 Localization / Config 门面一致；重复 `Initialize` 补 LogWarning
- **Settings `Reset` / `Load` 忽略 `defaultFactory`**：该工厂此前只在构造函数一条路径上生效——`Reset` 硬编码 `new T()`、`Load` 直接取 store 的返回值。后果是玩家点「恢复默认」拿到的是字段初始值，而不是 README 承诺的设备自适应默认值，**文档与实现相反**。`Load` 侧还需先问 `Exists()`：`ISettingsStore.Load` 的契约是「无数据时返回 `new T()`」，管理器无法区分「读到了持久化数据」与「根本没有数据」
- **Settings 损坏文件会让游戏每次启动都崩**：原实现完全不捕获异常，读失败或 JSON 解析失败一律抛给主循环。最坏的后果不是「读不到设置」，而是存档被写坏后**此后每次启动都崩、且玩家无法自救**——设置文件由游戏自己写，玩家不知道该删哪个。现读失败与解析失败一律 LogWarning 并回退默认值；主文件损坏时尝试一代备份 `.bak` 回退，但**主文件不存在时不回退备份**——否则 `Reset`（删文件）之后的下一次 `Load` 会把玩家刚重置掉的旧数据从备份里复活
- **Settings 已释放后仍可写盘**：`Dispose` 只终止了通知流、未置释放标志，`Save` 照常写盘，`Observe` 返回一个永不回调的空句柄（`EventStream` 对已 `OnCompleted` 的流正是返回空订阅）而调用方毫无察觉。现补释放标志，除 `Dispose` 外所有公开成员释放后抛 `ObjectDisposedException`
- **Settings 测试用例生来就错**：`ObserveField_FieldIsolation_IndependentCallbacks` 断言「只改 Volume 时 Name 观察者不回调」，与同一文件里的 `ObserveField_FirstValue_AlwaysPasses` 直接矛盾。该用例随「移除 R3 依赖」那次提交与该测试文件一同引入即错，并非后续回归——R3 原实现的 `Select().DistinctUntilChanged()` 同样放行首个值，故它在 R3 下也一样会失败
- **文档修正（Settings）**：`Documentation/XFramework.md` 的「设置操作」表此前记录了一整套**代码中不存在**的 API（`Get<T>()` / `ResetToDefaults` / `ApplyAsync` 等），第三方照文档接入会踩空；现改为如实描述。Settings 模块 README 同步重写，补入两级订阅分工、写入契约与「已知限制」一节
- **Update 已注销对象被 LOD 迁移复活**：待处理操作缓冲是「先全部注销再全部注册」两阶段，而 LOD 迁移被拆成一条 remove + 一条 add；同一帧内既迁移又被注销的对象在 remove 阶段删掉旧条目后，add 阶段又被插进新桶，此后永久继续被派发且不会再有人来清它。现改为单条有序队列、按入队先后逐条应用，迁移改为**条件操作**（应用时仍在桶里才生效）
- **Update 派发期禁用/启用覆盖他人条目**：`entries[i] = entry` 的写回用的是回调前查到的下标，而 `Disable`/`Enable` 会就地增删元素——禁用排在自己前面的节点后，写回会覆盖顶替上来的节点（对方永久停更、自己此后每帧被派发两次）；禁用自己是桶内最后一个元素时直接抛 `ArgumentOutOfRangeException`。现统一入队后，派发期间没有任何代码能改活表
- **Update `ProcessImmediate` 回调期改写活表**：同源缺陷的第三处——回调里注销该对象会让随后的写回落到已易主的槽位、并把已注销对象插回桶里。现复用迭代闩锁，回调期间的操作进缓冲、返回后统一应用
- **Update 关闭域重载后整个模块静默死亡**：`AutoInit` 在编辑器分支只挂 `[InitializeOnLoadMethod]`，而关闭 Reload Domain 后进入播放不会重载程序集、该回调不再执行；退出播放时又置了不可逆的 `_shutdown` 并丢弃调度器。结果是第二次进入播放后 `IsInitialized` 恒为 false，十余处守卫全部静默 return 且 `Clear` 也救不回来。现无条件挂 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 并让 `AutoInit` 幂等（只在缺失时重建、`Application.quitting` 幂等订阅），删掉那个恒真的 `_shutdown` 闩锁
- **Update 暂停语义缺失**：此前 `timeScale = 0` 时切片相位照常推进、节点仍被派发但 delta 恒 0——暂停期间白跑派发，恢复后相位已漂移（`Frame32` 在 60fps 下意味着半秒多的空窗）。现逻辑轴冻结时既不派发也不推进帧计数；显式 `Pause()` 恢复时重锚时间基准，因此恢复后第一帧 delta 为 0 而不是「整段暂停时长」（刻意不追赶）；`timeScale < 0` 的负间隔钳制为 0
- **Update `ProcessImmediate` 的既有语义写进文档**：派发期间调用它只重置时间基准、不执行更新（在别人的 `OnUpdate` 里再次回调自己会形成嵌套派发）。该行为本次未改，只是补上说明——此前文档没有提，容易误以为「任何时候都能立即执行」
- **文档修正（Update）**：模块 README 重写（修正 `Frame30`→`Frame32`、失效的目录树、一段**无法编译**的 `static class : IUpdateable` 示例，补入三个时机/双时间轴/暂停/派发期语义/已知限制）；`Documentation/XFramework.md` 中不存在的 `UpdateManager.Bind(root)`、少一个形参的 `OnUpdate` 签名与启动流程示意一并订正

- **UI 遍历中改集合导致崩溃**：`UIManager.Update` 原先在 `foreach` 活动面板集合的过程中调用 `OnUpdate`，而面板自关时关闭路径是同步走完的（默认控制器的 `OnBeforeCloseAsync` 同步完成），于是当场抛 `InvalidOperationException`。现改为先取快照再派发
- **UI 同类型并发打开会各自实例化**：第二次 `RegisterPanel` 会把第一个实例直接回池，而它的 `IsOpen` 仍是 true、首个调用方拿到的是池中失活引用，`PanelOpenedMessage` 还会发两次。新增在途打开表，后来者加入同一次打开并共享结果（加入者各持自己的 `UniTaskCompletionSource`——UniTask 只支持单个 continuation，共享同一个会被第二个等待者覆盖）
- **UI `PushAsync` 的失焦不回滚**：打开失败或取消时栈顶会永久停在失焦态（`IsPaused` + Raycaster 关闭）。现补齐回滚，且只恢复交互、不动显示栈——栈根本没变，回到焦点不等于回到栈顶
- **UI `CloseSelfAsync` 在 `OnOpen` 内静默失效**：`IsOpen` 原先在 `await OnOpenImpl()` **之后**才置位，而该方法的守卫是「未打开则忽略」。现引入显式状态机，打开期间 `IsOpen` 即为 true；关闭请求延迟到 `OnOpen` 返回后执行——在 `OnOpen` 的调用栈上重入 `OnClose` 与关闭动画，子类几乎必然写出「先初始化再被清理」的乱序
- **UI 面板回池不解绑 ViewModel**：`UIPanelBinding.Unbind` 原先只在 `OnDestroy` 调用，而面板是回池不销毁，该回调永不触发——关闭后 ViewModel 与订阅会一直存活到下次打开同一面板，此后再不打开就永久驻留。改由 `OnPoolRecycle` 解绑（挂在回池点而非关闭点是刻意的：关闭可被 `OnBeforeCloseAsync` 拦下，那时面板仍然开着）
- **UI HUD 回池漏调 `OnPoolRecycle`**：Canvas 排序值从此在回池时复位，不再留在实例上被复用时误盖别的面板
- **UI Tip 是模块内最后一条自建帧通路**：`UITipItem.PlayAsync` 原先跑 `UniTask.Yield` 自循环并读 `Time.deltaTime`，于是 Tip 既不受 `UpdateManager.Pause` 约束、也不进 LOD 调度，且每条 Tip 一个循环实例。现改为由驱动器逐帧推进，与面板、HUD 共用同一条通路
- **UI 遮罩的 `clickToClose` 事后设置无效**：原先该开关只在「创建遮罩」那条分支里被读过，已存在的遮罩上传 true 没有反应。现创建与事后切换共用同一段逻辑（开关用 `Button.enabled` 而非增删组件——`Object.Destroy` 要到帧末才生效，切换后同帧查询会看到残留组件）
- **UI 遮罩走 `AssetManager.DestroyInstance` 销毁**：遮罩由 `new GameObject` 创建、从未经 AssetManager 托管，而 `DestroyInstance` 开头就 `EnsureGlobalInitialized`——于是只要走过 `ShowMask`，`UIManager.Destroy()` 就要求 AssetManager 存活，未初始化时直接抛异常。改为直接销毁
- **UI Tip 与 HUD 的 `sortingOrder` 撞车**：两者原先同为 `999000`，覆盖顺序不确定。现分列 `30000` / `31000`
- **UI `GetTopSortingOrder` 算错**：它忽略了层偏移（返回 `counter × 1000 + 1000`），与 `GetNextSortingOrder` 口径分裂。该 API 已随排序空间重做一并删除
- **UI 层级「整体禁交互」会被焦点变化撤销**：`OnFocus` 与 `OnOpenImpl` 都会无条件把 raycaster 打开，它们并不知道层被整体禁用过。现由管理器在焦点回调之后、打开之后、以及 Push 取消回滚之后统一压回层状态
- **UI `UIBinder.BindToLocalizedText` 的订阅无人释放**：面板回池不销毁，故每次打开都会多一条全局 Localization 订阅，且回池后仍会给失活的 TMP 写文本。现归口到 `UIViewBase.Track`
- **UI `UIBinder` 跨模块引用 `XMessage.Internal`**：它只用了那里的 `ActionDisposable`（十行适配器），内联成本地私有类即可切断这条实现细节依赖
- **UI 门面探测型查询在未初始化时抛异常**：`IUIManager` 的文档与 `UIManagerImpl` 的实现都明确写着 `GetState` / `OpenCount` / `IsAnyOpen` / `Panels` / `CanGoBack` / `IsMaskShowing` / `DumpState`「未初始化时返回空值，便于场景加载早期探测」，而静态门面给它们统一套了 `EnsureGlobalInitialized()`——两处散文与实测行为相反，`UIStateWindow` 为此不得不先判一句 `IsInitialized`。现一律返回空结果（`Panels` 给空视图而非 null）。操作类成员与 `IsOpen<T>` / `GetPanel<T>` / `GetTopPanel` / `CopyPanels` 仍照抛——后四个在实现里本就调 `EnsureInitialized()`
- **UI 重新打开已打开的面板时只排序、不聚焦**：`OpenAsync` / `PushAsync` 的「已打开」分支只调 `BringToFront`，而它按契约只重排渲染次序。于是面板渲染在最上却 `IsPaused`（`DriveTier` 跳过它）且 raycaster 关着，而原栈顶仍 `IsFocused`——「看得见、摸不着、也不更新」，两个面板同时声称有焦点。触发序列很普通：Push 开二级页把主面板暂停，再 `OpenAsync` 主面板
- **UI 批量关闭会把同一个面板关两次**：`CloseAllAsync` / `CloseLayerAsync` 先快照再逐个 await，而它们直连的 `ClosePanelInternalAsync` 没有「还在不在册」的检查——快照里的 A 在自己的 `OnClose` 里关掉同批的 B 之后，外层循环走到 B 时会再关一遍：`OnClose` 跑两次、消息发两次，而回池没有去重（AssetManager 直接 Push），同一个 GameObject 会被压进池里两次。守卫加在所有 await 之后：放在方法入口挡不住控制器 await 期间的重入
- **UI 活动字典不随显示栈一起剪枝**：第三方绕过 `CloseAsync` 直接销毁面板后，`IsOpen<T>` / `OpenCount` / `IsAnyOpen` 继续报「还开着」，`GetPanel<T>` 还会把伪 null 交给调用方；而同一次读取里 `Panels` 不剪、`CopyPanels` 剪，两条读路径对同一份状态给出不同答案。剪枝现收敛进一处，字典扫描只在真剪到空洞时才做（稳态仍是零分配）
- **UI 关闭失败时面板永不回池**：清理段写在 `await DoCloseAsync` 之后且没有 `finally`，而 `DoCloseAsync` 只保证落到 Closed 终态、异常照旧外抛。于是一旦 `OnClose` 抛异常，面板已从活动集合摘除却既不回池也不发消息——没有第二条路径能再碰到它，连同它持有的资源引用一起留在场景里。现抽出 `RecyclePanel` 作为回池的唯一出口，关闭路径与 `Dispose` 共用
- **UI `Dispose` 跳过回池钩子**：原先直接 `_factory.Release`，于是面板 `Track` 的订阅不退、ViewModel 不解绑、Canvas 排序不复位——这些都活在池中实例上，会跟到它被重新取出使用
- **UI 在途打开在管理器销毁后仍写回**：`Dispose` 只把加入者以 null 了结，并不取消主打开方的续体。续体恢复后仍会注册并打开，把面板泄漏进已销毁的管理器。现于每个 await 之后自查：① 防 `UIRoot` 已为 null 时取层容器的 NRE，② 不注册进已拆掉的管理器、也不为一个永远不会被看见的面板跑用户 `OnOpen`，③ 真开起来了的先按关闭路径收干净再回池
- **UI Tip 在 `CloseAllAsync` 之后永久失效（本批次引入的回归）**：`DetachAll` 会把「管理器已拆除」的粘性标志置位，在途实例化完成后据此立刻回池；而 `CloseAllAsync` 收在播 Tip 时用的也是同一个 `DetachAll`，管理器却仍在服役，那个标志没有任何地方复位——于是关过一次全部之后，每次 `ShowTipAsync` 都实例化完立刻销毁且不报错。根因是两个性质不同的调用方（「管理器要退役了」与「只是把在播的清掉」）共用一个标志，现改为世代号。HUD 侧原拟照抄同一标志，一并改为世代号
- **UI HUD 三条清理缺陷**：① 打开失败没有回滚（失败点在注册映射之前，`DetachAll` 也找不到它），实例成为常驻 `Layer_HUD`、持着资源引用的孤儿；② `DetachInternal` 先 `.Forget()` 再立刻回收，关闭的续体会跑在实例已回池、甚至已被另一个目标复用之后，把新持有者的 HUD 关掉；③ 摘账按 `target` 删，而 `target` 可能为 null（HUD 自己上报目标丢失时），残留的死条目会让下一次 `DetachAll` 二次回收
- **UI 遮罩层级不钳制**：面板打开路径一直在钳制，遮罩却把调用方给的层级直接送进 `MaskOrder`（= 层级 × 32 + 31）——传 2000 算出 64031，而 `Canvas.sortingOrder` 是 16 位的，静默回绕成负值后遮罩跑到所有面板后面：既挡不住射线也看不见
- **UI 恢复整层交互会点亮失焦面板的射线**：层开关的语义是「允许这一层交互」，不是「让这一层里每个面板都可交互」——被上层盖住的失焦面板会隔着弹窗吃点击。现按焦点把它们压回去
- **UI `UIRootNode` 会拆掉别人在用的管理器**：`OnDestroy` 的判据是「全局是否已初始化」而非「是不是我」，于是叠加场景下卸载任意一个节点（包括那个从未生效、被静默忽略的第二个节点）都会把另一个场景仍在用的管理器一起拆掉。现改为引用同一判定。「第二个节点被忽略」这半保持现状——多根是另一类需求
- **UI README 四处与代码不符**：HUD 容器排序值三处写作 `999000`（代码是 `UISorting.HudOrder = 30000`，且该值会触发同文档论证过的 16 位回绕）；自定义 Controller 示例的五个方法都漏了 `CancellationToken` 参数（照抄直接 CS0535，现已编过确认）；示例里的 `dialog.WaitForResultAsync()` / `dialog.Result` 全仓不存在；示例层级 900 越过 `MaxPanelLayer = 899`。结构树补上整块缺失的 `Tip/`


## [0.2.0] - 2026-08-20

### 移除 R3 依赖（重大变更）

- **响应式引擎自研化**：移除 R3 NuGet 依赖与 NuGetForUnity，新增零依赖自研响应式引擎（`XFramework.XReactive.Internal`：Subject/ReplaySubject/AnonymousDisposable/Unit）
- **公共 API 不变**：MessageManager、ReactiveProperty、ReadOnlyReactiveProperty、ISignal、InputManager.ObserveXxx、SettingsManager.Observe/ObserveField、UIBinder 签名与行为语义保持不变（订阅立即回调、相同值去重、异常隔离）
- **修复既有缺陷**：MessageBroker 缓冲通道订阅前发布的消息不再丢失；消息过滤器拦截现在真正生效
- **清理**：删除 packages.config、NuGet DLL、残留 R3 csproj、探针测试；XFrameworkDependencyInstaller 仅保留 UPM 依赖安装
- **第三方集成简化**：安装依赖仅需 UniTask + YooAsset 两个 UPM 包

## [0.1.0] - 2026-01-13

### This is the first release of *\<XFramework\>*.

*Short description of this release*
