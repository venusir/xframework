# Changelog
All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)
and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).

## [Unreleased]

> **本版含新增模块与公开 API 变更，其中有破坏性变更**：新增 `XScene` 场景管理模块；
> `AssetManager.LoadSceneAsync` 改形（改为返回可卸载的句柄）、`IAssetManager.LoadSceneAsync` 从主接口
> 移入可选能力接口 `IAssetSceneLoader`。迁移见 `### Changed` 的「破坏性变更迁移表」与「行为变更」。
> 因含破坏性变更，**下次发版应为 `0.4.0`**。

### Added

#### Asset

- **新增可选能力接口 `IAssetSceneLoader` 与场景句柄 `AssetSceneHandle`**：场景的加载 / 卸载从
  `IAssetManager` 移入能力接口（同 `IAssetPoolController` 的形态——不扩主接口，第三方实现零影响）。
  `AssetSceneHandle` 是 `readonly struct : IDisposable`、字段全为值类型（零 GC）、**公开构造**
  （第三方后端也要能产出句柄，同 `AudioHandle` 的取舍），成员 `IsDefault` / `IsValid` / `UnloadAsync(ct)`
  / `Dispose()`（即发即忘，不抛）。门面配 `LoadSceneAsync`（改形，返回句柄）与 `UnloadSceneAsync`
  （**唯一「未初始化也不抛」的成员**——它会被 `Dispose` 调到）。**无卸载路径**这条缺口由此关闭。
- **新增 `AssetPlayMode.EditorSimulate` 编辑器模拟模式**（2026-10-18）：资源直接经 `AssetDatabase`
  加载（虚拟 bundle），**无需先构建资源包**；初始化时框架按包名调用 YooAsset 的
  `EditorSimulateModeHelper.SimulateBuild`，产物在 `Bundles/<ActiveBuildTarget>/<包名>/Simulate`。
  仅 Unity 编辑器可用——Player 构建传此值抛 `PlatformNotSupportedException`（不静默降级）；
  `RemoteServices` 与 `DecryptionServices` 在该模式忽略。枚举按上游 `EPlayMode` 顺序排列
  （`EditorSimulate = 0` / `Offline = 1` / `Host = 2`）。`0.3.0` 的枚举是 `Offline = 0` / `Host = 1`，
  但该 tag **从未分发过**（2026-10-19 用户确认），因此无需为既有序列化让位；若从 `0.3.0` 的源码 / 场景
  升级，整数值已变，反序列化后需重新选择运行模式（见 `### Changed` 的行为变更节）。
  **公开 API 源兼容新增，基线 +1 行**。WebPlayMode / CustomPlayMode 仍不映射
  （理由与重开判据见模块决策台账）。回归用例：
  `AssetDecryptionServicesTests.CreateEditorSimulateParameters_WiresEditorFileSystem` /
  `..._EmptyRoot_Throws`。

#### Camera

- **新增 `XCamera` 相机管理模块：后端中立的激活 / 反激活 / 过渡入口**（2026-10-17）：静态门面 `CameraManager` +
  `ICameraManager` + `UnityCameraManager` 默认实现 + `CameraBlendStyle`。查询面含 `OutputCamera`（当前输出相机，
  世界↔屏幕 / 射线直接用 `UnityEngine.Camera`）、`CameraIds`、`ActiveCameraId`、`IsTransitioning`。
  **核心包零 Cinemachine 依赖**：相机编排（跟随 / 瞄准 / 震屏 / 约束）仍归 Cinemachine，框架只统一状态与过渡；
  Cinemachine 3.1.7 的一等实现由**独立可选扩展包** `com.venusy609.xframework.cinemachine` 提供（单独 asmdef，
  单独安装，不随核心包发布）。原「不做：相机管理」中「编排归 Cinemachine」仍成立，推翻的是「不需要统一入口」
  这一半。**使用方不受影响**，除非你要用它：不注入实现时所有门面成员抛「尚未初始化」。
- **`CameraHandle` 动态相机注册 / 激活**（2026-10-17）：`ICameraManager` 新增 `Register<T>(T)` /
  `Unregister(CameraHandle)` / `ActiveHandle` / 句柄版 `Activate` / `Deactivate` / `TransitionTo` / `IsActive`；
  `CameraManager` 门面同名转发；`UnityCameraManager` 与 Cinemachine 扩展包用槽位表 + 代际实现。命名 string id
  保留（双键）；动态相机随预制 `Register` / `Dispose`，不需要 authoring id。`CameraHandle` 的 `Generation == 0`
  保留给 `default`，便捷方法默认短路。**对第三方 `ICameraManager` 实现是源码破坏**（需补 7 个成员）；模块
  未发布、当前无仓外实现。
- **合并 `ActiveCameraId` / `ActiveHandle`**（2026-10-17）：`ActiveHandle` 成为接口 / 实现上唯一的活动查询；
  `CameraHandle` 新增 `NamedId`（命名 id；动态 / `default` 为 null）；`CameraManager.ActiveCameraId` 直接读
  `ActiveHandle.NamedId`（O(1)、零遍历）。`ICameraManager` / `UnityCameraManager` / `CinemachineCameraManager`
  移除 `ActiveCameraId`——**对第三方实现 / 直接使用具体类的代码是源码破坏**（模块未发布、无仓外实现）。
- **过场相机边界**（2026-10-17）：Timeline / 过场相机不经过本模块；Cinemachine 扩展包对「Brain 的活动相机未注册」
  告警；`ActiveCameraId` / `ActiveHandle` 对未注册活动相机返回 null / default，`OutputCamera` / `IsTransitioning`
  仍可用。
- **`LogCategories` 新增 `CameraManager`**（同上）。

#### File

- **门面新增 `FileManager.DeleteAsync(FileDomain, string, CancellationToken)`**（2026-10-15）：把
  `IFileProvider.Delete` 放进 `RunOnThreadPool` 执行，**不阻塞调用线程**——这是门面**唯一一处自己决定 IO 线程**
  的原语（`IFileProvider` 没有异步删除孪生，且各平台的后端删除本质上都是同步原语，故由门面代劳最自然）。
  **零接口变更**：`IFileProvider` 一个成员都没动，第三方实现零影响（基线 +1 行，属**源兼容新增**）。
  缺失即 no-op（与同步版同语义）；**取消**只保证「尚未开始的删除不会发生」（委托一旦进入就会跑完）。
  **使用方不受影响**，除非你要用它；**但第三方 `IFileProvider` 需要自核一条**：Provider 的 `Delete` 现在会在
  **池线程**上被调用（与其余异步原语同一条约束）——详见 `### Changed` 的行为变更一条。
  取舍与两条被否形状（给主接口加成员 / 新增可选能力接口）见 `Runtime/File/README.md`。

#### Settings

- **`EncryptedSettingsStore` 实现 `IAsyncSettingsStore`（2026-10-17，SET-001）**：装饰器此前只实现
  `ISettingsStore`，包住平台异步后端后管理器的能力探测探测不到异步接口 → 降级为线程池调同步成员，
  平台侧数据永不参与读写且无告警（违反「装饰器必须实现底层可选能力接口」不变量）。现在内层异步则转发
  （加解密在调用线程完成），否则自身按管理器同款线程池降级。**公开 API 新增（源兼容）**：类声明增加
  `IAsyncSettingsStore` 与 `ExistsAsync` / `LoadAsync` / `SaveAsync`；基线已更新。
  回归：`EncryptedSettingsStoreTests.AsyncInnerStore_AsyncCapabilityIsForwarded`（改前红）。

#### Scene

- **新增 `XScene` 场景管理模块**（`当时的候选模块路线图` §三 的「场景管理」条目立项；该条目是本表最大的一条，
  本轮把论证与实施一并做完）：静态门面 `SceneManager` + `SceneLoadOptions` + `SceneLoadProgress`，**只管
  「一个场景资源怎么加载、怎么卸载」**。**没有**场景栈、没有加载界面编排、没有场景间传参通道、没有场景
  生命周期回调——四条边界逐条落地（理由与配方见 `Runtime/Scene/README.md` 的「设计取舍」）。
  **无 `ISceneManager` 接口**：本仓「有接口」的分界线是「是否存在可替换的后端」，本模块的后端就是 `XAsset`
  的 `IAssetSceneLoader`（同 Timer 的取舍）；**无静态状态**，故无初始化、无会话复位、无诊断面板。
  模块与 `XAsset` 的分工是「编排入口 vs 资源后端」——场景是资源，但资源系统的门面不是场景的入口。
  **门面与 `UnityEngine.SceneManagement.SceneManager` 同名**（用户裁定接受）：同时 `using` 两者会 CS0104，
  README 首屏给两条别名写法；模块源码自身一律写别名——**裸写不报错**，会静默遮蔽成自家类型（首轮编译踩到
  的是另一个同族坑：Unity 6 自己也有 `SceneHandle`，与 YooAsset 的同名类型在同一文件里 CS0104，已用别名消解）。

#### Update

- **新增 `UpdateManager.IsRegistered(IUpdateLifecycle node)`**（2026-10-14）：回答「该节点是否仍在调度中」——
  `OnUpdate` / `OnLateUpdate` / `OnFixedUpdate` 抛异常会让调度器把节点**自动注销**，此前注册方没有任何查询
  能知道这件事。跨时机节点**取或**（任一时机仍持有即算在调度中）；被 `Disable` 的节点**仍算注册**（`Enable` 可恢复）。
  **与 `IsEnabled` 的分工**：一个答「是否在管理中」、一个答「是否被禁用」，两个谓词方向相反（未注册时前者为
  `false`、后者为 `true`）——这条不对称写进了 XML 文档，别当成缺陷。**使用方不受影响**，除非你要用它；
  它是诊断 / 防御接口，成本随节点数增长，不要放进每帧路径。配套的边沿修复见 `### Changed` 的行为变更一条。
- **`ProcessImmediate` 形参放宽为 `IUpdateLifecycle`（UPD-003）**：纯 `ILateUpdateable` / `IFixedUpdateable`
  节点现在可直接立即处理；传 `IUpdateable` 的既有调用源兼容。迁移表见 `### Changed`，公开 API 基线已同步
  （两处签名变化）。

#### Timer

- **`TimerHandle` 实现 `IEquatable<TimerHandle>` 与 `==` / `!=` / `GetHashCode`（TMR-007）**：按
  TableId / Slot / Generation 三元组比较，可直接作字典键 / `HashSet` 元素（相等句柄哈希一致）。
  **代际是身份的一部分**：同一槽位被复用后旧句柄与新句柄不等（ABA 防线）；`Generation == 0` 的无效语义
  不变——两个默认句柄相等，默认句柄与任何有效句柄不等。源兼容新增；公开 API 基线已同步（接口与
  `Equals` / `GetHashCode` 行）。

#### UI

- **`PreconditionChain.ExecuteAsync(CancellationToken)` 重载（UI-C-007）**：在**条件之间**检查取消——
  令牌取消即抛 `OperationCanceledException`，不再执行后续条件。条件内部是否响应取消由该条件自己的
  实现决定（链不把令牌注入条件，`Add` 的委托形状保持不变）。源兼容新增；公开 API 基线已同步。

#### 测试基础设施

- **共享零分配尺子 `AllocationRuler`（测试侧）**：本环境 `GC.GetAllocatedBytesForCurrentThread()` 与
  `Profiler.GetTotalAllocatedMemoryLong()` **恒返回 0**（分配 1 MB 后增量仍为 0），唯一可用的是
  `Profiler.GetMonoUsedSizeLong()`（粒度约 4–8 KB，单次装箱看不见，**必须放大迭代**）。放大的写法此前只有
  一份私有实现（`ReactiveEnumerationAllocationTests`），各模块照抄它就是「好形态靠复制扩散」——现在抽成唯一实现，
  并配三条**尺子自检**（看得见 1 MB、看得见 10 万个 32 B 小对象、零分配时保持安静）与两条**源码守卫**
  （两把恒零尺子在**整个包**内零命中；可用的那把尺子只许出现在 `AllocationRuler.cs` 一个文件）。
  使用方不受影响，但**你的项目若照抄过老写法**（`Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() 前后差)`），
  那类断言在本环境是恒真的，请按 `维护向的工作流手册` §六 的纪律重写。

- **Config / Localization / Pipeline / Pool 的 PlayMode 侧覆盖**（4 个新 fixture，共 11 例）：这四模块此前的用例
  全在 EditMode，而它们各有「EditMode 结构上测不到」的一面——Config 的挂起期可观测与**跨帧**共享、
  Localization 的 `MonoBehaviour.destroyCancellationToken` 绑定（EditMode 下 `DestroyImmediate` 不触发它）、
  Pipeline 的真跨帧挂起与**生产墙钟超时**（EditMode 必须注入假计时器，生产路径从未被跑到）、
  Pool 的跨帧持有 / 归还 / 复用与 `IPoolable` 回调的**帧号**。

- **公开 API 基线守卫（测试侧）**：Runtime 程序集的公开面由 `Tests/Runtime/Architecture/PublicApiBaselineTests`
  锁住——反射渲染成扁平行集合（全限定成员名 `::` 签名）与 `PublicApiBaseline.txt`（1961 行 / 222 个公开类型 /
  34 个 `protected` 扩展点）逐行比对，差异分「**已消失 / 已变更**（破坏性——改名、删成员、可见性收窄、
  形参名或返回类型变更在行文本上都表现为「这行没了」）」与「**新增**（源兼容）」两段。签名含参数名、默认值、
  返回类型、可访问性、泛型约束、枚举值与 `const` 字面量——0.3.0 那批真实变更里最轻的两类（**仅形参名**、
  **返回类型**）编译器一声不吭，只有这种指纹挡得住。更新走
  `pwsh -File Tools/run-tests.ps1 -UpdateApiBaseline`（**只写盘、永不给绿灯**，绿灯由随后一次不带开关的重跑给出）。
  **使用方不受影响**：产出的是一份测试侧基线文件，公开面一个字都没动。
  **射程**：只查 Runtime 程序集（Editor 那 5 类型 / 10 成员不在内）；只查**形状**不查语义——行为变更
  （如「遮罩从此真正吃点击」）仍只能靠本文件的「行为变更」表。

- **包 README 四张清单表的机械守卫（测试侧）**：`Tests/Runtime/Architecture/ReadmeTableTests` 锁四张手改表——
  「模块索引 / 可替换的实现一览（末行 `Bootstrap / Pipeline` 合并行拆开）/ 可配置项一览」的模块集与
  `Runtime/` 直接子目录**双向相等**；模块索引表的文档链接逐字等于 `Runtime/<模块>/README.md` 且落盘存在；
  命名空间列与 Runtime 程序集**双向覆盖**（File 的 `XFileManager` 为登记例外，`XUI.Data` 这类子命名空间按
  前缀归属）；「主要功能」表按**登记表**守：`FeatureTableRegistry`（15 模块 → 触发词）与
  `FeatureTableExemptions`（8 模块 → 理由）必须与模块全集双向相等，**未登记即红**，豁免句的八个模块名也在守内。
  **为什么**：ReactiveList 建成那轮三张表全漏、跨一轮才被补（`f1da8be` 逐表核对是一次性的），本条把那次人工
  核对变成每次全量门禁都跑的用例。**射程**：只守集合与链接——行内说明文字的陈旧（ReactiveList 那轮的另一半）
  与四表刻意各异的顺序不在内；`check-docs.ps1` 对 `.md` 仍零覆盖（守卫在测试侧不在脚本侧）。
  **使用方不受影响**：产出的是一份测试侧守卫，公开面一个字都没动。

- **Bootstrap 的 GameLauncher 真生命周期 PlayMode 覆盖**（3 例，`Tests/Runtime/Bootstrap/GameLauncherFrameTests`）：
  既有 `GameLauncherTests` 刻意不让 `Start` 真跑 `RunAsync`（怕真初始化 YooAsset），本 fixture 覆写
  `ConfigureStages` 只登记假阶段把那半边补上：真 `AddComponent` 下 `Awake → Start → OnDestroy` 全走完。
  三例把 `Modules/Bootstrap.md` 记的两条「推理，未实测」（销毁 × 在途启动交叠）升级为**实测**——用真实
  `AssetBootstrapStage` + 真实 `AssetManager` 门面 + 假实现端到端**复现了预言的「启动失败」错误日志链**
  （三条 Error），并实测出一条更重后果：`Shutdown` 清理整张登记表后，**被 Shutdown 的后置相位仍会被
  在途管线执行**。**该后果已随同日的「彻底修」关闭**（见 `### Fixed` → Bootstrap）：用例 2 的钉现状
  断言按预言精确翻转（`later.ExecuteCount == 0`）；用例 3 原样保留——不响应取消的在途阶段仍会走失败链，
  是修复覆盖不到的边界证据。使用方不受影响：纯测试新增，公开面一个字都没动。

### Changed

- **（公开常量值变更）`UISorting.MaxIndexInLayer` 由 31 改为 30**：遮罩独占层内第 31 位，面板序号
  上限随之降为 30；同层第 31 个及以后的面板会被钳到 30（UI-C-002）。`PanelOrder` / `MaskOrder`
  现在也会钳制越界 layer（UI-C-003）。公开 API 基线已同步。
- **（行为变更）会话开始时静态状态被复位**：框架在 `RuntimeInitializeOnLoadMethod` 的 `SubsystemRegistration`
  档复位六个门面（Input / UI / Asset / Config / Localization / File）、`Bootstrap` 的静态登记表与
  `InstanceTracker` 的活跃计数——**只丢引用、不调 `Dispose`**（上一会话的实例可能握着已随域销毁的 Unity 对象），
  也**不动调用方持有的句柄**（`ConfigManager.ConfigChanged` 的外部订阅、Input 帧脉冲上的订阅）。
  **编译期抓不到**，但两个前提都成立时才可见差异：① 关闭域重载（Project Settings → Editor → Enter Play Mode
  Options → 取消 Reload Domain）；② 跨播放会话。正常单会话运行下这些复位是幂等空操作。
  **`AssetManager` 的「只报一次」闩锁随之改为「每会话一次」**（`_poolCapabilityWarned` /
  `_sceneCapabilityWarned` / `_mainThreadViolationLogged`）——跨会话仍只报一次，换了会话就该重新提示。
  **重复 `Initialize()` 的语义没有变**：会话内的重复调用仍是「告警 + 忽略」。

#### 破坏性变更迁移表（命名与签名）

| 旧写法 | 新写法 | 说明 |
| --- | --- | --- |
| `AssetManager.LoadSceneAsync(loc, additive: true, progress, ct)` | `AssetManager.LoadSceneAsync(loc, LoadSceneMode.Additive, progress, ct)` | **编译器逐处报错**。返回类型由 `Scene` 改为 `AssetSceneHandle`，`bool additive` 改为 `LoadSceneMode mode`；失败判据随之由 `scene.IsValid()` 改为 `handle.IsDefault` |
| `IAssetManager.LoadSceneAsync(...)` | `IAssetSceneLoader.LoadSceneAsync(...)` | **整条从主接口移出**，改由可选能力接口承载（同 `IAssetPoolController` 的形态）。第三方自定义 `IAssetManager` 实现**不必**再实现它；要提供场景能力的实现改挂 `IAssetSceneLoader`，门面用 `is` 探测 |
| `handle.Status == YooAsset.EOperationStatus.Succeed` | `handle.Status == AssetOperationStatus.Succeed` | **编译器逐处报错**（返回类型变更，且不再需要 `using YooAsset`）。四个句柄 `AssetHandle<T>` / `RawFileHandle` / `SubAssetsHandle` / `AssetDownloaderHandle` 同改，四个成员与上游**一一对应**（`None` / `Processing` / `Succeed` / `Failed`），语义与取值时机完全不变。理由：公开面不再绑在上游的类型上（YooAsset 3.x 曾把一批类型移进默认不编译的兼容层，透出上游枚举会让本包的公开面被动破坏） |
| `ICameraManager.ActiveCameraId` / `UnityCameraManager.ActiveCameraId` / `CinemachineCameraManager.ActiveCameraId` | `ICameraManager.ActiveHandle.NamedId`（或门面 `CameraManager.ActiveCameraId`） | 接口与实现上的 `ActiveCameraId` 移除——两者并行，`ActiveHandle` 是唯一活动查询；命名 id 由句柄携带（`NamedId`，动态为 `null`）。门面 `CameraManager.ActiveCameraId` 保留，直接读 `ActiveHandle.NamedId`（O(1)） |
| `new CameraHandle(id, generation)` | `new CameraHandle(id, generation, namedId)` | 构造签名扩展：`namedId` 有默认值 `null`，**两参调用源兼容**（基线把签名变化记为「已变更」）。命名槽位填命名 id，动态槽位传 `null` |
| `UpdateManager.ProcessImmediate(IUpdateable, float, float)` / `(IUpdateable, float, in UpdateClock)` | 同上，形参改为 `IUpdateLifecycle` | **形参放宽**：纯 `ILateUpdateable` / `IFixedUpdateable` 节点现在可达。传 `IUpdateable` 的既有调用源兼容（实参隐式上转），重新编译即可；已编译的第三方程序集需重编。跨时机语义见下一条行为变更 |

#### 行为变更（编译器抓不到，升级前请自己核对）

- **`AllPanelsClosedMessage` 只在真正全关时发布（R3-ADJ-13）**：此前 `CloseAllAsync` 无条件发布——
  控制器拦下关闭（面板仍开着）或本来就没有面板时也发，订阅者会误判 `OpenCount == 0`。现在只有
  「确实发起过关闭且结束时 `OpenCount == 0`」才发布一次。
- **注入的 Tip / HUD provider 会收到 `SetUIRoot`（R3-ADJ-12）**：此前只有门面自建的默认实现收到；
  初始化后经 `SetTipProvider` / `SetHudProvider` 注入的实现拿不到，与接口文档「Initialize 时自动调用」
  不一致。现在初始化后注入会补调一次；初始化前注入的仍在 `Initialize` 时补调。默认 provider 的调用次数不变。
- **`AssetPlayMode` 的整数值与 `0.3.0` 不同（R3-AST-001）**：当前为 `EditorSimulate = 0` /
  `Offline = 1` / `Host = 2`，而 `0.3.0` 是 `Offline = 0` / `Host = 1`。该 tag **从未分发**（2026-10-19
  用户确认），故不恢复旧值；若你从 `0.3.0` 的源码 / 场景升级，反序列化后请重新选择运行模式——
  `0` 现在解释为 `EditorSimulate`，在 Player 构建里会抛 `PlatformNotSupportedException`。
- **`UpdateManager.ProcessImmediate` 的跨时机语义写清（UPD-003）**：一次调用向三套调度器**全部转发**——
  同时实现多个时机接口的节点会在每个时机各派发一次（`OnUpdate` + `OnLateUpdate` 各一次），不是「一次更新」；
  固定步时机只重新定锚、不写时刻。此前 XML 只写「一次更新」，与实现不符。

- **`DataManager.ApplySnapshot` 的清空阶段逐块隔离**（DATA-007）：第三方 `IDataBlock.OnClear` 抛异常不再
  中止整次恢复，改为记一条 `[Data]` Error、计入返回的失败块数（Save 据此回滚 `Corrupt`），其余块继续清空。
- **场景加载失败路径不再泄漏底层句柄**：此前失败时 `SceneHandle` 从不 `Release`，该 provider 与它的
  bundle 引用会永久留在 YooAsset 里（成功路径本来会自愈，见 `### Fixed`）。
- **取消场景加载之后框架会善后**：YooAsset 的场景加载中止不了，取消只让 `await` 提前抛出。此后框架接力到
  后台——Additive 加载完成后**卸掉**（世界回到调用前），Single **保留并告警**（见 `### Fixed` 的取舍说明）。
  取消后的一小段时间里场景状态仍会变。
- **`AssetManager.UnloadSceneAsync` 是本模块唯一「未初始化也不抛」的成员**：它会被
  `AssetSceneHandle.Dispose` 调到，异常从 `Dispose` 里冒出来没人接得住。未初始化时告警并返回 `false`。
- **`Bootstrap.Shutdown` 现在会中止在途的 `RunAsync`**：取消经链接令牌传播，管线在下一个阶段边界停止，
  在途运行以 `OperationCanceledException` 收场（此前清理与在途运行互不感知，被清理过的阶段仍会被在途
  管线执行）。中止是**协作式**的：不观察令牌的阶段会先跑完自己的沉降点；`Shutdown` 返回后同帧再调
  `RunAsync` 仍会被重入守卫忽略。`GameLauncher.Start` 同时改传 `destroyCancellationToken`——启动途中
  销毁 `GameLauncher` 不再产错误日志（不响应取消的阶段除外，见 `### Fixed` → Bootstrap 的覆盖边界）。
- **`Bootstrap.RunAsync` 的 `OperationCanceledException` 携带本次运行的链接令牌**（不再是调用方令牌）：
  `Shutdown` 触发的中止里调用方令牌并未取消，携带它会让 `catch when (ex.CancellationToken == myToken)`
  这类过滤与语义都对不上。判据改用 `OperationCanceledException.CancellationToken.IsCancellationRequested`。
- **Asset / Data / Save 三个引导阶段的 `Shutdown` 只销毁自己初始化的门面**：此前它们无条件
  `AssetManager.Destroy()` / `DataManager.Shutdown()` / `SaveManager.Shutdown()`，使用方先手动
  `Initialize` / `SetInstance` 再登记默认组合时，`Shutdown` 会把使用方的实例一起拆掉。现在三者与
  Localization / Input / UI 一致：只在自己确实完成初始化时才清理，已初始化早退路径不再接管。
  回归用例见 `BootstrapStageTests` 六个所有权用例（三条改前必红：`Expected: True But was: False`）。
- **字段句柄（`SettingRef`）的重放登记改为会话级**：播放会话边界清空重放表，句柄在下次被
  **读 / 写 / `TryWriteValue` / `Subscribe`** 时自动重新入表。**订正（2026-10-17，SET-004）**：此前这里
  写的「会话 1 遗留的订阅者不会因会话 2 的重放复活」与实现相反——边界只清登记表、**不清句柄自身的订阅流**，
  句柄在会话 2 被使用即重新入表，旧订阅者仍会收到重放；要避免旧回调请在会话 2 重新 `Ref` + `Subscribe`，
  或在边界前释放旧订阅（彻底清流仍待裁）。`static readonly` 句柄跨会话照常重放。`Destroy` 语义不变：
  注册表仍不随 `Destroy` 清空。
- **`Save` 的删除不再在主线程做同步 IO**：`DeleteSlotAsync` / `MoveSlotAsync` / `DeleteAllSlotsAsync` /
  `DeletePlayerAsync` 的删除改经 `FileManager.DeleteAsync` 在池线程执行（此前是 1~4×N 次同步 `FileManager.Delete`
  压在主线程上，Console 等平台的 SDK 删除若是数百毫秒级会直接卡住）。随之而来的三条：**批量删除跨帧**
  （每个文件一次切回主线程＝一帧，忙碌门禁为真的时长随文件数增长）；**删除变为可取消**，且取消只保证
  「尚未开始的不会发生」——可能留下部分删除，由下次启动的恢复扫描收敛；进度回调仍在主线程、次数与计数不变。
  仍然留在调用线程上的只有 3 处**单次**探测（判据是「是否成批」而非「是否同步」）。
- **`FileManager.DeleteAsync` 会在池线程上调用第三方 `IFileProvider.Delete`**（编译器抓不到，**最需要自核的一条**）：
  Provider 的删除实现须容忍池线程调用（与其余异步原语同一条约束）。**逃生舱缺口如实说明**：其 `Delete` 必须
  留在特定线程的 Provider（典型如 WebGL 的 JS 桥）在 `DeleteAsync` 下不可用，同步 `Delete` 不受影响；
  真出现这样的 Provider 时会补一个可选能力接口（重开判据见 `Runtime/File/README.md` 的「已知限制」）。
- **删除槽位的顺序改为「先清备份」**：此前顺序是「载荷 → `.meta` → `.bak` → `.tmp`」，而恢复扫描的语义是
  「载荷缺失且有 `.bak` ⇒ 用备份原子写回」——于是「载荷已删、备份还在」的中断点（取消 / 崩溃）会让下次启动
  把该槽位**复活成上一代内容**。先清 `.bak` 之后，任何中断点只剩「原封不动」或「彻底删除」两种形态。
- **`UpdateManager` 的自动注销现在会补发一次 `OnDisable`**：派发回调抛异常时，调度器照旧记 Error 并**永久
  摘掉**该节点（只摘抛异常那一套时机），但**在该帧收尾时给它补发一次 `OnDisable`**——它同样是「离开派发
  集合」的边沿。补发之后，README 明写的「两者严格交替、且以 `OnEnable` 起头，不会出现没有配对的
  `OnDisable`」才真正成立（此前自动注销走的是「不宣告」那条分支，是唯一一处静默的配对断裂）。
  **升级需自核**：抛异常的节点会**多收一次 `OnDisable`**（清理请写成幂等的，README 早有此要求；仓内 6 个
  内置实现的 `OnDisable` 全为空，故框架自身行为不变）。**显式 `Unregister` / `Clear` 保持静默**——那两次
  由调用方自己发起，配对在「停止管理」处断裂是既有语义。想在 `OnDisable` 里自愈（重新 `Register` 自己）
  **必须带退避**，否则是每帧一条 Error 的活锁。查询面见 `### Added` 的 `UpdateManager.IsRegistered`。
- **`DataSnapshot` 现在携带生成时的脏集合，`ApplySnapshot` 应用时按它还原**：同进程内「留回滚点 →
  应用别的快照失败 → 应用回滚点」之后，加载前的未保存改动**仍然是「未保存」**——此前回滚只恢复数据、
  脏标记集合丢失，将来做增量保存会退化成「下次保存跳过本该保存的块」（数据丢失）。
  **对从磁盘读回的快照零影响**（那类快照不携带脏集合，应用后仍与今天一样是干净的）；
  脏集合是**进程内回滚元数据、不进存档**，故存档字节、校验和与存档格式逐字节不变。
  登记在 `Roadmap.md` §五 的 `CreateSnapshot(bool clearDirty)` 形状**被取代**（它不闭合：
  `ApplySnapshot` 没有第二个信息通道，旋钮拿不到「加载前脏了谁」），死因留在 `Data 模块的维护向记录`。
  回归锁：`DirtyTrackingTests` 三条（含一条字节级不变量用例）+ `SaveManagerImplTests.TryLoadAsync_BlockRestoreFails_RollsBackDirtyFlagsToo`。
- **非泛型 `RegisterTable(Type, IConfigTable)` 现在在注册期校验行类型**（CONF-008）：`table.Data` 为 null
  或其 `Dictionary<TKey,T>` 与 `rowType` 不一致时抛 `ConfigException`，不再等到查询期才在包装器构造处失败。
  同一 CLR 类型同时以 Table 与 Global 身份并发加载时两者**各自独立完成**（互不加入）；在途等待者直接拿到
  加载结果，先醒者重入 `Unload<T>()` 不再影响后续等待者（CONF-009/010）。
- **`ConfigTable<T>` 的 null 参数改抛 `ArgumentNullException`**（CONF-011）：构造函数与 `BuildIndex` 的
  `indexName` / `keySelector` 为 null 时，不再以 `NullReferenceException` 或 `Dictionary` 的裸 ANE 收场。
- **`LocalizationManager.Destroy` 不再向调用方抛第三方 `Dispose` 的异常**（LOC-005）：异常记一条
  `[LocalizationManager]` Error，门面状态仍复位——`Bootstrap.Shutdown` 不会被一个清理失败的实现中断。
- **语言加载的过期续体会被丢弃**（LOC-004）：`SwitchLanguageAsync` 在 `await` 期间若实例被 `SetInstance` /
  `Destroy` 换掉，本次切换不生效（不写新实例、不抛异常），只记一条 Warning。
- **Localization 的空表与空值口径统一**（LOC-006/009）：引导阶段遇空表记 Warning 并跳过（不再静默装上空表）；
  `FallbackLanguage` / `SwitchLanguageAsync` 的 null/空串与 `SetPlaceholder` 的 null 值改抛
  `ArgumentNullException`。异步加载器对空表仍是硬失败。
- **语言 JSON 的重复键现在会记 Warning**（LOC-007）：行为仍是后者胜（与主流解析器一致），但不再静默。
- **`GetFormat` 的对齐格式项补齐 .NET 空格语法**（LOC-003）：`{0, 5}` 等带空格写法现在正常格式化；
  `+` 号不再被当作合法符号（按非格式项字面输出，而不是交给 `string.Format` 抛异常）。
- **Input 的 ActionMap 失败路径不再改变状态**（INP-008）：`SwitchActionMap` 传 null/空名/不存在的 map
  只记 Warning，不再先 `DisableAllActionMaps()`；`DisableActionMap` 找不到 map 也会记 Warning。
- **调度器缺失时 `Initialize` 会告警**（INP-003）：帧驱动注册失败不再静默；`UpdateManager` 就绪后
  `Destroy()` + `Initialize()` 可恢复。
- **`playerId > 0` 的读取跟随 `effectivePath`**（INP-009）：重绑定 / 覆盖变更后读到新控件；直接
  `ApplyBindingOverride` 不经模块时缓存不会自动失效。
- **`Observe*` 的 null/空 `action` 现在立即抛 `ArgumentNullException`**（INP-011）：此前订阅成功、
  每帧在事件流里抛 `[Event]` ANE。
- **重绑定事件的订阅者异常被逐个隔离**（INP-013）：一个处理器抛异常不再阻止其余处理器，日志为
  `[Input] Rebinding On... subscriber threw exception`。
- **同名动作跨已启用 map 会有一次歧义 Warning**（INP-014）：解析仍按资产顺序，警告指出胜出的 map。
- **Serialize 的第三方格式不跨播放会话保留**（SER-006，仅关闭域重载时可见）：会话开始时注册表清空并
  重建内置格式，第三方需在新会话重新 `Register`。
- **`DataManager` 在会话开始时只丢实现引用**（DATA-003，仅关闭域重载时可见）：第二会话由引导阶段或
  使用方重新 `Initialize`；旧实例不 Dispose。
- **`SaveManager` 在会话开始时只丢实现引用**（SAVE-001，仅关闭域重载时可见）：第二会话的
  `SaveBootstrapStage` 会重新初始化并重接 `SaveOptions.CryptoProvider`；旧实例不 Dispose。
- **`AssetManager` 在会话开始时退订旧实例的低内存事件**（AST-008，仅关闭域重载时可见）：复位只丢引用、
  不 Dispose，但旧实例不再被 `Application.lowMemory` 钉住；第三方实现需自行退订自己的静态事件。
- **`InputManager` 在会话开始时退订旧 provider 的设备事件**（INP-002，仅关闭域重载时可见）：复位只丢
  引用、不 Dispose，但默认 provider 不再被 `InputSystem.onDeviceChange` 钉住；第三方 provider 需自行退订。

### Fixed

#### Pipeline

- **终局状态晚于广播、超时失败不带阶段名（PIPE-002/003）**：取消 / 失败分支先 `Broadcast()` 再写
  `_status`，订阅者在终局进度回调里读到 `Running`；超时分支没有写 `failStageName`，`FailureReason`
  退化为 `Failed: : Stage 'X' timed out…`。修法：`_status`（失败时连同 `FailureReason`）移到广播之前；
  超时分支按「阶段主动置失败」同一口径写阶段名。回归：`PipelineFrameTests` 两例（改前分别
  `Expected: Cancelled But was: Running` 与 `Expected: String starting with "Failed: 挂死: "`）。
- **`AddStage(null)` 静默忽略（PIPE-004）**：与仓内「参数防御用 `ArgumentNullException`」的约定不一致，
  装配错误被吞掉。修法：两个重载都抛 `ArgumentNullException`，`IPipeline` XML 补 `<exception>`。
  回归：`PipelineTests.AddStage_Null_Throws`（改前 `Expected: <ArgumentNullException> But was: null`）。

#### Diagnostics

- **注册表变空后报告与「复制」停在旧页签（DIAG-002）**：`DiagnosticsWindowModel.Tick` 在 `!HasPanels`
  时直接返回，`_report` 与 `Failed` 不清理，复制按钮仍输出上一个页签的陈旧内容。修法：变空时清空报告、
  复位失败标记，并在「本来有页签、这一次变空」时返回 `true` 请求一次重绘（空窗口的后续 Tick 仍返回
  `false`，保持 `Tick_NoPanels_ReturnsFalse` 的契约）。回归：
  `DiagnosticsWindowModelTests.Tick_PanelsDisappear_ClearsReportAndRequestsOneRepaint`（改前 `Expected: True But was: False`）。

#### Event

- **池化订阅节点归还时不清 `Next`（EVT-001）**：`SubscriptionNodePool.Return` 只清 `Handler`，池中的空闲节点
  仍指向它曾经链接的节点——若那是一个仍在订阅的节点，整个 Handler 闭包链被池钉住；关闭域重载时跨会话
  也不释放。修法：归还时一并 `node.Next = null`。回归：`EventStreamTests.SubscriptionNodePool_Return_BreaksNextChain`
  （改前 `Expected: null But was: <node>`）。
- **`DisposableBag.Clear` 一项释放失败中断整批（EVT-002）**：某个句柄的 `Dispose` 抛异常会打断循环、
  其余句柄永久丢失且不可观测。修法：逐项 `try/catch`，失败记 `[Event]` Error 后继续；`Dispose` 走同一条
  `Clear` 路径。回归：`DisposableBagTests.Clear_OneItemThrows_OthersStillDisposed_AndDoesNotThrow`（改前
  `Expected: No Exception to be thrown`）。README 的「行为契约」补第 15 条。

#### Pool

- **用户回调抛出时池账本不回滚（POOL-001）**：`Get` 的租出回调（`onRent` / `IPoolable.OnRent`）抛出时
  `CountActive` 已 +1 且实例不在栈里，计数永久虚高；`Return` 的归还回调抛出时 `CountActive` 不递减。
  修法：`Get` 在回调异常时回滚计数与 Editor 追踪集（实例不归还池，可能已被部分初始化）；`Return` 把递减
  放进 `finally`。回归：`PoolTests` 两例（改前 1≠0）。

#### Bootstrap

- **引导阶段的「只清理自己初始化的那份」在重跑 / 失败路径不成立（AST-004 / DATA-002 / SAVE-005）**：Asset 阶段
  初始化失败后所有权标记仍为 `true`，使用方随后手动注入的实例会被本阶段的 `Shutdown` 一并销毁；Data / Save
  阶段在同一实例上重跑时把标记覆写为 `false`，`Shutdown` 不再注销第一次由本阶段初始化的门面。修法：Asset
  在失败 `catch` 里复位标记；Data / Save 只在首次成功初始化时置位。回归：`BootstrapStageTests` 三例（改前红）。

#### Data

- **脏集合默认判等、同名索引误删、快照遍历/清空无隔离（DATA-004/005/007/008/010）**：`_dirtyBlocks` 改用
  引用同一比较器（重写 `Equals` 的不同块不再塌成一条）；`RemoveBlock` / `RegisterBlock` 摘名称索引时只摘
  属于自己的项，`BlockName` 在写入容器前校验（不留半注册态）；`CreateSnapshot` 先取块快照再遍历（`OnSave`
  里增删块不再抛 `InvalidOperationException`）；`ApplySnapshot` 的清空阶段逐块隔离并计入返回值，快照里的
  null 条目计数而不 NRE。回归：`DataRobustnessTests` 五例（改前分别 1≠2、索引被误删、NRE、OnClear 异常
  逃逸、枚举失效）。
- **写入端恒用 `Serializer.Default`，与读取端 `defaultFormat` 不对称（DATA-006）**：经 `Factory` 子类把
  快照 `defaultFormat` 设为非 `"json"` 时，整份存档读不回。修法：`BuildBlockSnapshot` 按快照的
  `defaultFormat` 取序列化器（`Serializer.Get`）；单块导出仍用 `Serializer.Default.Format`。
  回归：`DataRobustnessTests.CreateSnapshot_UsesSnapshotsDefaultFormat`（改前 `Expected: 1 But was: 0`）。
- **块版本过新被 Save 报成 `Corrupt`（DATA-011）**：Data 对版本过新的块只跳过并计入失败，Save 据此
  整份回滚并报 `Corrupt`。修法：应用快照前按 blockName 对照当前块版本，命中即返回 `VersionTooNew`
  （主文件与备份路径都覆盖）。回归：
  `SaveManagerImplTests.TryLoadAsync_BlockVersionTooNew_ReturnsVersionTooNew`（改前旧路径整份回滚并记
  `[Save] 有 1 个数据块未能恢复`）。
- **槽位清理漏侧车 `.bak`/`.tmp`、查询面只看载荷（SAVE-002/003）**：侧车也经原子写，其 `.meta.bak` /
  `.meta.tmp` 不在删除面内；仅备份可恢复的槽位被 `SlotExists` / `GetSlotMeta` / 列表隐藏。修法：
  删除路径补侧车配套文件（恢复扫描的孤儿清理同步）；查询面在载荷缺失时回退一代备份，列表也纳入
  仅备份可恢复的槽位。回归：`SaveManagerImplTests` 四条（改前分别 `Expected: False But was: True` /
  `Expected: True But was: False`）。
- **IO 失败形态不一致、恢复扫描无逐槽隔离（SAVE-004/012）**：文件被占用 / 权限错误时 `TryLoadAsync`
  直接抛（违背「预期失败不抛」）；恢复扫描一个槽位损坏会中止后续相位。修法：`LoadCoreAsync` 捕获
  非取消异常并返回 `Corrupt`；恢复扫描逐槽位 `try/catch`（取消仍传播）。**覆盖缺口如实记**：
  这两条的错误注入需要可抛 IO 的 File Provider 缝，本轮未构造自动化用例。
- **初始化半态与槽位前导零别名（SAVE-009/013）**：`Initialize` 在选项应用失败时把 `_impl` 留在半配置
  状态，重试被「已初始化」早退；`slot_01.save` 与 `slot_1.save` 被识别为同一槽位的两条元数据。
  修法：选项应用失败回滚 `_impl = null`；`TryParseSlot` 拒绝前导零别名。回归：
  `SaveManagerImplTests.LeadingZeroSlotAlias_IsNotListedAsSeparateSlot`（改前 `Expected: 1 But was: 2`）。
- **门面跨会话存活（DATA-003）**：关闭域重载时 `_impl` 带进第二会话，`DataBootstrapStage` 见到
  `IsInitialized` 为真即跳过初始化，旧实例与数据块被复用。修法：新增 `ResetForNewSession`
  （`SubsystemRegistration`）只丢 `_impl` 引用、不 Dispose。回归：
  `SessionResetTests.Data_ReinitializesAfterSessionReset`（改前 `Expected: False But was: True`）。
- **门面跨会话存活、第二会话加密接线丢失（SAVE-001）**：关闭域重载时 `_impl` 带进第二会话，
  `SaveBootstrapStage` 的 `Initialize` 因「已初始化」早退——它同时跳过了 `SaveOptions.CryptoProvider`
  的接线，而 File 的会话复位已清掉加密提供者，第二会话于是在**无加密**状态下读写存档。修法：新增
  `ResetForNewSession`（`SubsystemRegistration`）只丢 `_impl` 引用、不 Dispose，引导阶段重新初始化时
  重接加密。回归：`SessionResetTests.Save_ReinitializesAfterSessionResetAndRestoresCryptoWiring`
  （改前 `Expected: False But was: True`）。

#### UI

- **以基类 `T` 打开的面板在关闭 / 导航 / 查询路径失效（UIA-001）**：打开记账用 `typeof(T)`，而关闭 /
  导航 / 置顶按实例运行期类型 `GetType()` 反查——`T` 是基类时（随包示例正是 `OpenAsync<UIPanelBase>`）
  第二个面板会命中原面板并 Refocus、`CloseAsync(panel)` 静默不关、`Pop*` 静默失效。修法：打开记账改为
  面板的运行期类型；去重按「请求类型精确匹配 → 同一资源地址」两步；`IsOpen/GetPanel/CloseAsync<T>` 对
  具体类型精确匹配、对抽象类 / 接口回落到派生实例。示例写法保持原样。
  回归：`UIPanelBaseTypeOpenTests` 两例（基类打开的第二个面板可开/可关/可 Pop + 同资源仍聚焦），改前红。

#### Pool

- **池的静态状态跨会话存活且无会话复位（POOL-002）**：关闭域重载时第二会话继承上一会话的池实例、配置与
  生成器闭包；`Configure` 被静默忽略，`MaxSize` / `PrewarmSize` 失效，集合池的 `Configure` 可能永久被拒。
  修法：`PoolManager.ResetForNewSession`（`SubsystemRegistration`）清空 `_pools` / `_configs`（只丢引用
  不 Dispose），并让 `CollectionPoolManager` 注册/执行各集合池的内部池重建；已登记 `AutoInitTests` 族清单。
  回归：`SessionResetTests` 两例（业务池 + 集合池），改前红。

#### UI

- **`UpdateManager.Clear()` 后 README 的恢复路径不成立，分档面板永久静止（UIA-002）**：`Initialize` 因
  `_instanceInitialized` 早退，连每帧驱动器也不重注册；`SetInstance` 只恢复每帧驱动器，Tier1–7 面板因
  `_tierDemand` 边沿不重发而永久静止。修法：把 `EnsureFrameDriverRegistered` 提到早退之前，并让它遍历
  现存 `_tierDrivers` 一并重注册；README 的恢复路径同步。
  回归：`UIPanelTierTests.UpdateManagerClear_ThenInitialize_RestoresAllDrivers`，改前红。
- **弹窗尺寸单位错配：Canvas 参考单位当屏幕像素（UI-C-001）**：`ShowPopupAsync` 把 `rect.rect.size`
  传给 `UIPopupPosition.CalculatePosition`，而锚点与可用区域都是屏幕像素；挂 `CanvasScaler`
  （项目侧常规）时弹窗位置与钳制整体错位。修法：改传 `UIPopupPosition.GetScreenRect(rect).size`
  （与锚点同一条转换）；README 的手动示例同步。
  回归：`UIPopupTests.Popup_WithCanvasScaler_StillPlacesByScreenPixels`（2× CanvasScaler，改前红）。
- **HUD 每帧派发无逐实例异常隔离（UI-B-001）**：`UIHudManager.Update` 直接循环调 `hud.OnUpdate`，
  一个 HUD 抛异常会中断本轮遍历（外层 provider 隔离只保住整条 UI 通路并阻尼日志），症状是「其他 HUD
  静默停更」。修法：照面板侧 `DriveTier` 加逐 HUD try/catch + `UpdateFaulted` 停更标记（回池复位）。
  回归：`UIHudManagerTests.HudOnUpdateThrows_OtherHudsAreStillDriven`，改前红。

#### Input

- **长按时长只为「已被其它读取 API 访问过」的动作计时（INP-001）**：`UpdateButtonPressDurations` 只遍历
  `_actionCache`，而缓存只在 `WasPressedThisFrame` / `IsPressed` / `Read*` 或 `GetAction` 时写入——从未被
  这些 API 访问过的动作永远不计时，`GetButtonPressDuration` / `ObservePressDuration` 恒返回 0，
  无异常无日志；README 的蓄力条示例正是这种写法。修法：`GetButtonPressDuration` 开头 `GetAction(action)`
  预热缓存；`IInputProvider` XML 与 README 写明保证。
  回归：`InputSystemProviderTests.GetButtonPressDuration_NeverReadAction_IsTrackedAfterWarmUp`，改前红。
- **调度器缺失时帧驱动静默丢失、ActionMap 失败路径三态不对称、覆盖/重绑定后 playerId>0 读旧控件
  （INP-003/008/009）**：`RegisterTicker` 盲信 `UpdateManager.Register`（void），调度器为 null 时静默
  空转而 `IsInitialized` 仍为真、`Observe*` 全哑、零日志；`SwitchActionMap` 名写错时先禁用全部 map 再
  Warning，`DisableActionMap` 找不到 map 完全静默；`_playerBindingPaths` 不在覆盖/重绑定后失效，且取
  `binding.path` 而非 `effectivePath`。修法：`RegisterTicker` 用 `IsRegistered` 复核，失败 Warning 且不保留
  ticker；Map 三入口先校验后动作、失败一律 Warning 且不改变状态；路径缓存改用 `effectivePath`，并在
  `LoadBindingOverrides`（finally）/ `Reset*BindingOverrides` / 重绑定完成时失效。回归：5 条新用例（改前
  分别 Expected log did not appear（两条）、map 被禁用、Unity ANE、Expected: True But was: False）。
- **Observe* 空 action 延后到每帧炸、重绑定回调无异常隔离、同名动作零告警（INP-011/013/014）**：
  `Observe*(null, …)` 订阅成功后在每帧的 EventStream 抛 `[Event]` ANE；`SystemRebindingOperation` 的
  三个用户事件直接多播 `Invoke`，第一个抛异常的订阅者会中断其后全部订阅者；同名动作跨已启用 map
  按资产顺序静默胜出，README 却声称「只在启用 map 时给出一次警告」。修法：8 个 `Observe*` 在订阅前
  校验 `action` 非 null/空；重绑定事件按 `GetInvocationList` 逐个 try/catch 并记 `[Input]` Error；
  `GetAction` 命中后扫描其余已启用 map，按动作名 Warning 一次。回归：3 条新用例（改前分别
  `Expected: <ArgumentNullException> But was: null`、`InvalidOperationException: subscriber boom`、
  `Expected: 1 But was: 0`）。
- **会话复位后旧 provider 仍挂在 `InputSystem.onDeviceChange` 上（INP-002）**：`ResetForNewSession`
  只丢引用、不 Dispose，而默认 provider 在初始化时订阅了静态设备事件——关闭域重载后旧实例被钉住，
  第二会话的设备消息逐会话叠加。修法：`InputSystemProvider` 抽出幂等的 `Attach/DetachDeviceChanges`，
  门面经内部能力接口在丢引用前退订（只 `-=`、不 Dispose）。回归：
  `SessionResetTests.Input_ResetUnbindsOldProviderDeviceChanges`（改前 `Expected: False But was: True`）。

#### Audio

- **短于一拍的 one-shot 卡在 `Starting` 到 60 拍超时并误报（AUD-001）**：`PlayCoreAsync` 在 `Source.Play()`
  后只置 `Starting`，下一拍 `Sweep` 时短 clip 已播完、`isPlaying` 为 false——占槽约 2 秒（Tier1 × 60 拍）
  后被强制回收并打「起播超时」误告警。修法：`Play()` 后同步读一次 `isPlaying`（P-06 实测为真），为真
  直接转 `Playing`；超时告警措辞改为「连续 N 拍从未观察到 isPlaying 变真」。
  回归：`AudioPoolTests.ShortOneShot_IsReclaimedAfterItEnds`（0.01s clip，改前 0.3s 后仍占槽）。
- **`UpdateManager.Clear()` 后 ticker 不再重注册（AUD-002）**：`EnsureTickerRegistered` 只信本地
  `_tickerRegistered`；调度器被外部摘掉后该标志仍为 true，下一次 `Play` 不再注册，回收永久停摆。
  修法：以 `UpdateManager.IsRegistered(_ticker)` 为准（注销同理）。回归：
  `AudioPoolTests.Play_AfterUpdateManagerClear_ReregistersTicker`（改前 `Expected: 0 But was: 2`）。
- **外部销毁的 `AudioSource` 让 Sweep 抛 MissingReference（AUD-003）**：`Sweep` 直接读 `isPlaying`，
  一次异常中断整轮扫尾并留下永久占用的槽位。修法：状态机入口按 Unity 假 null 收尾回收；`Pause` /
  `Resume` / `PlayCoreAsync` 同样加假 null 防御。回归：
  `AudioPoolTests.ExternallyDestroyedSource_IsReclaimedWithoutThrowing`（改前 Unhandled log：
  `[UpdateScheduler] AudioTicker.OnUpdate threw exception`）。
- **无效租约仍持有释放动作却不 Dispose（AUD-004）**：location 指向非 AudioClip 时底层加载成功但
  `Asset` 为 null，租约 `IsValid=false` 却仍有 YooAsset 句柄的释放动作；`SafeDisposeLease` /
  `DisposeClipLease` 按 `IsValid` 早退，引用计数无界增长。修法：无条件 `Dispose`（`default` 是安全
  空操作）；租约契约改为「IsValid 只表示本体是否拿到」。回归：
  `AudioClipLifetimeTests.InvalidLeaseWithReleaseAction_IsDisposed`（改前 `Expected: 1 But was: 0`）。
- **删除无消费点的内部成员（AUD-012）**：`AudioSourcePool.MaxVoices` / `HasHost` 全仓零引用
  （`HasHost` 的注释称「测试用来断言」，实际测试用的是 `CountVoices` / `FindHosts`）。删除。
- **全局暂停期间在播的一次性音效被误回收（AUD-013）**：探针实测 `AudioListener.pause = true` 会让
  `AudioSource.isPlaying` 变 `false`——`Sweep` 的 `Playing` 分支据此把暂停中的一次性音效当成「播完」，
  句柄立即失效；`Starting` 分支还会在暂停 60 拍后强制回收。修法：两态都识别全局暂停（暂停期间不计
  超时拍、不回收），恢复后按正常判据收尾。回归：
  `AudioTickerTests.GlobalListenerPause_DoesNotRecycleOneShot`（改前 `Expected: True But was: False`）。

#### Scene

- **加载成功后的回收段受调用方令牌约束，取消丢句柄（SCN-001）**：加载成功 → 进入跨帧回收 → 令牌取消 →
  OCE 抛出、`return handle` 不可达；Additive 场景留在 Hierarchy 且外部无卸载凭据，Single 模式调用方误以为
  加载失败。修法：回收调用改用 `CancellationToken.None`，XML/README 写明「加载成功后的回收阶段不响应取消」。
  回归：`SceneLoadTests.LoadSceneAsync_CanceledDuringCleanup_StillReturnsHandle`（门闩式假件，改前红）。
- **场景加载登记表在外部卸载后不清理（AST-003/SCN-003）**：`_sceneOperations` 只在显式 `UnloadSceneAsync`
  里按句柄清；Single 模式切场景或外部卸载时 Unity 自己卸掉旧场景，登记项无人清理，会话内线性增长。
  真适配器下原「外部卸载后清登记项」分支也因 YooAsset 同步 Release 后 `SceneObject` 归 default 而不可达。
  修法：订阅 `SceneManager.sceneUnloaded` 按场景清表（YooAsset 已自行释放，管理器不二次 Release）；
  `Destroy` 退订，复用实例时重新挂上。回归：
  `SceneLoadOperationTests.ExternalSceneUnload_PrunesSceneOperationRegistration`（改前 `Expected: 0 But was: 1`）。
- **`ActivateAfterLoad` 丢弃 `SetActiveScene` 的返回值（SCN-004）**：激活失败不可观测。修法：失败时记
  一条 `[SceneManager]` Warning，说明新实例仍挂在原激活场景下；README 已知限制同步。
  **覆盖缺口如实记**：引擎拒绝激活的场景在自动化用例里难以稳定构造，本条只有文档与代码核对。
- **操作报成功但场景对象无效时先补发 1f（AST-007）**：违反 `IAssetSceneLoader`「失败路径不补发 1f」的
  契约，调用方进度条停在满格却拿到无效句柄。修法：把 `progress?.Invoke(1f)` 移到场景有效性检查与登记
  之后。回归：`SceneLoadOperationTests.SucceededButInvalidScene_DoesNotReportOneAndReleases`（改前
  `Expected: not collection containing 1.0f`）。

#### Asset（资源加载）

- **资产加载族取消不释放 YooAsset 句柄（AST-001）**：`LoadAsync` / `LoadSubAssetsAsync` / `LoadRawFileAsync`
  在取消时把已 `RefCount++` 的句柄丢掉，无人 `Release` → provider 与 bundle 引用永久驻留，
  `UnloadUnusedAssetsAsync` 再也回收不掉；场景族有取消善后、资产族没有。修法：三条路径共用
  `WaitOrReleaseAsync`，取消时 `Release()` 后重抛；为可测性抽出 internal 等待缝 `IAssetLoadWait`
  （生产适配 `HandleBaseWait`）。README 的取消契约订正为「取消时句柄被释放后重抛，拿到的是异常不是句柄」。
  回归：`AssetLoadWaitTests` 两例（反向注入 `Release` 后改前红）。
  **端到端缺口如实记**：真实 YooAsset 包上的「取消后 provider 不增长」仍无自动化用例（审计报告 §7 Asset 残留）。
- **会话复位时旧实例仍被 `Application.lowMemory` 钉住（AST-008）**：`ResetForNewSession` 只丢引用、
  不 Dispose，而实例在初始化时订阅了静态低内存事件——关闭域重载时整只旧实例跨会话存活，第二会话的
  低内存事件仍回调上一会话的对象。修法：`AssetManagerImpl` 抽出幂等的 `AttachLowMemoryHandler` /
  `DetachLowMemoryHandler`，复位在丢引用前退订（只 `-=`、不 Dispose）。回归：
  `SessionResetTests.Asset_ResetDetachesLowMemoryHandlerFromOldInstance`（改前 `Expected: False But was: True`）。

#### File

- **`ICryptoProvider.Decrypt` 返回 `null` 时两个读面结果不一致（FILE-001）**：同一解密失败在
  `ReadAllBytesAsync` 返回 `null`（=文件不存在）、`ReadAllTextAsync` 抛 `ArgumentNullException`
  （既非 IOException 也不在失败契约表里）；Save 据此把 `Corrupt` 报成 `Missing`。修法：装饰器把
  `null` 统一转成 `IOException("解密失败")`，两个读面一致；内置 XOR 永不返回 null，默认路径不变。
  回归：`FileManagerCryptoTests.DecryptFailure_ByteAndTextFaces_ThrowSameIOException`，改前红。
- **Dedicated Server 平台落默认分支、空域根静默兜底（FILE-009/011）**：`WindowsServer` / `LinuxServer`
  不在平台 switch 里，Dedicated Server 构建会抛「无内置 Provider」；`GetDomainRoot` 的空根兜底绕过
  `PrimeRoots` 的空串校验，返回空串会让相对路径落到进程工作目录。修法：补两个 Server 平台分支
  （走 Desktop Provider）；空根改为抛 `InvalidOperationException`。
  **覆盖缺口如实记**：平台分支需对应构建、空根需 Unity 路径未就绪，均未构造自动化用例。

#### Log

- **同会话重建实现截断同名 JSONL 且 `seq` 重计（LOG-001）**：文件基名只含会话起始时刻 + 会话 id，
  `Configure` / `Shutdown` 后重建的 sink 以 `FileMode.Create` 打开同一文件，启动段被截掉；新实现的
  序号从 1 重数，违背 README 的「会话内单调」。修法：基名改用 sink 创建时刻（含毫秒），同会话重建
  生成新文件、不截断旧文件；`seq` 提到 `LogSession`（会话级、`Interlocked` 分配，`Reset` 时归零）。
  回归：`LogFileSinkTests.ConfigureFileSink_SameSession_NewSinkDoesNotTruncateExistingLog`，改前红。
- **自定义 sink 在 `Write` 里调 `Debug.Log*` 会同步重入捕获 → StackOverflow（LOG-002）**：Unity 的日志回调
  在调用线程上同步触发，嵌套捕获再次分发到同一个 sink，无限递归且不可捕获；README 却写「sink 内再写日志
  是安全的」。修法：`LogManagerImpl` 加 `[ThreadStatic]` 分发深度，`CaptureUnityLog` 在分发期间直接丢弃
  嵌套的 Unity 日志（构造条目之前就返回，零分配）；经 `LogManager` 的嵌套投递保持既有行为
  （`ReentrantSink` 用例锁定）。README / `ILogSink` XML 区分两条重入路径。
  回归：`LogCaptureTests.CustomSink_WritingThroughDebugLog_DoesNotRecurse`，改前红。
- **捕获路径在档位过滤前解析分类，且没有「无输出端」早退（LOG-004）**：低于全局档的外部日志仍会为
  `[标签]` 解析付出一次 `Substring`；没有非控制台输出端时也会构造条目（最终被丢弃）。修法：
  `CaptureUnityLog` 先查非控制台输出端、再按全局档早退，最后才解析分类。**行为变更**：分类覆盖不能
  再让低于全局档的外部日志通过（刻意的性能取舍）。回归：
  `LogCaptureTests.GlobalMinimumLevel_EarlyOutsBeforeCategoryOverride`（改前会写入文件）。
- **会话复位残留（LOG-009）**：`_cachedFrame` 跨会话存活，第二会话首条池线程日志会带上一会话帧号；
  `_impl` 的读取未走屏障。修法：`LogManager.AutoInit` 调用 `LogManagerImpl.ResetCachedFrame()`；
  `CaptureTarget` / `IsInitialized` 改 `Volatile.Read`。分类名跨会话保留属刻意（分类句柄是静态的），
  已写进 README 已知限制。回归：`LogLifecycleTests.ResetCachedFrame_ClearsSessionStaleFrame`。
- **`LogOptions` 零校验（LOG-010）**：`MaxFileBytes=0` 每条日志切一个文件、`MaxRetainedFiles<=0` 删光
  旧文件、`FlushEveryEntries=0` 每条 flush、启用文件 sink 但 `FileDirectory=null` 首次写入即静默停用。
  修法：`LogManager.Configure` 入口做最小校验并抛 `ArgumentOutOfRangeException` / `ArgumentException`。
  回归：`LogLifecycleTests.Configure_RejectsFileOptionsThatWouldMisbehave`。
- **文件 sink 的 IO 失败后仍留在册、不计 `DroppedSinkCount`（LOG-003）**：`Die()` 只置 `_dead` 并关写入端，
  sink 占据 `_sinks` 直到显式移除，诊断页签照旧显示。修法：分发循环在 `Write` 返回后检查
  `LogFileSink.IsDead`，命中即摘除并计入现有 `DroppedSinkCount`（不新增健康回读 API）。回归：
  `LogLifecycleTests.FileSink_WriteFailure_IsDroppedAndCounted`（改前 `Expected: greater than 0 But was: 0`）。
- **`AddSink` 允许重复登记、`RemoveSink` 只摘首个并释放（LOG-006）**：重复登记会让同一 sink 写两次，
  移除后剩余登记写到已释放实例。修法：`AddSink` 按引用去重并告警（锁外告警防死锁）；`RemoveSink`
  移除全部匹配且只 `Dispose` 一次。回归：`LogLifecycleTests.AddSink_Duplicate_IsIgnoredOnce` /
  `RemoveSink_DisposesOnce`。

#### Settings

- **`Observe` 的立即回调抛出后订阅泄漏（SET-005）**：`Observe` 先注册再同步回调；回调抛异常时句柄
  永远交不出去，订阅留在 `_changedStream` 上直到管理器释放。修法：`try { callback } catch { handle.Dispose(); throw; }`
  （照 `SettingRef.Subscribe` 先例）。回归：`SettingsManagerImplTests.Observe_ImmediateCallbackThrows_SubscriptionIsCleanedUp`
  （改前 Apply 会再次回调，`Expected: 1 But was: 2`）。
- **`TryWriteValue` 在派发后重读 `Value`（SET-006）**：订阅者在写入派发中销毁管理器时，重读会抛异常
  并穿过 UI 事件。修法：`actual` 直接回传写入值（Settings 的 setter 不做规范化）。回归：
  `SettingRefTests.Ref_TryWriteValue_SubscriberDisposesManager_DoesNotThrow`（改前 `Expected: No Exception to be thrown`）。
- **`Migrator` / `Validator` 释放后仍可读写（SET-007）**：`Dispose` 的契约是「之后所有成员抛 ODE」，
  两个自动属性仍照常读写。修法：改为带 `ThrowIfDisposed` 的显式属性。回归：
  `SettingsManagerLifecycleTests.Dispose_ThenMigratorValidatorAccess_Throws`。
- **在飞 `SaveAsync` / `LoadAsync` 不感知 `Dispose`（SET-008）**：`await` 期间释放管理器，续体仍会提交
  保存计数、替换设置并广播。修法：`SwitchToMainThread` 之后补 `ThrowIfDisposed`。回归：
  `SettingsAsyncTests.SaveAsync_DisposeDuringAwait_ThrowsObjectDisposed` /
  `LoadAsync_DisposeDuringAwait_ThrowsObjectDisposed`（Save 改前 `Expected: True But was: False`）。
- **默认值工厂返回 null 无防御（SET-009）**：store 返回 null 有防御 + 告警，工厂没有。修法：
  `CreateDefault` 对工厂产出做 null 检查并回退 `new T()`。回归：
  `SettingsDefaultValueTests.Constructor_FactoryReturnsNull_FallsBackToNewT`。
- **默认路径占用在初始化成功前写入、键比较区分大小写（SET-010）**：初始化失败（工厂抛异常）会留下
  占用，另一个同名类型被误报冲突；文件系统不区分大小写而字典区分。修法：占用移到初始化成功之后，
  字典改用 `StringComparer.OrdinalIgnoreCase`。回归：
  `SettingsManagerLifecycleTests.DefaultPathClaim_WrittenOnlyAfterSuccessfulInitialization`
  （改前改判为 `Expected: No Exception to be thrown`）。
- **同步降级 `SaveAsync` 把活对象交给池线程序列化（SET-013）**：`JsonUtility.ToJson` 在池线程读
  主线程可能仍在修改的对象。修法：在调用线程先 `ToJson` → `FromJson` 得到独立快照再下池线程
  （版本化路径同样快照 `Data`）。回归：
  `SettingsAsyncTests.SaveAsync_SyncOnlyStore_SerializesSnapshotBeforePool`（改前 `Expected: False But was: True`）。
  `SaveAsync` 与 `Reset`/`Load` 的并发无序化（SET-014）按裁定只文档化，写进 README 已知限制。
- **Settings 写入口无主线程守卫（SET-018）**：`SettingRef` 的重放表与门面的 `Managers` 都是静态无锁
  结构，并发写会静默损坏。修法：补 Editor-only 首错断言（`SettingRef.EnsureRegistered`、`SettingsManager`
  的 `Initialize` / `Destroy` / `Ref`；`ResetForNewSession` 复位闩锁），README 线程契约同步。回归：
  `SettingsManagerLifecycleTests.Initialize_FromBackgroundThread_LogsMainThreadViolationOnce`（改前
  Expected log did not appear）。
- **`Ref<T,TField>` 字段类型不匹配抛 BCL 裸异常（SET-015）**：字段是 `int` 而 `TField` 写成 `float` 这类
  「看似可隐式转换」的调用能通过编译，但表达式树赋值在运行期抛
  `Expression of type 'System.Single' cannot be used for assignment to type 'System.Int32'`——无模块前缀、
  不含字段名与修复方向。修法：包装为 `[SettingsManager]` 前缀的 `ArgumentException`，消息指出字段路径
  与 `TField`；不改变签名。同轮编译探针确认 `Initialize<T>(null)` 在 `(string, Func<T>, SettingsOptions)`
  与 `(Func<T>, SettingsOptions)` 之间 CS0121，XML/README 写明用命名实参或显式强转；CONF-016 的
  `PreloadTableAsync<T>(path, default)` 二义同样只补文档；SAVE-015 的 `GetSlotMetasAsync(default)`
  **未复现**二义（正确绑定到 `CancellationToken` 重载）。回归：
  `SettingsManagerFacadeTests.Ref_FieldTypeMismatch_ThrowsWithRepairHint`（改前 `Expected: String
  starting with "[SettingsManager]" But was: "Expression of type 'System.Single'…"`）。

#### Update

- **`AutoDriveEnabled` 跨会话存活且不复位（UPD-008）**：关闭域重载时第二会话可能继承上一会话的
  `false`，整套自动派发静默停摆而 `IsDrivingPlayerLoop` / `IsInitialized` 仍报 true。修法：补
  `UpdateManager.ResetForNewSession`（`SubsystemRegistration`）复位为 `true`；AutoInit 本身不覆盖它
  （测试会显式关掉再调用 AutoInit）。已登记 `AutoInitTests` 族清单。回归：
  `SessionResetTests.Update_ResetForNewSession_RestoresAutoDrive`。

#### Camera

- **被外部销毁的活动相机仍被视为活动（CAM-001）**：Unity 的假 null 让 `OutputCamera` 返回一个
  「非 null 但不可用」的引用，`ActiveHandle` / `IsActive` 继续报活，README 的「返回 null」不成立。
  修法：查询面统一经 `IsUsable`（`Used && Camera != null`）判定；被销毁的活动相机按 null / default /
  false 报告。回归：`UnityCameraManagerTests.DestroyedActiveCamera_IsReportedAsInactive`（改前
  `Expected: null But was: <null>`）。
- **`CameraManager.Destroy` 先 Dispose 后置空（CAM-002）**：第三方 `Dispose` 抛出时 `_impl` 仍非空，
  之后 `Initialize` 永久早退。修法：先清槽再 Dispose（Audio 先例）。回归：
  `CameraManagerTests.Destroy_DisposeThrows_StillClearsInstance`（改前 `Expected: False But was: True`）。
- **过渡时长无有限性校验（CAM-003）**：NaN 让过渡永不结束且逐帧写坏位姿，+∞ 永久挂起；文档写
  `duration <= 0` 而实现是 `<= 0.0001`。修法：入口拒绝 NaN / ±∞（`ArgumentOutOfRangeException`）；
  `ICameraManager` 两处与 README 两处统一为 `<= 0.0001`。回归：
  `UnityCameraManagerTests.TransitionTo_NonFiniteDuration_Throws`（改前 `Expected: <ArgumentOutOfRangeException> But was: null`）。

#### UI

- **`ReleaseCurrentInstance` 在 Dispose 抛出时不复位初始化标记（UIA-003）**：只置空实例，`Initialize` 因
  `_instanceInitialized` 仍为 true 永久早退。修法：`finally` 里一并复位。回归：
  `UIPanelSelfCloseTests.SetInstance_OldOwnedDisposeThrows_InitializeRecovers`（改前 `Expected: True But was: False`）。
- **语言切换刷新无逐面板隔离（UIA-005）**：一个面板的 `OnLanguageChanged` 抛异常会中断其后所有面板。
  修法：逐面板 `try/catch`，失败记 `[UIManager]` Error 后继续。回归：
  `UIPanelSelfCloseTests.LanguageChanged_OnePanelThrows_OthersStillRefresh`（改前 Unhandled log 为
  `[Event] EventStream handler threw exception`）。
- **批量关闭无逐面板隔离（UIA-007）**：`CloseLayerAsync` / `CloseAllAsync` 一个面板关闭异常即中断整批，
  HUD/Tip 已回收、`AllPanelsClosedMessage` 不发布。修法：逐面板 `try/catch`（取消仍向上传播），
  失败记 Error 后继续；收尾消息与控制器回调照常执行。回归：
  `UIPanelSelfCloseTests.CloseAllAsync_OnePanelThrows_OthersStillCloseAndMessagePublished`（改前
  Expected log did not appear）。
- **View/HUD/Binding 生命周期复位（UI-B-004/005/006/008/011）**：`UIViewBase` 补 `OnDestroy` 兜底释放
  `Track` 订阅（直接销毁路径）、`DisposeTracked` / `UIPanelBinding.Unbind` 逐项隔离；回池复位
  `_state` / `IsFocused` / `IsPaused` / `Raycaster`；HUD 映射摘除已销毁条目；`EnsureContainer` 失败时
  `AttachAsync` 早退。回归：
  `UIPanelSelfCloseTests.Destroy_RecycledPanel_ReportsNotOpenOrFocused`（改前 `Expected: False But was: True`）/
  `DirectDestroy_ReleasesTrackedSubscriptions_AndIsolatesThrowing`。
- **遮罩与同层第 31 个面板同序、公开排序入口不钳 layer（UI-C-002/003）**：`MaskIndex == MaxIndexInLayer == 31`
  而面板序号从 1 起，第 31 个面板与遮罩同序；公开 `PanelOrder` / `MaskOrder` 对 layer 不设防。
  修法：面板层内上限改为 30（`LayerStride - 2`），遮罩独占 31；两个公开入口补 `ClampPanelLayer`。
  回归：`UISortingTests.PanelIndexAtMaskBoundary_IsClampedBehindMask`（改前 `Expected: less than 16031
  But was: 16031`）/ `PublicOrderEntries_ClampOutOfRangeLayers`（改前 `Expected: 28769 But was: 28801`）。
- **弹窗校验、遮罩显示判定与捕获层栈（UIA-006/UI-C-005/006）**：`ShowDialogAsync` / `ShowPopupAsync`
  在 `OpenAsync` 之后校验面板仍打开，避免为自关面板挂孤儿遮罩/捕获层；`SetMaskClickToClose` 同时
  检查遮罩是否显示中；捕获层由单目标改为栈式（`List<UIPanelBase>`），关内层后回落到外层。
  回归：`UIMaskTests.SetMaskClickToClose_AfterHideMask_Warns`（改前 Expected log did not appear）/
  `UIPopupTests.NestedPopups_CatcherClick_ClosesInnerThenOuter`（改前第二次点击外层仍开着）。
- **控制器丢弃与回收截断（UI-C-008/010）**：`UIBootstrapStage` 已初始化早退时静默丢弃构造传入的
  controller → 早退路径应用它；`UIManagerImpl.Dispose` 的面板回池循环逐面板隔离（AssetManager 已销毁
  时一个面板抛异常不再截断其余清理）。回归：
  `UIBootstrapStageTests.ExecuteAsync_AlreadyInitialized_AppliesController`（改前 `Expected: True But was: False`）。
- **重复 `ShowDialogAsync` 复用上一轮结果通道（UI-B-013）**：面板仍开着时再次调用不会重置结果，
  上一轮已 `SetResult` 的等待方会立即返回旧值。修法：重复打开前对现有面板调 `BeginDialogCycle`
  （并以默认值释放上一轮遗留的挂起等待者）；回归：
  `UIDialogPanelTests.RepeatedShowDialog_ResetsResultChannel`（改前 `Expected: Pending But was: Succeeded`）。

#### Message

- **`IDestroyCancellationToken` 的 XML 示例缺 `IMessageSubscriber`，照抄 CS1929（R3-MSG-003）**：R2 修掉了
  「销毁后读 `_cts.Token` 抛 ObjectDisposedException」半边，但示例类仍只实现 `IDestroyCancellationToken`，
  而 `Subscribe<T>` 扩展方法的接收者是 `IMessageSubscriber`——照抄示例得到 CS1929，随包 XML 仍不可编译。
  修法：类声明补 `IMessageSubscriber`，并注明 `Subscribe<T>` 是 `IMessageSubscriber` 的扩展方法。
  验证：临时 `netstandard2.1` 编译探针（原样 CS1929，补接口后 0 error）。
- **首条 `Publish` 时同步订阅清零导致通道在缓冲写入前被回收（MSG-001）**：最后一个同步订阅者在回调里
  自退订 → `IsReclaimable` 判真（此时缓冲流尚未创建）→ 通道被摘除，随后的缓冲写入落在孤儿通道上，
  该条消息永久失去可重放性。修法：`MessageChannel` 加投递深度闩锁，`IsReclaimable` 在投递期间恒为
  false；四条发布路径（类型 / 键值 × 同步 / 异步）共用 `EmitSyncAndBuffer`，整段禁止回收。
  回归：`MessageEvictionTests` 两条（类型与键值路径），改前红。
- **销毁令牌的取消登记不随订阅句柄回收（MSG-004）**：`TryBindToDestroy` 丢弃 `token.Register` 返回的
  `CancellationTokenRegistration`；长寿命 `IDestroyCancellationToken` 所有者反复订阅/退订会持续累积登记，
  直到令牌取消。修法：返回合并句柄 `DestroyBoundDisposable`（释放时先撤销登记再释放订阅）；12 个订阅扩展
  方法改为返回合并句柄，Input / Localization / UI 的共用入口同步适配。回归：
  `DestroyTokenBindingTests.DisposeSubscription_AlsoDisposesCancellationRegistration`（改前句柄释放后取消令牌
  会二次释放订阅，`Expected: 1 But was: 2`）。
- **同步订阅的过滤器异常落到 `[Event]` 文案（MSG-007）**：过滤与回调组合的闭包抛异常后由事件流兜底，
  日志是 `[Event] EventStream handler threw exception`，与处理器异常同文案、不在 `[Message]` 检索面。
  修法：四条同步过滤路径共用 `InvokeFiltered`，过滤异常记 `[Message] Sync subscription filter threw exception`
  并跳过本条；异常隔离语义不变。回归：`MessageBusTests` 三条既有用例改断言新文案（改前 Unhandled log
  为 `[Event] …`）。
- **删除无调用点的 internal `MessageManager.SetInstance`（MSG-009）**：XML 宣传「注入 mock broker」，
  但 `MessageBroker` 是 sealed 具体类，且全仓零调用点。删除后不再维护一个不可兑现的入口。

#### Timer

- **TMR-002 的补扫只在表内，跨表新建仍被拖到粗档（R3-TMR-001）**：状态表（带 `TState`）的重复定时器
  回调里创建无状态定时器，后者落在默认表；默认表在本趟已先被扫过，扫尾的 `firedAny` 补扫只扫本表槽位，
  本趟 `min` 漏掉新定时器——档位停在 Tier5，0.1 秒定时器迟到约 0.33 秒（实测；审计估算最坏约 2.13 秒）。
  修法：`ITimerTable.Sweep` 返回「本趟是否触发过回调」，全部表扫完后由 `TimerCore` 统一调用
  `GetBornThisPassMin` 汇总所有表的本趟新生槽位。回归：
  `TimerTierTests.StateCallback_CreatingStatelessTimer_IsCoveredAcrossTables`（改前 0.333s，改后约 0.1s）。
- **回调里新建的定时器可能晚一个档周期触发（TMR-002）**：一次性定时器在回调前释放槽位，回调里新建的
  定时器经自由链复用同一个下标，而 `Sweep` 的循环已越过它——本趟 `min` 漏算，`SelectTier` 保持粗档，
  同轴新建的 0.1 秒定时器可晚约 2.13 秒触发。修法：扫尾在「本趟触发过回调」时补扫一次
  `BornPass == 当前趟` 的槽位、重新折入最近截止（扫尾期间新建定时器只可能发生在回调里，故不增加常态开销）。
  回归：`TimerTierTests.TimerCreatedInsideOneShotCallback_FiresWithinTheNextTierStep`，改前红。
- **泄漏告警只看正在计时数，漏掉「Stop 后忘记 Dispose」（TMR-005）**：`WarnIfLeaking` 用 `_runningCount`
  判定，而主泄漏形态下 Stop 后 RunningCount 已回到 0，告警永远不响。修法：改用 `_allocatedCount`，
  消息里分列「已分配 / 正在计时」。回归：`TimerLifecycleTests.LeakWarning_FiresOnAllocatedSlots_EvenWhenStopped`
  （改前 Expected log did not appear）。
- **`AxisCount` 是跨模块常量的第二份副本（TMR-009）**：手写 `2` 与 `UpdateTimeMode` 脱钩。修法：改为
  `Enum.GetValues(typeof(UpdateTimeMode)).Length`；回归 `TimerLifecycleTests.AxisCount_MatchesUpdateTimeModeEnum`
  锁住派生关系。
- **新表的扫尾趟号从 0 起算，回调里用全新 `TState` 建的 `After(0f)` 同拍触发（TMR-010）**：新表在
  同一趟被扫到时趟号 +1，新生槽位被当成旧槽位。修法：核心维护各轴当前趟号；新表继承该趟号、本轮
  新表不再自增趟号，分配时把轴趟号写入 `BornPass`。回归：
  `TimerTierTests.StatefulTimer_NewTableCreatedInsideSweep_DoesNotFireInSameBeat`（改前 `Expected: False But was: True`）。
- **写入口无主线程守卫（TMR-006）**：`After/Every` / `Stop` / `Restart` / `Release` / `CancelAll` / `Sweep`
  直接改写无锁槽位表，池线程调用静默与主线程派发竞争。修法：`TimerCore` 补 Editor-only 首错断言
  （沿用 Message/Pipeline/Asset 的内联形态，不抽公共防线；Release 零开销），README 线程契约同步。
  回归：`TimerLifecycleTests.WriteEntry_FromBackgroundThread_LogsMainThreadViolationOnce`（改前
  Expected log did not appear）。

#### Lock

- **解锁回调里重加同类型锁被随后的清理静默丢弃（LOCK-001）**：`RemoveLock` 原先**先派发、后摘除**——
  最后一把锁移除后，`OnGlobalUnlocked` / `OnUnlocked` 回调里对同一 (主体, 类型) `AddLock` 会取到尚未
  摘除的空集合、加锁成功并播报 `OnGlobalLocked`，返回后整个集合被删：新锁无解锁事件地消失，
  `IsHeld` / `IsLocked` 为 false。改为照 `RemoveAllLocks` 的先摘除、后派发；
  `TryUnbindDestroyBinding` 留在派发后按现有条件执行（它自检锁与订阅，不会误摘）。
  回归：`LockDispatchRobustnessTests` 两条（同形状的红证 + `RemoveAllLocks` 负向控制），改前红。

#### Config

- **R2 的示例修复只修了一半：裸字段不实现 `IConfigRow<TKey>.Id`，照抄 CS0535（R3-CONF-001，P0）**：R2 把
  Json / ScriptableObject 示例从自动属性改成裸字段（修掉了运行期主键恒 0），但 `IConfigRow<TKey>` 的成员是
  **属性** `TKey Id { get; }`，C# 字段不能实现接口属性——三处示例照抄即编译失败，比原缺陷更早暴露。
  修法：三处示例统一为「字段承载序列化 + 显式接口实现 `int IConfigRow<int>.Id => Id;`」；README 的
  「已知限制」把这条写成成文边界。验证：临时 `netstandard2.1` 编译探针（原样 CS0535，补显式实现后
  0 error）；`JsonRowSerializationTests` / `ExplicitInterfaceRowTests` 已用同形钉住。
- **SO Table 资产形状零文档、普通 SO 得到误归因（R3-CONF-002）**：`ScriptableObjectLoader` 只接受
  `ScriptableObjectTable<TRow,TKey>` 的资产，但 README 只说「包含 `T[]` / `List<T>` 的包装 SO」，
  公开基类零提及；按文档建普通 SO 会在加载时得到「Ensure AssetManager is initialized…」的误导消息。
  修法：README 补最小子类示例与资产创建步骤；loader 先按 `ScriptableObject` 加载再自检形状，
  普通 SO 得到「must inherit ScriptableObjectTable<…>」；新增 `ScriptableObjectTableTests` 覆盖
  正确/错误形状、空列表与重复主键。
- **README 行类型示例用自动属性，Json / ScriptableObject 路径主键恒 0（CONF-001，P0）**：三处示例把主键写成
  `public int Id { get; set; }`。Json 路径经 `JsonUtility`、SO 路径经 Unity 序列化，二者都**不序列化属性**——
  多行 JSON 撞 `Duplicate Id '0'` 加载失败，单行则静默产出键 0 的表（`.Get(1001)` 报 not found），
  SO 路径的 Inspector 里也无处填写；CSV 走反射写属性，恰好不受影响，进一步掩盖了问题。
  示例改为字段，README 的「已知限制」补一条成文边界；`Tests/Editor/Config/JsonRowSerializationTests`
  用属性版 / 字段版两条断言钉住该事实（属性版读回 0、字段版读回 1001）。
- **CSV Global 只有表头、预载返回 null、注入条目残留路径（CONF-005/006/007）**：只有表头行时把表头当
  数据行（全 string 字段静默得到「字段值 = 列名」）；自定义 Loader 返回 null 仍记为已加载；`RegisterTable` /
  `RegisterGlobal` 不清理 `_assetPaths`，`CopyLoaded` 报旧路径、下一次 Preload 打假 Warning。修法：
  CSV Global 要求 `lines.Length >= 2`；预载 null 抛 `ConfigException`；三处 Register 清掉路径记录。
  回归：`ConfigManagerImplTests.PreloadGlobalAsync_LoaderReturnsNull_ThrowsAndDoesNotMarkLoaded`
  （改前 `Expected: <ConfigException> But was: null`）。
- **非泛型注册不校验行类型、跨身份在途任务互相加入、等待者广播期间重入卸载（CONF-008/009/010）**：
  `RegisterTable(Type, IConfigTable)` 把 `rowType` 直接当键、不校验 `table.Data` 的行类型，
  `IsLoaded`/`CopyLoaded` 报错类型、查询期才在 `ConfigTable<T>` 构造处炸；`_inFlightLoads` 按 Type 单键且
  不记录身份，Table 侧会加入 Global 的在途任务（反之 Global 直接 return、漏掉注册）；等待者广播同步执行
  续体，先醒者重入 `Unload<T>()` 后后续等待者重读 `_tables[type]` 抛 `KeyNotFoundException`。修法：注册期
  用与 `ConfigTable<T>` 相同的字典校验（抽 `ConfigDictionaryValidator`）；`InFlightLoad` 记录
  `Kind`/`Result`，只加入同身份任务，等待者直接返回携带的结果；`CompleteInFlight` 先移除登记再无条件广播。
  回归：`ConfigManagerImplTests` 五条（改前分别 `Expected: <ConfigException> But was: null`、
  `Expected: 1 But was: 0`、`KeyNotFoundException`）。
- **`ConfigTable` 的 null 参数抛裸 NRE、`Data` 可变暴露的契约未成文（CONF-011）**：
  `new ConfigTable<T>(null)` 在 `dict.GetType()` 处抛 NRE，`BuildIndex(name, null)` 在首次遍历时抛 NRE，
  `IConfigTable.Data` 暴露可变字典但文档只说「暴露」——修改它会让 `Count` / `GetAll` / `BuildIndex`
  （构造期快照）与 `Get` / `TryGet` / `Contains`（实时字典）脱钩。修法：null 字典 / null selector /
  null 索引名改抛 `ArgumentNullException`；XML 与 README 写明 `Data` 的只读契约。回归：
  `ConfigTableTests` 三条（改前分别 `NullReferenceException` 与 `Expected: "indexName" But was: "key"`）。

#### Localization

- **`Destroy` 无 try/finally、在途语言加载经静态门面写回（LOC-004/005）**：第三方 `Dispose` 抛异常后
  `_instanceInitialized` 仍为 true——`Initialize` 永久早退、调用打到半销毁实例，并中断 `Bootstrap.Shutdown`
  的逆序清理；`SwitchLanguageAsync` 的续体在 `await` 后经静态门面写入，加载期间 `SetInstance` 会把旧配置
  注入新实例、`Destroy` 会让续体抛「尚未初始化」。修法：`Destroy` catch + `LogManager.Exception` + finally
  复位；门面加实例代次（Initialize / SetInstance 引用变化 / Destroy / 会话复位），loader 捕获发起时实例 +
  代次，`await` 后先查代次再解析，过期则 Warning + 丢弃。回归：`LanguageAssetLoaderTests` 两条 +
  `LocalizationManagerTests` 一条（改前分别 `Expected: False But was: True`、`InvalidOperationException:
  尚未初始化`、`Dispose boom`）。
- **空表两条入口相反、`{0, 5}` 被判非法、重复键静默、空值参数三种处置（LOC-003/006/007/009）**：
  引导阶段静默装上空表（`IsInitialized == true`、零警告）而异步加载器硬失败；解析器把合法的带空白对齐
  格式项 `{0, 5}` 判为非法并转义为字面量（同时接受了 .NET 不接受的 `+`）；JSON 重复键静默后者胜；
  空值参数三种处置（`FallbackLanguage = null` 下次 `Get` 抛裸 ANE、`SwitchLanguageAsync("")` 去加载
  `"lang_"`、`SetPlaceholder(name, null)` 静默替换为空串）。修法：空表在引导阶段 Warning + 跳过
  （不置已初始化），异步加载器维持硬失败；解析器按 .NET 实际语法放行索引后 / 逗号后 / 对齐值后的
  ASCII 空格、只接受负号；重复键记 `[LocalizationManager]` Warning 后后者胜；`FallbackLanguage` /
  `SwitchLanguageAsync` / `SetPlaceholder` 在门面与 impl 两侧补校验。回归：7 条新用例（改前分别
  `Expected log did not appear`、空表置已初始化、`Expected: <ArgumentNullException> But was: null`、
  `[{0, 5}]` 字面输出、`ParamName "targetLanguage"`）。

#### Serialize

- **序列化注册表跨会话存活（SER-006）**：关闭域重载时上一会话的静态注册表会带进第二会话，第三方格式
  继续可用、内置同名覆盖也保留；且 `Reset` 到 `AutoInit` 之间存在 `Default` 抛异常窗口。修法：新增
  `ResetForNewSession`（`SubsystemRegistration`）：清空注册表并**立即重注册两个内置格式**；第三方格式
  需在新会话自行重新注册。回归：`SessionResetTests.Serialize_ResetClearsCustomFormatsAndRestoresBuiltins`
  （改前 `Expected: False But was: True`）。

#### Asset（场景加载）

- **失败路径的 `SceneHandle` 从不 `Release`**。失败时没有场景，YooAsset 挂在 `sceneUnloaded` 上的兜底
  永不触发，该 provider 与其 bundle 引用永久留在 `ResourceManager.SceneHandles` 里，此后
  `UnloadUnusedAssetsAsync` 再也回收不掉它。**成功路径本来会自愈**（`ResourceManager.cs:55` 订阅、
  `:376-394` 按 `SceneObject` 匹配后 `Release()`）——这条只在失败路径成立，别读成「加载一次泄漏一次」。
- **取消场景加载不中止，且此后无人收拾**。`UniTask.Yield(ct)` 抛 OCE 后底层照跑完、场景仍会进来
  （YooAsset 没有对外可用的 Abort）。现在接力到后台收干净：**Additive 完成后卸掉**（世界回到调用前）、
  **Single 什么都不做只告警**。计划原文写的是 Single「只 `Release`」，实施时改正——释放会让引用计数归零，
  此后一次 `UnloadUnusedAssetsAsync` 就能回收掉这个**仍在场**的场景所引用的 bundle。
- **按名字回查场景**。改用操作自己的 `SceneObject` 并校验 `IsValid() && isLoaded`（无效则记错误 + 释放句柄）：
  `SceneManager.GetSceneByName` 在「同名场景已存在」或「场景尚未进入查询表」时会给错（或给不出）。

**这一批的射程边界（如实记）**：Runtime 测试程序集不引用 YooAsset，测试环境也没有可运行的资源包——
真路径在用例里跑不起来。修法先在 `YooAssetManagerImpl` 与 `YooAsset.SceneHandle` 之间抽了一条 internal 缝
（`ISceneLoadSource` / `ISceneLoadOperation`）：**缝之上**（轮询、进度、失败释放、取消善后、取场景对象、
句柄登记与卸载）有「改前必红」的用例覆盖，**缝之下**约 40 行机械转发只有读源码核对与人工验收。
红证实测（临时退回旧行为）：`SceneLoadOperationTests` 10 例中 8 例红——失败释放两例 `Expected: 1 But was: 0`、
取消善后两例（未卸载 / 期望的告警没出现）、按名回查四例；两条与本修复无关的用例保持绿。

#### 会话级复位（跨 Input / UI / Asset / Config / Localization / File / Bootstrap / InstanceTracker / Settings）

- **关闭域重载时，第二个播放会话里静态门面静默失效**。缺陷有两道锁：门面 `Initialize()` 因 `_initialized`
  仍为真而**告警后早退**，以及 `_ticker` 之类的对象字段非空拦掉重建——`Destroy()` 能复位一切，但正常游戏流程
  不会调它（「谁初始化谁清理」只在退出路径成立，而使用方在启动时调一次 `Initialize` 之后就不再碰了）。
  逐落点的处置：Input（两道锁一起解）、UI / Config / Localization / File（实例 + 门锁）、
  **Asset 另作废在途初始化**（上一会话半路中断的初始化不得在第二会话里让实例「复活」——那正是代际号存在的理由）、
  **`Bootstrap` 清静态登记表**（否则第二会话的 `ConfigureStages` 只能追加新实例，旧阶段实例被一起装配：
  同相位并行双跑、退出时双份 `Shutdown`）、**`InstanceTracker` 清活跃计数**（其退出订阅从「静态构造函数 +
  匿名 lambda」改成幂等 `AutoInit`——那处 lambda 退订不了，按特性扫描的族清单也扫不到它）。
  每个落点都带**改前必红**的回归用例（`Tests/Runtime/Architecture/SessionResetTests` 10 例 +
  `Tests/Runtime/Input/InputSessionResetTests` 3 例）。
  **验证手段**：`Tools/run-tests.ps1` **验不了这条**（每次是全新 batchmode 进程，跨会话的静态存活不会发生）；
  真验收是手工的——把 `ProjectSettings/EditorSettings.asset` 的 `m_EnterPlayModeOptions` 临时置 1，
  编辑器内连跑两遍 PlayMode 全量，跑完改回（仓内不常住该设置）。

- **Settings 是同一轮被漏掉的第八个门面（2026-10-14 补齐；判据是同一句原话）**：`SettingsManager.Initialize<T>`
  见 `Managers` 里已有同类型即「告警 + 返回旧实例」，而 `Managers` 只在显式 `Destroy` 里清——使用方照文档
  再调一次无法自救（第二会话复用上一会话的管理器：旧 store、旧脏计数，其自动保存 ticker 也已不在被清空的
  `UpdateManager` 里）。同轮修掉同根因的另两处：**重放注册表跨会话累积**（第二会话里同一字段再建句柄，
  `Apply` / `Load` / `Reset` 会重放两次——重复日志与重复副作用）与 **`SettingsPauseNotifier` 计数不归零**
  （上一会话的计数不随边界归还，第二会话最后一次 `Release` 归不了零，`DontDestroyOnLoad` 宿主永不销毁、
  计数逐会话累积）。修法为「会话边界清空 + 句柄『用即重登记』」，取舍弃取（弱引用、纯清空两条路各有硬伤）
  见 `Runtime/Settings/README.md` 的「已知限制」。回归用例 `Tests/Runtime/Settings/SettingsSessionResetTests`
  7 例（含四条「用即重登记」的逐入口守卫）+ `SessionResetTests.Settings_ReinitializesAfterSessionReset`。

#### Bootstrap

- **`Shutdown` 与在途 `RunAsync` 互不感知（2026-10-13 实测 + 修复，`948aadb`）**：`Shutdown` 清理的是
  **整张登记表**（含本轮从未执行的后置相位），而在途管线是装配期快照、不受清理影响——**被 `Shutdown`
  过的阶段仍会在自己的 `Shutdown` 之后被执行**；真实场景下销毁 `GameLauncher` 还会因初始化代际作废
  产出三条 Error（`[Pipeline] Parallel stage failed` / `[Pipeline] Pipeline failed` / `[GameLauncher] 启动失败`，
  `GameLauncherFrameTests` 用例 3 留作端到端证据）。修法：**Bootstrap 内部链接取消源**——`RunAsync`
  在重入守卫之后创建 `_runCts`（链接调用方令牌）并把链接令牌传给管线，`Shutdown` 先 `Cancel()` 再按
  相位逆序清理，`GameLauncher.Start` 同时改传 `destroyCancellationToken`（纵深防御）；`ResetForNewSession`
  一并中止并丢弃遗留取消源。**覆盖边界如实记**：中止是协作式的——不响应取消的在途阶段仍会跑完并产
  同一条错误链（用例 3 原样保留），停止点是该阶段的沉降处；`Shutdown` 返回时在途运行只是「已收到信号」，
  同帧再调 `RunAsync` 仍会被重入守卫忽略。两条实现硬约束（写错会让既有用例变红）：取消源创建不得早于
  重入守卫（否则二次调用覆写字段、首次运行的收尾误 Dispose）；`finally` 只 Dispose 创建时捕获的局部
  变量、`ReferenceEquals` 判身份后才清字段（`ResetForNewSession` 在途时清 `_isRunning`，允许新旧运行并存）。
  红证：两处断言（用例 2 的 `later.ExecuteCount`、新用例的 OCE）在修前分别以
  `Expected: 0 / But was: 1` 与 `Expected: <System.OperationCanceledException> / But was: null` 判红，修复后转绿。

- **运行中 `RegisterDefaults` / `Clear` 原本静默改表**：README / XML 承诺「运行中修改登记表只告警」，
  但这两条路径直接改表、不产任何提示。现在两者都打 `[Bootstrap]` 警告并说明后果（新增阶段本轮不执行、
  但会被 `Shutdown` 扫到；`Clear` 清掉的阶段在途仍执行、但拿不到 `Shutdown`）。回归用例
  `BootstrapTests.RegisterDefaults_WhileRunning_WarnsButProceeds` / `Clear_WhileRunning_WarnsButProceeds`，
  改前必红：`Expected log did not appear`。

- **会话复位后旧运行会清掉新运行的重入守卫**：`ResetForNewSession` 允许新旧运行并存，而旧实现的
  `finally` 无条件 `_isRunning = false`——新会话的运行已开始时，旧运行收尾会把它的守卫一起清掉，
  第三个 `RunAsync` 被错误放行、同一批阶段并发跑两遍。现在用运行代际号做归属：只有仍持有当前代际
  的运行才清守卫。回归用例
  `BootstrapTests.ResetForNewSession_DuringInFlightRun_OldRunDoesNotClearNewRunsGuard`
  （改前必红：`Expected: 1 But was: 2`）。

- **5 个文件 / 11 处零分配断言此前是恒真的**（两把尺子在本环境恒返回 0，锁的结论从未被验证过）。
  11 处全部改成两道防线：**结构断言优先**（句柄 / 快照 / 统计行是值类型、状态槽位按类型参数泛化、
  表声明为具体字典……——「零分配的原因」本身可反射验证），**粗尺子放大兜底**（阈值取实测信号 1/4），
  Log 的「未启用不格式化」另加**直接观测**（`ToString()` 计数探针 + 计数 sink + 正对照）。
  改后实测：**11 处站点的堆增量全为 0 B**——零 GC 卖点第一次被真实测量。改造中的一条发现已写进
  `维护向的工作流手册` §六：粗尺子对**瞬时垃圾**型回归的灵敏度会被测量窗口内的 GC 削掉一截，
  那类回归以直接观测为准。

### Documentation

- **订正 Audio 的钳制点与已知限制（2026-10-19，R3-AUD-005/006/007）**：`AudioChannelConfig.Volume` 的钳制点是
  `RegisterChannel` 快照时（不是 `SetChannelVolume`，README 与 XML 同步）；已知限制补「不要直接 Dispose
  门面持有的管理器实例」与「3D 空间参数不可配置」。
- **订正 Serialize 的多态/Default/type 形参口径（2026-10-19，R3-SER-001/002/003/005/006）**：Newtonsoft 实现未启用
  `TypeNameHandling`，**不保留多态类型信息**（README 与 XML 同步）；替换内置格式在初始化之后 `Register` 也会
  覆盖（删去「必须在初始化之前」两句）；`Get(null)` 补 `ArgumentNullException`；`Serialize` 的 `type` 形参
  内置实现忽略；`Default` 按 `json` 是否注册判断，与 `IsInitialized` 不是同一面。
- **订正 Config 的注册/预载语义与 XML（2026-10-19，R3-CONF-004/008/009/011）**：`Register` 后 `Preload` 命中
  「已加载」直接返回注入数据、不会加载 assetPath（反向才由 `Register` 覆盖）；`PreloadGlobalAsync(path, default)`
  与 Table 同形二义（CS0121）；CSV 主键列只对「字段承载主键」是必需的（计算/显式主键可省略）；
  `ConfigIndexView.TryGet` 失败时 `values` 为 null，与 `Get` 的空数组不对称。
- **订正 UI 的驱动器恢复、回池钩子与绑定示例（2026-10-19，R3-UIA-010/UIB-001/004/008）**：`SetInstance` 不会
  重接驱动器（只有 `Initialize` 会）；`UIDefaultController` 注释改为「默认静默 + verbose 模板化」；
  `OnPoolRecycle` 标注「重写必须调用 base」；绑定示例改用 `UIViewBase.Track` / `UIPanelBinding.RegisterBinding`，
  不再引用私有字段。
- **订正 Update 的 Clear 恢复指引与 ProcessImmediate 边界（2026-10-19，R3-UPD-001/002/003/004）**：Timer 只有在
  `Clear` 时处于空闲才会在下一次 `After` / `Every` 重新注册（有定时器在计时时需先 `CancelAll`）；UI 的
  `SetInstance` 不重接驱动器；`ProcessImmediate` 对已禁用/缓冲中的节点静默 no-op；`FlushPending` 注释改为
  「`Clear` 作为缓冲操作排队」；删去「四个消费方」固定计数。
- **归档第三轮审计的 14 条待裁与框架级结论（2026-10-19）**：`Modules/Asset/Audio/Localization/Log/Settings/Timer/UI.md`
  的待裁条目按 R3-ADJ-01..14 落定（`不做` / `暂不做` / `未做` / `已做`）；`Ledger.md` 补「公开枚举整数值纳入
  基线」`待裁`、「依赖清单从 asmdef 反查」`未做`、「Cinemachine 扩展包门禁」`未做`，并追加第三轮判据复核；
  `Modules/README.md` 的机械守卫射程描述与实际检查对齐（文件表，不是 Ledger 索引）。
- **包 README 补「包自身 Git URL 不钉修订」说明（2026-10-19，R3-ADJ-14）**：写明与第三方依赖必须钉 `#tag`
  的有意不对称、如何钉 `0.3.0`（`?path=` 在 `#` 之前），并提示该 tag 早于 Camera / Scene 且 `AssetPlayMode`
  整数值不同。
- **订正 `RecoverAsync` 的线程契约表述（2026-10-19，R3-ADJ-08/SAVE-004/SAVE-006）**：实现会在**每次读侧车**
  时切回主线程（`EnsureSidecarAsync` 无条件调 `TryReadSidecarAsync`），而文档写成「只有重建侧车时切一次」。
  `SaveManager` / `SaveManagerImpl` / `SaveBootstrapStage` / Save README 与 File 侧四处「全程不切回主线程」
  残留统一改为「文件原语在池线程执行，读侧车时切回主线程」；`Modules/Save.md` 的双路径改造条目由 `待裁`
  转 `未做`（本轮只订正文档，改造单独计划）。
- **`AssetPlayMode` 的序列化迁移说明与 Host 模式 tooltip（2026-10-19，R3-AST-001/009）**：`0.3.0` 的枚举是
  `Offline=0/Host=1`，当前是 `EditorSimulate=0/Offline=1/Host=2`；该 tag 从未分发（用户确认），故保留当前
  顺序，只在行为变更节写明「从 0.3.0 源码升级需重新选择运行模式」。`DefaultGameLauncher` 的 Host tooltip
  补「需注入 `RemoteServices`，否则启动抛 `ArgumentException`」。
- **删除不可实现的「用项目自己的 AudioMixer」配方（2026-10-19，R3-AUD-001）**：框架动态创建的播放源
  `outputAudioMixerGroup` 恒为 `null`，声音不会进入项目 Mixer 的总线/效果链，`AudioMixer.SetFloat`
  对框架声音静默无效。README 的该配方已删除并改为真实边界（要接入需整体替换 `IAudioManager`），
  已知限制补一条；决策台账同步订正原理由。
- **包 README/安装器补 Input System 依赖（2026-10-19，R3-INP-001）**：核心程序集 asmdef 无条件引用
  `Unity.InputSystem`，但依赖表、安装 JSON 与一键安装器只列 UniTask / YooAsset / Newtonsoft——第三方
  按 README 装完会因程序集引用缺失整体编译失败。三处补 `com.unity.inputsystem`（1.19.0）；
  `DependencyDeclarationTests` 的逐字一致守卫自动覆盖新条目。
- **Config XML/异常消息口径订正（2026-10-17，CONF-004 部分）**：未加载异常消息改教真实存在的
  `PreloadTableAsync<T>()` / `PreloadGlobalAsync<T>()`（原文的 `PreloadAsync` 不存在）；`IConfigLoader` 不再
  声称可扩展 `ConfigFormat` 枚举（自定义 Loader 走重载）；非泛型 `RegisterTable` 改为「经 `IConfigTable.Data`
  取字典，无反射」；CSV 写明**必须包含主键列**（缺列时多行撞 `Duplicate Id`）。
- **Config 剩余口径订正（2026-10-17，CONF-004 尾）**：`ConfigChanged` 的 XML 不再写「注入的自定义实现需要
  自行派发」——静态字段式事件只能由声明类型触发，与 README 的「不触发且无法触发」对齐；三处 `Register`
  补 null 的 `<exception cref="ConfigException">`；`IConfigRow` 的三键示例改为真正的三键行类型（原示例仍是
  双键）；`ConfigLoadedInfo.ToString` 的示例改为实际输出形态；README 文件树的 `ConfigLoadHelper` 描述从
  「加载调度助手」改为「泛型委托缓存」。
- **Pipeline / UI / File / Input / Data / Save / Update 边界补文档（2026-10-17，PIPE-005/006、UIA-004、FILE-004、INP-010/014、DATA-009、SAVE-006、UPD-002）**：
  Pipeline 补「容器实例不可跨管线并发复用」「`Destroy` 不中断在途运行、`Status` 不保证恒为 Idle」；
  UI 的 `Update` 改为「只驱动 Tier0 与 HUD」（`IUIManager` / 门面 / README 三处），手动逃生口改用
  `UpdateManager.Clear()`（不再写 `Destroy`）；File 补原子写的 `.tmp` 残留与降级替换的短暂缺失窗口；
  Input 补 `playerId > 0` 复合绑定只读首子项与同名动作歧义告警；Data 补脏集合载体不在公开面；
  Save 补删除跨帧的读面窗口；Update 补 `Clear` 恒压过同窗口操作。
- **Log 口径订正（2026-10-17，LOG-008 部分）**：`DroppedSinkCount` 补齐 `Flush` 抛异常也会摘除计数；
  `ILogManager.Shutdown` 不再说「实现随即不可用」——旧实现对象仍报告 `IsInitialized = true`，可用性以门面
  是否持有该引用为准；`LogOptions` 改为「每次构建实现时读取」（`Configure` 或懒建/自动初始化）；
  README 的 `CaptureUnityLogs` 改为「默认值与 `EnableFileSink` 相同（构造时一次性拷贝，之后改不联动）」；
  维护文档的 `XFrameworkDependencyInstaller.cs` 行号 `135` 订正为 `134`。
- **Settings / Reactive / Camera / CLAUDE.md 口径订正（2026-10-17，SET-011、RCT-009、CAM-005、AST-009）**：
  `JsonFileStore` 的「主文件缺失不回退备份」理由改为实际原因（Reset 会同时删 `.bak`；缺失按无数据处理），
  不再写「防 Reset 复活」的错误理由；Reactive README 写明不提供主线程断言及理由；Camera 的
  `OutputCamera` / `Unregister` 契约窄化（适配器可恒非空、注销只清管理器视图，CAM-005）；CLAUDE.md
  的宽容语义豁免补 `AssetManager.UnloadSceneAsync`（代码自称第三处）。
- **Asset / Diagnostics / Audio / File 文档订正（2026-10-17）**：
  Asset（AST-006）：API 表补 `ClearPool` / `ClearAllPools`。
  Diagnostics（DIAG-004）：概览页签补 Camera 行。
  Audio（AUD-006/011）：生命周期表补 `CopyChannels` 未初始化返回 0；台账的「不做只读通道快照」改 `已推翻`
  （`CopyChannels` 已落地），剩余逐 voice 明细改 `未做`。
  File（FILE-005/006/007/010/012）：补移动端 Streaming 枚举静默返回空数组、`DeleteAsync` 取消可能已完成删除、
  `ConsoleFileProvider` 五个抽象成员的直接文档、移动端写 Streaming 不重定向、台账去固定计数。
- **Input / Update / UI 文档订正（2026-10-17）**：
  Input（INP-005/006/007/012）：README 依赖版本对齐 6000.3 / InputSystem 1.19.0；类 XML 不再写「每帧需调
  `Tick`」（自动驱动）；`SetProvider` 明确不注册帧驱动；空 `bindingId` 回退时 `OnCompleted` 的 Id 为 `""`。
  Update（UPD-001/007）：`Clear()` 的连带影响与各模块恢复入口写进 XML/README；永不 tick 的调度器上跨时机
  操作无界缓冲写进已知限制。
  UI（UI-B-003/007/009/012、UI-C-004/009/012）：Tip 改为 `Begin` 且不再自驱动；`UIPanelBinding` 回池自动
  解绑（`OnDestroy` 仅兜底）；HUD/Tip 无档位声明通道；核心类型表两处陈旧描述；遮罩样式「最后一次
  `ShowMask` 为准」；`OnAfterClose` 是回池不是销毁、`OnAfterOpen` 的自关/异常边界；控制器日志条数去固定值。
- **Config 文件形状与前置条件补文档（2026-10-17，CONF-003/013）**：README 补 Json `{"Items":[...]}` schema、
  三种格式都经 `AssetManager` 读取（必须先初始化）；门面完备性注释删固定计数；补录审计 F10 的台账条目。
- **Lock / Timer / Localization 文档小错订正（2026-10-17，LOCK-003/004、TMR-004/011、LOC-008/011）**：
  Lock 的 `CopyLockedTypes` 成本改为 O((n+m)²)、`RemoveAllSubscriptions` 的「三张表」改为两张表 + 组合订阅、
  `UnboundSubscribedSubjectCount` 不再一律称为孤儿；Timer 订正 `CancelAll` 不重置逻辑时钟、`TimerHandle`
  成员名 `Slot`、`TimerInfo.IsRunning` 的取消态、`CancelAll` 计数口径、跨轴限制的静态闩锁范围；Localization
  补无并发写的读前提与占位符全局命名空间。
- **Event / Serialize / Scene / Data 文档订正（2026-10-17，EVT-003/004/005、SER-003/004、SCN-002/005、DATA-013）**：
  Event README 的「每订阅零分配」「被两个模块使用」「五个公开文件」订正，`SubscriptionTracker` 明确无自动复位
  钩子；Serialize 的消费方改为 Data / Save（模块 README、包 README、门禁豁免理由三处），`TryGet` / `Unregister`
  的 null 参数契约补齐；Scene 补「后端缺 `IAssetSceneLoader` 时返回无效句柄」与主线程守卫理由句；Data 补
  `ApplyBlockSnapshot` 的 `OnClear` 调用点与半态。
- **Reactive 相等 / 分配 / 异常边界订正（2026-10-17，RCT-002/004/005/006/007/010/011）**：相等语义改为
  `EqualityComparer<T>.Default`（重写 `Equals` 即按 `Equals`，不是引用相等）；`Keys` / `Values` 首次访问
  缓存视图；自定义键比较器下「键稳定」只对比较器等值成立；`Sort` 异常后可能已部分重排且不补发 `Reset`；
  值比较器不可注入记 `未做`；未实现 `IEquatable<T>` 的结构体去重 / 查找装箱；`Subscribe` 立即回调抛出时
  不登记订阅。XML 与 README 同批。
- **Pool 已知限制补四处口径（2026-10-17，POOL-004/006/007/009）**：`MaxSize <= 0` 等于无上限；池的日志分类
  按简单类型名合并（跨命名空间同名类型互相影响）；集合池 `Configure` 重建后旧 `GetPool()` 引用失效；
  `RemovePool` 丢弃闲置实例但不走丢弃回调。四个集合池的 `GetPool` XML 同步。
- **Log 分类命名空间与懒建线程边界（2026-10-17，LOG-005/011）**：README 的已知限制补「分类是全局裸字符串键、
  同名静默合并、外部标签只增」；线程契约补「`Shutdown` 后工作线程首条日志会触发懒建并在该线程读 Unity API」，
  同时订正 sink 重入的两种路径（LOG-002 遗留的另一处表述）。
- **Message 淘汰语义与线程契约订正（2026-10-17，MSG-003/005）**：`EvictBuffered*` 会 `Dispose` 整条缓冲流、
  终止仍挂着的缓冲订阅者——README 原写「只丢重放缓存，不会回收仍有活订阅者的通道」，已订正并记 `待裁`
  （「只清缓存保订阅」需 Event 新原语）；线程契约扩写断言射程（`EvictBuffered*` / `TrimEmptyChannels` /
  统计族无断言）。
- **Save 线程契约与忙碌门禁订正（2026-10-17，SAVE-007/008/010）**：`ISaveManager` 的 XML 不再声称
  「内置实现会检查忙碌并抛异常」（实际无守卫，2026-09-27 裁定维持）；`RecoverAsync` 的「全程不切回主线程」
  订正为「需要重建侧车时切回一次」；README 线程契约补「公开写入口必须在主线程调用」。
- **Reactive 已知边界补链式 Select 的中间派生值（2026-10-17，RCT-001）**：`d2 = d1.Select(g)` 后 `d1`
  不可达，`d2.Dispose()` 不会拆 `d1 → root` 的订阅；README 写明「逐级持有、逐级释放」，是否提供
  「拥有上游」的显式入口记 `待裁`（用户裁定：本批只文档化）。
- **UI 暂停逃生口订正（2026-10-17，UI-B-002）**：README 原写「先 `Destroy` 再自行驱动」——`Destroy` 后
  门面未初始化，`UIManager.Update` 会抛。改为「`UpdateManager.Clear()` 摘驱动器后自行调用」，并给出恢复
  自动驱动的路径；不加公开 API（用户裁定：文档化）。
- **Diagnostics 登记幂等承诺订正（2026-10-17，DIAG-001）**：去重只按同一实例——关闭域重载时用 `new` 登记
  会每次进播放多出一个同名页签；管理器 XML、模块 README 与随包示例统一改为「静态只读实例登记」或退出时
  `Unregister`，去重语义不变（用户裁定：改文档，不改行为）。
- **Timer 已知限制补「粗档下新建定时器可提前一个档周期」（2026-10-17，TMR-003）**：锚点取上一次派发的
  逻辑时刻，60 秒定时器存在时 `After(1f)` 可能约 40ms 后触发；干净修法需要 Update 暴露当前轴时刻查询面，
  记 `待裁`（用户裁定：本批只文档化）。
- **File 线程契约补移动端 Streaming 例外（2026-10-17，FILE-002）**：`MobileFileProvider` 的异步入口在池线程
  读 `Application.streamingAssetsPath` / 构造 `UnityWebRequest` 会抛 `UnityException`；README 的线程契约、
  性能代价与已知限制三处改为「移动端 Streaming 异步入口必须主线程，桌面端与其余域不受影响」。
- **Settings 线程 / 生命周期 / 会话边界文档订正（2026-10-17，第二轮审计）**：线程契约补「默认值工厂与
  迁移器 / 校验器同线程（同步 store 的 `LoadAsync` 降级在池线程）」（SET-002）；生命周期表补「释放请用
  `SettingsManager.Destroy()`，直接 `Dispose` 会留僵尸登记」（SET-003）；会话边界订正「旧订阅者不会复活」
  ——边界只清登记表、不清句柄订阅流，`SessionBoundary_HandleReadInNewSession_StillReplays` 钉住现状
  （SET-004）。
- **Update 时间轴与 Localization 重载文档（2026-10-17，第二轮审计）**：`IUpdateable` / `ILateUpdateable` 的
  `time` 参数与 README 时间表补「`Unscaled` 轴为 `Time.unscaledTime`」（UPD-005）；Localization 补
  `new LocalizationBootstrapStage("en", null)` 的 CS0121 二义与显式强转写法（LOC-001 文档侧）。
- **公开面模板句与固定计数清扫（2026-10-17，第二轮审计）**：删除 Config / Save / File / Lock / Input /
  Localization / Asset / Audio / Pipeline 的「N 个文件都是公开面 / 本模块没有 internal」模板句，文件树按磁盘
  逐文件标注 `internal`（CONF-002 / SAVE-011 / FILE-003 / LOCK-002 / INP-004 / LOC-002 / AST-005 /
  AUD-010 / PIPE-001）；Message 公开面表补 4 个公开类型（MSG-006）；删掉 Camera / Diagnostics / Data /
  Update 的固定计数（CAM-006 / DIAG-006 / DATA-012 / UPD-004）；Pool API 表的 `Configure` 前提改为
  「无活跃实例时」（POOL-005）。
- **契约句订正（2026-10-17，第二轮审计）**：Serialize README 的「替换内置格式」语义写反（SER-002：
  `Register` 总是静默覆盖，内置注册只在 `Initialize` 时让位于使用方）；Asset README 热更链仍写
  「预检 → 下载 → 激活」（AST-002，改为「预检 → 激活 → 下载」）；Audio 的 `Play` 失败契约在接口 remark /
  门面 XML / README 三处与实现相反（AUD-007：加载失败不返回 default，`IsPlaying` 也区分不了「加载中」与
  「加载失败」）、通道惰性创建的入口清单（AUD-008：音量 / 静音设置与 `RegisterChannel` 同样会创建）、
  `Stop` 后「代际递增」的措辞（AUD-009：代际在下次预占才递增）；PoolStats 的 `CountAll` 注释与 README
  补「`Clear()` 不归零，集合池 `Configure` 重建才归零」（POOL-003）。
- **随包 XML 示例修正（2026-10-17，第二轮审计）**：`TimerManager.After<TState>` 的示例把 `this` 与 lambda
  写反（TMR-001，照抄 CS1503）；`IDataBlock` 示例缺 `DataVersion` / `OnMigrate`（DATA-001，照抄 CS0535）；
  `Serializer.Default` 的 `<exception>` 写成 `KeyNotFoundException`、异常消息又教了一个 no-op 的
  `Initialize()`（SER-001）；`Select` 的 comparer 示例引用了仓内不存在的 `FloatComparer`（RCT-003）；
  `IDestroyCancellationToken` 示例在 `Dispose` 之后再读 `_cts.Token` 会抛 ODE（MSG-002，改为构造时缓存令牌）。
  MSG-002 另补 `DestroyTokenBindingTests.Subscribe_AfterOwnerDisposed_DoesNotThrow`（改前红：ODE）。
- **XCamera 模块文档与包 README 四表**（2026-10-17）：新增 `Runtime/Camera/README.md`（18 节骨架、5 项必答）、
  维护向模块记录、包 README 的功能 / 模块索引 / 可替换实现 / 可配置项四表同步；`ReadmeTableTests` 登记 `Camera`。
  **核心包依赖表不新增 Cinemachine**（核心不依赖它）；Cinemachine 3.1.7 的安装与版本归独立扩展包。
- **Cinemachine 扩展包维护向记录**（2026-10-17）：扩展包的版本、Cinemachine 3.x API 实测、升级与漂移点落在
  维护向记录（不随包发布）；扩展包自己的 README / CHANGELOG 随扩展包发布。
- **XCamera 动态句柄与过场边界文档**（2026-10-17）：模块 README 增加命名 / 动态双键、`CameraHandle`、动态预制
  配方、Timeline 边界；扩展包 README 增加动态 vcam 与未注册活动相机告警；维护向记录补槽位 / 代际与主接口
  直扩的裁定。
- **普通相机行为功能的推荐路径**（2026-10-17）：模块 README 补齐「需要跟随 / 震屏 / 缩放等行为时」的三条路径
  （Cinemachine 级 → 扩展包；简单需求 → 项目侧脚本；别的后端 → 实现 `ICameraManager`）；扩展包 README 明确
  「适配器，不是行为库」；无 Runtime / API / 依赖变化。
- **模块 README 规范增补「自定义实现」按需节（2026-10-17）**：一级骨架 17 节 → 18 节（必答仍 5 项），
  新增按需节 `## 自定义实现`，排在 `## 典型场景`（含特性节）之后、`## 内部机制` 之前。动因是普查发现
  实现方内容没有稳定的家：23 份中 17 份有可替换后端 / 扩展点，但主落点分散在 `## 典型场景`（6）、
  `## API 参考` 的 `###`（3）、`## 接口承诺到哪为止`（1）与其余散落（7）；File 的实现方契约被拆成 6 处，
  Settings 的「自定义存储后端」写了两遍，Asset / Save / Data / Localization 只有一句「实现 X + SetInstance」。
  - **判据**：模块**接受**第三方实现（接口注入 / 注册 / 工厂委托 / 基类继承 / 可选能力接口 / 按类型钩子），
    **且**至少有一条非显然的实现方义务或接线语义（线程亲和、不得抛、可选能力缺失后果、装饰器透传、
    注册顺序、所有权与释放）。两条缺一不建；只有公开接口、框架没有接受实现入口的 Event / Reactive / Pool
    不建节——义务留在 `接口承诺到哪为止` / `API 参考`，不制造「可以注入」的假象。
  - **分界**：`典型场景` = 调用方配方；`自定义实现` = 实现方路径，且只做**索引**——义务留在 `行为契约` /
    `线程契约` / `已知限制`，签名留在 XML 注释；「为什么没有替换点」仍归 `设计取舍`（Scene / Timer 不建节）。
    可选 `### 推荐第三方实现` 只收能对上框架契约的现成实现（Rewired / MessagePack / Wwise / 平台存档），
    每条点名契约与已知代价；不写版本号 / 安装 URL / 接入教程。
  - **旧名表改映射**：`扩展点` / `第三方扩展` / `定义一个阶段` / `接入 Console 平台（Xbox / PS5 / Switch）` /
    `多平台账户隔离` / `扩展：自定义 IInputProvider（如 Rewired）` → `自定义实现`；`三种接入方式` /
    `自定义输入封装（第三方游戏必须做）` / `把第三方事件接进流` 仍归 `典型场景`。
  - **门禁不动**：`ReadmeSectionTests` 只守 5 个必答节（子串匹配），新增按需节不在射程；
    `check-docs.ps1` 只按「模块结构」定位文件树，新节名不含该词。
  - **落地范围**：规范 + File 打样，其余 14 个建节模块分三批迁移完毕（A：Settings / Input / Log / Audio；
    B：Asset / Save / Config / Data / Localization；C：UI / Diagnostics / Bootstrap / Pipeline / Serialize）。
    **不建节的 8 个模块**：Event / Lock / Message / Pool / Reactive / Scene / Timer / Update——没有
    「框架接受第三方实现」的接线入口；Scene / Timer / Update 已在原有章节解释为何没有替换点。
- **模块 README 结构统一（2026-10-16：规范 + 门禁 + 23 个模块逐次重整）**：普查 23 份模块 README
  （共 9196 行）得到的结论是**问题不在内容缺失，在不可预测**——同一件事有多种写法（`快速使用` 14 家
  vs `快速开始` 6 家）、API 小节 8 种名字、**线程小节 5 种名字且 11 家一个字没写**、`##` 级标题里只
  出现一次的有 78 种、H1 有 4 种形态。立项依据是一组对照：**唯一被守卫锁死的「已知限制」23/23 齐，
  完全没守卫的「线程契约」11 家空白**——没守卫的约定必然腐烂。
  - **规范**：新建模块 README 结构规范（一级骨架 17 节、**5 项必答** = 概述 / 快速使用 / 模块结构 /
    线程契约 / 已知限制；特性节；旧名→新名对照表；文件树两份分工；H1 与措辞；门禁射程）。对照表的
    完备性判据是「遍历全部模块 README 的 `##` 标题，每个名字要么在目标列、要么在保持 / 模块特有列」
    （实测 121 个去重标题名 0 未覆盖）。
  - **门禁两条**：`ReadmeSectionTests` 加五节必答表（逐模块重整期以「已重整模块」登记表过渡——
    让每个提交自带验证、窗口期为零；23/23 完成后整表退休、作用于全部模块）；`check-docs.ps1` 加
    「README 的文件树 **== 磁盘**」检查，同日纳门为第五项判定。两条各做过负向控制。
  - **文件树两份分工**（用户裁定）：README 也列全、`internal` 逐个标注；权威侧（维护文档）仍是
    逐文件详述版。**两处都由磁盘锚定**——README 侧与磁盘的一致性由守卫抓，漏收与残影两个方向都管。
  - **顺带抓出的存量问题**：**11 份权威副本各漏一个公开文件**（全是「类型从别的文件拆出来后树没跟上」：
    `TimerInfo` / `DiagnosticItemKind` / `LogCategoryInfo` / `UpdateNodeInfo` / `AudioChannelInfo` /
    `LocalizationLanguageInfo` / `PoolStats` / `PoolLogCategories` / `InputSystemOptions` /
    `InputSystemOptionsAsset` / `InputBootstrapStage` / `UIBootstrapStage`）；Log README 的内置分类
    计数陈旧（31 vs 源码 33）；`Save/README.md` 一处「包内指向包外」；`Scene/README.md` 一处悬空的
    「§四」节号；`Audio` 一处失实声称（包 README 写「Audio README 已登记」而实际没有）；**5 处 `.cs`
    死指针**（`Event`×2、`Message`×3 都指向 README 里根本不存在的节）；`File` 一处原文重复。
- **「包内不指向包外」扩到包内全部 `.md`（用户裁定「包内不要引用包外资源」），清出 48 处**：守卫
  `PackageBoundaryTests` 新增 `PackageMarkdown_DoNotReferenceRepoDocumentation`（扫包内全部 `.md`，
  与原 `.cs` 那条共用豁免登记处）。48 处 = `CHANGELOG.md` 47 处 + 包 `README.md` 1 处——此前只扫
  `.cs`，这些一直没被看见。改法统一为**用描述代替路径**（「X 模块的维护向记录」「框架级决策台账」
  「维护向的工作流手册」…）。**判据只有「有没有那条路径」一条**，不再区分「指路」与「历史陈述」；
  确需保留路径的走豁免登记处写明理由。
- **「包内不指向包外」补齐源码注释这一路，并机械化**：两轮清出 22 处历史违例（1 处 `Runtime/Scene/README.md`
  + 7 处引用当时将删的评估报告 + 15 处引用其余维护向文档；其中 **2 处在 Runtime 生产代码的 XML 注释**里，
  随包发布、第三方在 IDE 里直接读到死指针）。全部改成**自足表述**（结论内联、不给出处路径）；新增守卫
  `Tests/Runtime/Architecture/PackageBoundaryTests`（扫包内 `.cs` 原文含注释与消息串，豁免登记处照既有形状），
  `CLAUDE.md` 的规则文本从「只讲 README」扩到「含源码注释」。
- **删除那份框架级评估报告**（2026-10-11 那一轮的框架级评估报告）：决策早已迁入
  `Ledger.md` + 各模块台账、流水在 `CHANGELOG`；残留资产**分流**——§三 同类框架对照表 + §二 的
  「已经领先的地方」进 `Ledger.md` §五（标为快照、只作立项调研起点），公开 API 基线的形态取舍与
  `Tests~` 的成本测算并入对应条目，其余（规模数字、探针事实、已落地条目的原事实留痕、§5.x 过程档案、
  §七 落点表）按裁定不再保留。删前先清 7 处**包内/工具**注释对它的引用（顺带修掉同类「包内指向包外」）。
- `FrameworkReview.md` §六（已否决项复核 10 条）**迁入 `Ledger.md` §四**，原节改指针；报告头部补定位段
  （**状态查台账、流水查 CHANGELOG**，本文只留分析与过程档案）。**§4.x / §5.x / §二 / §三 有意不迁**——
  流水已在 `CHANGELOG`、快照数字（22 模块 / 94k 行）会腐烂、过程档案不该进活台账。
- **决策台账落成（文档治理一轮）**：`框架级决策台账`（新——框架级「已做 / 未做 / 待裁 /
  暂不做 / 不做」条目 + 模块台账指针；`Roadmap.md` 并入后删除，其 §三/§四/§五 的条目与专段全部迁入）、
  `维护向记录的目录规范`（决策台账的条目格式与状态词表；登记六个此前未入表、形态一致的
  「暗节名」）、23 个 `<模块> 模块的维护向记录`（「已完成功能与未做」「已评估未采纳与未决」
  两节合流为「决策台账」——状态写行首、条目永不移动、每条带日期与证据）、
  `Tools/check-docs.ps1`（文档通道第四项判定：每模块有台账、状态词合法、模块文件表与 `Runtime/`
  双向相等；**只守结构，守不了内容属实**）、`CLAUDE.md`（文档分层表 Roadmap 行换 Ledger 行 +
  模块行补台账；文档门禁三项改四项）、`Workflow.md` §二/§四、`FrameworkReview.md`（引用改指；
  §5.1/§5.2 明标为那一轮评审的过程档案）、全仓引用改指约 30 处（含包内三处 XML 注释去掉对外部
  文档的指点）。**顺带订正的既有失效**：`ModuleAudit.md` 的「三档判据」误引两处与 §八 一行陈旧标题、
  `FrameworkReview.md` §5.2 九处已失效的「步骤 1 仍待办」残留、`Modules/{Log,Config,Input,Asset}.md`
  的行号与「会增长的量」陈旧计数、`Runtime/Scene/README.md` 一处「包内指向包外」的规则违反、
  `Modules/UI.md` 一处同文件内自相矛盾（「锚定弹窗未做」块与已落地记录并存）。
- `维护向的工作流手册` §六：零分配断言的纪律改为机械可依（唯一尺子、自检、守卫、结构断言优先），
  并记「关闭域重载连跑两遍」这条验收**脚本表达不了**。
- `Input / Asset / Bootstrap / Config / UI / Localization 六模块的维护向记录`：会话复位的处置记录；
  `Roadmap.md` §三 一句失实陈述（`InstanceTracker`「有族级守卫」）加订正框。
- `Runtime/{Input,UI}/README.md` 的「已知限制」：相关条目改写为已修。
- 四个模块文档补「PlayMode 侧覆盖」记录。
- 公开 API 基线：`Workflow.md` §五 参数表加 `-UpdateApiBaseline`、§六 记两条边界（「测试反写仓库文件」的
  唯一入口；基线与 `CHANGELOG` 迁移表之间**没有机械联系**）；`FrameworkReview.md` §4.3 第 4 条与 §五 第 16 项
  标完成（并记「为什么没用现成分析器」）；`ModuleAudit.md` §八 的候选门禁表**新增**「公开 API 基线」一行。

- 场景管理立项轮：`Scene 模块的维护向记录`（新——四条边界的逐条结论、S1/S2/S3 落点取舍、
  人工验收配方、**实测 vs 推理逐条标注**、测试壳里不可达的那一处）、`Modules/README.md` 文件表、
  `Modules/Asset.md`（文件结构 + 时间线）、`Roadmap.md`（§三 条目搬迁、`§2.6` 原地留指针、专段由三条改两条）、
  `FrameworkReview.md`（§三 能力矩阵、§4.1 缺口段回填为完成态、§5.1 归档 #14、§5.2 步 10 标完成并注明
  「本轮跳过了步骤 1–9」）、包 `README.md` 四张表、`Runtime/UI/README.md`（「切场景」小节指向新模块，
  并声明两条正解与「不做自动检测」不因它改变）、`Runtime/Asset/README.md`（API 行、模式选择表、
  已知限制 4 条、接口承诺、指向 `XScene` 的使用方入口）。

- 包 README 四张表守卫（`FrameworkReview.md` §5.2 步骤 4 / #22 落地）：`FrameworkReview.md` §4.4-1 标完成
  （射程与「守不到」一并写明）、§5.1 归档 #22、§5.2 步骤 4 标完成（诊断与红证实录）；`Workflow.md` §六 补
  守卫射程一条（`check-docs.ps1` 对 `.md` 零覆盖**不变**）；`ModuleAudit.md` §八 候选门禁表**新增**
  「包 README 四张清单表」一行；`CLAUDE.md` 补一条同义的常驻规则。

- check-docs 门禁扩容（`FrameworkReview.md` §5.2 步骤 5 / #18 落地）：统计面从两码（CS1570 / CS1574）
  扩到**十一个码**（+ CS0419 / CS1591 / CS1573 / CS1571 / CS1572 / CS1734 / CS1735 / CS1584 / CS1658）
  并**全部纳入 `-Enforce`**，失败清单点名码；`check-docs.ps1` 顶部新增 `$CodeLabels`（码 → 中文标签，
  键集即统计面）与 `$GatedCodes`（门禁面）两张表，统计面之外的码只计一行明示、不进判定——CS1571 /
  CS1572 正是这一行上线首跑现形后补入的。诊断先行实测：**71 条存量全部清零**（按模块 8 个提交）；
  红证：注入一处裸名 `Tick` → 门禁判红并点名 `CS0419×1` → 还原绿。`Workflow.md` §二/§五、
  `ModuleAudit.md` A3 探针与 §八、`CLAUDE.md` 文档门禁与 cref ③ 同步；`FrameworkReview.md` §4.4-3
  标完成、§5.1 归档 #18、§5.2 步骤 5 标完成。**使用方不受影响**：包内注释订正 + 仓根工具，公开面
  一个字都没动。

- 测试覆盖失衡六处逐处裁定（`FrameworkReview.md` §5.2 步骤 6 落地）：`FrameworkReview.md` §4.2 该行改写
  （口径订正——原括号数字是 `Tests/Runtime/<模块>/` **文件数非用例数**；Asset 漂移订正）、§5.1 归档 `步6`、
  §5.2 步骤 6 标完成；`Modules/Bootstrap.md` 两条未决由「推理」改「实测」（含机制链、修法候选与其实测
  效果、覆盖边界）；`Modules/{Serialize,Event,Save,Reactive,Asset}.md` 各落「为什么不补」结论
  （Serialize 另订正一条已过期的未决——`Initialize` 同名注册保留使用方那条 0.3.0 已修）；
  `Runtime/Bootstrap/README.md` 已知限制**新增一条**：启动途中销毁 × 在途启动交叠的使用方面后果
  （含「被 Shutdown 的相位仍会被执行」）。

- Bootstrap「彻底修」的落点（`948aadb`）：`Bootstrap 模块的维护向记录` 两条未决处置为已修
  （实际采纳的修法、两条实现硬约束、覆盖边界）；`Runtime/Bootstrap/README.md` 已知限制改写（协作式中止
  的两条边界、OCE 令牌判据）、设计取舍补「为何 `Shutdown` 先中止」、测试注意事项补静态态；包
  `README.md` 启动引导示例注释同步。

- 裁定销账轮（滚动表步骤 2/3 结清，`6672eec` + `9f35757` + 本提交）：**#19** provider/store 守卫范围
  裁定「不守」并订正守卫形态（`ModuleAudit.md` §八 / `FrameworkReview.md` §4.4-4）；**#23** 相机 /
  远程配置 / 崩溃上报三条「建议不做」经用户确认移入 `Roadmap.md` §四（§三 专段撤下留指针，§四 复核
  记录注明「增补三条、非重开」）；**#17 CI** 成本要点成文于新 `CI 成本与裁定记录`（Unity 许可形态 /
  runner / 与测试壳关系，外部事实标核对状态），裁定**暂不接入** + 三条可判定重开判据，`Workflow.md`
  §六 与 `FrameworkReview.md` §4.3-5 落纸；`FrameworkReview.md` §5.1/§5.2 回填。**使用方不受影响**：
  全部结论落在维护向与仓根文档，公开面一个字都没动。

- 步骤 7 文档欠账收口（`FrameworkReview.md` §5.2 步骤 7 / #21 等五项）：
  **五个模块补「已知限制」**——`Runtime/{Event,Message,Pipeline,Pool,Serialize}/README.md` 按既有形态
  （逐字一致的开场白 + 「**结论句。** 机制 / 代价 → 那就改用 X」条目）补写，素材取自各 README 正文与
  `Modules/<模块>.md` 的在册结论，不新造事实；`Runtime/` 下 23 个模块 README 由此 23/23 都有该小节。
  同日**补上守卫** `Tests/Runtime/Architecture/ReadmeSectionTests`（每个 `Runtime/<模块>/README.md` 必须有该小节；
  Reactive 的两个集合级小节天然满足，无例外表；确实不需要者去豁免登记处写明理由），改前必红已验，
  `CLAUDE.md` 同步补一条同级门禁规则。「模块全集 = `Runtime/` 直接子目录」的枚举随之从 `ReadmeTableTests`
  抽进 `SourceScanner`（两处守卫共用一份定义）。
  **`Modules/{Pipeline,Pool}.md` 补「已评估未采纳与未决」**（23/23 模块文档齐），其中 Diagnostics 那条
  Pool 的「未采纳」按其落点表迁归 Pool（那边留指针）。
  **Config 的两处失效断言订正**：「CHANGELOG 里一条都没有 / 出现次数是 0」按字面**写下时就不成立**，而
  「手册从本模块的缺陷史里学到的是零」**仍然成立、但理由要换**——判据归纳的快照是 2026-09-27，本模块第一条
  `### Fixed` 记录 2026-09-30 才出现，是「归纳入早于缺陷史入册」（0.3.0 已发布小节加订正框，原文保留）。
  **`ModuleAudit.md`** §七 补上判据来源的盲区（没进过 `### Fixed` 的缺陷史，归纳法从它身上学不到）、
  §二 的普查步加缓解（必读该模块的 `Modules/<模块>.md` 沿革与 `git log` 缺陷史，不只照清单扫）。
  **包 README 写明**开发版本 `6000.4.5f1` 与最低要求 `6000.3` 的区别（「最低要求 ≠ 开发版本，不是漂移」）。
  **对使用方是纯增量的文档改动**：五节「已知限制」正是给他们看的；公开面与运行时行为零改动。

- 热更编排与网络基础层两条边界裁定（`FrameworkReview.md` §5.2 步骤 8）：`Roadmap.md` §三 新增两个专段——
  **网络基础层「暂不做 + 可判定升级判据」**（仓内零消费者、协议/鉴权归项目；第三方对照一手读 UGF：同形 `WebRequest`
  模块有队列/优先级/超时/事件但**同样没有重试**，对协议零假设），**热更编排「不建模块」**（读包内 YooAsset 2.3.18
  源码核对后推翻了「下载编排可进框架」的旧判——**队列并发 / 看门狗超时 / 断点续传 / 暂停恢复 / 单文件失败重试 /
  CDN 主备站上游都已提供**，本仓 `Asset` 也已包成门面；真缺口是「版本对比 / 整体重试 / 超时 / 更新决策 / UI 接线」
  一律归项目侧）。
  **顺带修掉两条已核实的缺陷**：① `Runtime/Asset/README.md` 的热更链路顺序 + `IAssetManager` 的承诺**订正**
  （`CreateDownloader` 按**当前激活清单**算待下内容，而预检**不激活**——原文档把激活写在下载之后，照抄会**静默空转**；
  现顺序为「请求版本 → 预检（可选）→ **激活** → 下载 → 加载」，已知限制新增第 12 条）；② 四个句柄的 `Status`
  改用框架自有 `AssetOperationStatus`（**破坏性**，迁移表见 `### Changed`）。
  `Asset 模块的维护向记录` 的「已评估未采纳与未决」同步记录，文件结构补漏列的一行。

- 红点 / GM 命令台 / 画质档位 / 代码热更四条边界裁定（`FrameworkReview.md` §5.2 步骤 9）：`Roadmap.md` 的落点分两类——
  **移入 §四（已否决，2026-10-13 用户确认）**：**红点系统**（树结构与父子计数是**领域模型**、「红点怎么算」由策划配置决定；
  框架侧那一半**已经给了**——`ReactiveProperty` + 集合变更流提供节点可观察性，而 Reactive 刻意只有一个 `Select`、
  不做算子面）与**画质 / 性能档位**（框架给死的档位**几乎必然被改**；**真问题另有其解**＝项目自定义 settings POCO——
  `Validator` 钳范围 + 默认值工厂按设备定档，载体是引擎自己的 `QualitySettings`；并注明**别与 `Update` 的「更新档位」
  （`UpdateTier`）折叠**，那是 LOD 调度、另一件事）。
  **留 §三「暂不做 + 可判定判据」**：**GM / 调试命令台**（形态已有一半＝诊断窗口的「Editor 工具 + Runtime 数据契约」，
  缺的那半——文本输入 / 解析 / 权限——仓内**没有原语**：`IInputProvider` 是纯动作名轮询；顺带**订正一处误读**：
  原先被列为「可复用件」的 `File` 的 `ConsoleFileProvider`，其实是**游戏机平台（Xbox / PS5 / Switch）的文件提供者
  抽象基类**，与命令台无关）与**代码热更（HybridCLR）**（**不建 Runtime 模块**：HybridCLR 本身就是第三方包、自带上载器与
  Editor 工具，与 YooAsset 同型；框架代码不该被热更，而热更要求「热更代码与 AOT 代码分属不同程序集」，与「Runtime 单一
  asmdef」正面冲突。形态＝Editor 工具 + 一份 HybridCLR 配方文档，**配方待真实接入再写**——从未接入时写配方
  就是臆测）。
  **使用方不受影响**：公开面与运行时行为零改动，改动全部落在 `维护向文档目录` 下不随包发布的两份文档（包内新增的只有本记录）。

- 包形态两条裁定与一条门禁缺口（`FrameworkReview.md` §5.2 步骤 11 / §4.3 第 11–12 条；2026-10-14 用户确认）：
  **`Tests~` 形态不采纳**——`~` 在 Unity 里只是「隐藏 / 不导入」，**不是「不分发」**（官方包布局文档 + 本机
  `Library/PackageCache` 旁证：第三方包的 `Documentation~` / `Samples~` / `Tests/` 都在磁盘上），而 `Samples~`
  那次的先例证明**本仓自己会跟着不编译**；更要紧的是**门禁会给假绿**——实测把 `Tests` 改名后双平台各报
  「总计 0 / 失败 0」而脚本退出码 0，退出判据会被假满足。**「把测试移出包目录」也不采纳**：代价 499 个文件与
  96 行文档引用，而收益只是消费方的导入与磁盘（**不是「少编译 45k 行」**——测试程序集在导入方本就不产出；
  「45k 行」按实测订正为 51,056 行）。
  **包在子目录不重构**：`?path=` 是 UPM 官方支持的用法，「不能直接当子模块**包**用」**≠**「不支持子模块」——
  包 README 的「方式二」已经给了 `file:../Submodules/XFramework/Assets/XFramework` 这条配方。两条各留可判定的重开判据。
  **裸消费方探针复核**（2026-10-14）：用 `file:` 引本仓包的工程里 `Library/ScriptAssemblies` 只产出
  `Venusy609.Xframework` 与 `Venusy609.Xframework.Editor` 两个程序集、零 `error CS`；**同一个工程里五个官方包
  （Input System / uGUI / Multiplayer Center / Test Framework / SBP）的 `Tests/` 也一个都没产出**，而
  `UnityEngine.TestRunner` 在位——「未列入 `testables` ⇒ 测试程序集不编译」由此证实是**全局规则**，不是本包特例。
  **仓根工具一处修复**：`Tools/run-tests.ps1` 全量跑 0 用例时原先退出 0（上条实测暴露的假绿缺口），现判失败；
  `Workflow.md` 的参数表同步。**使用方不受影响**：公开面与运行时行为零改动（包内新增的只有本记录）。

- 仓库形态五项设施逐项裁定（`FrameworkReview.md` §5.2 步骤 12 / §4.3-13；2026-10-14 用户确认）：
  **仓库根新增 `.editorconfig`** —— 把 `CLAUDE.md` 的编码规范落到编辑器（与 `.gitattributes` 分工：后者管仓库内容
  的归一，这一份管保存时怎么写）。内容按实测而非惯例：`[*.cs]` 钉 4 空格（全包实测 4,324 行 4 空格 / 0 行 Tab），
  **`[*.md]` 关掉尾空白 trim**（仓内有 5 处在用「行尾恰好两空格 = Markdown 硬换行」，trim 会改渲染），Unity 的 YAML
  同样关掉（Unity 自己会写回），`*.bat`/`*.cmd` 镜像 `.gitattributes` 保持 CRLF，JSON 刻意不钉缩进（仓内本来就混）。
  **Git 钩子 / Issue·PR 模板 / `CONTRIBUTING` 三项不做**——它们的预期用户是外部贡献者与来件，而实测 0 fork / 0 issue /
  0 PR；三条共用可判定的重开判据，与 `CI 成本与裁定记录` 的裁定联动（届时四项同轮重裁）。性能基准套件按原文仍「单议」。
  **仓库根另补了一份极简 README**（评估中发现：这个公开仓原本没有门面，GitHub 落地页是一张裸文件列表），只指路不列内容。
  **使用方不受影响**：五项全部落在包目录之外，包内一个字都没动（本记录是唯一例外）。

- 工作区行尾归一化（`FrameworkReview.md` §5.2 步骤 13 / §4.3-9 尾注；2026-10-14）：工作区残留的 **259 个文本文件**已就地重写为 LF，判据 `git ls-files --eol` 的 `w/crlf` 归零。
  它们是 2026-10-11 `.gitattributes` 落地**之前**、系统级 `core.autocrlf=true` 下 git 自身物化工作区留下的**冻结核**（此后零新增；`Tools/` 写盘、Editor 写盘、Unity 的 `.meta` 导入三条嫌疑均已实测排除，探针见 `维护向的工作流手册` §六）。
  **索引与入库内容零变化**：`.gitattributes` 一直保证入库为 LF，故本步**没有任何内容提交**，`git status` 全程干净。
  **使用方不受影响**：公开面与运行时行为零改动，归一的是开发机**工作区字节**这一处旧账（`.gitattributes` 管仓库内容、`.editorconfig` 管编辑器保存，本条补上第三处）。
  **未封堵的一处如实记录**：Unity 菜单新建的脚本仍会以 CRLF 落盘（`ProjectSettings/EditorSettings.asset` 的 `m_LineEndingsForNewScripts = 0` / OS Native，2026-10-14 用户裁定保持不动）——只影响工作区，不影响入库内容；判据与再归一化配方同在 `Workflow.md` §六。

- 会话级复位手工验收已执行并结案（`FrameworkReview.md` §5.2 步骤 1 / 步骤 14；2026-10-14）：
  关域重载连跑两遍 PlayMode 全量——**第一遍绿、第二遍红**，红在 XSettings 的跨会话静态态累积
  （门面 `Initialize` 告警早退、重放注册表跨会话累积、`PauseNotifier` 计数不归零；两遍唯一不对称是
  `[SettingsManager]` 27→28）。同轮修复（见 `### Fixed` 的 `#### 会话级复位` 与 `### Changed` 的
  「重放登记改为会话级」）后重跑：两遍全绿、`Unhandled log message` 0、两遍逐类完全相同。
  **这份验收自证了存在价值**：相关用例在全新 batchmode 进程里全绿，缺陷只在跨会话下显形——
  也就是脚本表达不了的那一类。**使用方不受影响**：修复只改静态态的生命周期，公开面与运行时行为零改动
  （**订正（2026-10-17）**：唯一可见后果并非「会话 1 遗留的订阅者不会因会话 2 的重放复活」——边界只清
  登记表、不清句柄订阅流，旧订阅者仍会随句柄在第二会话的重新入表而被重放；详见 `### Changed` 的
  「重放登记改为会话级」与模块 README 的已知限制，SET-004）。

- 主线程断言三份副本经评估**不抽公共防线**（`FrameworkReview.md` §4.2 / `Roadmap.md` §五 销账；2026-10-15）：
  Message（`MainThreadGuard`）/ Pipeline（阶段写入）/ Asset（门面入口）三份同形副本，**共同部分只有三行**
  （`PlayerLoopHelper.IsMainThread` 判定 + `#if UNITY_EDITOR` 门控 + 一个布尔首错闩锁 + 一次 `LogError`），
  而**有难度的部分逐模块不同**——断言绑在哪个语义位置（入口契约 / 越线程写入 / 门面入口）、文案、以及
  **闩锁寿命**（每 broker · 每次运行 · 每会话）。判据：**抽公共防线的门槛是「共享的那部分是不是有难度的那部分」**
  （同款先例：`XEvent` 抽取时刻意留下的两份 internal 副本，「十行适配器不值得跨模块绑定」）。
  **改为补齐缺口**：Asset 那份守卫**此前零覆盖** ⇒ 新增 `Tests/Editor/Asset/AssetMainThreadGuardTests`
  （4 例：负向控制 / 闩锁 / 真接线 / 「会话复位让提示重新具备」——那行赋值此前没人钉）；其闩锁字段是
  唯一留在 `#if UNITY_EDITOR` 外的 ⇒ 对齐（**无观测面，如实标注**）。
  **使用方不受影响**：三份断言的对外行为一字未变，公开面零改动（基线未动）。

## [0.3.0] - 2026-10-11

> **本版涵盖 `[0.2.0]`（2026-08-20）之后的全部未发布内容**：基础设施、资源与内容、存档与设置三面均有新增与修复，其中带「破坏性」标记的条目 30 余条。
> **升级前先看 `### Changed` 顶部的三张迁移表**——「旧写法 → 新写法」逐条列出，编译器抓不到的行为变更另列一张，请照单核对。
> **本版没有 `Deprecated`**：破坏性变更一律立即生效（编译器报错级），不留过渡别名——它们大多从未随任何版本发布过。

### Added

#### Bootstrap

- **启动引导模块 `XBootstrap`**：`IBootstrapStage`（= Pipeline 的 `IPhaseStage` + 同步 `Shutdown`）与 `Bootstrap` 静态门面（`Register` 显式登记 / `RunAsync` 相位装配运行 / `Shutdown` 逆序清理）。框架内置的引导阶段归位到各自模块——`AssetBootstrapStage` 落 `Runtime/Asset/`，Data / Save / Localization 同理，框架不再在自己的目录里装着别人的服务。两点相对旧启动路径的实质改进：`RunAsync` 首次提供 `CancellationToken`（旧 `StartupAsync` 不可取消），且失败与取消会**抛出**而非只留日志（旧实现只订阅 `OnProgressUpdate`、从不订阅 `OnFailed` / `OnCancelled`，阶段失败时调用方无从感知）。登记按**实例**去重而非按类型——登记表是「初始化步骤列表」，参数化的阶段用同一类型登记多次是合法的；`RegisterDefaults()` 例外，它按类型跳过已存在的内置阶段，故可重复调用

- **Bootstrap 新增 `Unregister(IBootstrapStage)` / `Unregister<T>()`：替换内置阶段不再依赖调用顺序**。此前唯一姿势是「先 `Register` 再 `RegisterDefaults`」，而 `GameLauncher.Awake` 会无条件登记默认组合，与使用方自己 `Awake` 的先后由 Unity 决定——反序时两者都被登记，门面幂等守卫只留一条告警、**自定义 options 被静默忽略**。`Unregister<T>()` 按**精确类型**移除全部匹配项（与 `RegisterDefaults` 的跳过口径一致：派生类不被连带移除、传接口类型不命中）。README 的 ⚠ 替换指引改写为 `RegisterDefaults → Unregister<T> → Register`，并写明「派生类不被 `RegisterDefaults` 识别为已有 Save，会双份登记」

- **新增 `BootstrapPhases` 相位常量**（Asset=0 / Data=3 / Save=4 / Localization=90 / UserStart=90）：相位号此前是四个阶段类里各写一份的字面量，约定只活在两份 README 表格中，第三方只能照抄数字。四个内置阶段改引用常量；`BootstrapStageTests` 补「常量与阶段同源」守卫（防改了常量忘改阶段的单边漂移）；README 相位表改列常量名并写明 **0–89 由框架保留**（「1/2 空闲」只是现状、不是承诺），需要精确插进框架阶段之间的场景用 `Unregister` 换掉内置阶段自行编排。Pipeline README 的重复相位表改为指向 Bootstrap（该约定的唯一真相）

- **Bootstrap `RunAsync` 重入守卫**：每次调用都会装配一条**新**管线（管线自身的重入守卫是实例级的），并发两次会让同一批阶段跑两遍。重入现在打警告并立即返回——被忽略的那次**不代表启动完成**（要等启动结束应共享第一次调用的任务），README 已写明。`Register`/`Unregister` 在运行中照常执行但打警告，说明两条反直觉后果：运行中登记本轮不执行（装配期已快照）但会被 `Shutdown` 扫到；运行中注销本轮仍执行但拿不到 `Shutdown`。**改前必红**：`Expected: 1 But was: 2`（旧实现真的跑了两遍）

- **`GameLauncher.ConfigureStages()` 扩展点：带配置启动不再需要放弃这个组件**（**同版内 `GameLauncher` 又被拆为抽象底座 + `DefaultGameLauncher`**，成员面以 `### Changed` 那条为准）。此前它是「zero-config or nothing」的入口——`Awake` 无条件 `RegisterDefaults()`，要用 `SaveOptions` 之类的参数就只能整个弃用它、自建启动流程。现 `Awake` 改调 `protected virtual void ConfigureStages()`，其**默认实现就是原来那句 `RegisterDefaults()`**（默认行为逐字不变，纯增量）：调 `base` 之后 `Unregister<T>()` + `Register(你的实例)` 替换内置阶段，不调 `base` 则完全不登记默认组合。只加这一个成员——`Start` / `OnDestroy` 刻意不改 `virtual`（`async void Start` 会诱使覆写者漏调 `base`）。PlayMode 用例 3 条覆盖三种姿势（默认登记 Asset/Data/Save 三件 / 不调 base 登记表为空 / 调 base 后替换 Save 只剩自定义那一个）。**同一轮评估并否决了「把它做成框架的配置与编排核心」**：配置的形状是代码不是数据（`AssetInitOptions.RemoteServices` / `SaveOptions.CryptoProvider` 是服务实例）、一半配置的生效时机早于任何场景（`LogOptions` 走 `SubsystemRegistration`、File 域根预热走 `BeforeSceneLoad`），且两处沿革刻意不挂靠它——理由与「重开判据」的同步归档在 `Bootstrap 模块的维护向记录` 与 `Roadmap.md`

- **`GameLauncher` 声明式配置字段：Inspector 里填、`Awake` 里注入、`Start` 里跑管线**。起于用户连问三轮「为什么不在 GameLauncher 声明所需配置项再注入各模块」——重新推导后我此前两条反对意见不成立（Unity 的标准回答是 ScriptableObject provider，服务实例可以进配置面；真正早于场景的只有 Log，其余模块的配置都在 `Initialize` 时消费，场景组件覆盖得了），剩下的只是形状约束：6 个 options 类型都没有 `[Serializable]`，字段面只能**逐项重述**而非引用 options 对象（改名/删除有编译器兜底）。落地五组字段：Asset（包名 / 运行模式 / 低内存回收）、Save（存档版本）、UI（Canvas 根）、Input（资产 / 初始 ActionMap）、Localization（默认语言 / 语言表 `TextAsset`）。**全为默认或留空时，登记表与行为与零配置逐字相同**；留空的分组不登记阶段（不替使用方初始化他没要的模块）。服务实例（`RemoteServices` / `CryptoProvider` / `DecryptionServices`）与 `LogOptions` 仍不进字段面，理由写进类 XML 与 README

#### Pipeline

- **通用管线模块 `XPipeline`**：通用异步阶段编排——阶段实现 `IPipelineStage` 只声明「做什么」（`Name` / `Weight` / `ExecuteAsync`），管线负责「按什么顺序、报多少进度、失败怎么办」；另附**相位分组**这一声明式装配形态（`IPhaseStage` 声明相位号，`Pipeline.BuildPhaseGroups` 一键装成「每相位一个 `ParallelStage`」，同相位并行、相位升序串行；启动引导 `Bootstrap.RunAsync` 的管线即由此装配）。实例经 `Pipeline.Create()` 创建、即用即弃，非全局单例，模块对框架其它模块零依赖。三处取向值得记：① **事件驱动进度**——阶段经 `PipelineStageContext` 主动写入（进度/描述/状态/任务名），写入点同步触发加权聚合 `Σ(w·p)/Σ(w)`（已完成记全权、执行中记 w·p、失败阶段权重移出分子与分母、`Weight = 0` 不占进度）与广播，管线不轮询、不持有帧泵；广播按「进度变化 ≥1% 或描述/任务名/状态变化」节流、终局必播，载荷 `PipelineProgress` 实现 `IProgress<PipelineProgress>` 可直接转交下游（也可当快照持有者：传一个实例进 `RunAsync`，回调后读它即最近一次快照）；② **单一执行契约**——阶段是唯一执行单元，异常 → 置 `Failed` 并写入描述、`OperationCanceledException` → 视为取消（不报失败）、正常返回未写终态 → 契约兜底补 `Completed`，完成/失败/取消三路终局互斥，且阶段沉降后 token 已取消即判取消（阶段对取消无感并正常返回也不会被误报成完成）；③ **容器表达拓扑**——顶层固定串行，组内并行归 `ParallelStage`、组内串行子段归 `SequenceStage`，二者可任意嵌套成「串行/并行容器树」，组内任一子阶段失败即置组失败并报出首个失败子阶段的诊断（并行组同时取消在途兄弟）。每阶段可选超时（`AddStage(stage, timeoutSeconds)`）是**乐观超时**：触发时取消该阶段并置 `Failed` 经 `OnFailed` 报告，但不等待其沉降——不响应取消的挂起阶段被放弃后仍在背景运行，只是其后续上下文写入不再影响管线（与 Polly 的 `TimeoutStrategy.Optimistic` 同语义）。两条健壮性契约：四个事件的**订阅者异常被隔离**（记 `[Pipeline]` LogError 后继续——UI 回调的 bug 不会把无辜阶段打成失败、不会把完成/取消改报成失败）；**终局可从拉取面读**（`IPipeline.Status` 为 `Idle`/`Running`/`Completed`/`Failed`/`Cancelled`，与 `FailureReason` 在运行结束后保持终局值、仅 `Destroy` 回落，不必订阅终局事件自己记账）

#### Asset

- **Asset 低内存自动回收**：`AssetInitOptions.AutoReclaimOnLowMemory`（默认 true）监听 `Application.lowMemory`，自动清池并卸载全部包中未使用资源

- **Asset 批量加载**：`AssetManager.LoadAllAsync<T>` 按序返回句柄数组，单项失败为 default 句柄，取消时自动释放已完成项

- **UI 面板资源真释放**：`UnloadPanelAssetAsync(assetPath)`，配套 Asset 侧新增能力接口 `IAssetPoolController`（`ClearPool` / `ClearAllPools`）。此前 `UnloadAsset` 声称「释放内存」而只清一个记账字典——查证后确认这不是实现偷懒而是能力缺口：`DestroyAllPooledInstances` 是 private，且回池时实例保留 `AssetHandle` 保活资源，于是池里只要有一个闲置实例，该预制体的引用计数就不会归零。能力接口与 `IAssetManager` 分开，故不破坏第三方自定义实现

- **Asset 门面主线程契约（断言 + README 一节）**：门面的初始化协调是无锁的「检查-再赋值」序列（创建者选举、等待者登记、代际递增），此前**一字未提**也没有任何机制——两个线程同时进入会造出两个 `AssetManagerImpl`（前者不被释放，YooAsset 包引用泄漏）。现 `InitializeAsync` / `SetInstance` / `Destroy` 三入口加 Editor-only 断言（越线程记一条 `[AssetManager]` 前缀 Error，首错闩锁每会话一次；Release 下整段不编译），README 新增「线程契约」节写明「为什么」与「Release 下无保护」。主线程检测复用 UniTask 的 `PlayerLoopHelper.IsMainThread`（同 Message 的 `MainThreadGuard`、Pipeline 的阶段写入断言）；本模块**内联本地副本**而非引用 `XMessage.Internal`（模块边界规则，先例 UI 内联适配器）

- **Asset 池语义用例（PlayMode，9 条）**：池路径**不依赖 YooAsset**（`DestroyInstance` 不调 `EnsureInitialized()`，`SetHandle` 接受 `default` 句柄），却一直是**可测而未测**——回池、去重、池满、清池、容量下限、默认容量、活跃计数全部补齐

- **Asset 门面完备性守卫（第九个采用者）**：`IAssetManager` 的 32 个声明成员全部有同名转发（守卫不是回归），**另加一条能力接口断言**（`IAssetPoolController` 的两个成员也必须在门面上——超越八个先例之处，理由：能力接口同样是公开面）。写之前用 BCL 实测确认 `typeof(I).GetMethods()` 只返回接口**自己声明**的成员，故 `IDisposable.Dispose` 不会误报；写完做**负向控制**（把零覆盖的 `PreDownloadContentAsync` 改名 → 守卫点名报红）。另补 16 条零覆盖用例（12 条转发 + 池能力两条分支 + `SetInstance` 二次/在途）

- **`AssetInitOptions.DecryptionServices`：加密资源包的出口**（此前**无法给 bundle 加密**——YooAsset 的文件系统工厂收 `decryptionServices`，而框架一个参数都没接）。新增 public `IAssetDecryptionServices` + `AssetDecryptResult`，镜像 YooAsset 的 `IDecryptionServices`（五个成员，`DecryptFileInfo` 展开成三个参数，返回类型全是 Unity/BCL 类型），公开面不出现 YooAsset 类型——与 `IAssetRemoteServices` 同一包法，第三方接口变更由 internal 适配器吸收。**Offline 与 Host 两条路径都接线**（不接的话编辑器/单机模式读不了加密资源）。`packageRoot` 刻意不开（YooAsset 里它是**每个文件系统各一个**），写进 README 已知限制

- **`AssetBootstrapStage(AssetInitOptions options = null)`** 与 **`LocalizationBootstrapStage(string defaultLanguage, TextAsset table)`**：字段面要把配置喂给阶段，这两个阶段此前接不住（Asset 阶段写死 `options: null`；Localization 只有字典入口）。语言表的解析放在 `ExecuteAsync` 而非构造期——格式错误要沿引导管线的标准失败路径报出来；`LanguageAssetLoader.ParseJson` 因此从 private 升为 internal（仓内既有先例：把解析抽成 internal 纯函数以便复用/单测）

#### Update

- **Update 三个派发时机**：新增 `ILateUpdateable` / `IFixedUpdateable` 与 `UpdateManager.RegisterLate` / `RegisterFixed`，并把生命周期回调抽成三者共用的 `IUpdateLifecycle`（`IUpdateable` 继承它，既有实现零改动）。每时机一套独立调度器（各自的桶、帧计数、切片相位、暂停状态），因为共用一个实例会让两种时机的切片相位互相干扰。固定步长时机的时间基准是 `Time.fixedTime`——那里的档位以**固定步**计（默认 0.02s 一步），因此它没有时间轴参数（Unity 固定步本就随 `timeScale` 停摆）、也不能由变步长时钟驱动

- **Update 双时间轴与暂停**：新增 `UpdateClock`（time + unscaledTime + isPaused）与 `UpdateTimeMode`（Scaled / Unscaled），桶按「时间轴 × LOD」二维组织。需要「暂停期间仍运行」的逻辑（暂停菜单、UI 动画、手柄振动到期）终于有正规表达方式——此前生产代码里已经出现绕过（`InputSystemProvider` 自己读 `Time.unscaledTime`）。配套 `Pause` / `Resume` / `IsPaused`；节点树侧可经 `IUpdateTimeMode` 声明自己的轴

- **Update PlayerLoop 自驱动**：驱动注入 `Update` / `PreLateUpdate` / `FixedUpdate` 三个阶段，不再要求场景里存在 `GameLauncher` 或任何 MonoBehaviour；`IsDrivingPlayerLoop` 可查询注入状态，注入失败打 `LogWarning`（门面是宽容语义、不会抛异常，不留痕的话故障表现只是「静止」）。注入基于 `GetCurrentPlayerLoop` 且只插入不替换，因此与 UniTask 等同样靠注入工作的库共存（有断言钉住）

- **Update 补齐长周期档位**：`UpdateLOD`（同版内改名为 `UpdateTier`，见 `### Changed`）增加 `Tier6`（约 1067ms / 64 个固定步）与 `Tier7`（约 2133ms / 128 个固定步）。原阶梯封顶在 `Tier5`，而它在 30fps 下是 1.07 秒、144fps 下只有 222ms——「每秒醒一次」这类需求此前无法表达，只能自己在节点里按 `deltaTime` 累加。档位是 2 的幂的等比梯子，桶数组尺寸随 `UpdateLOD.Max` 自动推导，故补齐只是加枚举成员

- **`UpdateManager.AutoDriveEnabled` 公开**：此前它是「仅供测试关闭」的 internal 钩子，而注入是 `[RuntimeInitializeOnLoadMethod]` 自动执行的——项目想自己驱动（确定性回放等）没有公开途径，而手动 `Tick` 会与已注入的驱动器每帧各派发一次。语义无需新代码（三个驱动委托本就各自 `if (!AutoDriveEnabled) return;`），只是把它公开、重写文档（置 false 后 `IsDrivingPlayerLoop` 仍为 true，它只报注入状态）

#### Message

- **异步发布 `PublishAsync`**：同步投递先行的基础上等待全部异步处理器完成，`MessagePublishStrategy` 可选 Parallel（默认）/ Sequential；`Publish` 仍以 fire-and-forget 触发异步订阅

- **缓冲通道淘汰**：`EvictBufferedChannel`（类型级 / 按键）、`EvictBufferedChannels`、`TrimEmptyChannels`；订阅清零的通道自动回收，缓冲通道需显式淘汰（保留「订阅前发布可重放」语义）

- **按 Key 跨消息类型淘汰**：`EvictBufferedChannels<TKey>(key)` 一次淘汰该 Key 在**所有**消息类型下的缓冲通道。此前淘汰只能按消息类型进行，实体销毁处要为其参与的每种带 Key 消息各写一次调用，且每新增一种消息都得回去补一处——漏掉的那处即「复用同 Id 的新实体重放到旧值」的静默事故。新 API 与实体的生命周期同形，README「内存管理」的推荐写法已同步改为按 Key 淘汰

- **运行统计**：`MessageManager.GetStats()` / `GetChannelStats<T>()` 返回 `readonly struct` 快照（通道数、订阅数、缓冲通道数、发布/请求次数等），零分配，用于排查订阅泄漏与缓冲内存驻留

- **过滤器管理**：`RemoveFilter` / `ClearFilters`

- **请求处理器注销**：`Unregister<TRequest, TResponse>`

- **请求-响应补全**：`HasHandler<TRequest>()` 探测响应方是否已注册；`TryRequestAsync<TRequest, TResponse>` 在一次调用内完成「查 + 发」，未注册处理器时返回 `(false, default)` 而不抛异常。模块定位是跨模块解耦，请求方本就不该假定响应方已就绪，此前只能靠 try/catch 接 `InvalidOperationException`——既是用异常做控制流，也无法与处理器内部抛出的同类型异常区分。`false` 只表示未注册，处理器自身的异常照常上抛

- **节点订阅扩展**（**该扩展面随节点系统在本版内被整体删除**，见 `### Removed`「删除节点系统」）：`NodeExtensions.SubscribeAsync(this BaseNode, ...)`；`Subscribe` 去掉 `where TMessage : class` 约束，框架内置 struct 消息现可用

- **节点缓冲订阅**（**同上，随节点系统删除**）：`NodeExtensions.SubscribeBuffered<TMessage>(this BaseNode, handler)`，订阅即收到最近一条并自动绑定节点生命周期。刻意只提供这一个缓冲重载——带 Key 与带过滤条件的变体一旦同时存在，`(TKey, Action<TMessage>)` 会捕获本意为过滤器的委托实参，使该调用退化为 CS0121 或被静默解析为「把谓词当 Key」

- **发布/订阅入口的主线程断言（Editor）**：`Publish` / `PublishAsync`（含键值重载）与全部 12 个订阅重载的入口在 Editor 下检查是否位于 Unity 主线程，越线程调用记一条 `[Message]` 前缀的 Error——每个 broker 只报首错（`Clear()` 换新实例后重新具备提示能力），Release 构建不编译该断言、零开销。此前这条契约只写在 README 里，而 `PublishAsync` 的旧文案还暗示「不在主线程调用也行，只要自行切回主线程」：文档说一套、成员文档暗示另一套，入口处却没有任何机制。**覆盖边界（三处刻意不判，均已写进文档）**：① 退订路径——令牌可在任意线程被取消（谁取消令牌，退订就在谁的线程执行）；② 4 个 `SubscribeAsync` 在令牌已取消时的早退路径——它不触碰任何共享状态；③ 处理器内部的线程行为。断言实现为 `internal static MainThreadGuard`，`ref` 闩锁由 broker 实例持有，故测试可用局部变量直接驱动、零全局状态。配套 5 条 EditMode 用例（守卫本体 2 条 + 接线 3 条），其中「主线程调用不得记日志」那条是**负向控制**——缺了它，一个恒打日志的守卫也能让越线程用例通过

- **`SubscribeBuffered` 补上键值带过滤的扩展重载**：`IMessageSubscriber` 扩展面此前只有三个缓冲订阅重载（类型级、带 Key、类型级带过滤），缺「带 Key + 带过滤」这一个——而静态面四个齐全，README 也明写「类型级与带 Key 的 `Subscribe`/`SubscribeAsync`/`SubscribeBuffered` 均有含过滤条件的扩展重载」，第三方照 README 写 `this.SubscribeBuffered(key, filter, handler)` 直接编译不过。加重载前先做了**重载决议的编译复现**（本仓纪律：涉及重载决议不靠纸面推理）：镜像扩展面 12 个重载形状 + 14 个调用形状全部解析成功（含最可疑的两参 `SubscribeBuffered(pred, handler)`，它仍正确落到类型级带过滤版；相邻 `Subscribe`/`SubscribeAsync` 族不受影响），且同一程序在人为造出二义与形状不匹配时确报 CS0121 / CS1503——证明验证程序本身有判别力。配套 2 条用例（销毁令牌自动退订、过滤对重放与实时同时生效），它们在改动前**连编译都过不去**。另补 2 条契约用例：扩展面判空（参数名不得在转发时与 `handler` 调换）、以及把复现程序里的解析结论钉进仓库——两参 `(filter, handler)` 必须落到类型级带过滤版而非被 `(TKey, Action<TMessage>)` 捕获（复现程序不入库，结论会随时间腐化）。**与 `[0.3.0]` 里那条「节点缓冲订阅刻意只提供一个重载」的取舍无关**：那条担心的是 `(TKey, Action<TMessage>)` 捕获本意为过滤器的委托实参，而本次编译复现实测该担心在本方法集上不成立（两参调用仍落到过滤版；键值变体是 3 参，不参与两参调用的竞争），且节点扩展已随节点系统一并删除

- **运行统计支持按消息类型下钻 `CopyTypeStats`**：`GetStats()` 只给总数、`GetChannelStats<T>` 要先知道类型或 Key——而这两个 API 的既定用途恰恰是排查订阅泄漏，「是哪个消息类型在泄漏」原本答不出。新增 `MessageTypeStats`（消息类型 + 通道数 / 同步订阅数 / 异步订阅数 / 缓冲通道数 / 键值通道数）与 `MessageManager.CopyTypeStats(List<MessageTypeStats> buffer)`：**一行一个消息类型**（该类型的类型级通道与全部键值通道**归并**成一行），调用方持有缓冲区、进入即清空、返回写入行数、零分配——与 UI 的 `CopyPanels` 同惯例。行序未定义（受内部字典遍历序影响），要排名请调用方自行排序；最坏复杂度为 O(键值存储数 × 消息类型数) 而非 O(通道数)，文档如实写明，没有照抄「统计为 O(通道数)」那句。归并查重与键值计数用线性扫描而非字典：字典要么每次调用分配（与「调用方持有缓冲区」的承诺冲突）要么得引入池依赖，而消息类型数是个位数、本方法又不在每帧路径上。顺带把 `GetChannelStats<TMessage>` 里内联的「按消息类型汇总键值通道数」抽成共用助手，并订正两处会误导的过期注释：`MessageStatsAccumulator.BufferedChannelCount`「持有重放缓存的通道数」（上一批订正过同类措辞、这里漏网）与 `KeyedChannelStore.Count`「当前 Key 的通道数量」（它返回的是**全部 Key**）。**口径差别写进了文档**：`GetChannelStats<TMessage>()` 的四个计数只算类型级通道，`CopyTypeStats` 的一行是类型级 + 全部键值通道的合计——两者是两种视图，`KeyedChannelCount` 是唯一共有字段且共用同一份扫表实现，不把前者改成合并视图（那会静默放宽既有公开语义）。6 条新用例，其中两条是设计价值所在：**各行计数之和必须等于总线级计数**（两套统计互为校验，任一侧口径被改先红）与**反复调用零分配**（预热后测 100 次，`GC.GetAllocatedBytesForCurrentThread` 差值为 0（**该尺子后经实测恒返回 0，此结论不成立**，见 `### Documentation` 同条），抄 UI 侧同一写法）

#### Reactive

- **Reactive 契约测试补口（8 处「文档已承诺但无人验证」的缺口）**：`Subscribe(null)` 抛 `ArgumentNullException`、无参构造取类型默认值、`Select(null, selector)` 抛 `ArgumentNullException`、**`Select` 在源已释放时抛 `ObjectDisposedException`**（上一版才写进文档的行为，此前既无测试也无文档）、派生值的「`Dispose` 后再订阅抛异常」与「重复 `Dispose` 幂等」（可写类型两条都有、只读类型一条都没有），以及 `TryWriteValue` 失败时 `actual` 为 `default`——原用例用 `out _` 把这个明确承诺丢掉了，现改为捕获后断言。另补两条用反射钉住的**接口不变量**：`IReactiveProperty<T>.Value` **无 setter**、`ReadOnlyReactiveProperty<T>` **不**实现 `IReactivePropertyWriter<T>`。后两条与 `UIFacadeCompletenessTests` 同类——把「只读接口不暴露写」「派生值不获得写能力」这两条设计承诺变成改坏即红的断言，而不只是注释里的说法

- **Reactive 补诊断面 `SubscriptionCount` 与 `ToString()`**：本模块此前**零内省**——订阅泄漏在运行时无声无息，却没有任何手段能发现它。`ReactiveProperty<T>` 与 `ReadOnlyReactiveProperty<T>` 各加一个只读的 `SubscriptionCount`（引擎的 `EventStream.SubscriptionCount` 就在同一程序集里，一行透出），以及诊断用的 `ToString()`（`ReactiveProperty<Int32>(50)` 形态，与 Settings 的 `SettingRef.ToString()` 同形——那个 `ToString` 存在的理由就是让注册表能记下是哪个句柄失败）。两者都标注「仅供排查，不要拿来做逻辑分支」。与既有工具同族：Message 的 `MessageBusStats` / `GetStats()`（其自述用途正是「诊断订阅泄漏」）、UI 的 `UIStateSnapshot` / `DumpState()`。补 5 条测试，其中一条把上一版 README 才开始告诫的「`Select` 返回值被就地丢弃即永久订阅」**变成可观察的事实**——断言丢弃派生值后源的订阅数确实为 1；另一条用订阅数替代原先的调用计数来验证「立即回调抛异常后订阅已清理」，比原来的写法直接得多

- **UI 绑定接收者放宽到 `IReactiveProperty<T>`**：`UIBinder` 的 7 个绑定方法与 `UIPanelBinding` / `UIPanelBase` / `ViewModelBase.CreateReadOnlyProperty` / `ReadOnlyReactiveProperty.Select` 的接收者由具体类放宽到接口（对调用方源码兼容）。此前绑定 API 只吃具体类型，任何非框架实现都无法接入。必须是**替换**而非新增重载——两版并存时具体类更精确、永远胜出，接口版会沦为死代码

- **UI 双向绑定**：新增能力接口 `IReactivePropertyWriter<T>`（继承 `IReactiveProperty<T>`，只读接口本身不变），`ReactiveProperty<T>` 与 `SettingRef<T,TField>` 各自实现，故任何第三方实现都能接入。`UIBinder.BindTwoWay` 支持 Slider / Toggle，内部以重入守卫阻断回灌，并在目标规范化写入值（取整、钳制）时回填控件。约定式绑定补上 `sld_` / `tgl_`（此前 `UIPanelBinding` 缓存了这两类组件却无人消费），`btn_` 走新增的 `BindClick`

- **`ReactiveProperty<T>` 的相等比较器可注入**（上一轮清点记下的三项之一）：去重（相同值不通知）此前写死 `EqualityComparer<T>.Default`——浮点容差、大小写不敏感等自定义相等语义做不到，而这正是本模块对外的行为承诺之一。新增构造重载 `(T initialValue, IEqualityComparer<T> comparer)`（原 `(T)` 重载签名不变，转调新重载），`Select` 增加可选 `comparer` 参数（派生值的去重此前写死默认比较器）；传 `null` 等价默认。用例 3 条（容差比较器去重 / 默认比较器对照 / Select 派生同理），红基线为编译级

- **新增 `XReactive.ReactiveList<T>`（集合型响应式 v1）**：可订阅增删改的列表 + 只读接口 `IReactiveList<T> : IReadOnlyList<T>` + 零 GC 的 `readonly struct` 变更载荷 `ReactiveListChange<T>`（`Kind` / `Index` / `Item` / `OldItem`）+ 词表 `ReactiveCollectionChangeKind`（Add / Remove / Replace / Reset）。起因是一次提问（`ReactiveProperty<T>` 的 `T` 是 `List` / 数组会怎样——去重退化为引用相等、就地修改零通知、`Value` 是活引用）：这类「一组东西变了，告诉我」的监听（怪物 / 弹幕 / 单位）本仓一直靠手写（`UIManager.Panels` + 三条消息），即同一个轮子被每个模块各写一遍。四条与直觉相左的决定及理由：
  **① 订阅不做立即回调**——与同模块 `IReactiveProperty<T>.Subscribe`（契约是「订阅时立即同步回调当前值」）**语义相反**，因为列表订阅的是**变更流**（不重放、也无法重放），语义对齐 `IEventStream<T>.Subscribe`；初始化姿势是「订阅后立即读一次列表」（主线程同步，两步之间不可能有变更）。
  **② 索引只在当次通知内有效**——载荷是事件的即时描述，不是可重放的操作日志；`Clear` / `Sort` 用一条 `Reset` 兜底（「整表变了，重读」），要全量状态就重读列表，别拿增量拼镜像。
  **③ 不做批量与算子面**——没有 batch 作用域 / `AddRange` / `RemoveRange` / `RemoveAll` / `Select` 派生；批量装载的正解是构造（静默、不发通知），对已订阅列表连续增删就是 N 条通知。
  **④ 无变化不发通知**——空表 `Clear`、与旧值相等的索引器赋值、元素少于 2 的 `Sort` 都静默；但**排序即使次序未变也发 `Reset`**（检测次序变化要多跑一趟全量比较，不做）。
  订阅经 `EventStream.Create` 裸透传（无立即回调即无注册期异常要回滚，比 `ReactiveProperty.Subscribe` 少一层 try/catch 补偿），登记自动落进 `SubscriptionTracker` 仅有的两个登记点之一——不自建订阅表，否则泄漏断言对它静默失明。实现期修正一处立项时的推断：`foreach` **无论直接用还是经接口都会**装箱枚举器（原以为只有接口路径会），文档按实际写明「热路径用索引 `for`」。用例 34 条（含改前必红抽查）。

- **新增 `XReactive.ReactiveDictionary<TKey,TValue>`（按键寻址的集合型响应式 v1）**：可订阅增删改的字典 + 只读接口 `IReactiveDictionary<TKey,TValue> : IReadOnlyDictionary<TKey,TValue>` + 零 GC 的 `readonly struct` 变更载荷 `ReactiveDictionaryChange<TKey,TValue>`（`Kind` / `Key` / `Value` / `OldValue`）；索引器 `set` 是 upsert（新键 `Add` / 已有键 `Replace` / 等值静默且不替换实例），`Add` 键重复抛 `ArgumentException`（BCL 镜像），`Remove` 未命中静默，`Clear` 非空才发一条 `Reset`。**三处与 `ReactiveList<T>` 不同**：
  **① 无索引哨兵**——`TKey` 没有通用非法值，`Reset` 的 `Key` 是 `default(TKey)`（文档明写「不要读」）；
  **② 键比较器可注入**（构造重载 + `Comparer` 属性）——依据是仓内**已发生过**的审计 E 类事故（Lock 的引用类型键走默认 `Equals`，两个实体静默合并成一把锁）；
  **③ 键比索引稳定但条目会被移除**——重入后果按此重写（嵌套那一轮可能已把该键移除，外层载荷随后照发，此时按键去查已查不到）。
  **两处刻意与第三方分歧**（对照 UniRx `ReactiveDictionary` 与 Cysharp `ObservableDictionary`——源码一手读取、**仓内不可复验**，按转述标注）：它们都不做值相等去重、`Clear` 都始终发；本仓取「无变化不发通知」家族规则——等值 upsert 静默**且不替换实例**（换来可测的不变量「**有通知 ⇔ 状态确实变了**」，正面回答审计 F 的双观测面判据）、空表 `Clear` 静默。其余取向与列表一致：单流 struct 载荷、订阅不做立即回调、不做 batch / Range（批量装载走构造）、遍历顺序**显式写明不承诺**、`foreach` 装箱如实写明、零 GC。**明确不做**：键的生命周期自动移除（销毁令牌绑定）——两条硬阻断：`IDestroyCancellationToken` 在 Message 模块（会新增 `Reactive → Message` 依赖）、令牌回调跑在**取消者线程**（与全族主线程专用冲突）；重开判据三问记在模块文档。构造决议用一次性探针实测（不留仓）：5 个构造调用全部按预期解析；负向控制 `new ReactiveDictionary<string,int>(null)` → **CS0121**（编译级报错、非静默，同 D2 先例）。用例 39 条（含两处改前必红抽查）。

- **立项依据与前一轮不同，值得记住**：`ReactiveDictionary` 的立项**不是**「回填既有设施」——逐条核对后仓内 5 处「键 → 值 + 通知」的手写设施 **0 处可被替换**（`LockManager._locks` 是**键 → 集合**的三级结构且带聚合边沿与销毁绑定、`SettingRefRegistry<T>` 根本不是键值表、UI 的公开读面是栈序列表、Localization / Config 是整表级 / 类型级广播），依据是前瞻性使用方场景（实体 id → 状态 / 槽位 → 内容 / 叠加状态 → 层数 / 资源 id → 计数）。这条差异写在 `Roadmap.md` §2.5 与 `Modules/Reactive.md` 的沿革里，免得下一轮拿 §2.4 的论证来套。

#### Localization

- **`LocalizationManager.SetPlaceholderFromKey(name, localizationKey)`：按语言解析的键值占位符**。全局占位符存的是**字面量**，所以值本身需要翻译时只能写成 `SetPlaceholder("Guild", Get("guild_legendary"))`——取的是注册当时那个语言的值，切语言后模板更新了、**嵌进去的值还是旧的，没有异常也没有日志**。新入口把翻译推迟到替换发生的当下：注册一次，跟随语言。四条语义各有用例钉住：**注册一次、跟随语言**（替换时经 `GetRaw` 按当前语言 + 回退链解析）；**解析规则与 `Get` 同一条**（两边都没有时返回那条表项的键本身，便于发现漏配）；**单趟、不递归**（替换进去的值不再扫）；**一个名字只有一种含义**（字面量与键值绑定两张表互斥，后注册的覆盖前者）。实现上 `ReplacePlaceholders` 的早退从「`_placeholders` 为空」改成「**两张表都空**」——否则注册了键值绑定却不扫描；`Dispose` 两张表成对置空；门面同步加转发（漏了会被上一轮的 `LocalizationFacadeCompletenessTests` 当场判红）。**验证的看点不在「改前必红」**（新 API 不存在时程序集本就编译不过，那是平凡的），而在语义用例的判别力——做了三次**负向控制**，各只挂一条、失败信息直指语义：「替换时解析」改成「注册时求值」→ `Expected: "Guild: Legendary Guild" But was: "Guild: 传奇公会"`（正是真实症状）；对替换进去的值再扫一遍 → `Expected: "内层：{Inner}" But was: "内层：会被替换"`；去掉两表互斥 → `Expected: "来自表的英文名" But was: "字面量"`

#### File

- **File 原子替换与一代备份**：改用 `File.Replace` 一步完成「替换正式文件 + 保留一代 `.bak`」，平台不支持时降级为三步移动。原实现以「删正式文件 → Move」实现替换，两步之间进程被杀即为「旧档已删、新档还在 `.tmp`」——从上层看是存档凭空消失且无从恢复。两条路径都维持「任意时刻至少一份完整副本」的不变式

- **File 目录枚举可选能力**：新增 `IDirectoryProvider.GetDirectoriesAsync` 与 `FileManager.GetDirectoriesAsync`。`IFileProvider` 只有非递归的 `GetFilesAsync`，上层无法发现「存在但当前没有文件的目录」；按 `IAtomicFileProvider` 的既有模式做成可选能力，不给 `IFileProvider` 增加成员，第三方 Provider 实现零影响。装饰器 `CryptoFileProvider` 同步透传——装饰器漏实现可选能力会把底层 Provider 的能力遮蔽掉

- **File 加密按域限定**：`SetCryptoProvider(provider, FileDomain?)`。原实现把整个 Provider 包成加密装饰器、即加密全部域，从存档侧接线会静默连带加密 AppData / Cache

- **File 门面同步内容 API**：`FileManager` 新增 `ReadAllText` / `WriteAllText` / `ReadAllBytes` / `WriteAllBytes` / `WriteAllBytesAtomic` / `GetFiles` / `GetDirectories`。此前门面只有 `Exists` / `Delete` / `CreateDirectory` / `GetPhysicalPath` 是同步的，内容读写一律异步，同步封装藏在 `FileManagerExtensions` 里——该类名暗示它是 `FileManager` 的扩展方法，实则其中只有 2 个成员是真扩展方法（且扩展的是 `FileDomain`），第三方很难找到。现把同步内容读写摆上门面，与既有的 `Exists`/`ExistsAsync` 成对形态一致；`FileManagerExtensions` 保留同名成员并改为一行委托（公开签名与行为不变），类文档指回门面。同步方法阻塞等待各自的异步实现，失败契约、原子写与子目录枚举的降级行为都与异步版完全一致，适用范围同既有的同步 `Exists`：编辑器工具、小型配置文件、启动期加载

- **Settings 原子写入与一代备份**：改用 `FilePathUtility.ReplaceFileAtomically`（与 Save/File 模块同一套替换原语）。原先 `File.WriteAllText` 直接覆盖正式文件，写到一半崩溃就留下截断 JSON，配合「解析失败即抛异常」的行为足以让玩家此后每次启动都崩

#### Data

- **Data 快照应用上报失败块数（破坏性）**：`IDataManager.ApplySnapshot` 由 `void` 改为返回未能恢复的数据块数量。此前单块恢复失败只记 warning 后吞掉、方法正常返回，调用方无从判断本次加载是否**完整**——「部分块未恢复」等价于内存里少了一半数据

#### Save

- **Save 元数据侧车与校验和**：槽位旁新增 `slot_N.save.meta`（元数据 + FNV-1a 64 校验和），使枚举不必为拿元数据而读入并反序列化整个载荷。侧车缺失或不可解析时回退全量解析并重建；**校验和不符时刻意不重建**——那是「载荷可能已损坏」的信号，重建等于把它抹掉，应留给加载侧走备份回退。定位写明：只发现损坏，不提供防篡改

- **Save 加载结果枚举与备份回退**：新增 `SaveLoadStatus` / `SaveLoadResult` / `TryLoadAsync`，把「槽位不存在」「文件损坏」这类预期内失败以状态回报而非抛异常——存档界面需要区分这些情况，做成异常会迫使调用方用 try 表达正常分支。主文件不可用时自动回退到一代备份并回报 `LoadedFromBackup`

- **Save 快照版本门禁**：`SaveOptions.CurrentVersion` / `SetCurrentVersion`。保存时写入快照，加载时高于当前客户端则整份拒绝（`VersionTooNew`）、低于则正常加载并回报 `Migrated`。此前只有 Data 侧的按块门禁，而它是「跳过该块」——结果是新格式存档被半加载（一半新数据、一半空着），比干净地拒绝危险得多

- **Save 启动恢复扫描**：`SaveBootstrapNode` 初始化后扫描存档目录，用一代备份还原丢失的载荷、清理崩溃残留、重建缺失侧车

- **Save 槽位复制与移动**：`CopySlotAsync` / `MoveSlotAsync`，基于 provider 读写原语实现，所有存储后端与加密层都成立；目标侧车按目标槽位重建（照抄源侧车会让元数据把目标报成源槽位）

- **Save 进度上报**：多步操作（枚举槽位、批量删除）支持 `IProgress<SaveReport>`，按框架惯例以重载而非改签名新增

- **Save 加密接线**：`SaveOptions.CryptoProvider` 非空时只对 `FileDomain.SaveData` 域接线加密

- **Settings 脏标记与可选自动保存**：`IsDirty` / `MarkDirty`，以及 `AutoSave` / `AutoSaveDelay` / `SaveOnQuit`（默认全关，保持「调用方显式保存」的既有取舍）。脏标记用**变更计数而非布尔标志**：`Save` 开始时取快照、结束后把已提交档位设为快照值，这样「保存过程中用户继续拖动滑条」产生的改动不会被吞掉——布尔标志会在保存结束时无条件清除，把那次改动丢掉。自动保存是**去抖而非节流**：等待窗口从最后一次改动起算，拖动滑条期间一次都不写盘、松手静默后写一次；若做成节流，一次三秒的拖动会写六次。关闭时不注册任何帧回调，默认路径零开销

#### Pool

- **Pool / Config / Settings 三组诊断回读 + 三个页签**：`PoolManager.CopyPoolStats(List<PoolStats>)` / `PoolCount`（新公开类型 `PoolStats`）、`ConfigManager.CopyLoaded(List<ConfigLoadedInfo>)` / `InFlightLoadCount`（新公开类型 `ConfigLoadedInfo` / `ConfigLoadKind`）、`SettingsManager.CopyRegisteredTypes(List<SettingsTypeInfo>)` / `RegisteredTypeCount`（新公开类型 `SettingsTypeInfo`）。三条取向：
  **① 全部走门面专属成员，一个主接口都不动**。Pool 本就无接口；Settings 照 `ISettingsDirtyFlush` 先例加了一条同级内部缝 `ISettingsDiagnostics`（泛型不变的 manager 表遍历不了）；Config 直接读内置实现的 `IDictionary` 表——三者合起来让第三方的 `IConfigManager` / `ISettingsManager<T>` 实现零影响。
  **② Pool 加的是内部缝的成员而不是新公开接口**：`IUntypedPool` 补三个只读属性，`Pool<T>` 的三个**已有公开属性**隐式实现它们，池本身零改动。没有加公开的非泛型 `IPool` 基接口——那会让用显式实现 `IPool<T>` 的第三方编译不过（CS0535）。
  **③ `IsDirty` 不造第二份真相**：`SettingsTypeInfo.IsDirty` 的文档直接转述 `ISettingsManager<T>.IsDirty` 的口径（有改动待提交 ≠ 已落盘）。

- **XPool 补 `CountActive`（`IPool<T>` / `Pool<T>`）**：`CountAll` 是「自建池以来生成过多少」，只增不减——归还、清空、超容丢弃都不减。于是「还有多少实例在外面」根本无从读出，而集合池的 `Configure` 守卫只能拿 `CountAll - CountInactive` 冒充活跃数（见 `### Fixed` → Pool 的「集合池 `Configure` 守卫用差值冒充活跃数」一条）。新增的 `CountActive` 随取还增减，超容丢弃的实例同样离开活跃态（不再有人持有它）；递减钳在 0，避免 `CollectionCheck` 关闭时重复归还把它减成负数。文档同时写明：`CountAll - CountInactive` **不等于** `CountActive`，被丢弃的实例仍计入 `CountAll` 却既非活跃也非闲置

- **XPool 池满丢弃回调**：闲置达到 `MaxSize` 后归还的实例原先被直接解引用、无人知晓，持有非托管资源或需要确定性释放的对象只能等 GC——这是本模块相对 Unity 官方 `ObjectPool`（有 `actionOnDestroy`）与微软 `ObjectPool`（`Return` 可返回 false 拒绝回收）唯一的功能缺口。`Pool<T>` 构造新增 `onDestroy` 委托，另立 `IPoolDiscardable` 可选能力接口（不给已发布的 `IPoolable` 加成员——那是设计给第三方数据类实现的，加方法即源码破坏），`PoolManager.Configure<T>` 增加三参重载把丢弃回调透传到池。语义要点：归还回调仍**先于**容量判断触发，被丢弃的实例先收 `OnReturn`（清理状态）再收 `OnDiscard`（释放资源）；`Clear()` / `ClearAll()` **不触发**丢弃回调——`ClearAll` 会在应用退出时自动调用，退出期跑用户回调容易碰到已销毁的 Unity 对象

#### Input

- **其余页签（概览 / Message / Lock / Data / Bootstrap / Save / File / Input / Serialize）+ 第三方示例**：全部建立在**已有公开查询面**之上，零 Runtime API 变更。概览页是一张模块状态矩阵（哪些模块已初始化、各自的规模一句话），是诊断窗口的落地点；其余页签各自回答一类问题（Message 的订阅/缓冲规模、Lock 的未绑定主体、Data 的脏块、Bootstrap 的登记表、Save 的忙碌状态、File 的加密提供者、Input 的设备识别、Serialize 的已注册格式）。另加 `Samples~/Example/DiagnosticsSample.cs`——第三方页签的完整样板（实现 → 登记 → 采集三步）。
  **三条边界已写进各页签的文档**：Save 的槽位列表是异步磁盘 IO 不适合轮询（本轮不做）；Input 的启用 ActionMap 列表要动 `IInputProvider`（本轮不做）；Message 的键值通道 key 无法枚举（只给到类型粒度）。

- **`InputManager.Initialize(InputSystemOptions)`：自己加载输入资产的入口**。无参 `Initialize()` 走 `Resources.Load("InputSystem_Actions")`——零配置起步，却也是唯一路径；资源钉在 `Resources/` 意味着它总被打进包、且不受 YooAsset 之类资源系统管理。新增的 `XFramework.XInput.Default.InputSystemOptions { InputActionAsset Asset }` 把**加载**那一步交还给使用方（YooAsset / Addressables / 自己的加载器皆可），接线与无参路径完全相同（同一个默认提供者、同样注册帧驱动）。**Unity 输入类型只出现在 `XInput.Default`**——门面与 `IInputProvider` 保持插件中立（Rewired 那类用户看到的公开面不受影响）。顺带把三个 `Initialize` 重载共用的尾巴提成私有 `AdoptProvider`，新重载与既有两条同形（先建好、成功后才接管）

- **`InputSystemOptions.InitialActionMap`**：默认提供者此前把初始 ActionMap 名 `"Player"` 写死在 `SwitchActionMap`，项目只能在 Initialize 之后再切。现可配（默认值不变）；传 `null`/空串 = 不自动切换，保持资产 `Enable()` 后的全部 map 常开。包 README 的「框架约定的名字」表同步新增 `Player` 一行

#### Audio

- **新增 `XAudio` 音频模块**（`当时的候选模块路线图` §2.1 立项的第一个模块）：静态门面 `AudioManager` + `IAudioManager` 接口 + internal 实现，自持隐藏宿主并池化 `AudioSource`；字符串通道 + 三档音量相乘 + 代际安全的 `readonly struct` 句柄 + 播完自动回收 + 管理器级暂停。**公开面不含任何 Unity 内置音频类型**——资源一律以 location 字符串标识，故 `IAudioManager` 可被 Wwise / FMOD 等中间件整体替换（同 `InputManager` 换 Rewired 的形态）。三条与直觉相左的决定及理由：
  **① 没有 `Play(AudioClip)` 重载。** 保留它会同时毁掉后端中立性（Wwise 的事件、FMOD 的 `EventInstance` 都没有 `AudioClip`）与「资源生命周期只有一套语义」——原设计里「调用方在播放期间释放自己持有的资源句柄 → 声音静默中断」这个只能靠文档挡的坑随之消失。代价是回不到 Inspector 直引的用法（已写进模块 README 的已知限制）。
  **② `AudioHandle` 的字段公开、构造函数可由第三方调用。** 照抄 `LockHandle` 的 `internal` 构造会把「整体替换引擎」这条路彻底堵死——实现方既造不出句柄，也读不回自己编码进去的值。框架级的唯一契约是 `default(AudioHandle)` 必须被任何实现视为无效。
  **③ 回收判据是框架自己的状态机，不是 `AudioSource.isPlaying`。** 前置探针实测确认 `Pause()` 之后 `isPlaying` 就是 `false`，只看它会把暂停中的播放当场回收——`AudioVoiceState.Paused` 因此存在。
  **实测记录**（batchmode、无音频设备；这几条此前都只是记忆而非事实）：音频子系统照常推进（0.2s 内 `time` 走到 `0.192`，播完后 `isPlaying=False` 且 `time` 归零），因此回收用例走**真实播放驱动**而不必造内部 seam；`AudioSource.volume` 的 setter 被引擎钳到 `[0,1]`（`2.5 → 1.0`、`-1 → 0.0`）——框架仍自行钳制，不把公开承诺押在引擎行为上。
  **被新用例抓出的一个真 bug**：`AudioPlayOptions.Default` 原写作 `=> new AudioPlayOptions()`，而 **C# 对结构体的 `new S()` 一律产生「字段全为零」的默认值、不会调用任何构造函数**（即便存在一个全部参数都可选的构造函数）。于是 `Default` 返回的是 `VolumeScale == 0` 的零值，与它自己的文档承诺相反——播放路径不受影响（省略实参走的是 `default(AudioPlayOptions)`，由实现侧归一化兜住），但凡是直接用 `Default` 的调用方都会拿到一个静音的声音。

#### Timer

- **Timer / Localization / Update / Audio 四组诊断回读 + 四个页签**：`TimerManager.CopyActiveTimers(List<TimerInfo>)`、`LocalizationManager.CopyLoadedLanguages(List<LocalizationLanguageInfo>)` / `CachedLanguageCount`、`UpdateManager.CopyNodes(List<UpdateNodeInfo>)`、`AudioManager.CopyChannels(List<AudioChannelInfo>)`（新公开类型 `TimerInfo` / `LocalizationLanguageInfo` / `UpdateNodeInfo` / `AudioChannelInfo`）。四条取向：
  **① 回读的是「名单」而不是「计数」**：`TotalCount` / `GetCount` 回答有多少，`CopyNodes` 回答是谁——「谁在每帧跑」「哪个定时器没触发」这类问题此前只能靠猜。
  **② 定时器表包含已 `Stop` 未释放的槽位**（`TimerInfo.IsRunning` 为 false）：那是「停掉的定时器仍占着槽位」这类泄漏的唯一可见面；令牌已取消的槽位算不在计时，与 `IsActive` 同一口径。剩余时间按**各自那条轴**的逻辑时刻算——两轴时钟不同源，用错轴会把暂停期间的剩余算成负数。
  **③ `UpdateTiming` 由 internal 提权为 public**（纯放宽）：它要出现在 `UpdateNodeInfo` 的字段类型上，否则 CS0052。取值即内部数组下标，既有取值不可改动。
  **④ 全部走门面专属成员，主接口一个不动**（Timer/Update 本就无接口；Localization/Audio 照 `CaptureTarget` 先例探测内置实现，注入自定义实现时回读返回空）。

- **新增 `XTimer` 定时器模块**（`当时的候选模块路线图` §2.3 立项的第二个模块）：静态门面 `TimerManager` + 代际安全的 `readonly struct` 句柄，一次性延时与固定间隔两类。**纯静态服务**——无 `ITimerManager`、无 `Initialize`、自动初始化、零配置（与 Update / Lock / Message / Pool 同族：本仓「有接口」的分界线是「是否存在可替换的后端」，Timer 没有）。立项时写下的四条「比 `UniTask.Delay` 多给什么」全部兑现：可查询的 `Remaining`、零分配的可复用句柄、不漂移的固定间隔（锚点 + k×间隔网格，跳拍不补发）、与 Update 的 Tier 自动升降档联动（60 秒的定时器每 2.1 秒才被扫一次，且临近截止自动升回每帧档，触发精度不受影响）。**红线照原样执行**：暂停与时间缩放完全转接自 Update 的双时间轴，模块内没有一行暂停代码。四条与直觉相左的决定及理由：
  **① `TimerManager` 上没有 `Pause` / `Resume`。** 暂停与时间缩放必须架在 Update 的双时间轴之上，另开一套时间口径等于替使用方决定时间模型。代价是使用方要理解 `UpdateTimeMode`——但那与他们在 Update 里学到的是同一套，不必为定时器再学一遍。
  **② 换档位不能用「重新 `Register` 驱动器」。** 已读源码验证：`UpdateScheduler.ApplyOp` 的注册分支插入的是一条 `NeedsAnchor = true` 的新条目，下一次派发 `deltaTime` 记 0——每次创建都吞掉一个档位周期，高频创建时逻辑时钟会**永久停摆、定时器永不触发**。故已派发过的驱动器一律走 `ProcessImmediate`，并把**上一次收到的时刻**原样传回作为时钟（传 `Time.time` 会把基准推到真实时刻，而逻辑时钟是按 `deltaTime` 累积的，两者在暂停之后不再同源，会把整段暂停算成流逝时间）。
  **③ 新增「出生趟号」这个概念。** `ProcessImmediate` 会同步跑一趟扫尾，若刚建的 `After(0f)` 在这趟里到期，回调就在 `Create` 返回句柄之前触发了。故扫尾分「派发轮」与「立即重估」两种：后者不推进趟号，于是刚建好的槽位仍算本趟出生——**只参与最近截止的估计**（否则新定时器算不进档位、白拉细一趟），**绝不触发**。它同时让锚点落在 `Create` 那一刻而不是「下一趟扫尾」。
  **④ 取消令牌不调 `CancellationToken.Register`。** 令牌随槽位存储、在扫尾与各查询点上直接读 `IsCancellationRequested`：省掉「令牌回调在别的线程上跑、而活表只能主线程改」这条竞态，省掉「扩容换数组时标记写丢」，也省掉 `Register` 自身那次分配——「不传令牌时零额外分配」因此是结构性的而非测出来的。
  **实测记录**：跨模块集成用例 `TimerTierTableTests` 量出的各档真实派发周期与标称值（33.33 / 66.67 / 133.33 / 266.67 / 533.33 / 1066.67 / 2133.33 ms）**逐位吻合**；零分配用例确认创建/释放 1000 次、值类型载荷 1000 次、稳态推进 60 帧的 `GC.GetAllocatedBytesForCurrentThread()` 增量**均为 0 字节**（**该尺子后经实测恒返回 0，此结论不成立**，见 `### Documentation`「`Workflow.md` 记入『零分配断言可能恒真』」）（顺带确认 `UpdateManager.Tick` 自身也是零分配的）。
  **过程中被用例抓出的两处**：一是 `After(0f)` 原本要两帧才触发（`NeedsAnchor` 让定锚的那一趟不触发），改成出生趟号后恢复为「下一拍即触发」；二是一条用例假设「先创建的槽位下标更小」，而自由链是 LIFO、槽位顺序会反转——它顺带暴露了一条值得写进文档的真实语义：**同一次扫尾里，全停之前已被走访的定时器仍会触发**。

#### Log

- **Log 页签与日志回读面**：`LogManager.CopyCategories(List<LogCategoryInfo>)` / `CopySinks(List<ILogSink>)` / `SinkCount` + 新公开类型 `LogCategoryInfo`（名称 / 生效档位 / 是否被覆盖）；Log 页签显示全局档位、分类表与输出端列表。三条取向：
  **① 不进 `ILogManager`，走门面探测内置实现**（照 `LogManager.CaptureTarget` 先例）：主接口是对第三方开放的替换点，加成员会让所有实现者编译不过（仓内就有 `ForeignLogManager` 这样的替身）。注入第三方实现时回读一律返回空，README 写明。
  **② 「生效档位」在调用时现算**（设了覆盖取覆盖、否则取当前全局档，在同一个快照里一起读）——因此改过全局档之后回读不会给出陈旧值，这条有专门用例锁定。
  **③ 分类数没有再在门面上加别名**：`LogCategory.RegisteredCount` 已公开提供同一个数，两个名字指向同一个值就是第二份真相（此条比计划**少**一个公开成员）。
  **顺带**：`LogRegistry` 加 `CopyTo(List<LogCategoryState>)`——先取一次字典引用再遍历，copy-on-write 下拿到的是完整快照，与读路径无锁的理由是同一条。

- **新增 `XLog` 日志模块**（`当时的候选模块路线图` §2.2 立项，条件是答得出「比 `Debug.unityLogger` + 自定义 `ILogHandler` 多给什么」）：静态门面 `LogManager` + `ILogManager` 接口 + internal 实现，六档分级（`Verbose/Debug/Info/Warning/Error/Fatal`，另加仅作阈值的 `Off`）+ 分类过滤（全局档 + 按分类覆盖，`LogCategory` 是 interned 的 `readonly struct`——档位存在被引用的状态对象里，复制出去的句柄同样生效）+ 可插拔 `ILogSink` + 控制台渲染。**零 GC 的边界是刻意的**：`Xxx(category, "x {0}", arg)` 在档位未启用时**不格式化**、零分配（`LogAllocationTests` 锁定（**该断言的尺子后经实测恒返回 0，结论不成立**——见 `### Documentation`「`Workflow.md` 记入『零分配断言可能恒真』」））；泛型重载避免的是 `params object[]` 数组分配，值类型参数在**启用路径**仍会装箱；3 参是不分配数组的上限（4 参起掉进 `params` 重载——实测全仓仅 2 处需要，改走现成字符串重载）。`LangVersion 9.0` 下 InterpolatedStringHandler 不可用，这是唯一的零 GC 形态。**四条与直觉相左的决定及理由**：
  **① 门面永不抛异常（对门面模板的第三处有意豁免，前两处是 FileManager 的懒初始化与 InputManager 的宽容语义）。** 日志是错误路径的最后一张面孔——让「模块没初始化」把它升级成二次故障，代价比一致性大得多；sink 抛异常即被静默摘除并计入 `DroppedSinkCount`（「因为日志坏了而记一条日志」会递归，且测试把意外 Error 当失败）。
  **② 分类名 = 迁移前的 `[前缀]` 逐字，因此同一模块内历史遗留的多个标签各是一个分类**（`[Config]` 与 `[ConfigManager]`、`[AssetManager]` 与 `[YooAssetManager]` 都是两个）。这是「迁移期间控制台文本逐字不变、30+ 处 `LogAssert` 零改动」的前提，代价是分类数偏多（31 个）——单独关掉某个标签的控制力反而更细。
  **③ 格式化走 `string.Format(CurrentCulture, …)`，不自研 formatter。** 转义、`null` 参数、`IFormattable` 格式说明符、culture 四项语义**与字符串插值同源**，这是「迁移后文本没变」的免费正确性证明；模板解析失败（消息里有裸花括号）时退回原文，绝不抛。
  **④ Release 默认档位是 `Info`，不是 `Warning`。** 迁移前 18 处 `Debug.Log` 映射到 `Info`，低于它就会静默改变现有可见性；Editor / Development 默认 `Debug`（`Verbose` 全环境默认关）。
  **JSONL 文件输出端**（schema 表与 `jq` 分析指引见模块 README：固定键序、会话头自描述、`seq` 定序、中文不转义）：每条日志一行 JSON，落在 `{persistentDataPath}/XLog/`，**每会话一个文件**（`xlog-{时间}-{会话id}-p{n}.jsonl`，会话 id 与计时起点由 `AutoInit` 复位——关闭域重载时静态字段跨播放会话存活，不复位会让两次运行共用同一个 session、按 session 切片随即失效）。超 32 MiB 切分片、每片首行重写会话头（任意单个文件都自描述）、目录内保留最新 10 个。**Warning 及以上立即落盘**、其余每 64 条批量落盘、`Shutdown` 与 `Application.quitting` 时冲刷——崩溃后 AI 要能读到现场，这是「不开后台写线程」的理由。**失败即静默停用**：磁盘满、目录被占等 IO 失败后该输出端自我关闭，不记日志（会递归）、不抛异常、不影响控制台通路。
  **实测记录（含两处自我更正）**：分类表由机械提取全部日志调用点后定，回填过程中抓住两类误收——① `[AudioManager]` 实为 GameObject 名、`[Slot:…]` 实为 `SaveMeta.ToString()`，都不是日志前缀；② `[XSerialize]`、`[Reactive]`、`[LanguageAssetLoader]` 三处**只出现在异常消息里**（`throw new …($"[模块] …")`），不是日志标签——分类表因此从最初声明的 34 个收敛到 **31 个**，且 `Reactive` 与 `Serialize` 两个模块**根本没有日志可回填**。异常消息按既有约定继续自带 `[模块]` 前缀，不归分类表管。`check-docs -Module Log` 报 **0 条** XML 告警。
  **被用例抓出的一条（8 条文件用例首轮全红）**：`Sharing violation`——Windows 的共享规则是**双向**的，写入端声明 `FileShare.Read` 只解决了「允许别人读」，读者还必须自己声明允许写者，而 .NET 的 `File.ReadAllLines` 内部声明的正是 `FileShare.Read`。修法在读者侧（显式传 `FileShare.ReadWrite`），已写进 README——它是「AI/脚本在游戏运行时读日志」的前提，不修就等于这份文件只能等进程退出后才能分析。
  **全量捕获**：挂 `Application.logMessageReceivedThreaded`，把引擎、第三方库（YooAsset 那类）、未捕获异常的日志也收进同一份 JSONL，每条标 `src:"unity"` 与来源 `LogType` 映射出的档位；框架自己的日志标 `src:"fw"`。**去重靠线程本地的回显深度**：控制台输出端调 `Debug.Log*` 前后增减计数，捕获回调据此丢弃自己的回显——一个框架日志因此恰好写一条，而不是「直写一条 + 被捕获再写一条」。外部日志同样走档位过滤（否则 `MinimumLevel` 的「一律丢弃」会自相矛盾），分类从 `[标签]` 前缀解析。**捕获回调在 `AutoInit` 时无条件挂上**（没有可写输出端时第一行即返回）：挂载成本只是每条 Unity 日志一次静态委托调用，换来的是没有「第一次 `LogManager` 调用之前发生的日志捕获不到」这个时序陷阱。同批把默认实现改为 `AutoInit` 时**立即装入**（不再是首次使用时懒创建）——否则启动阶段的引擎/第三方日志（恰恰是排查启动失败最需要的那些）会整段丢失。
  **回填已完成**：18 个模块（Pipeline / Bootstrap / Config / Data / Save / Asset / Settings / UI / Input /
  Audio / Timer / Message / Lock / Update / Pool / Localization / File / Event）加 Editor 工具与 Samples，
  共 20 个提交、约 190 处调用点，**全部按模块拆分、控制台文案与 LogType 逐字不变**——30+ 处既有
  `LogAssert`（精确匹配与正则）一条都没改，这是「渲染与迁移前一致」的机械证据。`Reactive` 与
  `Serialize` 两个模块无需回填（它们那两处方括号文本是异常消息，不是日志）。至此框架内**只有一条
  日志通路**：`Runtime` 下除控制台输出端自己那三行外，已无任何 `Debug.Log*` 调用。

- **`XLog` 异常入口支持指定级别**：新增 `LogManager.Exception(level, category, exception, message = null)`——此前只有固定 `Error` 的那个重载，于是「跳过一条损坏的存档」「设置密文解不开、回退默认值」这类**可恢复失败**要么被迫记成 `Error`，要么退化成 `ex.Message` 文本。两者都把异常对象送进 JSONL 的 `exc` 字段；正文约定「不重复异常文本」（类型与消息由渲染层补）。**被否决的两个形态**：把实参从 `ex.Message` 换成 `ex`（零 API 增长，但 `exc` 仍为空、堆栈混进 `msg`——正是要消掉的混法）；让 `Log(...)` 家族也收异常（+16 个公开成员，只为错误路径的模板一致性，不成比例）。顺带把门面完备性守卫从「只查同名」升级为「同名 + 同元数」——`Exception` 成为重载后，门面只转发其中一个重载时旧守卫查不出。
  **同批把框架里 47 处「异常没进结构化字段」的站点全部补齐**（`exc` 此前在生产代码里从未被写过一次）：17 处只传 `ex.Message` 的（含 Save 元数据解析的三个入口）、30 处「异常对象已作为格式实参传入、`ToString()` 混在 `msg` 里」的、以及 2 条把异常转成状态/文本后**丢弃对象**的主线——Pipeline 的用户阶段异常经 `PipelineStageContext.FailureException`（internal）一路带到编排级终局日志，**容器内子阶段抛出的同样传到底**；Save 的 `TryDeserializeSnapshot` 加 `out Exception`，反序列化失败与结构校验失败由此走两条路（后者本就没有异常，保留原文案）。两处站点的控制台 Error/Warning 行因此变长（正文后追加 `"\n" + 异常详情`），**既有 `LogAssert` 只改了 10 条**，全在两处：`UpdateSchedulerTests` 5 条（三处 `unregistering:` 的正则含异常文本、两条 `OnEnable/OnDisable threw exception:` 尾部带冒号）与 `PipelineSubscriberIsolationTests` 5 条（`subscriber threw:` 的尾部冒号）——它们都依赖 `": "` 这个已不存在的分隔符。其余全是不锚定、只匹配消息头的正则（实测穷举确认），一条未动。至此 `exc` 成为稳定 schema：`jq 'select(.exc)'` 可靠取出「类型 + 异常链 + 抛出点堆栈」，`msg` 恢复成「人读的一句话」；`stack` 与它的分工（日志调用点栈 vs 异常栈）已写进模块 README。

#### Settings

- **Settings 字段句柄 `SettingRef`**：把设置对象里的单个字段包装成响应式视图，实现 `IReactiveProperty<T>`，故 UI 模块现成的 `BindToSlider` / `BindToText` 等绑定扩展方法可直接使用。刻意**不缓存设置实例**——每次读写都解析当前实例，因此 `Load`/`Reset`/`Apply` 换实例后自动跟随、无需重新绑定（若让设置字段直接声明为 `ReactiveProperty<T>`，则每次换实例都要重绑、且旧对象上的订阅全部失活）。去重基准取 POCO 的实时值而非缓存，故无陈旧锚点。句柄**不实现 `IDisposable`**：它通常声明为静态字段并活到进程结束，带释放语义会让「面板关闭时 Dispose 掉 ViewModel」这类正常操作连带废掉它。选择器必须以字段结尾——属性不被 `JsonUtility` 序列化，用它做设置项会「改了但没存」，`Ref` 直接拒绝并说明原因

- **Settings 异步持久化**：`SaveAsync` / `LoadAsync` 与可选能力接口 `IAsyncSettingsStore`。此前模块只有同步 API，设置面板保存会阻塞主线程做文件 IO，而它是框架内唯一同步落盘的持久化实现。未实现该接口的存储后端不必改动：管理器做能力探测、把同步调用整体挪到线程池，语义与同步版完全一致（与 FileManager 对 `IAtomicFileProvider` 的处理同构）。能力接口必须带 `ExistsAsync`——管理器靠它区分「有持久化数据」与「根本没有数据」，缺了它异步加载会与同步加载产生不同的默认值

- **Settings 格式版本与迁移**：`SettingsOptions.CurrentVersion` + `ISettingsMigrator<T>`。README 一直声称「JSON 天然支持版本迁移」而代码零实现，字段改名或结构调整后旧文件会被静默按新结构解析、错位而不报错。版本与载荷一起落盘成信封（`{"Version":1,"Data":{…}}`）——`ISettingsStore` 只有泛型的 Load/Save、没有版本通道，把版本处理下推进 store 会让每个第三方 store 都得实现一遍。做成**可选**：默认 `CurrentVersion = 0` 时不启用，落盘仍是设置对象本身，保持「JSON 干净」这一卖点。加载时版本高于本版本则整份拒绝（按旧结构解析只会得到静默错位的设置）、低于则调用迁移器、缺迁移器同样回退默认值

- **Settings 切后台兜底 `SaveOnPause` 与 `SaveAllDirty()`**：移动端被切到后台后系统可能随时杀掉进程，此时 `OnApplicationQuit` 根本不触发——「退出时兜底」在这些设备上等于不存在，而三方资料里这是移动端最主要的设置丢失路径。`Application` 的静态事件里没有 pause（只有 `quitting` / `wantsToQuit` / `focusChanged` / `lowMemory`），`focusChanged` 又不等价于移动端的 `OnApplicationPause`，故框架自持一个隐藏宿主（`SettingsPauseNotifier`：仅本选项开启时创建、全部释放后销毁，关闭则零开销）。刻意**不**挂到 `GameLauncher` 上——后者自己的文档写明是可选件、不是框架的必需入口，把落盘挂在一个可缺席的组件上会让本选项的承诺落空。配套的 `SaveAllDirty()` 是另一套语义：显式指令，写所有脏了的类型、**不看任何开关**，既是「不想用内置宿主」的替代入口，也是菜单关闭 / 场景切换前的落盘时机。两者合成一个要么让显式调用受开关限制，要么让自动兜底越权

- **Settings 载荷校验钩子 `ISettingsValidator<T>`**：持久层里的值此前被当作可信，而玩家手改过 JSON、磁盘位翻转、迁移实现写错字段、`JsonUtility` 给新增字段填类型默认值（新加的 `int range = 3` 在旧存档上变成 `0`）都会让越界值静默进入内存，再在下游以更难查的方式发作。三方同类系统普遍有这个钩子（zenvin 的 `ProcessValue(ref T)`、.NET 的 `ValidateOnStart`、CycloneGames 的 `Validate`）。形态对齐 `ISettingsMigrator<T>`：就地校正、可选、不注入即零行为；生效于 `Load` / `LoadAsync` / `Reset` / `Apply`，位置在迁移之后、订阅者被通知之前，故迁移的产出同样被校验。实现上收敛到 `RunValidator` 单一出口，使「当前对象一定通过了校验」只依赖一处

- **Settings 加密存储 `EncryptedSettingsStore`**：`ISettingsStore` 的加解密装饰器。README 一直把「加密存储」列为该接口的用途、示例里却写着一个空壳假类，而设置模块的持久层走 `System.IO`、不经 `FileDomain`，也接不上 `FileManager` 的 `CryptoFileProvider`。装饰器不关心数据落在文件还是云端，只在「交给内层之前」与「从内层取出之后」各做一次变换，故内层原有的原子写、一代备份、失败降级全部保留。密文以 Base64 包进一层 `{"Data":"…"}` 外壳是接口形状决定的——`ISettingsStore` 只有泛型的 `Load/Save`、没有字节通道。加密对格式版本化透明：信封由管理器在加密之前套上。与此同时，README 补上了「为什么 `JsonFileStore` 不经 `FileManager`」这个原本沉默的决定（接上去能换来 `FileDomain` 与平台 Provider，但会丢掉一代备份——`FileManager` 没有重命名/复制原语）

- **Settings 门面补齐迁移器与校验器转发**：`ISettingsManager<T>.Migrator` 在门面上查无此成员，而 README 一直教用户在 `Initialize` 的返回值上挂迁移器——初始化一旦发生在别处（引导阶段、另一个程序集）就再也够不着它。同 `UIManager.SetLayerVisibility` 是同一类缺陷：接口上有、文档也提、转发没写。补 `GetMigrator`/`SetMigrator` 与 `GetValidator`/`SetValidator` 两对，与 `Store` 的 `Get`/`Set` 对称

- **Settings 门面完备性测试**：转发不变量（「接口新增成员必须同步在门面加转发，否则它在第三方眼里根本不存在」）此前只靠评审，`Migrator` 就是这么漏掉的。`SettingsFacadeCompletenessTests` 反射比对 `ISettingsManager<>` 的声明成员与门面的 public static 成员名，只查名不查签名——签名由编译器兜底，唯一会漏的就是「压根没写转发」。与 UI 版的两处必要差异：接口闭合在私有探针类型上取反射，以及生命周期成员用名字集合判定（`Initialize` 有三个重载，`GetMethod` 会抛 `AmbiguousMatchException`）。该测试自己拦不住接错线与假转发，故另配一条行为用例真的走一遍迁移

#### UI

- **`UIManager.ShowPopupAsync<T>` 锚定弹窗入口 + 点击外部关闭捕获层**（**门面成员，不进 `IUIManager`**，与 `ShowDialogAsync` 同理由）：把面板贴着锚点控件摆出来，用于下拉菜单、右键菜单、悬浮说明、组合框展开。四条取向：
  **① 摆位置之前先强制跑一次布局。** 布局驱动的尺寸在布局跑完之前读到的仍是**上一次**的值——池化复用的弹窗换了内容时尤其明显。这条前提**先做成了守卫用例再动手**（`UIPopupLayoutTests`，实测：设 300 → 改成 500 → 立刻读仍是 300、强制布局后才是 500）。它是本入口存在理由的一半：若哪天 UGUI 改成即时生效，价值会静默退化成「省四行」，用例的失败消息直接写着这一点。
  **② 可用区域取 `Screen.safeArea` 而非全屏。** 弹窗不该摆进刘海/圆角区；桌面与编辑器上它等于全屏，无差别。要换别的区域走 `UIPopupPosition.CalculatePosition` 手写那一步。
  **③ 返回面板而不是结果**，与 `ShowDialogAsync` 返回结果的不对称是有意的：下拉框要「选了哪一项」，右键菜单往往每个条目各自行动。要结果的让面板继承 `UIDialogPanel<TResult>` 再 `await panel.WaitResultAsync()` 即可。另：`async` 方法不能带 `out` 参数，故实际采用的方向不在此暴露——`CalculatePosition` 是纯函数，同样输入必得同样方向，调用方重算一次即可。
  **④ 捕获层是与 `UIManager_Mask` 同形但独立的单实例**（`UIManager_PopupCatcher`），**刻意不复用遮罩**：遮罩引用计数且**只管显示与否不管外观**（`ApplyMaskStyle` 每次重设排序与颜色、以最后一次为准），共用会让「模态要 0.5 变暗」与「下拉要全透明捕获」互相覆盖。释放点与 `ReleaseMasksOwnedBy` 同形挂在关闭路径上——面板被任何途径关掉，捕获层都不留在屏幕上继续吃点击；`Dispose` 时一并销毁。
  **捕获层自带 `GraphicRaycaster`**，这是它能挡输入的前提：UGUI 的 raycaster 只查**自己 GameObject 上那个 Canvas** 下注册的图形，而 `Graphic.CacheCanvas` 取 `GetComponentsInParent` 的第一个（自身优先）。层级取 `PanelOrder(layer, 0)` = `layer × 32`，严格夹在「`layer−1` 层最高面板」（`layer×32−1`）与「`layer` 层第一个面板」（`layer×32+1`）之间；序号 0 在 `RestampSortingOrders` 里从不分配给面板。
  **顺带**：`UIRootNode`/`UIMaskTests` 之外的全仓 `EventSystem` 引用数为 0（实测），而任何 UGUI 点击都依赖项目自带的事件系统——本入口的点击路径沿用仓内既有做法，用 `Button.onClick.Invoke()` 验证接线，真实指针事件在文档里标明其依赖。

- **`UIManager.ShowDialogAsync<TPanel, TResult>` 对话框入口**（**门面成员，不进 `IUIManager`**）：把四件必须互相配合、**少配一件都不报错只会「看起来不对」**的事收成一个入口——① 打开面板；② 按对话框所在层**自动推导遮罩层**；③ 把遮罩持有绑到面板上（面板关闭即自动释放）；④ 等待并兑现结果。三条取向：
  **① 遮罩层自动取 `layer - 1`。** 遮罩必须夹在「要遮住的内容层」与「对话框自身所在层」之间：`MaskOrder(L−1) = L×32 − 1` 而对话框 `PanelOrder(L, 1) = L×32 + 1`，正好差两格。**本仓文档此前教的默认组合正好是错的**——`ShowMask()` 用 `UILayers.Mask`(500) 层，`MaskOrder(500) = 16031` 远高于 Popup 层对话框的 6401，于是对话框被自己的遮罩盖住（变暗且点不动）。`layer` 会先经 `ClampPanelLayer` 再推导，两者基于同一个层号，超限时不会错位。层为 0（最低层）时没有更低的层可放，此时不显示遮罩。
  **② 不接 `UIMaskStyle`，拆成 `modal` / `maskColor`。** 遮罩层级由 `layer` 推出，收一个含 `Layer` 的结构体再把该字段忽略掉，等于静默丢弃调用方的输入——与本轮要消灭的故障同类。
  **③ 不自己 `Dispose` 遮罩句柄。** 持有经 `owner` 绑定到面板，释放点是「面板关闭」——手动释放会赶在关闭动画播完之前把变暗撤掉，那是个视觉故障。绑定同时覆盖了 `Dispose` 路径。
  **为什么不进 `IUIManager` 接口**：它是一次**组合**（用 `OpenAsync` + `ShowMask` 两个已有原语），进接口则每个第三方 `IUIManager` 实现者都要多实现一个成员，而他们本可白拿这个组合；放门面则**任何实现**（含注入的自定义 `IUIManager`）都自动具备。先例：门面上的 `Subscribe` ×3 同为门面专属成员。

- **`UIDialogPanel<TResult>` 对话框面板基类**（`View/UIDialogPanel.cs`）：在一次打开-关闭周期内把用户的选择回传给等待方。**它的存在理由是一个静默故障**——朴素做法（自己 new 一个 `UniTaskCompletionSource` 塞进 `userData`、在确定/取消按钮回调里完成它）在面板被**非按钮途径**关闭时会让等待方**永远不恢复**（`CloseAllAsync` 切场景、`CloseLayerAsync`、管理器 `Dispose`）。三条取向：
  **① 兜底挂在框架的回池点，不在 `OnClose`。** `UIManagerImpl.Dispose` 同步回池全部面板、**刻意不走关闭路径**（不 await 关闭动画），故 `OnClose` 在那一路上根本不会被调用；而回池是关闭与 `Dispose` **共用的唯一出口**，是唯一能覆盖两条路的位置。这与框架对「在途打开的等待者不能悬着」的处理同一取向（`Dispose` 里对 `_opening` 的加入者显式 `TrySetResult(null)`）。
  **② 回池时复位。** 面板是池化复用的，不复位会让重开的同类型对话框**立即返回上一次的结果**。
  **③ 子类契约与 `UIPanelBase` 完全一致。** 照常实现 `OnOpen` / `OnClose`，本类既不增加也不封闭任何生命周期钩子——**这是相对初版设计的修订**：初版打算封掉 `OnClose` 并另给一个 `OnCloseCore`，坐实 `Dispose` 那条路之后改为挂回池点，于是「封钩子」变成不必要，子类契约得以保持原样。
  实现侧：新增 `internal interface IDialogResultChannel`（框架与基类之间的实现契约，不是给使用方的扩展点），由 `RecyclePanel` 的 `finally` 调用——与 `UpdateFaulted = false` 同一位置、同一理由。

- **弹窗定位原语 `UIPopupPosition`**（新公开枚举 `UIPopupDirection` / `UIPopupAlign`）：纯几何静态工具，算出弹窗该摆在锚点元素的哪里——下拉菜单、右键菜单、悬浮说明、组合框展开、嵌套子菜单。补的是本模块此前缺的一种贴附：`ShowHudAsync` 跟**世界** `Transform`、Tip 是**固定位置配置**，两者都不是「锚到屏幕空间 UI 元素」。四条取向：
  **① 不进 `IUIManager`、不加门面转发、不登记 Bootstrap / AutoInit**——与 `UISorting` / `UILayers` 同类，是纯静态工具而非服务，无接口成员可加；`AutoInitTests` 的族清单只治理带 `[RuntimeInitializeOnLoadMethod]` 的成员，本类刻意没有（这是设计结论，不是遗漏）。
  **② 返回的是「pivot 应该落在的点」而不是角**：`rectTransform.position` 赋值设置的是物体的 pivot，因此 `popupPivot` 是必需参数——若写死「返回左下角」，中心 pivot（Unity 新建 RectTransform 的默认值）的预制体会**静默错位半个尺寸**。
  **③ 两个轴都收敛，且用显式分支而非 `Mathf.Clamp`**：只收敛一侧时「锚点贴屏幕左缘 + 弹窗比锚点宽」会把弹窗推出屏幕；而 `Mathf.Clamp` 在弹窗大于可用区域（上界 < 下界）时的返回值取决于输入值、语义不可靠——故弹窗过大一律贴低边，并在文档写明「无法保证完整可见」。
  **④ `Auto` 在四个方向里依次尝试**（Down → Up → Right → Left，取第一个放得下的），四个都放不下时取可用空间最大的那个再由收敛兜底；实际采用的方向经 `out resolved` 回给调用方，供翻转箭头或换动画用。
  **顺带**：`GetScreenRect(RectTransform)` 四个角各做一次 `TransformPoint` 后取下界/上界——不用 `GetWorldCorners`、不引入任何缓冲区，**零分配**；锚点被旋转时得到外接矩形，对定位而言正是想要的。**前置条件是画布为 `ScreenSpaceOverlay`**（本框架强制如此），已写进类文档。

- **UI 页签（接替原 `UI State` 独立窗口）**：`UIDiagnosticPanel` 显示面板栈（类型 / 层 / 档位 / 焦点 / 暂停）、状态快照、层级表（可见 / 可交互 / 已打开数，含项目自定义层）与 `DumpState` 全文——旧窗口的信息一项不少。数据全部取自公开查询面，因此与代码里读到的是同一份真相。

- **诊断窗口壳（菜单 `Tools/XFramework/Diagnostics`）**：左侧页签列表（Order 排序 + 搜索过滤）、顶栏（自动刷新开关 / 刷新 / 复制 / 页签计数）、右侧内容区（默认走 `DiagnosticReportView`；页签实现 `IDiagnosticPanelView` 时整块委托；采集失败时绕开自绘显示错误）。三条取向：
  **① 取数只在 tick 里做，OnGUI 只画。** 被删除的 `UI State` 窗口在 `OnGUI` 里 `Refresh()`，而一帧的 Layout/Repaint 会跑多次，等于每帧多次取数——新窗口把这条纠正过来（`EditorApplication.update` 驱动、只在采到新数据时 `Repaint`）。
  **② 判断逻辑全在可测的 `DiagnosticsWindowModel`**：时钟注入、节流判据、选中钳制、页签表重载、异常隔离、自绘路径选择；窗口里只剩「生命周期 / 转发 / GUILayout」三类语句，16 例 EditMode 用例锁住全部分界（含「自动刷新关着时换页签仍必须重采」）。
  **③ 采集异常写进报告本身**（`Notice(Error)` + 异常全文），不另存错误字段——显示、复制、自绘三条路径因此看到同一份东西。
  **顺带**：页签标题在重载时缓存并兜底（模块销毁后标题 getter 可能变成会抛的），避免一帧读几十次标题把窗口带崩。

- **`UIManager.TipAssetPath`：Tip 预制体地址可改**。`PF_UITipText` 此前是 `private const`——改不了，只能整体替换 `IUITipProvider`（那意味着把 223 行池化 / 世代守卫 / 逐帧回收逻辑抄一遍）。现在照 `LocalizationManager.LanguageAssetPath` 的先例给可写属性：**改后下次显示生效**；值存在 `UIManagerImpl`（唯一真相），设值时同步推给内置 provider，两处创建默认 provider 的地方（初始化、`SetTipProvider(null)` 重建）在创建后套用它。**不进 `IUIManager`**，照 `SetController` 的「只对内置实现生效」——注入自定义 provider 时地址由那个实现决定，接口不为它加成员（否则测试里的 Fake 会编译失败，且属对第三方的破坏性变更）

- **UI 面板级 LOD 与失焦剔除**：`UIViewBase.UpdateLod`（Inspector 可配、运行时热改即重排）。用不到每帧的面板（倒计时、进度插值）声明较低档位即可降耗；被覆盖而失焦的面板完全不派发。按档位懒注册驱动器——Tier0 复用常驻的那个，其余档位桶由空转非空时才注册、重新变空即注销，故初始化后调度器里仍只有一个条目。`IsPaused` 此前是无人消费的死字段（`OnBlur` 置位、从无读者），至此才第一次生效

- **UI 查询 API**：`OpenCount` / `IsAnyOpen` / `GetTopPanel` / `Panels`（活视图，读取零分配）/ `CopyPanels(buffer)` / `CopyPanelsInLayer`。此前查询只能逐类型试 `IsOpen<T>()`，「现在开着哪些面板」「哪个在最前面」无从回答，除非调用方自己维护一份账。`CopyPanels` 为零分配主入口，由调用方持有缓冲区

- **UI 遮罩引用计数**：`ShowMask` 返回 `UIMaskHandle`（`readonly struct`，与 `LockHandle` 同惯例），配 `UIMaskStyle`。多系统各自需要遮罩时（弹窗 + 引导 + 网络等待），先结束的那个不再把别人的遮罩一起关掉。支持 `owner` 面板联动——面板关闭时自动释放它开的遮罩。`UIMaskStyle` 刻意不用哨兵值：层级 0 是合法层级，全透明遮罩（挡输入但不显示）也是合法需求

- **UI 层级 API 可达**：`SetLayerVisibility` / `SetLayerInteractive` 此前只存在于内部实现上，**连 `IUIManager` 都没有**，而门面既无转发也无实例属性——第三方虽能在文档里读到它们，实际完全不可达。现补进接口并由门面转发，另暴露 `UIRoot`

- **UI 诊断**（**独立窗口 `Tools/XFramework/UI State` 同版内被删除**，内容并入诊断窗口的 UI 页签，见 `### Removed`）：`UIStateSnapshot`（`readonly struct`，零分配）+ `DumpState()` + `Tools/XFramework/UI State` 窗口。用于回答「面板是不是漏关了」「遮罩为什么还亮着」「哪个打开卡在半路」这类只能翻运行时状态才能定位的问题。窗口数据现取自显示栈与在途表，不是另记一份账

- **UI 生命周期钩子 `OnPause` / `OnResume`**：把 `OnBlur` 原先承担的两件事正交拆开——交互维度（`OnFocus`/`OnBlur` 管 `IsFocused` + Raycaster）与更新维度（`OnPause`/`OnResume` 管 `IsPaused`）。于是「失焦但仍要按低频更新」这类需求终于可表达，且被覆盖的面板仍会收到语言切换等回调（否则 Pop 回来会看到旧语言）

- **UI 安全区适配 `UISafeArea`**：把 RectTransform 收缩到 `Screen.safeArea`。推荐挂在 UIRoot 上——层级容器都是它的子节点，一处生效即全局生效

- **UI 订阅归口 `UIViewBase.Track(IDisposable)`**：面板与 HUD 都是回池而非销毁，挂在 `OnDestroy` 上的释放永不触发。这是「订阅随视图生命周期释放」的唯一出口，`BindToLocalizedText` 已归口进来

- **UI 排序空间单点定义 `UISorting` / `UILayers`**：所有 `sortingOrder` 取值一律由此推导，`UISortingTests.EveryBandValue_SurvivesCanvasRoundTrip` 会把越界取值当场拦住

- **`UIBootstrapStage`（Phase 2）/ `InputBootstrapStage`（Phase 1）**：让 UI 与 Input 也能走引导流程，从而由 `Bootstrap.Shutdown` 对称清理。两者同形——构造校验参数、`ExecuteAsync` 里**已初始化即早退**（`UIRootNode.Awake` 或使用方手动 `Initialize` 都算）、`Shutdown` **只销毁自己初始化的那份**（照 `LocalizationBootstrapStage` 的「谁初始化谁清理」先例）。`BootstrapPhases` 相应新增 `UI = 2` / `Input = 1`（两个现成空档；5–89 仍留给使用方插入点）

- **`Reactive 模块的维护向记录` 修正一条过期未决**（同批）：「相等比较器不可注入」记于 2026-10-02 全面清点，而 `69e0190` **同日**就已落地（构造重载 `(T initialValue, IEqualityComparer<T> comparer)` + `Select` 的 `comparer` 参数）却未同步——按 `UI.md` 先例**标注为已落地、不删除（保留沿革）**，教训写进条目自身；同节补 `ReactiveList` 的延后项（Dictionary / Set / 批量 / 集合 `Select` 派生）与两处 v1.1 候选（`Remove(T)` 的比较器注入、`Version` 计数器）。

#### Lock

- **Lock 诊断面 `LockStateSnapshot` / `GetSnapshot()` / `DumpState()` / `CopyLockedTypes()`**：Lock 此前是仓内唯一没有诊断面的成熟模块，而「玩家一直不能动」正是这类系统最经典的事故——旧手段 `GetLockObjects(subject, type)` 要求你先知道主体与类型，拿到的还是持有者对象本身。现在：`GetSnapshot()` 零分配（字段选的是可行动的判据——`UnboundSubjectCount` 数的是「有锁但没有销毁绑定」、即永远不会自动释放的那批，`UnboundSubscribedSubjectCount` 数孤儿订阅）；`DumpState()` 输出人读文本，`Global` 排最前并标 `global`，持有者一律以类型名呈现（**不调 `ToString()`**：那是用户代码）；`CopyLockedTypes(subject, List<int>)` 照 `CopyTypeStats` 的「调用方持缓冲、零分配」惯例，聚合视角含全局锁，一个 API 同时覆盖「被什么锁着」与「是否被任何类型锁着」（后者此前**无从表达**——类型域是开放的 `int`）。诊断面命名刻意避开 `LockSnapshot*` 前缀（已被派发缓冲池占用）

- **Lock 主体销毁自动释放与批量清理**：新增 `LockManager.AutoReleaseOnDestroy`（默认 `true`）与 `RemoveAllLocks` / `RemoveAllSubscriptions`（含 `LockableExtensions` 同形扩展）。此前 `_locks` 与两张订阅表都以主体作**强引用键**且没有任何按主体的清理原语——实体死亡/切场景/回池时若没走完释放路径，锁会永久留存（主体再也解不开）**且该对象被强引用住无法回收**（实测两次 GC 后 `WeakReference` 仍存活）。现在：主体是 `MonoBehaviour`（`destroyCancellationToken`）或实现 `IDestroyCancellationToken` 时，销毁自动释放它的全部锁并丢弃全部订阅；两者皆非时不绑定也不告警，由调用方显式收口。四处取舍：① **绑主体的令牌**而非订阅者的（泄漏的是字典的键，键就是主体）；② **每主体注册一次**、主体空闲时注销——逐句柄注册会把回调节点永不回收地堆在长寿命主体的令牌上；③ 销毁时**先丢订阅、再放锁**（濒死对象自己的回调不再跑用户代码，别人的订阅者照常收到）；④ 主体已销毁时 `AddLock`/订阅**短路**并记 `[Lock]` 告警，而不是「先加上再被立即触发的回调清掉」——后者会派发一对「加了又解」的幻影事件

- **Lock 聚合状态事件 `OnLockStateChanged(subject, Action<int,bool>)`**：只在 `IsLocked(subject, lockType)` 的**聚合**值真的翻转时回调，订阅时立即同步播报一次当前已锁定的类型。此前只有每个 (主体,类型) 集合的**边沿**事件（`OnLocked`/`OnUnlocked`），而查询面是含全局锁的聚合——全局锁释放时，仍被自己的锁挡住的主体照样收到 `OnUnlocked`，订阅者照事件维护「现在能不能动」这类镜像会显示错状态

#### Diagnostics

- **诊断渲染层（Editor 侧，公开给第三方复用）**：`DiagnosticReportView`（整份报告视图，实例持滚动状态）+ `DiagnosticItemDrawer`（逐项渲染原语：小节 / 文本 / 键值 / 表格 / 提示，无状态纯绘制）+ `IDiagnosticPanelView`（自绘页签的可选能力接口，窗口探测后整块委托，`Collect` 照常调用因此「复制为文本」不受影响）。三条取向：
  **① 分层，但两层都要能被第三方用**：Runtime 侧契约保持纯数据（可被文本、编辑器、将来的运行时覆盖层消费），IMGUI 全部落在 Editor 程序集；第三方既能 `new DiagnosticReportView()` 在自己的窗口里画任意报告，也能用单项原语混搭自绘。
  **② 不做「自定义条目类型 + 渲染器注册表」**：自绘接口配公开原语已覆盖自定义视觉的绝大多数需求，而给数据契约加 `object` 载荷会让文本渲染与运行时覆盖层对这类条目只能降级。
  **③ 列宽分配抽成纯函数并单测**：`DiagnosticTableLayout.Allocate`（放得下原样返回 / 放不下按可收缩余地等比收缩 / 余地用尽压到下限为止不再继续挤压，宁可溢出）——IMGUI 调用本身不可测，这是与它分离的那块数学，`DiagnosticTableLayoutTests` 9 例锁住全部分界。
  **顺带**：`Editor/AssemblyInfo.cs` 新增 `InternalsVisibleTo("Venusy609.Xframework.Editor.Tests")`——Editor 程序集此前没有对测试开放 internal，而纯函数缝需要它；对第三方的公开面仍只有 public 类型。

- **新增 `XDiagnostics` 运行时诊断契约**（框架诊断窗口的地基，窗口与各模块页签随后落地）：静态注册表 `DiagnosticsManager`（`Register` / `Unregister` / `IsRegistered` / `CopyPanels` / `PanelCount` / `Clear`）+ 页签契约 `IDiagnosticPanel`（`Title` / `Order` / `Collect(IDiagnosticReport)`）+ 报告词汇表（小节 / 文本 / 键值 / 表格 / 提示，`DiagnosticReport` 为默认实现、可 `Clear` 复用）+ 纯文本渲染 `DiagnosticReportFormatter`。**Runtime 侧零 GUI 依赖**——同一份报告可以被编辑器窗口、文本、将来的运行时覆盖层或第三方工具消费。四条与直觉相左的决定及理由：
  **① 拉模式，不是推模式。** 页签不缓存、不上报，渲染方每次刷新现调 `Collect`——「窗口里看到的」与「代码里读到的」永远是同一份真相，不存在第二本账；代价（昂贵数据要页签自己缓存）写进模块 README。
  **② 注册幂等是硬要求，但刻意不做「进播放时清空注册表」。** 关闭域重载时 `[RuntimeInitializeOnLoadMethod]` 每次进播放都重跑、`[InitializeOnLoadMethod]` 不跑，两条时序都不能产生重复页签；而清空会把编辑器侧注册的那批页签一并抹掉。
  **③ 没有自动初始化成员，且这是设计结论。** 注册表不订阅 Unity 事件、不做会话复位、不注入 PlayerLoop，静态字段随类型首次使用初始化即可——`AutoInitTests` 族清单因此零改动（已写进类文档，防止下一轮「为了对称」补一个）。
  **④ 表格「多余单元格被忽略」必须真的丢掉。** 首轮用例抓到实现只在渲染时忽略、`GetCell` 仍能越过 `ColumnCount` 读到它们（`Table_AddRowMoreCells_ExtraIgnored` 红），改为入表即按列数截断。
  **实测**：文本渲染的对齐口径锁到字符级（列宽按字符数、分隔线 `-+-`、行尾不留空白——末列全空时连分隔符都不输出）；`DiagnosticsRegistryTests` / `DiagnosticReportTests` / `DiagnosticReportFormatterTests` 三个 fixture 全绿（其余 `-Filter Diagnostics` 命中 65 例、0 失败），`check-docs.ps1 -Enforce` 一次通过。

### Changed

#### 破坏性变更迁移表（命名与签名）

| 旧写法 | 新写法 | 说明 |
| --- | --- | --- |
| `IUiHudProvider` | `IUIHudProvider` | **无需迁移**——该接口未发布（`[0.2.0]` 里没有它）。同批的 `UiDiagnosticPanel` → `UIDiagnosticPanel` 同理：Editor 程序集的 `internal sealed`，第三方碰不到 |
| `AudioChannels.Ui` / `AudioChannels.Se` | `AudioChannels.UI` / `AudioChannels.SE` | 改标识符，**编译器逐处报错**；**常量值不变**（仍是 `"ui"` / `"se"`）——传字符串字面量的调用与配置里的通道名一个字都不用改。示例里的 `PlayUiClick` / `SetSeVolume` 一并更名为 `PlayUIClick` / `SetSEVolume` |
| `IEventStream<T>.OnNext` / `OnCompleted` | `Emit` / `Complete` | 去 Rx 命名，编译器报错级。`IBufferedEventStream<T>` / `IReactiveProperty<T>` / `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` / `SettingRef` 一并跟进 |
| `Subscribe(Action<T> onNext)` 的形参名 | `Subscribe(Action<T> handler)` | **源兼容**——只影响具名实参调用；其中 `ReactiveProperty<T>.Subscribe` 这一处是**唯一触及 `[0.2.0]` 已发布面**的 |
| `ReactiveListChangeKind` | `ReactiveCollectionChangeKind` | **无需迁移**——该词表未发布 |
| `ReactiveList<T>` / `ReactiveDictionary<TKey,TValue>` 的 `GetEnumerator()` 返回类型 | 结构体枚举器 | **无需迁移**（转 `IEnumerable<T>` 照旧成立）。要拿零装箱收益，把 `foreach` 的静态类型写成具体类型即可 |

#### 行为变更（编译器抓不到，升级前请自己核对）

- **遮罩从此真正吃点击**：此前点得穿的模态遮罩以后点不穿，`clickToClose` 也才真正生效（根因与实测见 `### Fixed` → UI）。
- **`ShowTipAsync` 的完成时机**：在「已创建并开始播放」时完成，不再是「播放结束」。
- **`UIViewBase.OnBlur` 不再置 `IsPaused`**：暂停改由 `OnPause` / `OnResume` 承担（见 `### Changed` → UI）。
- **`UIDefaultController` 默认静默**：需要它的日志就 `new UIDefaultController(verbose: true)`。
- **`ReactiveProperty<T>.Value` 的 getter 改为宽容读取**：已释放后不再抛（见 `### Changed` → Reactive）。

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

| 旧写法 | 新写法 |
| --- | --- |
| `UpdateLOD` | `UpdateTier` |
| `Register(..., initialLOD: lod)`（`RegisterLate` / `RegisterFixed` 同） | `initialTier: tier` |
| `UpdateLOD.Tier3` 等枚举成员 | 不变（成员名与枚举值都没动） |
| `Register(node, depth: 0, ...)`（`RegisterLate` / `RegisterFixed` 同） | `Register(node, order: 0, ...)` |

#### Bootstrap

- **【破坏性】`GameLauncher` 拆为抽象底座 + `DefaultGameLauncher` 默认实现**（用户提议：「把它变为虚基类，目前的实现挪到子类作为默认实现，留下 `ConfigureStages` 让第三方自己实现」；命名按用户裁定**交换**——抽象的那个取 `GameLauncher`，具体的叫 `DefaultGameLauncher`）。**底座**只留生命周期接线（`Awake` = `DontDestroyOnLoad` + 调登记；`Start` = `async void` + 异常收敛；`OnDestroy` = `Shutdown`）与 `protected abstract void ConfigureStages()`，**不带任何配置字段**；**默认实现**装原八个 `[SerializeField]` 字段与原来的 `ConfigureStages` 方法体（`virtual` → `override`，保持非 sealed）。拆的理由：**Unity 无法隐藏继承来的序列化字段**，所以「完全自控的人 Inspector 里干净」只能靠类型层次保证；`abstract` 顺带让编译器兜住「忘了覆写」，杜绝「以为在自控、其实默认组合悄悄跑了」的中间态。**迁移（一行为记）**：原先 `class MyLauncher : GameLauncher` 现在**编译不过**（继承到抽象成员未实现）——① 想要字段面就改继承 `DefaultGameLauncher`；② 想要完全自控就在 `GameLauncher` 上实现 `ConfigureStages`（Inspector 里零字段）。全仓**无任何场景/预制体挂过它**（按名字与脚本 GUID 双查零命中），故不涉及存量序列化数据；`LogCategories.GameLauncher` 是字符串分类，与类型名无关，不动

#### Asset

- **Asset 并发初始化修复**：`InitializeAsync` 并发调用共享同一初始化任务（门面 + 实现层），`Destroy()`/`SetInstance()` 使在途初始化结果作废

#### Update

- **Update 档位类型更名为 `UpdateTier`（破坏性）**：`UpdateLOD` 更名为 `UpdateTier`，公开参数 `initialLOD` 更名为 `initialTier`；枚举成员 `Tier0..Tier7` 与 `Max`、枚举值、行为均不变。旧名有三处问题：本仓 3 字母缩写在标识符中一律 PascalCase（`JsonUtility` / `CsvLoader` / `UIHudItem`），只有 LOD 全大写，且已与 UI 模块的 `UpdateLod` 分裂成只差大小写的两种拼法；LOD 在 Unity 语境里指网格/贴图细节层级，与本模块「档位不是精度，是采样间隔」的语义相冲。类型名自 0.2.0 起即公开（当时成员为 `Frame1..Frame16`），故属破坏性变更。UI 模块自己那套 `Lod` 拼写（`UIViewBase.UpdateLod` / `LodDriver` / `LodDemandChanged` 等）一并统一到 `Tier`——那批 API 与其他面板级 LOD 特性同为未发布内容，不构成破坏性变更

- **Update 顺序参数 `depth` 更名为 `order`（破坏性：仅参数名）**：语义与行为零变化（仍是桶内按序插入、同值按注册先后），改的是名字与文档。旧名说的是「节点在**树**中的深度」，而节点系统已在本批次移除；公开文档还写着「数值越小越先执行」，可桶内下标同时决定切片相位——切片档里它并不表达「本帧谁先跑」，只表达排在哪一相位。新名与重写的 doc 一并说清这双重作用

- **Update 补写派发次序契约与边界声明（纯文档）**：README 新增「派发次序」与「分组怎么做」两节，写明一帧内的确定性次序（时间轴 → 档位 → 桶内 `order` → 时机），并给出反向声明——这些次序只保证**可复现**、不保证**可依赖**（跨档位的节点本就不在同一帧派发，需要「A 之后才做 B」请走事件）；「设计原则」补一条**不做并行**（派发是主线程同步回调、没有等待点，异步/分帧由节点自理，通用并行编排归 Pipeline）。分组则明确不提供 API，只列三种做法与代价——组一旦进入框架就要回答生命周期与嵌套，那是被删掉的节点系统。此前这些边界只散落在代码与讨论里，第三方读者只能靠猜

- **UI `OnUpdate` 携带 `deltaTime`/`time`（破坏性）**：面板可声明较低档位而被降频派发（Tier3 约 133ms 一次）。若面板继续用 `Time.deltaTime` 做积分，每 133ms 只前进一帧的量——**慢 8 倍**。故 `deltaTime` 必须由派发方给出，取值是「距上次派发」的间隔。这是 LOD 的正确性前提，不只是风格统一。门面无参 `Update()` 一并删除：它给不出正确的 `deltaTime`

- **Update 派发期间的操作改为帧末生效**：`Disable` / `Enable` 原先在派发期就地改活表，而遍历是「读 `entries[i]` → 回调 → 写回 `entries[i]`」，回调里改动下标更小的元素后写回会覆盖顶替上来的节点整条条目（对方永久停更）；现与注册/注销统一走操作队列，按调用顺序在帧末应用。代价是派发期发起的 `OnDisable`/`OnEnable` 推迟到帧末——不破坏契约（不会出现「`OnDisable` 之后又 `OnUpdate`」），且迭代外调用仍立即生效

- **Update `Unregister` 等门面形参放宽到 `IUpdateLifecycle`**：三个时机各有自己的接口，注销/启用/禁用/查询不再以 `IUpdateable` 为形参；门面把请求转发给各时机，只有持有该对象的那套会动作，因此不必维护「节点属于哪个时机」的映射表（那是第二份真相，漏同步即幽灵条目）

- **Update 切片改为步长切片**：原实现按连续下标区间划分，`count` 不是 `sliceCount` 整数倍时尾部切片会被夹空、前面的切片超载（17 个条目 8 个切片派发成 3,3,3,3,3,2,0,0，最后两帧白跑一遍循环）；改为「本帧只处理下标 ≡ sliceIndex (mod sliceCount) 的条目」，每帧派发量只差 1

- **Update 重复注册去重**：同一对象重复注册改为「重新注册」（先摘旧条目再按新 LOD/Depth 插入）。此前会产生两条条目、每帧被派发两次——树里挂了两个 `UpdateNode` 即触发，且单值桶索引表达不了「两条条目分处两个桶」，注销时的「删净」语义会漏删

- **Update 可见性收敛（破坏性）**：`UpdateScheduler` 由 `public` 改为 `internal sealed`，对齐「实现类默认 internal sealed」的框架约定；第三方若直接 `new UpdateScheduler()` 会编译不过，但正因它的内部结构仍在演进，收口可避免依赖上内部细节

- **Update LOD 档位改名（破坏性）**：`UpdateLOD.Frame1/2/4/8/16/32` 改为 `Tier0..Tier5`，枚举值与行为均不变（节拍基准的改动见 `### Changed` 的「Update LOD 节拍由帧数改为时间」一条）。旧名把「每 N 帧」这一实现细节写进了标识符，而档位的真正含义（周期）属于模块约定——做成序数名后，调整节拍基准时不必再改一次名。迁移按 `Frame(2^k) → Tier(k)` 机械替换（`Frame1`→`Tier0`、`Frame8`→`Tier3`、`Frame32`→`Tier5`），第三方只需改标识符

- **Update LOD 节拍由帧数改为时间（破坏性）**：切片节拍原先每帧推进一格，故第 k 档的周期是 2^k **帧**、随帧率缩放（`Tier3` 在 30fps 下 267ms、144fps 下 56ms，跨度 4.8 倍）。现按各轴自己的时间推进（一格 = 1/60 秒），周期与帧率无关。**固定步轴除外**：`Time.fixedTime` 每步恰好等长、本无漂移可修，那里保持每步一格，档位含义是「每 2^k 个固定步」。每帧最多补 3 格——上限决定「周期精确」能覆盖到多慢的帧（余量恒小于一格，故帧长不足 N 格时该补的格数不超过 N），取 3 即帧长 50ms（约 20fps）以内精确；补不上时丢弃**整格**债务而非累积到后续帧，卡顿不会滚雪球。切片算法本身、每帧派发量上限、`deltaTime` 语义均未变，高帧率下只是多出「本帧不推进」的空帧。副作用：`timeScale = 0.5` 时的节流强度不再与 `timeScale` 无关——逻辑轴按逻辑时间计拍，墙钟周期随之翻倍（这正是「逻辑时间轴」的应有之义，但推翻了旧文档的说法）

#### Event

- **事件流成员去 Rx 命名（破坏性）**：`IEventStream<T>.OnNext` → `Emit`、`OnCompleted` → `Complete`，订阅参数 `Subscribe(Action<T> onNext)` → `Subscribe(Action<T> handler)`（波及 `IEventStream<T>` / `IBufferedEventStream<T>` / `IReactiveProperty<T>` / `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` / `SettingRef`）。**对第三方的影响面**：XEvent 的公开面是 0.2.0 之后新增的、尚未进入任何版本段，改的是**从未发布过**的名字；唯一触及已发布面的是 `ReactiveProperty<T>.Subscribe` 的**参数名**（源兼容，仅具名实参调用受影响，仓内为零）。**为什么改**：`OnNext` / `OnCompleted` 是 Rx（`IObserver<T>`）术语，与本仓公开面语汇不一致——`On` 前缀在 Update / Pool / UI / Data 等十余处都是「框架回调使用方」的方向（`OnUpdate` / `OnRent` / `OnBound`），而这两个成员是持有者**推入**面；`OnCompleted` 还与 Pipeline 的同名**事件**（`IPipeline.OnCompleted`，`+=` 订阅）同名不同形。`Emit` / `Complete` 与 `Subscribe` / `Dispose` 组成全直白成员面，且不与 Message 的 `Publish` 争词。**为什么现在**：公开面从未发布是唯一窗口，越过后即成为对第三方的破坏性变更；`725422c`（去 Rx 命名）当时的「成员方法名保守保留」是为收窄改动面的权宜（该子句只留在提交信息里，CHANGELOG 那条被丢了），本次补齐。**已评估与否决的替代名**：`Publish`（与总线同词不同契约，会糊掉「流 vs 总线」二分）、`Subject`（带回整套 Rx 契约期望）、`Send`（MediatR 语感为「一对一请求」）、`Invoke`（与 Unity `MonoBehaviour.Invoke` 的按名延迟调用撞名，且描述机制而非动作）、`Broadcast`（本仓文档里「广播」正是总线的词，另撞 `MonoBehaviour.BroadcastMessage`）。内部一并清理：`SubscriptionNode<T>.OnNext` 字段 → `Handler`；`Deliver` 参数 → `handler`。**纯改名，行为零变更**（异常日志前缀仍为 `[Event]`）；Pipeline 的 `OnCompleted` 事件与 Input 的 `IRebindingOperation.OnCompleted` 是各自的事件，不在本次范围内。

- **Reactive README 标注引擎依赖为「已知待解决」**（**同版内已解决**：事件流引擎下沉为独立模块 `XFramework.XEvent`，见 `### Changed`）：本模块直接 `using XFramework.XMessage.Internal` 取 `EventStream<T>`，此前 README 把这条依赖写成「单向依赖：Reactive → Message」就算了事，读起来像是受支持的公开契约。现如实标注：这不违规（没有任何成文规则禁止跨模块引用该命名空间），但「没有规则」正是问题——全框架共用一个 asmdef，`internal` 不构成编译边界，既无编译器约束也无成文约定。同类引用合计 5 个跨模块文件（Reactive×2 / Input×1 / Settings×2），消费面只有 `Subscribe` / `OnNext` / `OnCompleted` / `Dispose` 四个成员；统一方案尚未确定。这条待办此前只活在一个提交信息里（「全仓库的模式……其余仍待统一方案」），每次回看都要重新考古一遍，故落到 README

- **补写两组容易被踩的语义（仅文档、注释与测试，无行为变更）**：① Reactive 模块 README 新增「派发顺序与重入」一节——派发顺序是 **LIFO**（后订阅先收到），且**回调内写入本属性会留下陈旧值**：订阅者 A、B 中 B 后订阅（故先收到），B 在收到 `1` 时写入 `2`，嵌套派发让 A、B 都先收到 `2`，随后外层循环继续把 `1` 投给 A——A 最终停在一个 `Value` 已不再是的值上，且不会再有通知来纠正它，故回调里不要写入本属性。这两条都由底层事件流决定，此前只能从引擎的代码注释里读到。② Message README 的「线程」提示补上引擎的**主线程前提**：内部的锁与快照只保证订阅链表与终止标志在并发退订下不被写坏，**不构成「可以多线程发布/订阅」的许可**——`SubscriptionNode.IsDisposed` 在派发循环里是无锁读取的，且快照收集与 `_publishDepth` 自增之间存在窗口，并发退订可能让节点在该窗口内被回池复用。另为 `UIPanelBinding.RegisterBinding` 补一条注释：那里的「立即同步当前值」**不是**上面订阅立即回调的重复——后者被 `isActiveAndEnabled` 挡住，面板未激活时只能靠它兜底，原样看像冗余、删掉即让未激活面板的绑定静默失效。①②两条语义均已补测试钉住（`Dispatch_OrderIsLifo_LaterSubscriberReceivesFirst` / `ReentrantWrite_LaterSubscriberEndsOnStaleValue`），断言值就是文档里的描述，改语义时测试会先红

- **事件流引擎下沉为独立模块 `XFramework.XEvent`（破坏性：仅对仓内）**：引擎（`EventStream` / `BufferedEventStream` 与其节点池、快照池）原先物理上住在 `XMessage.Internal` 里，而 Reactive ×2 / Input / Settings ×2 共 **5 个跨模块文件**直接 `using` 它——全框架共用一个 asmdef、`internal` 不构成编译边界，既没有编译器约束、也没有成文约定（Reactive README 与旧 CHANGELOG 都如实记着这一点，`61a5961` 只解掉 UI 那一处）。追根溯源它本来也不属于 Message：生于 `Runtime/Reactive/Internal/Subject.cs`（`1699e7f`），改名为 EventStream 五分钟后才被搬进 Message（`127a634`）。现按 **Pipeline 先例**（公开接口 + 静态工厂 + internal 实现）抽为中立模块：公开面 `IEventStream<T>` / `IBufferedEventStream<T>` / `EventStream.Create` / `CreateBuffered`，实现与池留在 `XEvent.Internal`；依赖方向随之变成**公开、单向**（Message / Reactive / Input / Settings → Event），全仓公开的模块间依赖从 1 条（Bootstrap → Pipeline）变为 5 条。**第三方零公开 API 变更**（引擎从来是 internal），唯一可观察差异是引擎异常日志的前缀 `[Message]` 改为 `[Event]`（若你在 `LogAssert` 里匹配过该串，需同步）。四条原先只写在 internal 注释里、却已被下游当承诺依赖的语义随接口**升格为公开契约**：`onEmpty` 的两条硬约束（可能发生在派发途中、回调不得回头调用本流）、`OnCompleted` 只置标志不清订阅（与 `Dispose` 的差别，Settings 已据此承诺「句柄静默失效」）、`SubscriptionCount` 是拉取面而非逻辑分支依据、`HasCachedValue` 不得代替「有没有缓冲流」的判断。顺带收敛：`OnEmpty` 从可变的 internal 字段改为**构造期注入的只读字段**（工厂参数），退订入口降为 private。`DispatchListPool` 与 `ActionDisposable` 在两个模块各留一份 internal 实现——不合并、也不提升为公开面，理由写在两处注释里（十行适配器不值得跨模块绑定，先例是 UI 的 `UIBinder` 内联）。引擎的语义总表（LIFO 派发、派发中退订、重入、异常隔离、completed、节点池与 ABA 防护、`onEmpty` 契约、缓冲重放）与线程契约现由 `Runtime/Event/README.md` 承载，Message / Reactive 的 README 改为指回。配套：引擎测试迁至 `Tests/Runtime/Event/`，另补 4 条公开面契约用例（空流通知、`Dispose` 不回调、订阅数、缓存值）；Message 侧新增 `MessageInternalsTests` 锁它自己那份 `ActionDisposable`

#### Message

- **销毁令牌迁入 Message 并真正接上订阅绑定（破坏性）**：`IDestroyCancellationToken` 从 `XNode.BaseNode` 迁到 `XMessage`，`MessageManager.TryBindToDestroy` 增加对它的识别分支。此前 `MessageManager` 的文档承诺「其他生命周期类型由 Core 层扩展方法负责桥接」，而**那段代码从未存在**——`TryBindToDestroy` 实际只有 `is MonoBehaviour` 一个分支，非 MonoBehaviour 对象的同步订阅根本没有自动退订途径，只能自己持有返回的 `IDisposable`。迁移后实现该接口即可自动退订

- **Message 异步订阅签名变更（破坏性）**：`SubscribeAsync` 处理器由 `Func<TMessage, UniTask>` 改为 `Func<TMessage, CancellationToken, UniTask>`；处理器收到的令牌即订阅自身令牌，退订会取消在途 await

- **Message 请求处理器签名变更（破坏性）**：`Register` 处理器由 `Func<TRequest, UniTask<TResponse>>` 改为 `Func<TRequest, CancellationToken, UniTask<TResponse>>`；`RequestAsync` 补可选 `CancellationToken`（令牌语义见下条）

- **Message `RequestAsync` 取消语义变更（破坏性）**：调用方令牌此前只原样转发给处理器、**不**中断本次等待——处理器若不响应令牌，调用方将永久挂起。现对齐 `PublishAsync`：令牌一方面仍原样转发给处理器，另一方面用于取消本次等待，取消时抛 `OperationCanceledException` 但不中断已启动的处理器。破坏性在于「传入已取消的令牌仍能正常拿到响应」这一写法不再成立，对应的既有测试已改写为用令牌同一性验证转发，并新增「在途取消」用例锁定新语义

- **Message 异步处理器执行时机变更**：异步订阅不再与同步订阅者同链交错，改为排在同步投递之后独立派发

- **Message 缓冲通道结构**：`MessageBroker` 的 6 个业务字典合并为「通道对象」两表（`MessageChannel<TMessage>` + `KeyedChannelStore<TKey,TMessage>`），键值键改由 `(消息类型, Key 类型)` 复合而成

- **Message 淘汰即回收**：`EvictBufferedChannel` / `EvictBufferedChannels` 丢弃重放缓存后顺带回收因此变空的通道与存储表项，不再残留「已可回收但仍在表中」的空壳；`TrimEmptyChannels` 退居兜底与诊断手段，常规路径下返回 0。返回值语义不变（只计淘汰数，不含回收数）。同时补文档：带 Key 的淘汰是语义要求而非可选优化——漏淘汰会把上一个同 Id 实体的旧值重放给新订阅者

- **节点订阅解析变更**（**该扩展面随节点系统在本版内被整体删除**，见 `### Removed`「删除节点系统」）：`BaseNode` 实现 `IMessagePublisher`/`IMessageSubscriber`，`NodeExtensions.Subscribe` 改以 `BaseNode` 为接收者以消除与 `MessageManager.Subscribe(this IMessageSubscriber, ...)` 的重载二义

- **Message 统计字段更名（破坏性）**：`MessageBusStats.MessageTypeCount` 改为 `ChannelStoreCount`。它的值本就是「类型通道表项数 + 键值通道表项数」，同一消息类型既有类型通道又配了键值通道（或配多种 Key 类型）会各占一项而重复计数——原名在说谎，而统计接口的用途正是排查泄漏，误导性命名会直接浪费排查时间。`ToString()` 的标签也由「类型」改为「通道表」。无行为变化，仅更名

- **Message 接口可见性收敛**：`IMessageBroker` 由 `public` 改为 `internal`——它只有一个 `Clear()` 成员，实现类 `MessageBroker` 与实例替换点 `MessageManager.SetInstance` 均为 internal，对外既无法实现也无处注入，公开它只会得到一个不可用的契约。`IMessagePublisher`/`IMessageSubscriber` 保持公开（它们是扩展方法的接收者类型，`BaseNode` 实现了二者）。外部代码本就无法取得 `IMessageBroker` 实例，实际无可观察影响

- **模块拆分**：消息总线与事件流引擎迁入新 `Runtime/Message/`（命名空间 `XFramework.XMessage`），Reactive 仅保留响应式属性；公共类型名不变，使用方仅需改 using（编译期破坏性变更）

- **引擎去 Rx 命名**：Subject→EventStream、ReplaySubject→BufferedEventStream、AnonymousDisposable→ActionDisposable；Unit 删除（InputManager 帧脉冲改发 `Time.frameCount`）；事件引擎日志前缀定稿 `[Message]`

#### Reactive

- **把「派生值必须释放」从沉默前提变成文档明写的规矩（仅文档与注释）**：`ReactivePropertyExtensions.Select` 在**构造时**就订阅源，故源的事件流一直引用着派生值——返回值被就地丢弃时它活到源消失为止，且此后每次源变化都还会跑一遍 selector。而框架自己的文档此前**教的就是这种写法**：`Settings/README.md` 的 `Settings.MasterVolume.Select(v => $"{v:P0}").BindToText(volumeLabel);` 与 `UI/README.md` 的 `_vm.Hp.Select(h => h / 100f).BindToFillAmount(hpBar);`。Settings 那条尤其重：句柄活到进程结束、**刻意不实现 `IDisposable`**，所以每执行一次就永久多一个活订阅并持续跑 format。三处示例改为安全写法，并点明一个反直觉之处：**`BindToXxx` 返回的是绑定方的句柄，释放它并不能释放派生值**，所以「把绑定句柄交给 `Track`」也不够——归口要用 `ViewModelBase.CreateReadOnlyProperty`（创建 + 登记）或自行持有并释放。UI README 的 `ShopPanel` 示例顺带改为「派生值在 ViewModel 的 `OnBound` 里建、面板侧只绑不建」，并给同一段里另外三个被丢弃的绑定句柄补上 `Track`

- **订正 Reactive 模块三处不实的契约陈述（仅文档与注释，无行为变更）**：① `out actual` 的理由写错了——原话说「写入派发中订阅者可能释放目标，那之后再读 `Value` 就会抛」，但本模块的读取是宽容的（`ReactiveProperty` 已释放后仍返回最后的值、`ReadOnlyReactiveProperty` 从来不抛），那句话在写下的下一版就失效了；现改为真实理由：`actual` 由实现直接给出，读它不必依赖 `Value` 对「已失效」采取何种策略。② 删掉 `actual` 会「规范化（取整、钳制到上下限）」的说法——随框架发布的两个实现都不规范化，`actual` **恒等于**传入的 `value`，唯一会规范化的是 UI 测试里的替身；`UIBinder` 那两处回填分支对发布实现永不触发，这点也一并写明。③「读宽容、写严格」与「相同值去重」此前被写成模块普遍原则、并称三个实现都满足，而 Settings 的 `SettingRef` **两条都不满足**（实例替换无条件重放、设置类型注销后读取抛 `InvalidOperationException`）。现新增「接口承诺到哪为止」一节，用表格逐条区分「接口保证」与「具体实现的特性」；接口自身的 XML doc 也从「契约：订阅即回调 + 相同值不通知」收紧为「**契约只有一条**：订阅时立即同步回调当前值」

- **`ReactiveProperty<T>.Value` 的 getter 改为宽容读取（破坏性）**：已释放后读 `Value` 此前抛 `ObjectDisposedException`，而同一模块的 `ReadOnlyReactiveProperty<T>.Value` 刻意不查、返回最后的值——`Value` 这个最常用的成员对「已释放」有两种态度。抛异常的那一侧代价已经外溢：`UIBinder` 的双向绑定必须靠 `TryWriteValue` 的 `false` 提前收手、否则回读 `Value` 就会把异常抛进 UI 事件回调，测试里还要专门写一条「目标失效时不抛」来钉住这层防御。现统一为**读宽容、写严格**：读是只读操作、不改变任何状态，让它抛只会把「先 Dispose 再读一次收尾值」这类正常写法变成地雷；写入与订阅是编程错误，照旧抛。这与框架其它门面对「未就绪/已失效」的惯例一致（`InputManager` 未初始化时查询返回默认值）。迁移影响：依赖「getter 抛异常」来判断失效的第三方代码需改判 `TryWriteValue` 的返回值或自行记录生命周期；异常消息同步由「请勿再访问 Value 或订阅」改为「请勿再写 Value 或订阅」

- **`IReactivePropertyWriter<T>.TryWriteValue` 回传写入后的实际值（破坏性）**：签名由 `bool TryWriteValue(T value)` 改为 `bool TryWriteValue(T value, out T actual)`。原签名只在目标已失效时返回 `false`、成功时一律 `true`（即便值被去重、压根没写），于是调用方要拿「规范化后的值」只能在写入成功后再读一次 `Value`——而那次读取可能落在写入的同步派发途中、目标已被某个订阅者释放之后。`UIBinder` 的 Slider / Toggle 双向绑定正是这么写的，异常会经 `Slider.onValueChanged` 被抛进 UI 事件回调，恰是引入该接口要挡掉的那类异常。现把「写入后的值」随写入一并回传：`ReactiveProperty` 从**已赋值的字段**取出而非走 getter，`SettingRef` 回传句柄解析到的实时值。这条约束至此由接口本身保证，不再依赖 `Value` 对「已释放」采取何种策略（后续即便调整该策略也不会让调用方重新踩上）。三个实现（`ReactiveProperty<T>` / `SettingRef<T,TField>` / 测试替身 `RoundingWriter`）与两个调用点同步更新

- **补 4 条测试（`TryWriteValue` 此前零覆盖）**：3 条在 `ReactivePropertyTests`（存活写入与 actual 回传、已释放返回 false 不抛、写入派发中目标被释放仍返回 true 且不抛），1 条在 `UIBindingTwoWayTests` 走完整的滑条双向绑定路径。UI 侧那条已用「临时恢复旧的二次读取」验证过改前必红，失败栈为 `ReactiveProperty.get_Value → SliderTwoWayBinding.OnSliderChanged → UnityEvent`

- **`ReadOnlyReactiveProperty<T>` 实现 `IReactiveProperty<T>`，UIBinder 删掉 7 个重复重载**：`Select` 派生值此前只实现 `IDisposable`，落在接口契约之外——于是既不能交给收 `IReactiveProperty<T>` 的绑定 API，也不能作为 `Select` 的源继续映射（`rp.Select(a).Select(b)` 编译不过）。最直接的代价是 `UIBinder` 里长回了 7 对**方法体完全相同**的重载（`BindToText` / `BindToSlider` / `BindToFillAmount` / `BindToSprite` / `BindToToggle` / `BindToActive` / `Bind`），每对里具体类型那版就是为绕过这个缺口而存在。这正是 `SettingRef` 那条裁定的残留：「必须是**替换**而非新增重载——两版并存时具体类更精确、永远胜出，接口版会沦为死代码」。现补齐接口实现并删除那 7 个重载（对调用方源码兼容，重载决议改走接口版），另补 4 条测试：2 条锁定「派生值可作为接口使用 + 可链式」，2 条在 `UIBindingInterfaceTests` 里用真实的派生值（而非测试替身）走一遍已被删除的重载原本覆盖的路径

- **Reactive 消息总线内部去 R3 化结构**：订阅直落自研事件流，移除内部 ObservableSignal 包装层与信号缓存；公共 API 与行为语义不变（投递顺序、缓冲保留、completed 语义保持）

- **Reactive 清理**：裁撤 Signal/ISignal/IReadonlySignal 死代码（公开类型名中 Signal 术语退场），ReactiveProperty 接入自足 `IReactiveProperty<T>` 接口链；注释与文档同步去 R3 叙述

- **变更词表改名 `ReactiveListChangeKind` → `ReactiveCollectionChangeKind`**（未发布，[Unreleased] 内原地订正）：字典与列表共用同一份词表——`Add` / `Remove` / `Replace` / `Reset` 四个值的语义完全相同，两套枚举只会各自漂移，消费方还得写两个 `switch`。实测 39 处 / 6 文件，`git mv` 文件与 `.meta`（GUID 保留），破坏是编译器报错级。

- **`ReactiveList<T>` / `ReactiveDictionary<TKey,TValue>` 的遍历改为零装箱**：两者的 `GetEnumerator()` 此前返回 `IEnumerator<T>`，**每次 `foreach` 都把 BCL 的结构体枚举器装箱一次**（还写在已知限制里，让使用方「热路径用索引 `for`」）。现照 BCL 原样公开结构体枚举器（`List<T>.Enumerator` / `Dictionary<TKey,TValue>.Enumerator`）+ 显式接口实现：**直接用具体类型 `foreach` 零分配**；经接口（`IReadOnlyList<T>` / `IReadOnlyDictionary<TKey,TValue>`）遍历仍装箱一次——那是接口本身的要求。源码兼容（接口转换照旧成立），两类型均未发布。**实测**（一次性探针，尺子 `Profiler.GetMonoUsedSizeLong`）：10 万次直接 `foreach` 的堆增量 512000 B（列表）/ 507904 B（字典）→ **0 B**（BCL 对照始终 0 B）。装箱的充要原因（反射可验）：返回类型是接口；该原因已被 `ReactiveEnumerationAllocationTests` 的结构断言锁住，粗尺子测量作第二道防线。

#### Data

- **未初始化异常消息统一**：各模块 guard 消息改为中文 + `[模块]` 前缀 + 修复提示；`DataException` 改为继承 `InvalidOperationException`

#### Save

- **Save 契约重整（破坏性）**：`ISaveManager` 全面收敛——异步成员统一 `Async` 后缀（此前同一文件里两种读法，`SaveAsync` 保留而 `GetSlotMetas`/`DeleteAllSlots` 丢弃）；玩家隔离 API（`SetCurrentPlayer` / `ClearCurrentPlayer` / `CurrentPlayerId` / `GetAllPlayerIdsAsync` / `DeletePlayerAsync`）由实现类内部成员提升到接口，消除门面里靠 `is` 降级访问、自定义实现静默空返回的问题；去掉同步成员（`DeleteSlot` / `SlotExists`）——同步 IO 在主线程执行，而 Console 等平台的 Provider 可能是数百毫秒的平台 SDK 调用

- **Save 存档 IO 后切回主线程**：Provider 的 IO 用 `RunOnThreadPool(configureAwait: false)` 完成时停留在子线程，此前 `SaveManagerImpl` 中所有 IO `await` 之后的代码——含 `DataManager.ApplySnapshot` 与第三方 `IDataBlock` 回调——都在线程池上执行，既是「Unity API 只能在主线程调用」违规，也是与主线程读写共享状态的真实竞态。代价：这些方法依赖 PlayerLoop 泵，**禁止在主线程同步阻塞等待**（`.GetAwaiter().GetResult()` 会死锁）

- **Save 序列化移出主线程**：载荷的序列化与反序列化改在池线程上执行（`CreateSnapshot` / `ApplySnapshot` 必须留主线程，它们要遍历并回调第三方数据块）。固有代价：目标类型的字段初始化器与构造器会在后台线程执行，第三方数据类若在其中访问 UnityEngine 对象会抛异常

- **Save 校验前移**：槽位号与 playerId 的校验集中到 `SavePathUtility` 并在入口快速失败。此前校验只在构造路径时生效——`SetCurrentPlayer("../../outside")` 会先污染玩家上下文、直到下一次保存才失败（那时快照已生成、脏标记已清空）；`SetCurrentPlayer(".")` 更能穿过路径沙箱把存档写进域根、绕开玩家隔离，而 `DeletePlayer(".")` 会删掉域根下全部存档

- **Save 写操作门禁**：`IsBusy` 改为 `Interlocked` 原子占位并覆盖全部写操作（保存/加载/删除/复制/移动）。此前删除类操作不受约束，可在「已写 `.tmp`、尚未替换」的窗口里把目标文件抽走；且普通 `bool` 字段跨线程无可见性保证。读操作刻意不进保护（替换是原子的，读只会看到完整的旧文件或新文件），该取舍已写入注释

- **Save 枚举结果确定化**：`GetSlotMetasAsync` 结果按槽位号升序排序（此前是文件系统顺序，跨平台不确定）；损坏槽位带 `SaveMeta.isCorrupted` 标记保留在列表中，不再从列表消失——消失会与 `SlotExistsAsync` 返回 true 自相矛盾，界面既看不到也删不掉它

- **Settings 存储替换与释放语义收紧**：`Store` setter 明确为「只换后端、不迁移数据」并在替换时打 LogWarning（刻意不做隐式重新加载——那会静默丢掉内存中尚未 Save 的修改，是更难查的故障）；`Load` 防御 store 违约返回 `null`；`Save(null)` 由静默 return 改为抛 `ArgumentNullException`，与 `Apply(null)` 对齐；`Dispose` 后除自身外所有公开成员抛 `ObjectDisposedException`

#### Pool

- **XPool 池泛型收紧为引用类型约束（破坏性）**：`IPool<T>` / `Pool<T>` / `PooledObject<T>` 加 `where T : class`。此前值类型能实例化 `Pool<int>` 并通过编译，但实例复用依赖引用同一性——Editor 活跃追踪集的重复归还检测、`CountActive` 的取还配对都会失真，而 XML 文档里只是一句「应为引用类型」的建议。Unity `UnityEngine.Pool`、GameFramework、Microsoft.Extensions.ObjectPool 全部带此约束。连带两处：`PoolManager.RemovePool<T>` 必须同步加约束（否则 `is IPool<T>` 触发 CS0452），`HasPool<T>` 一并对齐；`Pool<T>` 构造的 `config` 默认值由 `default(PoolConfig)` 改为 `PoolConfig.Default`——前者 `CollectionCheck` 为 false，会让「直接 new」与「经 PoolManager 建池」两条路径在 Editor 下的调试安全性不同

#### Audio

- **`AudioChannels.Ui` → `UI`、`AudioChannels.Se` → `SE`**（同一轮的命名统一）：两个都是**两字母缩写**，按新规则该全大写。同文件的 `Bgm`（三字母）与 `Voice`（词）保持 PascalCase——这不是例外，正是同一条规则的另一半。**常量值不变**（仍是 `"ui"` / `"se"`），只改标识符；`AudioSample.PlayUiClick` → `PlayUIClick`、`SetSeVolume` → `SetSEVolume` 一并跟进。

#### Settings

- **Settings README 的 `SettingRef` API 表漏了 `TryWriteValue`（订正）**：句柄实现了 `IReactivePropertyWriter<TField>`，故可直接交给 `UIBinder.BindTwoWay` 做双向绑定——而这条兼容性此前只能从 Reactive 模块的 README 或源码里读到，Settings 自己的表里查不到。现补上该行，并把「也实现 `IReactivePropertyWriter<TField>`」列为显式一条

- **Reactive 补上其余三处文档（仅文档与注释）**：① `Select` 此前只声明 `ArgumentNullException`，实际还会在源已释放时从 `source.Subscribe` 抛 `ObjectDisposedException`（同一函数上一行的取值却是宽容的——读得到、订不到），以及把 selector 自己抛的异常原样上抛；另写明 selector **在构造时会被调用两次**（取初值一次 + 源订阅的立即回调一次），非纯函数会跑两遍且只有第二次的结果留在 `Value` 上。② README 补主线程契约：它把「锁 + 快照线程模型」列为卖点，而引擎 README 明说按主线程设计，`SettingRef` 有「读写须在主线程」而本模块一字未提；跨线程写入会让去重判断与派发载荷分叉。③「派发顺序与重入」一节的两条建议改为可执行：顺序需求不能「靠比较收到值」解决（那建立不了订阅者之间的顺序，框架也没有排序手段），推迟写入也不能「经 `UpdateManager` 注册」了事（它只有 `Register/RegisterLate/RegisterFixed`，要 `IUpdateable` 并自行注销，没有一次性下一帧入口）

- **顺带修好 `SettingRef.Value` 丢失的文档注释**：`TryWriteValue` 被插入时插在了 `Value` 的 `<summary>` 与 `Value` 之间，于是那段「读取当前值；写入会与实时值比较，去重基准不缓存」的说明挂到了 `TryWriteValue` 头上，而 `Value`——一个公开成员——完全没有文档。现把注释归还给 `Value`

- **Settings 响应式契约重整（破坏性）**：移除 `ObserveField`，`Observe` 改为「订阅即回调当前对象」。`ObserveField` 用 `EqualityComparer<TField>.Default` 去重，对引用类型字段即为引用相等，`s => s.audio` 这类选择器在子对象内容变化时被静默吞掉且无文档；字段级能力改由 `SettingRef` 以**字段身份**路由，该缺陷随之消失，不必再修。两级分工至此明确：字段值变化归句柄、对象被整体替换归 `Observe`。此次变更还让 `[0.2.0]` 那条「订阅立即回调」的说明从与测试断言相反变为准确

- **Settings 实现类可见性收紧**：`SettingsManagerImpl<T>` 由 `public` 降为 `internal sealed`，对齐门面模板「公开面只留接口」。公开面从未暴露过该具体类型——`Initialize` 返回的一直是 `ISettingsManager<T>`，故实际无可观察影响

- **Settings 实例替换时向字段句柄重放当前值（行为变更）**：`Notify()` 此前只推 `Observe` 的事件流，从不触碰任何 `SettingRef` 自身的流，而它的四个调用点 `Apply` / `Load` / `LoadAsync` / `Reset` **全部是「换掉实例」**。于是绑定到句柄的 UI 在玩家点「恢复默认」后停留在旧值——而 README 与 `早期的框架文档` 都写着「句柄会自动跟随实例替换」，文档承诺与实现相反。现在实例一换，每个句柄都会向订阅者**无条件重放**一次当前值（即使数值未变）。刻意不做去重：可比较的只有「上一实例的值」，而需要知道的是「订阅者上次收到什么」，二者在本模块已文档化的「直改 POCO 不通知」缺口上分叉——直改成 1f 再 `Load` 回 1f，比较会判定「没变」而让订阅者永久停留在陈旧值，正是本次要消灭的那类缺陷。代价如实写进文档：回调语义应按「当前值是这个」而非「用户刚改了它」理解，非幂等的响应（如「画质变更 → 重建渲染管线」）会在实例替换时被多做一次；需要精确区分时用 `Observe`。重放注册表刻意不随 `Destroy` 清空，与门面 `DefaultPathClaims` 同因——关联关系比 `Destroy` 活得久

#### UI

- **`IUiHudProvider` 更名为 `IUIHudProvider`**（命名一致性）：同命名空间的兄弟接口是 `IUIManager` / `IUITipProvider` / `IUIController` / `IUIPanelFactory`，实现类也是 `UIHudManagerImpl` / `UIHudItem`——**只有它用了 `IUi`**。根因是**规则的空白**：`CLAUDE.md` 的「命名」小节只写了「接口 `I` 前缀」，**没有缩写大小写的约定**，于是评审没有判据可依，这条不一致长了很久。现按「两字母缩写全大写」统一（依据 .NET 框架设计指南），并把这条规则补进 `CLAUDE.md`、加一条源码扫描守卫（`NamingConventionTests`）防它再长出来。
  **对第三方无影响**：该类型尚未发布（`[0.2.0]` 明写「公共 API 不变」，全部 HUD 条目都在 `[0.3.0]` 内），故**不留 `[Obsolete]` 别名**——与 `ReactiveListChangeKind → ReactiveCollectionChangeKind` 那次同一处境。

- **`IUIManager` 新增 `IsLayerVisible(int)` / `IsLayerInteractive(int)`（破坏性变更，仅对第三方 `IUIManager` 实现者成立）**：这补上了 UI 里唯一一处「能写不能读」——`SetLayerVisibility` 早已公开，回读却只在内部实现上。正是 `UI 模块的维护向记录` 里记为「将来若要公开，走完整的接口 + 转发 + 完备性测试三步」的那一步，本轮按三步走完：接口 +2、`UIManager` 门面 +2 转发（`UIFacadeCompletenessTests` 自动覆盖）、`Tests/Runtime/UI/FakeUIManager.cs` 同步补齐。两条口径：
  **① 它们是探测型读**（未初始化时答 `true`，不套 `EnsureGlobalInitialized`）——「没有被显式隐藏过」在未初始化时同样是 `true`，与 `GetState` / `OpenCount` 同属一类；`UIFacadeProbeTests` 补了这条断言，README 的探测型清单同步更新。
  **② 读的是期望值而不是场景**：`SetLayerVisibility` / `SetLayerInteractive` 记录的字典是唯一真相，**层容器还不存在（该层没开过面板）时也有答案**——这正是这个缺口的本质。
  **迁移**：第三方实现者补两个成员即可（按期望值语义实现，通常就是查自己的层开关字典）；只想读的调用方无需改动。

- **补记「UI 门面恢复扁平」**（`9b5b426`）：`467c64d` 曾把门面分成 8 个嵌套静态类，转发时 11 个成员改了名——「门面名 == 接口名」这条唯一的人工核对手段随之失效，而接口新增成员时「忘了加转发」在第三方眼里等于该成员不存在。分组未发布（0.2.0 是扁平 API），故属撤回一个未发布的破坏性变更；完备性守卫换成 `UIFacadeCompletenessTests`（只断同名，签名由编译器兜底）。该规则已进 `CLAUDE.md`「门面保持扁平」

- **UI README 的约定式绑定章节此前描述的是一套不存在的机制（订正）**：那张表声称「面板下子 GameObject 名与 ReactiveProperty 名相同，自动根据组件类型选择属性绑定」，于是每一行都少了前缀，还列了 `InputField` / `TMP_InputField` / `TMP_Text` / `Slider(int)` 这些**代码里根本不支持**的组合。实际 `BindByConvention` 是：① 要为每个属性显式调一次，**不遍历 ViewModel**；② 拿「**前缀 + 属性名**」去子节点缓存里找，前缀决定组件（`txt_`→`Text`、`img_`→`Image`、`sld_`→`Slider`、`tgl_`→`Toggle`），且 `txt_`→`img_`→`sld_`→`tgl_` **顺序即优先级**；③ 组件类型必须与属性值类型匹配（`sld_` 只认 `float`、`tgl_` 只认 `bool`，`int` 配 `sld_` 绑不上），不匹配时**只在编辑器**打告警、Release 下静默；④ `btn_` 走另一条路（`BindClick`）；⑤ 缓存**最多 5 层深度**且只在 `Bind` 时建一次。照原表命名节点会静默绑不上——这正是它值得记一笔的原因。另订正同一节「`BindViewModel` 是等价写法」的错误：它**不绑定任何属性**，只调 `OnBound()` + 缓存组件，是那几行的前置条件

- **UI 导航栈统一入栈（破坏性）**：显示栈改存实例，`OpenAsync` 与 `PushAsync` 都入栈，`HasPrevious` → `CanGoBack`，`BackToAsync<T>` → `PopToAsync<T>`。此前只有 `PushAsync` 入栈，于是「Open 开主界面 + Push 开二级页」之后栈深恒为 1，`PopAsync`、`HasPrevious`、遮罩点击关闭会同时失效——而那恰是最常见的用法组合

- **UI 排序空间重做（破坏性，且修一处长期潜伏的正确性缺陷）**：`Canvas.sortingOrder` 名义上是 `int`，运行时却只保留 **16 位有符号**范围，超出会被静默截断回绕（实测 `100001 → -31071`、`500000 → -24288`）。既有方案 `layer × 1000` 因此只要层号 ≥ 33 就全盘失效，而框架推荐的层恰好越界：层 200 实存 `+3393`、层 300 实存 `-27679`，于是 **Top 层实际渲染在 Popup 之下**、遮罩也不在预期位置。之所以从未暴露，是因为此前没有任何用例断言过 `sortingOrder`。现全部取值经 `UISorting` 推导并落在 int16 内，另加一条把每个保留带取值写进 Canvas 再读回的守卫用例

- **UI `OnBlur` 不再置 `IsPaused`（破坏性）**：改由 `OnPause` / `OnResume` 承担。第三方若在重写的 `OnBlur` 里读 `IsPaused`，会看到旧值

- **UI `ShowTipAsync` 的完成时机变化（破坏性）**：从「播放结束」变为「已创建并开始播放」。Tip 的播放改由统一帧通路推进，故无从等待播完；需要知道何时播完请按 `TipConfig.Duration` 自行计时

- **UI `UIDefaultController` 默认静默（破坏性）**：它每次拦截都打日志，等于每开一个面板五条带字符串插值的日志。需要观察生命周期流程时用 `new UIDefaultController(verbose: true)`

- **UI 异步 API 全面接入取消令牌（破坏性）**：`IUIManager` 全部异步成员、`IUIController` 五个回调、`IUIHudProvider.AttachAsync`、`IUITipProvider` 均新增 `CancellationToken`（一律收盘）。两条提交点语义已写进文档：打开以「实例化返回」为提交点、关闭以「`OnBeforeCloseAsync` 放行」为提交点，之后一律走完——「已从集合摘除但未回池」的半关状态比「取消失效」难排查得多

- **UI `IUITipProvider` 与 `IUIHudProvider` 同形（破坏性）**：新增 `Update(deltaTime, time)` 与 `DetachAll()`

- **UI provider 归属下沉（破坏性）**：`_tipProvider` / `_hudProvider` 由门面静态字段移入 `UIManagerImpl`，`IUIManager` 相应新增五个成员。门面由此退化为纯转发——此前它要自己管懒初始化、还要回头摸 `_instance.UIRoot` 才能给 provider 挂根节点

- **UI `UnloadAsset` / `ClearAssetCache` 正名（破坏性）**：改为 `ForgetPreload` / `ClearPreloads`，并把真实作用写进文档——**只清记账，不卸载资源**。真释放见新增的 `UnloadPanelAssetAsync`

- **UI 每帧通路并入统一调度**：面板 / HUD 的每帧更新原由场景里的 `UIRootNode.Update` 驱动，那条通路既不在 LOD 调度里、也不受 `Pause` 约束（暂停游戏时面板照跑），还要求场景里必须存在 `UIRootNode`。现由 `UIManager.Initialize` 注册进 `UpdateManager`；面板 `OnUpdate` 的时机随之从「场景 MonoBehaviour.Update」变为「注入的 Update 系统内」，并从此可被 LOD 降频与统一暂停

- **UI `CloseAllAsync` 一并回收 HUD 与在播 Tip（行为变更）**：HUD 与 Tip 不是面板，但与面板共享同一个 UIRoot 与生命周期——「全部关闭」对调用方（切场景、回标题）而言就是「界面清空」，只关面板会让世界空间 HUD 与飘着的 Tip 继续留在场景里。实现里那句「已由门面处理」的注释与 README 的承诺都写着这个行为，此前并不存在。**遮罩刻意不在其列**：它是引用计数句柄，强制清掉会让别的系统手里的句柄凭空失效，要收需显式 `HideMask()`

- **UI `SetLayerVisibility` 记住期望值（行为变更）**：层容器是「该层第一次开面板」时才创建的，原实现只对已存在的容器生效，于是「先隐藏、后开面板」会让隐藏被悄悄撤销。现补一份与 `SetLayerInteractive` 对称的记忆，并在容器创建时立即应用

- **UI `UIManager.Subscribe` 的 `context` 放宽到任意生命周期对象（破坏性：签名）**：形参类型由 `MonoBehaviour` 改为 `object`，实现 `IDestroyCancellationToken` 的普通 C# 对象（ViewModel / Model）从此可用这个归口入口；两者皆非时不绑定并打一条告警。**源码兼容**（`MonoBehaviour` 实参照旧可传），但签名不同，二进制层面需重编。绑定逻辑一并改为复用 `MessageManager` 的同一份实现——此前三个重载各自手写了一遍，比底层弱三处：每次订阅分配一个闭包、令牌已取消时仍去注册、且只认 `MonoBehaviour`

#### Diagnostics

- **`UiDiagnosticPanel` 更名为 `UIDiagnosticPanel`**（同一轮的命名统一）：同级 20 个诊断面板全是 `XxxDiagnosticPanel`（`AudioDiagnosticPanel` / `InputDiagnosticPanel` / …），只有它用了 `Ui`。它是 Editor 程序集的 `internal sealed`，`InternalsVisibleTo` 只开了仓内的 Editor.Tests——**第三方碰不到，零破坏性**。

### Removed

#### Bootstrap

- **删除节点系统（破坏性）**：整个 `XFramework.XNode` 模块——`BaseNode` / `ParentNode` / `EntityNode` / `ContainerNode` / `DictionaryNode` / `RootNode` / `LeafNode`、`NodeFactory` / `NodePool` / `NodeExtensions` / `NodeUtility`、`StartupExtensions` / `StartupStages` / `AssetExtensions`，以及 `Node/Bootstrap/` 下的四个引导节点。节点系统同时承担了四件事：框架的启动路径、一套 GamePlay 架构、一个服务定位器、更新调度的桥接；只有最后一项是基础框架层该管的，而它已由 Bootstrap 模块覆盖。本项目定位是基础框架层，不该预设 GamePlay 架构。连带影响：① `GameLauncher` 改由 `Bootstrap` 驱动并移入 `Runtime/Bootstrap/`（命名空间 `XFramework.XNode` → `XFramework.XBootstrap`），仍是**可选**入口，场景里没有它也照常运转；② `UpdateManagerExtensions`（`ResolveTimeMode` / `RegisterUpdate` / `UnregisterUpdate`，接收者均为 `BaseNode`）与 `LocalizationBootstrapNode` 一并移除，Update 模块从此与节点系统无关；③ `IUpdateTimeMode` 保留但不再有自动轴发现——注册时请显式传 `timeMode:`；④ `AssetExtensions` 的 12 个方法全部是 `AssetManager` 静态方法的纯转发（`self` 参数从未被使用，其中 `PreloadAssetsAsync` 还比门面版少了 `progress` 参数），直接调用 `AssetManager` 即可

#### Update

- **删除包内模板残留的文档目录**（`XFramework.md` + `images/example.png`）：它是 UPM 插件模板留下的骨架，被写成了包内总览，但**已成第二份真相**——零入链（全仓无 markdown 链接指向它）、CHANGELOG 里有**四次针对它的「文档修正」**（每次都是它写了代码中不存在的 API：本地化表 3/4 行、设置表一整套 `Get<T>()`、`UpdateManager.Bind(root)` 等），而本轮核对又发现一批：模块索引只覆盖 17/21、目录树 13/21、速查表 9/21，`UIManager.ShowTipAsync(new TipConfig { Text = … })` 是**两处编译级错误**（签名首参是 `string text`、`TipConfig` 无 `Text` 字段），依赖表漏 Newtonsoft.Json 导致**照它的安装步骤配置会编译失败**，开头「纯 C# 实现、不依赖 MonoBehaviour 继承 / 引入后即可编写 GamePlay」与实现相反（Runtime 有 9 个 MonoBehaviour）。其中唯一独有且有价值的内容——模块索引表——已搬迁进包 README 并补全到 21 个模块；`example.png` 是 2023-08 的模板图、全仓零引用。**包内此后只有 README 一个总览入口**

#### Pool

- **XPool 删除 `PoolManagerExtensions`（破坏性）**：三个扩展方法挂在 `object` 上（`this.GetFromPool<T>()` / `this.ReturnToPool()`），全仓零引用、零测试，却让使用方工程里**每个变量**在 IDE 补全中都多出这两个条目。`GetFromPool` 的语义完全被 `PoolManager.Get` 覆盖，`ReturnToPool<T>(this T item)` 还带着与 `Return` 同源的静态类型定键问题。README 的文件树、API 速查表与示例同步删除

#### UI

- **`UI State` 独立窗口（`Assets/XFramework/Editor/UIStateWindow.cs`）与菜单 `Tools/XFramework/UI State` 删除**：内容并入诊断窗口的 UI 页签（面板列表 / 状态快照 / `DumpState` 全文一项不少，另加层级表）。迁移：改用 `Tools/XFramework/Diagnostics`。

#### 跨模块 / 框架级

- **包内三项「只删了内容、留下空壳」的残留**（起于「还有 NuGet 残留吗」这一问的顺带清点）：① `Third Party Notices.md` 是 2023 年起的未填模板，全文只有 `[provide component name]` 占位符。删它的判据是**包内零第三方代码**——284 个 Runtime 源文件无一条 `Copyright` 或许可头，vendored DLL 为 0，三个依赖均由使用方自行安装、并不随本包分发，因此没有可声明的内容；该文件本身也零入链。② `维护向文档目录` 与 ③ `Tests/Runtime/Core/` 是两个空目录加被跟踪的孤儿 `.meta`，来自两次「只搬走内容」的操作（删除包内模板 维护向文档目录 的那轮、以及 Tests 的 Core→Node 改名）。已做全包孤儿 meta 扫描，除这两处外无其它

- **仓库级的 NuGetForUnity 残留**：`Assets/NuGet.config`（含 `.meta`；该工具在 Assets 下自建的配置，GUID 除自身外全仓零引用，也不是 MSBuild 配置链的成员）、`Assets/Packages/`（空目录 + 未被跟踪的 `.meta`）与 `.gitignore` 的两条 `[Aa]ssets/Packages` 忽略规则。0.2.0 移除该工具时只清了工具本体与 NuGet DLL，配套物全部留下；`Assets/Packages*` 未被跟踪、只因那两条规则而隐形，所以 `git status` 一直看着干净。规则侧的「NuGet 包 → `Assets/packages.config` + NuGetForUnity」一条同步删除——它在教人使用一个仓内已不存在的工具。本包第三方依赖全走 UPM，Newtonsoft 由 Unity **官方注册表包** `com.unity.nuget.newtonsoft-json` 提供，不需要 NuGet 客户端

### Fixed

#### Bootstrap

- **Bootstrap `Shutdown` 按「登记序逆序」清理，而执行序由 `Phase` 决定——两者可相反，且 README 推荐的替换姿势恰好触发它**。执行序由相位决定（`RunAsync_ExecutesStagesByPhaseOrder` 专门锁定「执行序由 Phase 决定而非登记序」），清理却反向遍历登记表，只在两者恰好一致时才等价于文档自述的「后初始化的先清理」。README 推荐的「先 `Register` 再 `RegisterDefaults`」制造的正是不一致：`Register(new SaveBootstrapStage(opts)); RegisterDefaults();` 的执行序是 Asset→Data→Save，清理序却是 Data→Asset→Save（Save 依赖 Data，反而最后拆）。现改为**执行序的逆序**——相位降序，同相位内按登记逆序（同相位并行本无严格先后，取登记逆序作为确定性次序）。**改前必红实测**：新增的乱序登记用例在旧实现上 `Expected: "p4" But was: "p3"`；默认登记组合（Asset→Data→Save）行为不变

- **修一处模块边界越界：`GameLauncher` 的 Input 字段把 Unity 输入类型带出了 `XInput.Default`**（用户指出的）。字段面首版写的是 `[SerializeField] InputActionAsset _inputActions;`，而本框架的承诺是「**Unity 输入类型只出现在 `XInput.Default`**——门面与 `IInputProvider` 保持插件中立（Rewired 那类用户看到的公开面不受影响）」（见本文件 `InputSystemOptions` 条目）。核实后是**全仓唯一**一处越界。修法：新增 `InputSystemOptionsAsset : ScriptableObject`（**住在 `XInput.Default` 内**，字段是输入资产 + 初始 ActionMap 名，带 `ToOptions()`），`GameLauncher` 改持这个**框架类型**的引用——Inspector 里照常可配，且该资产还能被非 `GameLauncher` 的启动路径复用。**顺带补上守卫**：这条承诺此前只活在文档里，既有的两条边界断言查的是跨模块 `Internal` 与漏出的 internal 类型，可选包的 Unity 类型外泄不在射程内（所以它能活过编译、测试、文档三道门禁）；`ModuleBoundaryTests` 新增 `UnityInputTypes_StayInsideTheDefaultNamespace`（源码扫描，`Runtime/Input/Default/` 之外出现 `UnityEngine.InputSystem*` 即红），并在 `CLAUDE.md` 的模块边界节成文（判据写明是**可选包**而非「Unity 类型」——`Transform`/`TextAsset` 这类核心类型不受限）

#### Pipeline

- **Pipeline `FailureReason` 不带失败阶段名**：`StageExecution` 只把 `ex.Message` 写进描述，阶段名只进 `CurrentTaskName` 且无人使用——日志行有名字（`Parallel stage failed: bad (...)`），而 `Bootstrap` 据拉取面抛出的「启动失败」异常没有，多阶段项目从 `catch` 处定位不到是谁挂的。现 `FailureReason = "Failed: {名字}: {描述}"`，名字取 `ctx.CurrentTaskName ?? ctx.Name`（与容器日志同一「最具体的名字」口径：容器取首失败子阶段名，直接阶段没上报过任务名则回落自身 `Name`）。**日志行文案未动**（名字已在上一行，也避免牵动 `PipelineFailureTests` 的精确正则）。**改前必红**：Pipeline 侧 `But was: "Failed: boom"`、Bootstrap 侧 `But was: "[Bootstrap] 启动失败：Failed: offender boom"`

#### Asset

- **`AssetInitOptions.PackageName` 是静默陷阱：初始化与加载用的是两个包名**。它只决定「初始化哪个包」（经 `InitializePackageAsync`），而加载族（`LoadAsync` / `InstantiateAsync` / `PreloadAllAsync` / `LoadSceneAsync`）统一解析到硬编码的 `DefaultPackage`——于是 `InitializeAsync(new AssetInitOptions { PackageName = "MyPack" })` 的真实结果是**初始化了 MyPack、却从一个刚被新建、从未初始化的空 DefaultPackage 上加载**：初始化看着成功、之后每次加载失败（DEBUG 下 YooAsset 的 `DebugCheckInitialize` 直接抛）。现由 `InitializeAsyncCore` 经 `ResolveDefaultPackageName` 决定主包名（非空即采用）；`InitializePackageAsync`（额外包）保持不动。解析提成 `internal static` 纯函数并**补上此前零覆盖的测试**（`AssetPackageNameTests` 三态：null / 空白 / 非空）——它决定的正是「初始化与加载是否同一个包」。另清掉 `YooAssetManagerImpl` 构造参数上那份从未生效的 `"DefaultPackage"` 默认值（两处调用点都显式传参，留着只是第二份字面量）。**这是行为修复、无新增 API**：那条路本来就走不通

- **UI 遮罩走 `AssetManager.DestroyInstance` 销毁**：遮罩由 `new GameObject` 创建、从未经 AssetManager 托管，而 `DestroyInstance` 开头就 `EnsureGlobalInitialized`——于是只要走过 `ShowMask`，`UIManager.Destroy()` 就要求 AssetManager 存活，未初始化时直接抛异常。改为直接销毁

- **Asset 门面初始化的跨代际广播：作废的在途初始化会放行新代际的加入者（本轮高危）**。`_initWaiters` 是**全局**列表而广播不区分代际，`Destroy()`/`SetInstance()` 只递增代际号、清 `_initializeTask`，**等待者与异常槽原封不动**。于是「作废后在途初始化仍会收尾、新代际已启动」的窗口里两个创建者并存，旧代际完成时把**新代际的加入者**一并放行：成功面是 `await` 正常返回而 `IsInitialized` 仍为 false（下一次调用抛「尚未初始化」，错误现场与病灶完全脱节）；失败面是旧代际的异常被广播给新代际的加入者。现把等待者登记为 `(代际号, 完成信号)`、广播只放行同代际，异常改参数传递（顺带删掉同样跨代际共享的 `_initException` 槽）；`Destroy()`/`SetInstance()` 以同一个 `InvalidOperationException` 了结该代际的加入者；创建者代际不匹配时**抛异常**而非静默 `return`（**破坏性**：一条既有用例原钉「作废后正常返回」，语义经确认——作废对创建者与加入者同形）。**改前必红实测**：5 条红，其中一条**无关**用例因旧实现里未被观察的异常被 `UniTaskScheduler` 播出而连带判红（`Unhandled log message: InvalidOperationException: 旧代际失败`），修复后不再发生

- **Asset 非成功路径丢弃句柄（8 处）**。类型不匹配（`AssetManagerImpl` 同步/异步实例化各 1 处：`GetAssetObject<T>` 是 `as` 转换，地址指向的不是 GameObject 时返回 null 而句柄**有效**，代码直接 `return null`）与加载失败（`YooAssetManagerImpl` 6 处：`LogError` 后 `return default`）都把刚创建的句柄丢掉。实测 YooAsset：`ProviderOperation.CreateHandle<T>()` 在**创建**时就 `RefCount++` 并登记，`CanDestroyProvider()` 要求 `RefCount <= 0`——被丢弃的句柄让该 provider **永久不可销毁**、bundle 引用保持，此后 `UnloadUnusedAssetsAsync` 再也回收不掉它。加载失败是常见路径（地址写错就会走到），而类型不匹配那条**连日志都没有**。现统一先释放再返回 `default`（对外契约不变），类型不匹配另补一条 `[AssetManager]` 告警。对照：同文件「组件不匹配」分支一直有告警 + 销毁实例，漏的是 GameObject 这一层

- **Asset 回池不去重：同一实例进池两次**。`DestroyInstance` 对同一实例调两次会 `Push` 两次，两次取出拿到同一个 GameObject。`CHANGELOG` 里 UI「批量关闭会把同一个面板关两次」那条已把这条根因写在案（「回池没有去重（AssetManager 直接 Push）」），但当时的守卫只加在 **UI 侧**，Asset 侧至今没有兜底。现 `InstanceTracker` 增 `_pooled` 标志（回池点置位、取出时经 `OnEnable` 清除），已回池的实例直接忽略重复 `DestroyInstance`；顺带把 `TryReturnToPool` 改为**先判容量后动实例**（池满时不再先失活脱父，也不留空池条目）。**改前必红**：`Expected: 1 But was: 2`

- **Asset 下载器的取消路径会漏掉退订**。`DownloadAssetsAsync` 把 `handle.Dispose()` 写在 `await handle.WaitAsync(ct)` 之后——取消时 `WaitAsync` 抛 `OperationCanceledException`，退订被整条跳过，`ProgressChanged` 订阅与三个底层回调永久留在 downloader 上。改为 `try/finally`

- **Asset `WaitAsync` 在未 `Begin()` 时永久挂起**。YooAsset 的下载器在启动前既不完成也不失败，而本方法没有超时——忘了 `Begin()` 的调用方会无异常、无日志地永久卡在 await 上。现记 `_begun` 并在入口拒绝（已结束的不拒绝，如先 `Cancel()` 再等待仍返回状态）。理由同 File 模块对同步等待的态度：**拒绝比挂起好排查**（**行为变更**，无用例钉住旧行为）

- **Asset 场景加载对失败也补发进度 1f**：`progress?.Invoke(1f)` 原先在状态检查**之前**，失败时调用方的进度条停在满格而拿到的是无效 `default(Scene)`。现移入成功路径

- **Asset `GetPackageVersion` 的失败面不完整**：接口承诺「包不存在时返回 null」，实现只做了 `package?.GetPackageVersion()`，而 YooAsset 的该方法先做初始化检查并**抛异常**——包存在但未初始化成功（初始化失败后残留）会从这条缝里把 YooAsset 的异常抛给调用方。现补 `InitializeStatus` 守卫

- **Asset 下载器句柄 `Error` 未失败时是 `null` 而非文档说的空串**（YooAsset 的 `Error` 在成功路径从不赋值）。归一为 `?? string.Empty`，与另外两个句柄的 `LastError` 一致；另补一条事件文档：主动 `Cancel()` **不会**触发 `Completed`

- **一条发现被编译器当场证伪**（Asset）：取证曾报「`UnloadUnusedAssetsAsync` 不检查 `operation.Status`，失败静默」，修复写完即被编译器拦下（该操作没有 `LastError`）。读 YooAsset 源码确认 `UnloadUnusedAssetsOperation` **没有失败路径**（只置 `Succeed`，从不设 `Error`）——加一个恒真的检查是死代码，还会让人以为「失败会被报告」。**按纪律撤回修复**，改为就地注释记下已验证的事实

- **Asset 离线模式从未接线文件系统参数——默认 PlayMode 直接启动失败（本轮清点发现）**：`CreatePlayModeParameters` 的离线分支返回裸的 `new OfflinePlayModeParameters()`（`BuildinFileSystemParameters` 为 null），而 YooAsset 的 `InitializationOperation` 遇到**空参数列表直接判失败**（`The file system parameters is empty !`）——`AssetPlayMode.Offline` 正是默认值，YooAsset 自带示例无一例外都显式设置该参数。之所以一直没被发现：Asset 的既有测试全走假实现（`FakeAssetManager`），**真实 YooAsset 初始化路径从未被执行过**。现改为显式给出内置文件系统；`CreatePlayModeParameters` 同时提成 `internal` 以便单测（先例：`ResolveDefaultPackageName`），新 fixture 锁住「离线必须非空 / Host 两个都非空 / 不带解密时保持不设 / 带解密时两个文件系统共用同一适配器」

- **两个 Git 依赖改为锚定上游的发布 tag（YooAsset `#2.3.18` / UniTask `#2.5.11`），且 README 与本仓库的 `Packages/manifest.json` 同时改到同一对 tag**。起因是一个实测：README 此前给的是**裸 URL**（`…YooAsset.git?path=Assets/YooAsset`），而不写修订时包管理器取**默认分支的最新提交**；上游 YooAsset 已经走到 3.x，把 `IRemoteServices`、`IDecryptionServices`、`RawFileHandle`、`InitializeParameters` 等类型移进了 `Runtime/Compatibility/Compatible*.cs` 里默认不编译的 `#if YOOASSET_LEGACY_API` 兼容层，Asset 模块因此直接编译不过。**实测**（新建裸 Unity 工程 + `file:` 指向包本体）：不写修订时 51 处 `error CS0246/CS0234` 全部落在 Asset 模块，钉住后退出码 0、零 `error CS`。本仓自己一直没暴露这个问题，只因 `packages-lock.json` 是提交进仓库的——**锁文件保护不了照 README 手装的第三方**。**为什么是 tag 而不是提交哈希**：补查发现本仓原先锁定的两个提交**都不是任何发布版**，而是上游 main 上的中间快照（YooAsset 那个在 `2.3.18` 之后 1 个提交，UniTask 那个在 `2.5.11` 之前 3 个提交）——照 README 把别人家未发布的快照装给第三方，本身就不是个好建议，哈希对读者也零信息量。改用 tag 后版本号一眼可读，且**文档推荐的组合与本仓库编译测试的组合是同一个**（`packages-lock.json` 锁的正是这两个 tag 的提交）。写法约束来自 Unity 文档：`?path=` 必须写在 `#` 之前（反过来会失败）

#### Update

- **UI 门面记账与调度器实况脱节**（同轮 P1）：`UpdateManager.Clear()` 是 public 且明说「清空后仍可继续 Register / Tick」，它会摘掉 UI 的驱动器而门面毫无感知——`EnsureFrameDriverRegistered` 以「`_frameDriver` 字段非空」早退，于是 Clear 之后的 `Initialize` / `SetInstance` 都不会重新注册，UI 永久失联且**零日志**（驱动器已不在调度器里，连被派发的机会都没有）。现改为每次生命周期入口都真的注册一遍（同节点「先摘再插」、不重复宣告 `OnEnable`，幂等由调度器保证）；分档处刻意不照做（那条回调只在空↔非空跳变时触发，且无条件重注册会给切片桶换相位）。代价是极低频的同 order 节点重排一次，已写进注释

- **UI 文档三处与实现相反**（同轮 A1）：门面注释称「本区与 Query 里的只读成员一律不调 `EnsureGlobalInitialized`」，实际 `GetTopPanel` / `CopyPanels` / `CopyPanelsInLayer` / `IsOpen<T>` / `GetPanel<T>` 全调——而那五个「照抛」正是 `UIFacadeProbeTests` 有意钉住的契约（实现要先剪枝再取，属操作而非纯读），错的是注释；README 的「转发体一律是 `EnsureGlobalInitialized()` + `_instance.X(...)` 两行」被 7 个探测成员证伪；README 两处仍引用已删除的无参 `Update()`（该重载在「OnUpdate 携带 deltaTime/time」的破坏性变更里删掉）。另补一整套未成文的边界：README 新增「未初始化时的行为」对照表（探测 vs 操作）、`SetController` 只对 `UIManagerImpl` 生效、实例所有权（`SetInstance` 不碰注入实例 / `Destroy` 一律 Dispose）、`Time.timeScale = 0` 冻结整条 UI 通路、`UpdateManager.Clear()` 的边界、UI 无线程契约断言

- **Update 固定步时机的 `ProcessImmediate` 把节点锚到逻辑时刻**：`UpdateClock` 只有 Scaled / Unscaled 两条轴，而固定步条目恒挂在轴 0 上（`RegisterFixed` 不传时间轴），于是 `ProcessImmediate` 取到的「该节点所属时刻」实为**变步长逻辑时刻**——下一次真正固定步派发算出的 delta 就是 `fixedTime − 逻辑时刻` 这个跨轴相减的产物，既不是该轴承诺的 `2^k × Time.fixedDeltaTime`，也不是锚定规则给的 0。现落到固定步时机时**只重新定锚、不写时刻**（派发外与派发中两条路径统一）：下一次固定步派发 delta 记 0，与注册/启用后首次派发同一套语义。可达面有限——门面 `ProcessImmediate` 的形参是 `IUpdateable`，纯 `IFixedUpdateable` 够不到，生产影响 ≤1 帧、自带时刻的回放时钟下更大。**改前必红实测**：两条新用例在修复前分别红成 `Expected: 0.0f But was: 7.02000046f`（10.02 − 3.0，即跨轴相减的产物）与 `Expected: 1 But was: 2`（用例自身的计数写错，同批修正）；修复后 `XFramework.XUpdate.Tests` 133/133 通过

- **Update 异常日志固定报 `OnUpdate`**：三条时机共用这一份调度器代码，LateUpdate / FixedUpdate 时机上的异常会被报成节点上根本没跑过的回调，把排查引向错误的位置。现按实际时机报 `OnUpdate` / `OnLateUpdate` / `OnFixedUpdate`，与转型分支同分级（异常路径才构造字符串，每帧路径的零分配不受影响）

- **自动初始化统一为「两个特性都挂」（Lock / Message / Serializer / File）**：四处原先是 `#if UNITY_EDITOR [InitializeOnLoadMethod] #else [RuntimeInitializeOnLoadMethod] #endif`——**关闭域重载**（Enter Play Mode Options → 取消 Reload Domain）时 `#else` 分支根本没编进编辑器程序集，进入播放不再重新加载程序集，该回调也就不再执行（`UpdateManager` 已在此前踩过并修好）。现统一为「编辑器特性 + **无条件**的运行时特性」，档位按需要定：Lock/Message/Serializer 用默认 `AfterSceneLoad`（体内只有订阅/注册），**File 改 `BeforeSceneLoad`**——它注释里的意图是「子线程首次调用前预热」，而默认档位在构建里晚于首个场景 Awake，等于没预热（已实测三条 `Application.*Path` 在该时机非空，并留下长期护栏 `FileRootsTimingTests`：这是 Unity 的平台行为且失败**静默**，`PrimeRoots` 遇空串直接 return 不缓存不告警）。Lock/Message 的 `Application.quitting` 订阅同时改为**幂等**（`-=` 后 `+=`；重复 `+=` 在关闭域重载时会逐次累积，此前正是裸 `+=`）。**如实说明：这不是 bug fix**——Lock/Message 的 `AutoInit` 只做订阅，而该订阅在关闭域重载时跨播放会话存活、正常退出播放仍会触发清理，故不存在「上一轮播放的锁被带进下一轮」；真正买到的是「范式统一（不再有第二种写法供人照抄）」与「订阅不再累积」。File 的注释此前写着「两个特性都要挂」而代码是旧的（文档与实现相反），现已两段成真

- **XPool 重复归还检测按值比较，合法归还被误拒**：Editor 活跃追踪集用 `HashSet<T>` 的默认相等比较器，而模块文档承诺的是「复用依赖引用同一性」。池化类型重写 `Equals` / `GetHashCode`（POCO、记录式数据类）时，两个逻辑相等而引用不同的实例会被判成同一个：第二次归还的 `Remove` 落空，于是记一条「重复归还」错误日志并把实例丢给 GC。改用 `ReferenceEquals` + `RuntimeHelpers.GetHashCode` 的比较器（照 Update 模块 `NodeReferenceComparer` 的同型先例，整块包在 `UNITY_EDITOR` 内），对真正重复归还的检出能力不变。补回归用例，已实测摘掉比较器时它确实会红

- **Update 已注销对象被 LOD 迁移复活**：待处理操作缓冲是「先全部注销再全部注册」两阶段，而 LOD 迁移被拆成一条 remove + 一条 add；同一帧内既迁移又被注销的对象在 remove 阶段删掉旧条目后，add 阶段又被插进新桶，此后永久继续被派发且不会再有人来清它。现改为单条有序队列、按入队先后逐条应用，迁移改为**条件操作**（应用时仍在桶里才生效）

- **Update 派发期禁用/启用覆盖他人条目**：`entries[i] = entry` 的写回用的是回调前查到的下标，而 `Disable`/`Enable` 会就地增删元素——禁用排在自己前面的节点后，写回会覆盖顶替上来的节点（对方永久停更、自己此后每帧被派发两次）；禁用自己是桶内最后一个元素时直接抛 `ArgumentOutOfRangeException`。现统一入队后，派发期间没有任何代码能改活表

- **Update `ProcessImmediate` 回调期改写活表**：同源缺陷的第三处——回调里注销该对象会让随后的写回落到已易主的槽位、并把已注销对象插回桶里。现复用迭代闩锁，回调期间的操作进缓冲、返回后统一应用

- **Update 关闭域重载后整个模块静默死亡**：`AutoInit` 在编辑器分支只挂 `[InitializeOnLoadMethod]`，而关闭 Reload Domain 后进入播放不会重载程序集、该回调不再执行；退出播放时又置了不可逆的 `_shutdown` 并丢弃调度器。结果是第二次进入播放后 `IsInitialized` 恒为 false，十余处守卫全部静默 return 且 `Clear` 也救不回来。现无条件挂 `RuntimeInitializeOnLoadMethod(SubsystemRegistration)` 并让 `AutoInit` 幂等（只在缺失时重建、`Application.quitting` 幂等订阅），删掉那个恒真的 `_shutdown` 闩锁

- **Update 暂停语义缺失**：此前 `timeScale = 0` 时切片相位照常推进、节点仍被派发但 delta 恒 0——暂停期间白跑派发，恢复后相位已漂移（`Frame32` 在 60fps 下意味着半秒多的空窗）。现逻辑轴冻结时既不派发也不推进帧计数；显式 `Pause()` 恢复时重锚时间基准，因此恢复后第一帧 delta 为 0 而不是「整段暂停时长」（刻意不追赶）；`timeScale < 0` 的负间隔钳制为 0

- **Update `ProcessImmediate` 的既有语义写进文档**：派发期间调用它只重置时间基准、不执行更新（在别人的 `OnUpdate` 里再次回调自己会形成嵌套派发）。该行为本次未改，只是补上说明——此前文档没有提，容易误以为「任何时候都能立即执行」

- **文档修正（Update）**：模块 README 重写（修正 `Frame30`→`Frame32`、失效的目录树、一段**无法编译**的 `static class : IUpdateable` 示例，补入三个时机/双时间轴/暂停/派发期语义/已知限制）；`早期的框架文档` 中不存在的 `UpdateManager.Bind(root)`、少一个形参的 `OnUpdate` 签名与启动流程示意一并订正

- **Settings 自动保存因一次写失败而永久停摆**：驱动器裸调 `_owner.Save()`，而 `UpdateScheduler` 对抛异常的 `OnUpdate` 是「LogError + **永久注销**」。于是「store 抛异常」这个可由环境恢复的失败——云存档无网、平台 SDK 报错、配额耗尽——会变成**本次会话再也不自动保存**，且没有重新注册的路径。现吞住异常并按下一个去抖窗口重试（不重算窗口就是每帧重试，持续失败的 store 会以约 7.5 次/秒空转），每个失败周期只告警一次（否则是每秒两次的日志洪水），`IsDirty` 保持为真故改动不丢。退出兜底的同形问题一并隔离。显式调用的 `Save<T>()` 保持抛——配额、签名这类失败该被调用方看到，吞掉还会改变 `IsDirty` 语义

- **Settings `Destroy` 的异常会中断整批释放**：原先是一句裸的 `foreach` + `Clear`，任一管理器的 `Dispose` 抛异常就当场中断——其后所有实例都不再释放（自动保存驱动器留在 `UpdateManager` 上、退出订阅留在静态事件上），而 `Managers.Clear()` 也一并跳过，静态字典里留着已释放的实例而 `IsInitialized` 却继续说 true。现逐项隔离后继续释放，并把 `Clear()` 移进 `finally`（对齐 `UIManager.Destroy` 的同一取舍）。此路径无配套测试：`Dispose` 只碰 `UpdateManager`（不抛）、`Application.wantsToQuit`（不抛）与 `EventStream`（加锁遍历），没有注入抛出物的缝，为四行防御性代码新增测试接缝不划算

#### Event

- **`ActionDisposable.Dispose` 退订硬化**：改用 `TryGetValue`——批量清理/主体销毁会摘掉键，而调用方手里的句柄是合法的，退订必须退化为空操作（原先的索引器会抛 `KeyNotFoundException`）。`LockManager.Dispose()` 一并注销所有销毁绑定并复位 `AutoReleaseOnDestroy`，免得上一轮的令牌回调改到下一轮的容器

- **`SubscriptionTracker` 挪出 `.Internal`（Event/Message）**：跟踪器的「两处登记点」约束要求消息模块也调它，而把它放在 `XFramework.XEvent.Internal` 会让消息侧的 `using` 被判为「跨模块引用他人 Internal」——编辑模式门禁因此红了两个文件（`2520c44` 刚消掉的那类引用被 `8674c4b` 带回）。命名空间改为 `XFramework.XEvent`（可见性仍 `internal`），文件同步从 `Internal/` 挪到 `Event/` 根；类文档写明**为什么不在 `.Internal`**

- **BufferedEventStream 方法隐藏改为 override**：此前经 `IDisposable` 接口释放会落到基类槽位，缓存不清空，已释放的流仍向新订阅者重放陈旧值；同时修复「已完成（`OnCompleted`）后再 `OnNext` 会写回缓存并重放」的 completed 语义违背

#### Message

- **键值通道的 Key 类型隔离**：同一消息类型配不同 Key 类型（如 `Publish("k", 1)` 与 `Publish(1, 1)`）此前会强转到先创建者并抛 `InvalidCastException`

- **缓冲通道内存无界增长**：新增显式淘汰 API；`EvictBufferedChannels<T>` 按消息类型跨全部 Key 类型淘汰

- **订阅回池的重复归还**：淘汰缓冲流后陈旧句柄的 `Dispose` 会二次回池，同一节点被发放两次将形成 `node.Next` 自环并令派发快照死循环

- **`SubscribeAsync` 已取消令牌留下空通道**：四个重载此前先建通道再判令牌（实参先于被调方法求值），令牌已取消时通道（键值版连整条存储表项）被建出却没有订阅者来触发回收，只剩 `TrimEmptyChannels` 兜底；现将令牌守卫置于建通道之前，判空仍先于判令牌（保证 `null` 处理器照常抛 `ArgumentNullException`）

- **异步订阅令牌竞态下通道永久不可回收**：`AsyncSubscription` 构造期间向已取消令牌 `Register` 会同步内联触发退订，而该项尚未入表，退订落空、不回调空通知；此时若仍入表，登记表永远非空使 `IsReclaimable` 恒为 false，自动回收与 `TrimEmptyChannels` 共用该谓词而双双失效，只能等 `Clear()`。现改为已退订的登记项不入表（该窗口仅在跨线程 `Cancel` 时可达，属防御性加固）

- **节点消息订阅补全**（**同上，随节点系统删除**）：`NodeExtensions.Subscribe` 的 `where TMessage : class` 约束使框架内置 struct 消息全部编译不过，且与 `MessageManager.Subscribe` 同形导致重载二义；`BaseNode` 实现消息标记接口后二者同时解决

- **订阅与过滤器 API 判空**：此前 `null` 过滤器要到派发时才 NRE 并被 `try/catch` 吞成日志（静默失效且订阅保留）

- **异步与键值重载的二义**：键值 `PublishAsync` 的 `strategy` 取消默认值，避免两参调用 `(消息, 策略)` 与 `(Key, 消息)` 无法裁决

- **文档修正**：UI README 引用了不存在的 `MessageManager.Unsubscribe`；`IMessageSubscriber` 扩展文档夸大了生命周期绑定范围（实际仅 `MonoBehaviour`）；`早期的框架文档` 的 Node README 链接漂移

- **同步 `Publish` 的异步处理器异常日志与等待路径不一致**：`PublishAsync` 的等待路径把处理器异常统一收在 `InvokeGuardedAsync`（记 `[Message]` 前缀的 Error、当次即报、订阅已退订时随之结束的 `OperationCanceledException` 静默丢弃），而同步 `Publish` 触发的 fire-and-forget 路径只用一个 `try/catch` 兜住**同步段**，异步段（`await` 之后）的异常交给裸 `.Forget()`——它落到 `UniTaskScheduler`，记成无模块前缀的 `LogType.Exception` 并延后到调度器 tick 才报（`LogType.Exception` 还会让 Unity 测试框架判该用例失败）。同一个处理器，经 `Publish` 触发与经 `PublishAsync` 触发会得到两种日志。现改为两条路径共用 `InvokeGuardedAsync`：异常隔离语义不再因路径分叉，且该守卫**永不 fault**——并行/顺序两条等待路径早已依赖的性质，至此补齐到第三条路径。补 3 条用例（异步段抛出、返回已 fault 的 `UniTask`、令牌未取消时抛 `OperationCanceledException` 的负向守卫），前两条已实测改前必红，失败消息即缺陷本身：`Unhandled log message: '[Exception] InvalidOperationException: ...'`

- **无值的缓冲通道永远不可回收，`TrimEmptyChannels` 的兜底也摘不掉它**：`SubscribeBuffered` 在该类型**从未发布过**时同样会建出缓冲流（`GetOrCreateBuffered`），而通道的回收闸门只判「有没有缓冲流」。于是订阅退掉之后，这个通道既等不到自动回收（缓冲流刻意不挂 `OnEmpty`，它没有清零事件），也过不了 `TrimEmptyChannels`（共用同一闸门），只能靠用户知道类型名去调 `EvictBufferedChannel`；同一闸门还让 `GetChannelStats<T>().HasBufferedValue` 报出一个并不存在的重放缓存。现把闸门改为「无存活订阅者，且无值得保留的重放缓存」——缓冲流须同时满足「无缓冲订阅者」与「无缓存值」才不许回收；前半条不可省：只判无缓存值会把「订阅先于发布」的活订阅者连通道一起静默摘掉（已用一条守卫用例钉住）。**已知边界**：纯缓冲空壳没有清零事件，自动回收仍不覆盖它们，只由 `TrimEmptyChannels` 摘除，README「内存管理」已写明这一点。统计口径本次**刻意不改**（仍按「建有缓冲订阅流」计数，改它要动两条既有断言），只订正 `HasBufferedValue` / `BufferedChannelCount` 与 `MessageChannel.IsReclaimable` 的文档措辞。补 5 条用例（类型级回收、键值级回收与存储摘除、谓词三态直测、活订阅者守卫、回收后重订阅恰好投递一条），均已实测改前必红

- **请求与响应的类型不匹配会抛裸 `InvalidCastException`，`Unregister` 还会误删处理器**：处理器表的键只取请求类型，取出后向「调用方要求的响应类型」强转——注册时写的是 `Register<Req, A>`，之后用 `RequestAsync<Req, B>` 发请求就是一次非法强转。`UniTask<T>` 是结构体泛型，委托型变救不了，任何不匹配必然抛裸 `InvalidCastException`（已用独立复现程序确认：换成引用类型的泛型参数时这种强转本可放行），报错里既没有模块前缀、也没有修复线索；而 `Unregister<TRequest, TResponse>` 的 `TResponse` 更是个纯装饰——`Unregister<Req, 别的响应类型>()` 照样把处理器删掉，其后所有正常请求都会变成「未注册」。现把表项改为携带「注册时声明的响应类型」（单份数据源，不开并行表，值类型直接内联进字典桶、不产生装箱），取出时校验：不匹配抛带 `[Message]` 前缀与修复写法的 `InvalidOperationException`；`Unregister` 则不删除、记 Warning 并返回 `false`。判定顺序固定为「先查表、后校验类型」——未注册是运行时常态（响应方可能尚未就绪），类型不匹配是编程错误，两条报错必须能区分，已用一条守卫用例钉住。同批修掉两处报错文案缺陷：`Register` 与 `RequestAsync` 的提示里 `TResponse` 是**字面量**（原样写着 `TResponse` 这五个字母），照抄提示得到的代码根本编译不过——而校验一旦存在，那条提示指向的调用还会被它自己拒绝，不修就是自相矛盾。补 4 条用例（两条抛异常、一条 `Unregister` 不误删、一条判定顺序守卫），前三条已实测改前必红（失败栈即 `TryGetRequestHandler` 的强转）

#### Reactive

- **Lock 聚合订阅的订阅期缺陷（两处）**：① **异常泄漏**——`OnLockStateChanged` 的「订阅即播报」原先裸调用户回调：handler 抛异常时异常逃出，而两条内部订阅已经挂上、句柄却没返回给调用方，于是表里永久残留该主体键与委托、**主体被强引用住**（后续边沿却由 `DispatchSafely` 隔离——同一段用户代码，行为取决于时机）。现改为照 `ReactiveProperty` 的完整语义：**先拆掉刚挂上的订阅再上抛**，异常要被看见但不泄漏。② **双告警**——聚合订阅落在已销毁主体上时会经两条内部订阅各撞一次短路，告警两次并返回静默死订阅；现改为入口判一次、只记一条告警、返回空句柄，与 `OnLocked`/`OnUnlocked` 的既有短路同形

- **Reactive 订阅立即回调抛异常会永久泄漏订阅**：`ReactiveProperty<T>.Subscribe` 与 `ReadOnlyReactiveProperty<T>.Subscribe` 都是「先经引擎注册、再直接调用 `onNext(_value)` 补那次立即回调」。立即回调绕开了引擎的异常隔离路径（`EventStream.Deliver` 的 catch + LogError），于是同一个委托有两种异常行为——首次投递传播、后续投递吞掉；而更实际的后果是句柄：回调抛异常时 `Subscribe` 永远不会返回，那个已经注册成功的订阅从此**没有任何办法退订**。节点回收只有「退订」与「属性 Dispose」两条路，前者需要句柄、后者需要属性被释放，故在该属性活着的整个期间，这个订阅既无法退订也不会被回收，回调持有的闭包与目标对象被链表一直钉住——不是逻辑瑕疵而是内存泄漏。现改为在注册后包一层 try/catch：catch 里**先退订、再原样上抛**——异常照旧能被调用方看见（注册期失败不该被静默，调用点本就是绑定初始化），而句柄即便交不出去也不留残留。README 的「异常隔离」条目同步说明两条路径的分工。该缺陷此前无测试覆盖，已补 3 条（`ReactiveProperty` 侧两条 + `ReadOnlyReactiveProperty` 侧一条），断言方式为「处理器首次调用抛异常后，再写值不得再次触发它」

- **Reactive 带过滤订阅的过滤条件抛异常不再击穿派发**：记 Error 日志、该订阅本条不收、订阅保留、其余订阅者照常收到（含缓冲重放路径）

#### Localization

- **Localization 模块审计一轮（三项高危 + 五项收口）**：按 `维护向的模块审计手册` 的判据 A–F 全类扫过，修的不是「不够优雅」而是会**静默出错**的地方。① **`GetFormat` 把「保留的未注册占位符」当格式项解析**——`ReplacePlaceholders` 承诺未注册的 `{Name}`「原样输出」，紧接着 `string.Format` 就把它当复合格式项，抛 `FormatException`；两条各自被文档承诺的行为合起来就是一个异常，**README 自己的示例（`GetFormat("ui_guild_info", "5")`）就会抛**（实测：修复前三条红全是这条）。现在交给 `string.Format` 之前先做一次**花括号归一**：放行合法格式项与 `{{`/`}}`，其余转义为字面量——由此可得一条强不变式「当前能正常格式化的文本，输出逐字不变」。② **JSON 解析器不解码转义序列**：`\n` 得到的是字面反斜杠+n、`\uXXXX` 原样输出——`System.Text.Json` 默认就转义非 ASCII、Python `json.dumps` 默认 `ensure_ascii=True`，用它们导出的语言表整表中招。现解码 `\" \\ \/ \b \f \n \r \t \uXXXX`（含代理对；无转义时仍走零拷贝切片快路径）。③ **JSON 格式错误静默降级**：三处 `break` 直接返回**已经读到的半张表**——`{"a":1}` 产出非空字典 `{"a":""}`（一种「所有值都是空串」的语言，还绕过了 `Count == 0` 兜底），`{"a":"1","b"}` 静默丢掉 `b`，`{"a":"1"} oops` 照常成功；而类文档白纸黑字写着「加载/解析失败抛异常(不静默)」。现一律抛 `InvalidOperationException`（带字符位置），只对**尾随逗号**与 **UTF-8 BOM** 保持宽容（两者都不影响数据完整性）。另五项收口：`LocalizationManager.Subscribe` 改走 `MessageManager.TryBindToDestroy`（此前手写的那一遍，正是那份实现点名警告的三处：闭包分配、只认 `MonoBehaviour` 而漏掉 `IDestroyCancellationToken`、已取消令牌仍去注册——**改前必红是编译级的**：形参还原成 `MonoBehaviour` 后三个调用点全部 CS1503）；`LocalizationBootstrapStage.Shutdown` 不再销毁非本阶段初始化的管理器（空转路径 + 使用方手动 `Initialize` 的组合下，启动收尾会把使用方那份连数据一起销毁）；`EvictIfNeeded` 加进度守卫（找不到候选时旧写法是**主线程死循环**，今天不可达，但「必有候选」依赖一条只靠调用点自律的隐式不变式）；`Dispose` 补复位两个语言字段（它已经清了缓存与 `LanguageAssetPath`，意图就是复位到未初始化）；未初始化实现不再抛裸 `ArgumentNullException(key)`；删掉全仓零读的 `Impl.IsInitialized`，并让 `Get` 复用 `GetRaw` 消掉整段重复的查找逻辑。顺带补上模块两个招牌特性（LRU 淘汰、占位符引擎）的**首批用例**——此前零直接覆盖——以及第三个门面转发完备性守卫（照 UI / Settings 先例，已验证「改掉一个转发名即红」）

#### File

- **File 可选能力缺失的处置在加密前后不一致**：同一个能力缺失（底层 Provider 未实现 `IAtomicFileProvider`），未启用加密时是「告警 + 降级为普通写」，启用加密后却是**抛** `NotSupportedException`；`GetDirectoriesAsync` 更隐蔽——未加密时门面告警降级，启用加密后**静默**返回空数组，连那条告警都被吞掉。根因是门面探测的是**生效** Provider，而加解密装饰器恒实现可选能力接口，探测必然「通过」，判定被整个推给装饰器、由它自行重写。现改为门面探测**基础** Provider（它不随是否启用加密而变），判定权收归一处；装饰器侧的同类检查退为兜底，不重复告警、也不改变语义（降级为普通写而非抛出），故启用加解密对调用方完全透明。两处此前**均无测试覆盖**——这正是不一致得以存活的原因，已补

- **File 移动端 Streaming 的同步内容读会死锁，现改为拒绝**：该域经 `UnityWebRequest` 读取，其 `await ToUniTask()` 的续体要在 PlayerLoop 上推进——阻塞主线程等它就是等一个永远不会推进的循环，**无异常、无日志**，表现为游戏卡死。受影响的既有入口是 `FileManagerExtensions.ReadAllText` / `ReadAllBytes`（随 0.2.0 发布，其文档只写了「阻塞等待」，未提这条路径走不通）。现抛 `NotSupportedException` 并指向异步版本，判定做成纯谓词、查 `_baseProvider` 而非 `_provider`（启用加解密时后者是装饰器，类型探测会漏）。**不可与同一 Provider 的同步 `Exists` 类推**：那里用忙等，`isDone` 由引擎原生侧推进、不依赖托管 PlayerLoop，故可安全阻塞

- **File 域根在子线程解析，启动恢复扫描必崩**：`DesktopFileProvider` 的 `GetFilesAsync` / `GetDirectoriesAsync` 把 `GetPhysicalPath(domain, null)`（→ `Application.persistentDataPath`）放在了 `UniTask.RunOnThreadPool` 的委托**内部**，而 `RunOnThreadPool(configureAwait: false)` 会把池线程留给调用方续体——`SaveManagerImpl.RecoverAsync` 的线程契约偏偏是「全程不切回主线程」。于是**每次启动**的恢复扫描必然在池线程上读 Unity API 而抛 `UnityException`；即便从主线程调用，只要目标目录存在也照样抛（委托内那行不在提前返回的路径上）。现改为**域根在主线程解析一次并缓存**（`DesktopFileProvider.PrimeRoots`，由 `FileManager.Initialize` 与编辑器的 `InitializeOnLoadMethod` / 运行时的 `RuntimeInitializeOnLoadMethod` 各调一次），`GetPhysicalPath` 退化为纯字符串运算，两处 `rootDir` 取值一并提到委托外。`SaveManagerImpl.RecoverAsync` 的「不切回主线程」契约至此才真正成立，Save 侧一行未改

- **为什么此前没被单测抓到（同批补上）**：8 个 File/Save fixture 全部注入 `TempFileProvider`——它在构造函数里就把根路径定死，**结构性绕开 Unity API**，于是「域根解析是否碰了 Unity API」这一整类缺陷在替身上不可见。唯一用真实 `DesktopFileProvider` 的 EditMode 用例因此独自红了 7 次（失败消息逐字相同），还被当成了环境噪音。新增 `DesktopFileProviderThreadTests`：主线程与池线程两条路径各锁一次，改前 0/4 通过、改后 4/4

#### Serialize

- **`Serializer.Initialize` 不再静默覆盖使用方提前注册的同名序列化器**（同批三项之一）：`AutoInit` 在进入播放时注册内置两项，使用方若在它之前注册同名格式（如自定义实现接管 `"json"`），那次注册会被**静默覆盖**——而「提前注册」本就是在表达「我要替换它」。现在遇到同名注册会**保留使用方的**、跳过内置，并打一条 `[Serialize]` 告警说明如何恢复内置（`Register` 自身的「同名覆盖」承诺不变——那是显式动作）。这是 Serialize 模块的**第一条日志**，`LogCategories` 因此新增 `Serialize` 分类（该模块此前 `[XSerialize]` 那两处是异常消息前缀，不属分类表）。新增 fixture 3 条，红基线是**行为级**（旧实现下 `Expected: same as <FakeSerializer> But was: <NewtonsoftSerializer>`）

- **一键安装器漏装 Newtonsoft.Json**：`XFrameworkDependencyInstaller` 的 `RequiredUPMDependencies` 只写了 UniTask 与 YooAsset，而 `Venusy609.Xframework.asmdef` 引用 `Newtonsoft.Json` 程序集——用菜单 `XFramework/Install Dependencies` 装出来的第三方项目，Serialize 模块的 asmdef 引用解析不了，直接编译不过。根因：这张清单写于 0.2.0（移除 NuGetForUnity 那一轮），而 Newtonsoft 四天后才随「序列化：新增 NewtonsoftSerializer」入仓，清单此后没跟着 README 的依赖表一起长。现补上 `com.unity.nuget.newtonsoft-json`（`3.2.1`），并在字典上方注明**真相来源是包 README 的「第三方依赖」表**，两处必须一起改。插入逻辑零改动——值的写法对 Git URL 包与 Unity 官方注册表包同形（都是 `"id": "值"`），已存在判定照常生效

#### Save

- **Save `LoadAsync` 静默清空内存数据**：内容为合法 JSON 但不是存档的文件（如 `{"foo":1}`）同样能反序列化出快照，其数据块列表取到的是字段初始化器给的空列表而非 `null`，`ApplySnapshot` 会先清空全部数据块、再因列表为空直接返回——玩家的内存数据被零警告清空；传字面量 `null` 则先清空全部数据块再抛 NRE。现引入结构校验（非 null + 含数据块列表 + 版本号 ≥ 1）后才允许应用

- **Save `GetAllPlayerIds` 恒返回空数组**：原实现靠扫描非递归 `GetFilesAsync` 的返回路径、用分隔符切出玩家目录前缀，而根目录返回的路径永不含分隔符，该分支自诞生起从未成立，且无测试覆盖

- **Save 按玩家查询会串档**：`GetPlayerSlotMetas` 在 `await` 期间把全局玩家上下文临时改成目标玩家，且该路径不设门禁——窗口内发起的保存会写进被查询者的目录，而 `SetCurrentPlayer` 的 busy 守卫对此无效（它读的正是未被置位的 `IsBusy`）。现改为玩家上下文只作默认值、实际路径由参数决定

- **Save 数据块恢复失败被静默吞掉**：`ApplySnapshot` 会跳过恢复失败的块并正常返回，而 Save 侧据此回报 `Loaded`——内存里少了一整个块的数据却报告成功，玩家看到的是半个游戏加一行控制台 warning。现由 Data 侧上报失败块数，Save 侧据此判定加载不完整、回滚内存并回报 `Corrupt`

- **Save 加载失败无回滚**：应用快照失败后内存已被清空且不可恢复，现先留回滚点、失败时尽力恢复（回滚本身失败也必须记 Error 而不能静默，否则会留下「以为已回滚、实则是半清空」的状态）。已知局限：`CreateSnapshot` 会清空脏标记，故回滚恢复得了数据、恢复不了脏标记集合

- **Settings 已释放后仍可写盘**：`Dispose` 只终止了通知流、未置释放标志，`Save` 照常写盘，`Observe` 返回一个永不回调的空句柄（`EventStream` 对已 `OnCompleted` 的流正是返回空订阅）而调用方毫无察觉。现补释放标志，除 `Dispose` 外所有公开成员释放后抛 `ObjectDisposedException`

#### Config

- **Config 审计一轮（一处高危 + 一处 E/A2 与若干文档口径）**：高危是 **CSV 的值类型行静默产出默认值**——`CsvLoader` 的列赋值走 `SetMemberValue(..., object target)` 反射，而 `var item = new T()` 对 struct 是值类型局部变量，传进 `object` 形参**装箱**后反射只改装箱副本，局部恒为 `default(T)`：单行 CSV **静默**载入一条全默认值的行，多行则撞装载器自己的 `Duplicate Id`（错误信息指向「重复 Id」而不是「struct 不被支持」）。而 README 的「struct vs class 行类型」表明确推荐 struct；Json / SO 两个装载器无此问题（它们用 `List<T>` 直存）。之所以能长期存活，是因为**装载器整体不可测**（测试进程里 `AssetManager` 从未初始化，而 `AssetHandle<T>` 只有一个 internal 构造函数、必须包一个真的 YooAsset 句柄，替身造不出来）——现把解析抽成 `internal static ParseTable<T,TKey>(text, assetPath)` 与资源加载解耦（先例：`FileManager.IsSyncStreamingReadUnsupported` 的「纯谓词可直接单测」），再改赋值为「装箱一次、写回、取回」。**复现证据**：多行 struct 报 `Duplicate Id '0' found in Table 'CsvStructRow'`、单行报 `Expected: 7 But was: 0`，而**引用类型行的对照用例一直是绿的**——排除了「接缝本身没接对」的可能。另修：**订阅者异常在加载路径被包成 `ConfigException`，而三个字典已经写完**——调用方看到「加载失败」而数据其实已注册（重试还会命中「已加载」直接返回缓存），现抽出 `RaiseConfigChanged` 统一隔离（取舍同 `EventStream` 与 Update 的 `NotifyLifecycle`，整条多播一次 try/catch，代价是排在抛异常者之后的订阅者会被饿死，已写进 README 的已知限制）。**一条发现被复现证伪**：取证曾报「同一个实例连调两次 `SetInstance` 会留下两份订阅」，实际 `SetInstance` 里先 `UnsubscribeImplEvents(oldInstance)` 那两行对同一实例同样成立——生产代码一行未改，只留一条钉住该行为的用例

#### Pool

- **Lock 主体键改引用同一判定**：四个容器（锁、两张订阅表、销毁绑定）原先用默认比较器（按 `Equals`）。游戏实体按 Id 重写 `Equals` 很常见，于是两个**不同**实体会被判成同一个键——给 A 加锁连 B 也一起被锁、A 的订阅被 B 的锁惊动、`RemoveAllLocks(A)` 把 B 的锁一并清掉（实测计数合并成 2 而不是 1+1）。现改用模块内自持的引用同一比较器（照 `XPool.Pool<T>.InstanceReferenceComparer`、`UpdateScheduler.NodeReferenceComparer` 的形状）。代价写进 `ILockable` 文档：**必须由引用类型实现**该接口（struct 每次装箱都是新身份）

- **XPool 集合池 `Configure` 守卫用差值冒充活跃数，`ClearAll` 后永久拒绝重配**：守卫判的是 `CountAll - CountInactive`，而 `CountAll` 只增不减、`Clear` 只清闲置栈——任何一次 `Clear()` 或 `CollectionPoolManager.ClearAll()`（切场景时的推荐调用）之后，守卫都会看到「N 个活跃实例」并**永久**拒绝重配；集合池没有 `RemovePool` 这类逃生口，`PoolManager` 侧至少还教了那条路。四个集合池（List / HashSet / Dictionary / StringBuilder）一并改用真正的 `CountActive`，各加注释说明为何不能用差值口径。补一条回归用例，已实测改回旧算法时它确实会红

- **XPool `Return` 按编译期类型定键，基类 / 接口引用归还时静默丢件**：池按 `Type` 定键，而 `Return<T>` 用的是编译期类型——`Base b = Get<Derived>(); Return(b);` 查不到池，实例被静默丢弃，与「未注册类型静默忽略」的宽容语义混在一起无从察觉（GameFramework 的 `Release(IReference)` 改用 `reference.GetType()` 定键正是为了规避这一点）。改为 `typeof(T)` 未命中时按 `item.GetType()` 回落一次，经内部 `IUntypedPool` 逃生口调用（泛型池不变型：`Pool<Runtime> is IPool<T>` 当且仅当类型相等，所以必须有非泛型入口）。回落**只在编译期类型无池时发生**，两边都有池时行为与修复前完全一致——不会把实例改投进另一个池；两条用例分别钉住这两半

- **XPool `PooledObject<T>` 重复 `Dispose` 会把同一实例重复入池**：包装器是值类型，每被赋值或传参就复制一份、各份都指向同一个实例，而 `Dispose` 直连 `Return`——第二次归还会把同一实例重复入栈（Release 下没有重复归还检测兜底），此后可能被两次 `Get` 同时拿到。加 `_disposed` 标志后重复 `Dispose` 早返回，只保护**同一个存储位置**：复制出去的副本仍会归还两次，这一点写进了 XML doc。代价如实记录：`PooledObject<T>` 因此不能改 `readonly struct`

- **XPool 预热绕过 `MaxSize`**：预热循环无条件 push，`new PoolConfig { PrewarmSize = 10, MaxSize = 3 }` 会得到 `CountInactive = 10` 的池——一建池闲置数就超限，多造的实例没有去处，还会白占一份等大的栈容量。预热数量统一钳到上限，该值同时用作 `Stack<T>` 的初始容量；契约写进 `PoolConfig.PrewarmSize` 的文档

- **XPool 模块 XML 文档缺陷遮蔽了一批死链**：八处文档注释里 `<c>Get<T>()</c>`、`Configure<Enemy>(...)` 的尖括号未转义，被编译器当作 XML 标签（CS1570）而**丢弃整块注释**——生成出的 XML 文档残缺，且块内 `cref` 一律不被检查，死链因此长期隐形。修掉转义后暴露出跨命名空间且无 using 的 `<see cref="AssetManager"/>`、以及 `IPool` 与四个集合池里因文件无 `using System` 而未解析的 `<see cref="IDisposable"/>`，均改为全限定名。验证方式：以 `-p:DocumentationFile` 重编，模块内 CS1570 / CS1574 清零。顺带发现：本项目 csproj 未设 `DocumentationFile`，文档注释从不被验证，全项目现有 80 条唯一 CS1574——超出本模块范围，另议

#### Input

- **`InputBootstrapStage` 补 `IInputProvider` 重载：换输入后端（Rewired 等）也能一行 + 清理对称**（用户问「Rewired 项目 `GameLauncher` 的 Input 字段填什么」引出）。答案的前半是**留空**——那份 `InputSystemOptionsAsset` 是**默认后端**（Unity Input System）的数据载体，而换后端属于「服务实例」，按既定边界走覆写。但核实出两个坑：阶段只收 `InputSystemOptions` → 自定义后端想清理对称（走 `Bootstrap.Shutdown`）得自己写约 20 行的阶段；若图省事直接调 `InputManager.Initialize`，则**没人管清理**（`GameLauncher` 的生命周期方法刻意 private 非 virtual）。现补 `InputBootstrapStage(IInputProvider provider)`（后端中立：`IInputProvider` 在 `XFramework.XInput`，不含 Unity 类型，不触上一条刚立的边界）：`ConfigureStages()` 覆写里 `Bootstrap.Register(new InputBootstrapStage(myProvider))` 一行即可，`Shutdown` 经 `InputManager.Destroy()` 释放它（对自定义 provider 同样 `Dispose`）。**重载二义性按 D2 纪律用编译器验**：裸 `null` 实参确实 CS0121 二义（XML 那句注释因此是实测而非推理）。顺带把 `InputManagerReactiveTests` 里的私有 `FakeProvider` 提成共享文件 `FakeInputProvider`（两个夹具共用）。**不做**：不给 `GameLauncher` 加「自定义后端」的 Inspector 槽——字段面只配数据、换实现走代码（同 `RemoteServices` / `CryptoProvider`）

#### Audio

- **Audio `PrewarmVoices` 是死选项**：README 与 XML 都承诺「true 则在初始化时一次性建满播放源」，而全仓（含测试）除声明处**没有任何读取点**。现实现它（`AudioSourcePool.Prewarm` 复用既有 `CreateVoice` 循环，与惰性路径形状一致）。另订正 `AudioManagerImpl` 类文档里那段失效的 `<remarks>`——它称部分成员仍抛 `NotImplementedException`，而该异常在文件里早已不存在（全文件唯一出现即这句注释）

#### Log

- **`LogManager.Initialize` 注入非内置实现时提醒「Unity 全量捕获随之停用」**（同轮审计的附带发现：此前是静默的，无警告、无替代钩子）。警告在**替换之前**经当前实现发出——那一刻必然是框架实现，所以它 ① 不经过你注入的实现、不可能因它的异常而失效；② 不可能递归。这是本模块「自身永不记日志」的唯一例外，理由写在 XML 里；`LogCategories` 新增 `LogManager` 分类（该模块此前没有任何日志）

#### Settings

- **Settings 字段句柄 `SettingRef.TryWriteValue` 会抛异常，违背 `IReactivePropertyWriter<T>` 唯一的承诺**：该接口承诺「目标失效 → 返回 `false`、**不抛异常**——绑定层不该因为目标失效而把异常抛进 UI 事件回调」，而 `SettingRef` 的实现是 `Value = value; actual = Value;`，两条路径都经 `Current()` → `SettingsManager.Settings<T>()` → `GetManager<T>()`，后者在类型未注册时**抛** `InvalidOperationException`。失效场景已核实可达且不是误用：`Initialize` → `slider.BindTwoWay(volume)`（框架自己的测试就是这个组合）→ `SettingsManager.Destroy()` → 玩家拖动滑条 → 异常穿过 `Slider.onValueChanged` 抛进 UnityEvent——正是该接口要挡掉的那类异常；而句柄寿命长于管理器是**有意设计**（有测试锁定）。现以既有的 `SettingsManager.IsRegistered<T>()` 作非抛探测（不新增 API），类型不可用时 `actual = default; return false`。刻意**不**采用 try/catch：本仓对「用异常表达正常分支」有成文反感。同时明确分工——`Value` 的读写在类型不可用时照旧抛并附修复提示（那是编程错误，应当被看见），只有 `TryWriteValue` 承担「不抛」契约。该成员此前**零直接覆盖**，已补 3 条（可用时写入并回传 actual、注销后返回 false 不抛、未 Initialize 同样返回 false），并实测改前必红——失败栈即上面那条链

- **Settings 字段句柄 `SettingRef` 的订阅立即回调抛异常同样永久泄漏**：与 Reactive 侧同源——`Subscribe` 先经事件流注册、再直接调用 `onNext(Value)` 补那次立即回调，回调抛异常时句柄交不出去，订阅从此无法退订。句柄**刻意不实现 `IDisposable`**（它通常活到进程结束），泄漏后连「释放句柄」这条兜底路径都不存在，订阅会一直留在事件流链表上。现与 `ReactiveProperty` / `ReadOnlyReactiveProperty` 采用同一处置：注册后包一层 try/catch，catch 里先退订再原样上抛。补 1 条测试（断言方式同 Reactive 侧：处理器首次调用抛异常后，再写值不得再次触发它）

- **Settings `Destroy` 后永久不可用**：`_destroyed` 是单向闩锁——`Destroy` 置位后 `Initialize` 开头即抛 `ObjectDisposedException`，而该异常消息偏偏写着「SettingsManager 已被销毁，请重新调用 Initialize」，照着做会再抛一次。关闭 Domain Reload 时静态字段不复位，缺陷还会跨 Play 会话残留。现改为复位而非闩锁，与 Localization / Config 门面一致；重复 `Initialize` 补 LogWarning

- **Settings `Reset` / `Load` 忽略 `defaultFactory`**：该工厂此前只在构造函数一条路径上生效——`Reset` 硬编码 `new T()`、`Load` 直接取 store 的返回值。后果是玩家点「恢复默认」拿到的是字段初始值，而不是 README 承诺的设备自适应默认值，**文档与实现相反**。`Load` 侧还需先问 `Exists()`：`ISettingsStore.Load` 的契约是「无数据时返回 `new T()`」，管理器无法区分「读到了持久化数据」与「根本没有数据」

- **Settings 损坏文件会让游戏每次启动都崩**：原实现完全不捕获异常，读失败或 JSON 解析失败一律抛给主循环。最坏的后果不是「读不到设置」，而是存档被写坏后**此后每次启动都崩、且玩家无法自救**——设置文件由游戏自己写，玩家不知道该删哪个。现读失败与解析失败一律 LogWarning 并回退默认值；主文件损坏时尝试一代备份 `.bak` 回退，但**主文件不存在时不回退备份**——否则 `Reset`（删文件）之后的下一次 `Load` 会把玩家刚重置掉的旧数据从备份里复活

- **Settings 测试用例生来就错**：`ObserveField_FieldIsolation_IndependentCallbacks` 断言「只改 Volume 时 Name 观察者不回调」，与同一文件里的 `ObserveField_FirstValue_AlwaysPasses` 直接矛盾。该用例随「移除 R3 依赖」那次提交与该测试文件一同引入即错，并非后续回归——R3 原实现的 `Select().DistinctUntilChanged()` 同样放行首个值，故它在 R3 下也一样会失败

- **文档修正（Settings）**：`早期的框架文档` 的「设置操作」表此前记录了一整套**代码中不存在**的 API（`Get<T>()` / `ResetToDefaults` / `ApplyAsync` 等），第三方照文档接入会踩空；现改为如实描述。Settings 模块 README 同步重写，补入两级订阅分工、写入契约与「已知限制」一节

- **Settings 选项在初始化之后仍能影响行为**：`SettingsOptions` 是可变 POCO 而管理器长期持有它的引用，四个字段却各有各的读取时机——`AutoSave` / `AutoSaveDelay` 只在构造期读（事后改静默无效），`CurrentVersion` 每次读写实时读，`SaveOnQuit` 在构造与 `Dispose` 各读一次。后两者是真缺陷：`SaveOnQuit` 中途由 true 翻成 false 时，构造期订阅了 `Application.wantsToQuit` 而 `Dispose` 按「当前是否 true」判断，退订不执行，管理器被该静态事件**永久 root** 到进程结束；`CurrentVersion` 中途改会让上下半场写出的落盘格式不同，旧版本客户端读到下半场的文件会整份拒绝，用户的设置看起来凭空重置。现改为构造时快照进 readonly 字段，并**去掉 `_options` 字段本身**——不留活引用，「读活选项」在结构上不再可能，而 `AutoSave` / `AutoSaveDelay` 本就只在构造期用得上

- **Settings 加载过程中换后端会读到另一个后端的数据**：`LoadCore` 自身就读 `_store` 两次（先 `Exists` 问「有没有数据」、再 `Load` 取数据），`LoadAsyncCore` 还额外读一次做能力探测，三处之间没有任何约束保证是同一个后端。于是「读取过程中 `Store` 被替换」时——异步路径下这个窗口横跨 await，尤其宽——探测看到的与读到的可能是两个后端，读回的数据与前半程的判断无关，且毫无提示。现把 `ISettingsStore` 参数穿进 `LoadCore` / `LoadExisting` / `LoadFromStore`、在入口快照一次，对齐 `SaveAsyncCore` 已有的做法；同步 `Load` 与异步降级路径同时收口

- **Settings `JsonFileStore` 同路径并发读写**：同步 `Save`（主线程，自动保存可触发）与在飞的 `SaveAsync`（同步后端走线程池）会争同一个 `.tmp`；更严重的是 `FilePathUtility.ReplaceFileAtomically` 的三步降级路径里存在「正式文件短暂不存在」的窗口，此时并发的 `Load` / `Exists` 会命中「主文件不存在 = 无持久化数据」这条**刻意设计**的分支，**无告警地**返回默认值——静默的错答案，不只是少一条日志。现加实例级锁，四个公开成员全部进入（只锁写入口不够，读取同样要在窗口之外）；参数检查留在锁外。残余限制是两个实例指向同一路径不在此列，已写入类文档与 README。同样无配套测试：并发缺陷无法确定性复现，冒烟用例的绿色结果不构成证据

- **Settings 退出兜底改用 `Application.wantsToQuit`**：它在退出流程中比 `quitting` 更早触发，写盘更可能在被拆掉之前完成。两个事件在编辑器播放模式下都不触发、在 iOS / Android 上也都不保证触发，故文档写明它是「构建产物里的最后一道兜底」而非可靠机制——真正可靠的是 `AutoSave` 与 `SaveOnPause`。改动同时带来一条必须钉住的契约：`wantsToQuit` 是 `Func<bool>`，返回 `false` 会**取消退出**；处理函数恒返回 `true`，把「保存失败」翻译成 false 会让玩家关不掉游戏，那比丢一次设置严重得多

- **Settings 注释与文档四处订正**：① `SettingsAsyncTests` 里「Observe 当前不立即回调」的注释与实现相反（实现就是立即回调，另外三个 fixture 明确断言这一点，该用例靠相对断言侥幸通过）；② `ISettingsManager<T>.Dispose` 宣称释放检查能避免「订阅返回一个永不回调的空句柄」，而释放**之前**已发出的句柄恰恰会变成这种句柄（`EventStream.OnCompleted` 只置标志、不清订阅），措辞收窄为两半都说清；③ `SaveAsyncCore` 的「`JsonUtility` 非线程安全」不成立——Unity 文档明确 `FromJson` 可在后台线程调用，而它自己的降级路径本来就在线程池上序列化；④ 新增两条已知限制：`T : class, new()` 约束正是让字段初始化器生效的前提（缺无参构造时 `FromJson` 会让所有字段落到类型默认值、初始化器一律不执行），以及迁移器与校验器在「同步后端 + `LoadAsync`」组合下会跑在非主线程

- **全模块配置面审计：修正 12 处「文档与代码不符」+ 补齐路径/目录视图**（用户要求「审计所有模块的入口，列出可自定义实现的模块与可配置项」）。三个探查代理逐行回代码核对了 19 个模块，对照物是顶层 README 的两张表：新增 **「可替换的实现一览」**（19 个模块，写「无」的 7 个附代码原话理由）与 **「路径与目录」**（默认值 / 能否改 / 改的入口，含 8 处不可改的内部字面量），「可配置项一览」逐行修正并按「能持久改变行为的选择」口径补漏（纯操作不列）。**12 处修正里最实质的四条**：① Settings 说「四个注入点」——`IAsyncSettingsStore` 其实是 store 上的**能力探测**，不是独立注入重载；② File 说「初始化或运行时接线」——`IFileProvider` 是**一次性**的（换它须先 `Destroy()`）；③ Pool 的「首次建池时消费」只对 `PoolManager.Configure<T>` 成立，四个集合池是「**无活跃实例时**生效」；④ Localization README 说「换数据源只能替换 `ILocalizationManager`」——而 `SwitchLanguageAsync` 是门面方法、直接 new 内部 loader，**换实现换不掉那条加载路径**。其余：Asset 的字段面覆盖范围（是第 1/2/4 项）、Update 的 `timeMode`（`RegisterFixed` 没有）、Lock 的 `AutoReleaseOnDestroy`（还控制已销毁主体的准入门禁，XML 同步补齐）、Reactive 的「无配置点」（有 comparer）、Input 漏的 `InputBootstrapStage(IInputProvider)`、UI 的 `TipAssetPath` 是**静态属性**、`.tmp`/`.bak` 是 **`public const`**

#### UI

- **模态遮罩挡不住输入、`clickToClose` 从未生效**（`UIManagerImpl.EnsureMaskInstance` 缺 `GraphicRaycaster`）。
  **根因**：UGUI 的 `GraphicRaycaster` 只查**自己 GameObject 上那个 Canvas** 下注册的图形，而 `Graphic.CacheCanvas`
  取 `GetComponentsInParent` 的第一个（自身优先）。遮罩自带 `Canvas`（`overrideSorting`），于是它的 `Image` 注册在
  遮罩自己的 Canvas 下——挂在 `UIRoot` 上的 raycaster 看不到它，**整张遮罩不被任何射线命中**。同一个教训在捕获层上
  是落实了的（`AttachPopupCatcher` 的注释明写「它必须自带 `GraphicRaycaster`」，`UIPopupTests.Catcher_HasGraphicRaycaster`
  还专门断言「否则挡不住输入」），唯独遮罩漏了。
  **实测证据**（2026-10-11，PlayMode，测试壳 640×480）：屏幕正中 `EventSystem.RaycastAll` 命中数为 **0**；
  同一装置下带 raycaster 的正向对照物正常命中（`UIMaskPointerTests.PositiveControl_IsHitByRaycast` 绿）。
  **临时停掉修复那一行 → `Mask_IsHitByRaycast` / `ClickToClose_TakesEffectUnderRaycast` 立刻红（命中=0），加回即绿**
  ——结论与修复互为因果，不是纸面推理。
  **行为变更（对第三方可见）**：遮罩**从此真正吃点击**——此前点得穿的遮罩以后点不穿；`clickToClose` 也才真正生效。
  **为什么九个月没被发现**：`UIMaskTests.ClickToClose_PopsTopPanel` 一直是绿的，而它走 `Button.onClick.Invoke()`，
  验的是**接线**（谁在挡、点了关谁）而非 UGUI 的射线管道（该前提写在 `UIPopupTests` 头部）。接线通了，管道无人过问。
  现新增 `UIMaskPointerTests`（从射线起步的端到端），并在 `UIMaskTests` 加三条结构断言（含一条正向控制）。

- **UI 帧通路无异常隔离：任一 `OnUpdate` 抛异常即让整条每帧通路永久死亡**（UI 门面定向审计 P0）：`UIManagerImpl.DriveTier` 的派发循环直接调 `panel.OnUpdate`，异常逃逸到 `UpdateScheduler` 后按节点契约被「记 LogError + 注销该节点」——而门面记账是单向的（`_frameDriver` 非空即早退），驱动器再也回不来：面板 / HUD / Tip 全部静止，唯一线索是一条以内部类名结尾的 `[UpdateScheduler]` 日志（仓内先例：`InputManager.PulseFrame` 早有同款守卫）。现分三层隔离，各管一段：**逐面板**（`DriveTier` 循环内，记 `[UIManager] Panel '…'.OnUpdate threw …` 后**停更该面板**、其余面板照常——标记落在 `UIPanelBase.UpdateFaulted`，复位在 `RecyclePanel` 的 `finally` 而非可被覆写的 `OnPoolRecycle`：框架状态复位不能依赖用户代码）；**整层兜底**（门面 `FrameDriver` / `TierDriver` 的 catch，catch 后仍返回**原档位**——返回值漂移会被调度器改档；它覆盖注入实现的 `Update` 与将来新增而未加守卫的调用）；**provider 调用点**（HUD / Tip 各自隔离、首条日志后阻尼、换 provider 复位——provider 没有「停更」机制，停掉它等于停掉全部 HUD）。**改前必红实测**：stash 掉实现改动后新 fixture 4 条全红，失败消息即缺陷本身（`[UpdateScheduler] FrameDriver.OnUpdate threw exception, unregistering`，栈里 `DriveTier` → `FrameDriver.OnUpdate` → `TickEveryFrameBucket`，分档那条走 `TickSlicedBuckets`）；后两层各自单独 stash 复验，红点都精确落在本层新增的断言上。修复后 UI 全量 PlayMode 198/198

- **补记三条漏记的门面修复**（`bc92ce6` / `e5bc950` / `34aaab8`：提交在 `[0.3.0]` 段内，却从未进流水）：`SetInstance` 注入的实例也接入每帧驱动（此前只有 `Initialize` 注册驱动器，而驱动类是 private sealed、外部无从补注册——注入后面板 / HUD / Tip 全都没有人驱动）；换实例时先收掉旧实例与其分档驱动器（它们各自持有旧 `UIManagerImpl` 引用，换实例后没人再上报需求，会永远留在调度器里按周期空转）；`Initialize` 消费即清 `PanelFactoryFactory`、`Destroy` 的状态复位移进 `finally`（否则 `Dispose` 抛一次异常就把门面留在「`IsInitialized` 仍为 true、驱动器却已全部摘掉」的半死状态）

- **UI 双向绑定在目标已失效时泄漏 UnityEvent 监听**：`SliderTwoWayBinding` / `ToggleTwoWayBinding` 的构造函数先 `onValueChanged.AddListener`、后 `target.Subscribe(...)`。而 `Subscribe` 会抛（目标已释放 → `ObjectDisposedException`；设置句柄背后的类型已注销 → 上一版还走 `InvalidOperationException`），它抛出时构造函数随之失败、**绑定对象永远拿不到**，于是 `OnDispose` 里的 `RemoveListener` 永不执行。面板是回池的，残留监听会一直留在控件上，此后每次拖动/切换都打到**上一个 ViewModel 的属性**上。现改为先订阅后挂监听（`Subscribe` 抛出时监听尚未挂上，自然不残留），并为「挂监听本身失败」补了回滚——滑条已销毁时 `AddListener` 同样会抛，此时回滚刚建立的订阅。补 2 条测试（滑条/开关各一），用一个可失效的计数目标把「残留监听是否还在打这个目标」变成可断言的事实；已实测改前必红（`Expected: 0 But was: 1`）

- **UI 遍历中改集合导致崩溃**：`UIManager.Update` 原先在 `foreach` 活动面板集合的过程中调用 `OnUpdate`，而面板自关时关闭路径是同步走完的（默认控制器的 `OnBeforeCloseAsync` 同步完成），于是当场抛 `InvalidOperationException`。现改为先取快照再派发

- **UI 同类型并发打开会各自实例化**：第二次 `RegisterPanel` 会把第一个实例直接回池，而它的 `IsOpen` 仍是 true、首个调用方拿到的是池中失活引用，`PanelOpenedMessage` 还会发两次。新增在途打开表，后来者加入同一次打开并共享结果（加入者各持自己的 `UniTaskCompletionSource`——UniTask 只支持单个 continuation，共享同一个会被第二个等待者覆盖）

- **UI `PushAsync` 的失焦不回滚**：打开失败或取消时栈顶会永久停在失焦态（`IsPaused` + Raycaster 关闭）。现补齐回滚，且只恢复交互、不动显示栈——栈根本没变，回到焦点不等于回到栈顶

- **UI `CloseSelfAsync` 在 `OnOpen` 内静默失效**：`IsOpen` 原先在 `await OnOpenImpl()` **之后**才置位，而该方法的守卫是「未打开则忽略」。现引入显式状态机，打开期间 `IsOpen` 即为 true；关闭请求延迟到 `OnOpen` 返回后执行——在 `OnOpen` 的调用栈上重入 `OnClose` 与关闭动画，子类几乎必然写出「先初始化再被清理」的乱序

- **UI 面板回池不解绑 ViewModel**：`UIPanelBinding.Unbind` 原先只在 `OnDestroy` 调用，而面板是回池不销毁，该回调永不触发——关闭后 ViewModel 与订阅会一直存活到下次打开同一面板，此后再不打开就永久驻留。改由 `OnPoolRecycle` 解绑（挂在回池点而非关闭点是刻意的：关闭可被 `OnBeforeCloseAsync` 拦下，那时面板仍然开着）

- **UI HUD 回池漏调 `OnPoolRecycle`**：Canvas 排序值从此在回池时复位，不再留在实例上被复用时误盖别的面板

- **UI Tip 是模块内最后一条自建帧通路**：`UITipItem.PlayAsync` 原先跑 `UniTask.Yield` 自循环并读 `Time.deltaTime`，于是 Tip 既不受 `UpdateManager.Pause` 约束、也不进 LOD 调度，且每条 Tip 一个循环实例。现改为由驱动器逐帧推进，与面板、HUD 共用同一条通路

- **UI 遮罩的 `clickToClose` 事后设置无效**：原先该开关只在「创建遮罩」那条分支里被读过，已存在的遮罩上传 true 没有反应。现创建与事后切换共用同一段逻辑（开关用 `Button.enabled` 而非增删组件——`Object.Destroy` 要到帧末才生效，切换后同帧查询会看到残留组件）

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

#### Lock

- **Lock 派发防御（两处高危）**：① 全局锁派发期间订阅者增删订阅会抛 `InvalidOperationException: Collection was modified` 并从 `AddLock` 冒出去（订阅表是 `Dictionary`，派发时正 `foreach` 它）——「收到一次就退订」的一次性订阅、以及「解锁时顺手清理订阅」的 UI 都是自然触发路径；现在全局分支先把订阅者取进快照再遍历（模块内自持的池，因为回调里再加一把全局锁会重入派发）。② 订阅者抛出的异常原先会从加锁/解锁路径逃逸——`AddLock` 先入表再派发，异常出去后调用方拿不到句柄、锁却留在表里（实测 `GetLockCount` 残留 1，即没人能释放的永久锁）；现在四处派发点全部隔离（记 `[Lock]` LogError 后继续），覆盖 `OnGlobalLocked`/`OnGlobalUnlocked` 两个总线事件。粒度：全局分支逐订阅者隔离，主体分支与两个总线事件是整条多播一次 `try/catch`（不用 `GetInvocationList`，取舍同 `Pipeline.DispatchSafely`）

- **Lock 订阅入口归一**：`OnLocked`/`OnUnlocked` 现在把 null 主体归一为 `Global`（此前抛出的是 `ArgumentNullException`，paramName 是 `key`——模块其余入口一律把 null 当全局）；null handler 立即抛 `ArgumentNullException`（否则留下「有键无回调」的幽灵订阅，既永久强引用主体、又让「该主体还有没有订阅」的判定永远为真）；两个总线事件的载荷主体一并归一（此前传 null 就收到 null）。**以 `Global` 订阅此前是死订阅**（全局分支显式跳过 Global 键），现改为「只关心全局锁的加/解锁事件」

### Documentation

#### Bootstrap

- **`Runtime/Bootstrap/README.md` 与 `Runtime/Update/README.md` 补齐「引导流程 × 每帧派发」的边界**：一轮定向审计（问题：「如何保证框架加载完成才执行 Update」）确认**不存在、也不打算有**这样的门控——Update 的调度器建于 `SubsystemRegistration`、PlayerLoop 注入于 `AfterAssembliesLoaded`，都早于首个场景，`RunAsync` 期间（Asset 初始化可能持续数秒）派发照常。两侧都不缺防护：框架内建 ticker 全部「初始化成功后才注册 + 未就绪空转」，使用方回调的正解是 `await Bootstrap.RunAsync()` 之后注册。README 新增：Update 侧把「派发与启动/引导流程无关」写进 `已知限制`；Bootstrap 侧在 `设计取舍` 回答「为什么不自动暂停/不提供就绪门控」，并给出确需加载期安静时的既有写法（`Pause()` → `RunAsync` → `finally Resume()`，只冻逻辑轴，`Unscaled` 节点照常）。同轮归档：`Bootstrap 模块的维护向记录` 新增「已评估未采纳与未决」（自动门控与就绪句柄两个方向否决的理由；GameLauncher 启动途中销毁与在途 `RunAsync` 交叠列为未决，推理未实测）；`Update 模块的维护向记录` 已评估未采纳补一条「不加加载期硬门控原语」。

- **Roadmap 的「统一框架 Profile」条目**：修掉失效出处（`早期的框架文档` 已随包内模板残留删除），并在「重开判据」上标注状态——「先回答引导顺序」这条已于 2026-10-02 满足（`BootstrapPhases` 契约化），但不构成重开理由：另一条判据「不替代模块 Options」排除的正是中心化形状

#### Pipeline

- **Bootstrap 模块审计一轮（A–F 全量；此前只做过一次单问题定向）**：主题「编排方式对第三方自定义的友好度」，对照 Unity Entities `ICustomBootstrap` / Zenject / VContainer / UGF Procedure / MS.Extensions.Hosting 五种启动流程，7 项发现全部落地为代码或文档（上面四条 + 已知限制 + 归档）。README 新增 `## 已知限制`：**没有超时手段**（`IPipeline` 的超时只在阶段粒度，而 Bootstrap 每相位装配为一个 `ParallelStage`，从 Bootstrap 侧不可达——不响应取消的挂死阶段会挂死整个启动）；`Shutdown` **可能在阶段从未执行过时被调用**（启动在它之前失败/取消，或它是在运行中才登记的，清理实现要能安全空转——内置 `LocalizationBootstrapStage` 是范例）。`IBootstrapStage.Shutdown` 的 XML 补同一条契约，接口示例的 `Phase => 10` 改为 `BootstrapPhases.UserStart`（10 落在框架保留区间内，与新的区间约定冲突）。维护向结论归档进 `Bootstrap 模块的维护向记录`（五框架对照表 + 6 条已评估未采纳 + 3 条未决 + 审计轮次），`Pipeline.md` 的异常承载节补失败原因新格式。顺带按 A1 探针「一处改动找齐所有落点」订正三处同义句残留（`IBootstrapStage` 类注释、`Bootstrap` 类注释、包 README 的 `Shutdown` 注释与可配置项一览）

#### Asset

- **修四处「文档与实现相反」**：Localization README 说语言表放「`Resources` 目录」——那是 YooAsset 迁移前的残留，实现走的是 `AssetManager.LoadAsync<TextAsset>` 的 **YooAsset 地址**，照 README 放文件必然加载失败；该 README 的架构树漏列 `LanguageChangedMessage.cs`；`早期的框架文档` 的「本地化操作」表四行里**三行是不存在的 API**（`GetText` / `SetLanguageAsync` / `HasKey`）；`CLAUDE.md` 把 Localization 归在「**无参** `Initialize` + `SetInstance`」变体下，实际是两参必填——无参那档的例子改为 Config/Input，并补上「`Initialize` 也可以带参数」这一档

- **Asset 模块审计一轮（判据 A–F 全类扫过，一处高危 + 七项修复）**：范围由「审计门面」扩至整模块。README 新增 `## 线程契约` / `## 已知限制` / `## 接口承诺到哪为止` 三节（此前**从未有过**这三节，而维护文档声称它们在那里）并修掉两处**照抄出错的示例**（`InitializeAsync(progress, options)` 参数顺序相反、`T asset = handle.Asset` 与上文的 `<TextAsset>` 不一致）；`InitializeAsync` 的 XML 补全「options/progress/令牌一律取首个调用者」与作废语义，`SetInstance` 写明**不释放原实例**（与 `Destroy` 相反），`Destroy` 的「释放所有资源」改为精确表述（不卸载 YooAsset 侧的包与已加载资源——那正是「Destroy 后可重新初始化复用」的前提）。维护向结论归档进 `Asset 模块的维护向记录`（9 条已评估未采纳 + 4 条未决，含被证伪的那条与「`lowMemory` 回调线程需真机实测」）。`ModuleAudit.md` 第三节**新增判据 B6「共享协调协议代际覆盖面」**（凡「由当前一代写入、由任意一代读取」的共享槽都是代际漏洞候选；本仓实例即本轮 F1）

#### Update

- **Update 第二轮审计归档（定向 `UpdateManager.cs`，判据 A–F 全类扫过，仍无高危）**：四处新账——① `Enable` 档位口径的三处 XML 注释是上一轮 F1 的**另一半**（README 已勘误、注释没跟上，至今写着「回到注册时声明的档位」，与实现及用例 `Enable_AfterTierMigration_ReturnsToLatestTier` 相反）；② 门面类摘要把手动驱动入口指成 `Tick(float)`（与 `Tick()` / `Tick(float)` 的文档和 README 三处相反，是补无参重载前的残留）；③ `GetCount` 未写「不含禁用对象」、门面 `IsEnabled` 未写「未注册也返回 true」、`Pause` 未写「固定步时机一并冻结」；④ README「已知限制」补一条「注册表持强引用且不识别已销毁对象」。同轮补一条**覆盖缺口**：`Pause()` × 墙钟轴在 README 有承诺却**零断言**（该轴在仓内也零生产使用者），新增 `Pause_LeavesUnscaledAxisRunning` 并以**负向控制**证明其判别力——把冻结条件放开到两条轴后，23 条用例中恰好这一条变红、失败消息即该断言。归档落点：`Update 模块的维护向记录` 逐轮追加本轮记录；「已评估未采纳」新增四条（不钳 `ProcessImmediate` 的 `deltaTime`、不拆 `UpdateManager.cs`、不把注入匹配改成父链校验、不给 `UpdateClock` 加固定步时基字段）；「未决」把「LateUpdate / FixedUpdate 零生产使用者」扩写为「墙钟轴同样零生产使用者」

- **归档第二族「关闭域重载时第二个播放会话静默失效」**（`Runtime/Input/README.md`、`Runtime/UI/README.md`）——它与本次修的「自动初始化没被调用」常被混为一谈，分开记才不会下一轮重新论证。Input 最严重：退出播放时 `UpdateManager.OnQuitting` 释放并置空调度器，第二会话里 `Initialize()` 因 `_initialized` 为真而告警 return、`RegisterTicker()` 不再执行 → `_ticker` 还在但**没有调度器持有它** → 帧脉冲永不发布、`Observe*` 静默失效，唯一线索是一条「called more than once」警告。`Asset` / `Config` / `Localization` / `File` 同根因：使用方**照文档再调一次 `Initialize()` 会被「已初始化则忽略」变成 no-op——框架让他无法自救**。UI 因 `UIRootNode` 的成对生命周期部分自洽，条目写成边界提醒 + 多根场景未覆盖。三处都写明验证手段（关闭 Reload Domain 连跑两遍 PlayMode 全量；本仓当前两个 reload 都还开着，故缺陷在仓内休眠）

#### Message

- **`Runtime/Localization/README.md` 补 `已知限制` / `设计取舍` / `审计记录` 三节**（该模块首个完整审计归档）。使用方要知道的边界：语言切换**必须在主线程**（模块自身不做任何线程同步，而 `SetLanguage` 经 `MessageManager.Publish` 有主线程契约——这条此前一字未提）；并发切换是「**后完成者胜**」而非「后请求者胜」（无在途去重、无代次）；`SetLanguageData` 覆盖**当前语言**的数据时**不发通知**（查询面立刻变了、事件面没有）；注入的 `Dictionary` 按引用持有；`GetFormat` 会吞掉非法花括号；`Get` 在占位符路径上有分配；`MaxCachedLanguages = 4` 是自定值，且「4」里真正可淘汰的只有 2 个（当前 + 回退各钉一个位）。已评估未采纳六项（含「不给 `ILocalizationManager` 加 `IsInitialized`」的理由：接口加成员对第三方实现是**源码破坏**）与四项未决，全部写明理由，免得下一轮重新论证

#### Reactive

- **Lock README 新增「典型场景」七族**：角色控制封锁（多来源同时发生，bool 会互相覆盖）／全局与系统级／UI 交互门与防重入／替代互相打架的 bool／状态镜像／事故排查／生命周期收口。三处要点：**全局锁没有所有者**（挂在哨兵上，永不自动释放、不随场景切换清理——「玩家一直不能动」最常见的成因，那个 `using` 不是可选的）；**「等解锁」的正确配方**（必须先查一次 `IsLocked`，否则无锁定时订阅收不到任何回调、会永久挂起且无症状指向）；表现层与 `ReactiveProperty` 的合用桥

- **Lock README 新增「与相邻能力的边界」**（体例照 Event README）：锁 vs `ReactiveProperty`（开放集合 vs 闭合求和）、锁 vs `UIMaskHandle`（触及 vs 逻辑；两者**键语义相反**：遮罩是单调令牌计数、锁是值相等幂等）、锁 vs `PreconditionChain`（持续被挡 vs 单次校验），以及三个相邻电平开关（`Time.timeScale` / `UpdateManager.Pause` / `InputManager.DisableActionMap`——单写者、不叠加，Lock 的存在就是为了替换这一类但不接管它们）；并如实写明**执行侧仍可能被别处直接改而绕过**

- **Reactive README 补「集合（`ReactiveList<T>`）」节**（使用方向；其中「`foreach` 装箱」一条**同版内随零装箱改造消失**，见 `### Changed` → Reactive）：快速示例、与 `ReactiveProperty<T>` 的两处关键差别（订阅不立即回调；只有「无变化不发通知」而没有「相同值去重」）、六条已知限制（批量 = 逐项通知、索引瞬时有效、排序恒发 `Reset`、`foreach` 装箱、回调内改动的三种后果、没有 Dictionary / Set 与集合绑定）。原「集合型响应式：**没有**」一条改写为「部分有」，并**明确 `ReactiveList<T>` 不改变 UI 侧「长列表由项目自建」的既有结论**（`UI/README.md` 那句不动）；「接口承诺到哪为止」补「同模块两个 `Subscribe` 语义相反」的显式对照；Event README 的「被谁使用」与模块定位同步。

- **Reactive README 补「集合（`ReactiveDictionary<TKey,TValue>`）」节**：快速示例、与列表的四处差别（键稳定但条目会被移除 / 无哨兵 / 键比较器可注入 / 遍历顺序不承诺）、字典版已知限制（等值 upsert 不替换实例、键的生命周期由持有者收口——与 Message 的淘汰纪律同姿势）；「已知限制」与「本模块没有的」里「没有字典」的表述改成现状；「接口承诺到哪为止」的 `Subscribe` 语义对照扩到三个接口；「设计原则」把「列表走单流 struct 载荷」泛化为「集合」，并补字典专属两条（键比较器可注入、值相等即空操作）。

- **包 README 补账**：功能表与模块表**上一轮就漏列了 `ReactiveList`**（本轮同批补上），并加 `ReactiveDictionary<TKey,TValue>` 与键比较器配置点（可配置点表的 Reactive 行）；正文「响应式属性（XReactive）」措辞同步。Event README 的模块定位与「被谁使用」同步。

- **`Roadmap.md` §2.5 标为已建成**（正文按 §一 搬迁纪律只留指针）；`Modules/Reactive.md` 文件结构 + 沿革 + 延后项同步——延后项**不删除**：`ReactiveDictionary` 标注为重开并建成，其余各项照旧，字典侧新增「有序键值 / 按值查找」两条延后与「键生命周期自动移除」一条 v1.1 候选（含重开三问）。

- **`Modules/Reactive.md` 新增「实测记录」节**（本模块首条性能/分配数字）：① 稠密键值容器 vs BCL 两形状的三条门槛判定；② **本环境两把分配尺子是坏的**；③ 集合遍历零装箱的改造前后数字。全部标注环境（Editor Mono）与「真机 IL2CPP 未测」，方法写明以便复验。

- **稠密键值容器裁定「不做」**（同批）：用户澄清动机是**效率**（Actor 管理器：全量遍历 + 按键寻址 + N 大）而非顺序，故按本仓纪律**先探针、门槛事先写死**（遍历端到端 ≥1.3× 且 N=4096 单趟 <1% 帧预算；内存 ≤1.35×；查找退化 ≤1.15×）。实测**均不过**：遍历 1.22–1.28×、内存 1.54×、查找退化 1.45–1.61×，写路径另慢 2–3×。**正解写进包内 README**：需要「稠密遍历 + 按键寻址」请自建 `List<T>` + `Dictionary<TKey,int>` 索引（并给出参照量级：`ReactiveDictionary` 在 N=4096 时遍历约 0.12 ms/帧、占帧预算 0.7%）。

- **顺序线撤回 + 修正一条写错的延后理由**：插入序容器（`ReactiveOrderedDictionary`）**撤回立项**——顺序是**语义**需求不是效率需求，保序删除 O(n) 是它的价；两个候选形态（按 key 排序 / 插入序）保留在延后项，**重开判据改为语义判据**。同时修正 `Modules/Reactive.md` 上一轮写错的理由「有序键值**要新增重排词条**」（词典载荷没有位置字段，四个词条本就够用；顺序由容器结构决定，消费方重读）——它与本轮「移动不通知」的裁定直接矛盾。

#### Localization

- **`Runtime/Localization/README.md` 写清占位符的两类用法与四条被否决的形状**：由「`SetPlaceholder` 会不会有多语言需求」一问起头，两轮落定。背景事实：占位符的引入提交 `04b3158` 自述动机是「减少重复传参」，与语言语义无关；全仓 grep `按语言|语言无关|per-language` **零命中**，「按语言」从未被讨论过；至今**零生产调用点**。§7 重写为「按值会不会随语言变选入口」的对照表 + 两类各自的坑，新增「按语言取值的占位符」「语言无关的值」两小节；「已知限制」补两条（键值占位符的值若含 `{0}` 会被 `GetFormat` 当格式项消费；`GetFormat` 的数字 / 日期格式跟随 `CultureInfo.CurrentCulture`（设备文化）而非 `CurrentLanguage`，如实标为边界而非缺陷）；「设计取舍」补一条（**键值占位符做成独立入口，不在 `ReplacePlaceholders` 里隐式回退到语言表**）。对标物：Unity Localization 1.0.0 让 `LocalizedString` 本身可作变量（"When accessed in a Smart String this gives access to the **translated value**"），且其 Smart String 变量**必须显式注册**、不隐式回退到表——与本次的形状同向

#### Save

- **包 README 的存档布局格**从「可改」改为「**可改，但代价高**」（要改只能整体替换 `ISaveManager`）

#### Config

- **Config 模块的变更史不在本文件里——这是补记**。Config 有 31 条提交（含 `fix(...)` 与两次破坏性重构：「移除内建 Luban 支持」、「`IConfigRow.Id` 由固定 `int` 改为泛型 `TKey`」），而本 CHANGELOG 里 `Config` / `ConfigManager` 的出现次数是 **0**。后果不是「读者少看到几条」：`维护向的模块审计手册` 的 A–F 判据是对本文件的 `### Fixed` 段做归纳得出的，**所以审计手册从本模块的缺陷史里学到的是零**；UI 与 Update 都有的「破坏性变更迁移表」，Config 那两次破坏性重构也没有。变更史的事实来源目前是 git log 与 `Config 模块的维护向记录` 的「模块版本记录」（该表按年月记，含 2026-05 与 2026-08 两批）。**本模块其余的变更不回填**——那需要改写已发布版本的段落（等于改「0.2.0 当时到底记了什么」），与「一处只写一份真相」的取向相悖；此处只把缺口如实标出

> **订正（2026-10-13）**：上条里「本 CHANGELOG 里 `Config` / `ConfigManager` 的出现次数是 **0**」**写下即失实**——该条自身就含 `Config`，同一份 CHANGELOG 另有 13 行顺带提及它（`5f3b6fa` 时的实测）。它真正想说的是「**没有一条以本模块为主体的记录**」，那在写下之前成立，而在写下它的同一提交添了第一条（`### Fixed` → `#### Config`）之后不再成立；归并轮补齐了 `#### Config` 的模块轴，现共两条。
> **「手册从本模块的缺陷史里学到的是零」仍成立，但理由要换**：判据归纳的快照日期是 2026-09-27（`维护向的模块审计手册` 头部），本模块第一条 `### Fixed` 记录 2026-09-30 才出现——**归纳入早于缺陷史入册**，与「CHANGELOG 里一条都没有」无关。其余部分（31 条提交的主体不回填、变更史见 git log 与 `Modules/Config.md`）不变。**原文保留以留痕。**

- **Config README 补「与 Luban 一类外部配置表工具配合」的框架侧契约**（纯文档，零 API 变更）。方式三的示例此前假定生成的行类已经是 `IConfigRow<TKey>`，而 Luban 默认生成的行类**不实现它**——读者照抄会编译不过。另两处从未披露：生成代码的加载入口是**同步**委托，而框架 IO 全走异步通道；以及 `RegisterTable` 注入的表没有资源路径、不参与 `Preload` 的并发语义。现补三个前提、「`Register` 不要求 `new()` 而 `Preload` 要求」这条约束差异，并往「已知限制」加两条（注入的表没有加载路径；经 ConfigManager 会留下多份索引视图，不需要统一查询面的项目可以不走它）。
  **包内只留不随 Luban 版本漂移的框架侧契约**（判据是易腐性）：示例里不再出现 `ByteBuf` / `DataList` / `Tables` 构造形态这类随版本变化的东西，target 名、命令行、模板等落在仓库维护侧的 `Luban 接入配方`（不随包发布）。同批把两处源码 XML 示例里的 `tables.TbItem.DataList` 一并改成版本中立写法（纯注释）——模块 README 自己就注着「Luban 各版本 `DataList` 的 API 不同」，而源码注释把它当稳定契约写着。包 README 原先承诺的「Luban 集成指南」也据此收窄为「框架侧约定」。
  **本轮同时复评了「要不要重建内建 Luban 适配层」，结论是不重建**：该形状已于 `a25f6d8` 删除，而当时**只记了「移除 Bright.Serialization 依赖」这条动机、「这个形状为什么不好」从未落纸**。三条否决点（硬绑 `"Bright.Serialization.ByteBuf"` 类型名、绑死 `DataList` 反射约定、主键写死 `Dictionary<int, rowType>` 而被随后的 `IConfigRow.Id` 泛型化淘汰）与重开判据现补进 `Config 模块的维护向记录`。

#### Log

- **订正两处「把碰巧写成必须」的注释**：`LogManager.AutoInit` 的 XML 与 `Log 模块的维护向记录` 的边界节都写着「必须早于其它模块的 AutoInit（Update/Message/Timer 也在此档）」——而**同档内的相对顺序没有契约**（Unity 不承诺，`AutoInitTests` 锁的也只是档位而非先后），该说法当前成立只是因为另三个同档 AutoInit 恰好都不打日志。已改为如实口径，并写明补救方向（将来本档新增会打日志的 AutoInit 应挪出本档——否则那些日志会落在随后被丢弃的默认实现上）

#### Settings

- **Settings 的「没有 `SetInstance`」由未决改判为已评估未采纳**（同批三项之一）：可替换点刻意**下移到** Store / Migrator / Validator（它们才是「会变的地方」），而管理器本身的通知、脏标记与保存时机是**模块语义**——换它等于换模块，与 UI 排序分层、Update 切片节拍同一档（判据见 `Roadmap.md` §四）。理由写进 README 的设计理念表（「替换点在后端」行）

#### UI

- **UI 四项「未做」复核 + 搬迁遗留收尾**：四项（列表虚拟化 / 场景切换安全 / UI 特效层 / UI 引导层）随 `e613867`（2026-09-30 文档分层第五批）从模块 README 搬进 `UI 模块的维护向记录` 后一字未动，本轮逐项裁定——列表虚拟化、跨场景引用自动检测、内建特效层三项**判不纳入框架**（理由与替代路径写进 UI README 新增的 `## 设计取舍`；技术文档只留「评估过并否决」那一层，做法与边界不重复），UI 引导层**拆项**（遮罩挖洞**原语**留在 roadmap 并写明范围与做法边界：区域语义 + 单张满屏 `Image` 改可留洞构成、不用 shader、复用引用计数句柄与 `UISorting.SystemOrder` 预留带；**引导编排不做**——步骤机 / 触发器 / 进度持久化是 GamePlay 架构）。README 补 `## 设计取舍` 同时修实一处系统性漂移：`UI 模块的维护向记录` 第 4 行声称 README 承载它，而 README 此前只有 `## 设计原则`——「技术文档声称 README 承载 X、README 里却没有」的**第七例**。收尾搬迁遗留两处：`Runtime/Reactive/README.md` 那句「见 UI README 的 `[ ]` 列表」指向的清单已被搬走（断链；且不能改指 `维护向文档目录`——包内不指向包外），改为在原句内自述事实；`维护向记录的目录规范` 的模板代码块漏了表格里已有的 `## 已完成功能与未做（roadmap）` 一行。**搬迁本身（`e613867`）也一并登记**——它当时没进流水

- **UI 模块首轮审计归档（定向 `UIManager.cs`，判据 A–F 全类扫过）**：1 条高危 + 4 条中高（前两条已修；`Dispose` 半拆窗口的语义、`Destroy` 一律 Dispose 注入实例两条只记文档，连同三条未决一起归档进 `UI 模块的维护向记录` 新增的「已评估未采纳与未决」）。同轮补的覆盖缺口各一条用例：`SetController` 对注入实现的降级分支、注入实例不被 `SetInstance` 销毁、`Destroy` 之后操作类照抛 + 探测类归零 + `TierDriverCount` 清零、`Destroy` 不是单向闩锁、重复 `Initialize` 告警、`SetInstance(null)`、`Subscribe` 的另两个重载、`UpdateManager.Pause` × 面板、Tier1 专项；24 个 fixture 的 `SetUp` 统一改为先 `UIManager.Destroy()` 归零（NUnit 在 SetUp 中途失败时不跑 TearDown，残留的 `_instanceInitialized` 会让下一个 fixture 的 `Initialize` 静默 no-op，表现为一堆与自身无关的红）。模块 README 的「已知限制」补三条（`timeScale = 0` 冻结、`UpdateManager.Clear()` 摘驱动器、无线程契约断言）。**实测记录**：`Subscribe` 三个 `Action<T>` 重载下无类型 lambda 是编译错误 CS0121（不是静默误判），照纪律用临时探针文件在本仓程序集上编译验证并配了负向控制

- **配置/注入全面清点**（起于用户质疑「框架不应该预设过多配置，由第三方注入比较好」）：判据是「**不想要这个预设时，你要付出什么代价**」——一次注入 / 一个 options 字段 = 健康；抄一个实现 = 有边界但要写清；**不可能 = 越界**。结论：框架在「整体替换实现」层已普遍是注入式，「预设过多」的真身是十余处**策略**没有出口；本轮处理了三处越界出口（均在 `### Added`，按本条判据改为可配置/可替换）与一处死选项。其余分两类落档：**不该开**的策略（UI 排序分层、Update 切片与节拍基、Message 缓冲「只留最后一条」、Event 投递顺序、Timer/Pool/Lock 的静态性——它们是模块不变量，换了等于换模块）写进 `Roadmap.md` §四「不做」并逐条给理由；**本轮未做但可再拾**的三项（Reactive 相等比较器不可注入、Serialize 内置注册静默覆盖、Settings 无 `SetInstance`）记进对应模块技术文档的「已评估未采纳与未决」

- **UI 层容器撞名 = 代码证伪的假问题**：层参数是 `int`，`Layer_{层号}` 永远拼不出 `Layer_HUD` / `Layer_Tip`。结论记进 UI README 的已知限制，免得下一轮审计重提

#### Lock

- **`Runtime/Lock/README.md`**：删除上一轮写错的未决项（我曾把 File 归进「已改好」的一方，实际它才是唯一「注释说两特性都挂、代码只挂一个」的那个，且根本没有 quitting 订阅）；`Runtime/File/` 的注释与 README 线程契约节原先描述的，正是本次改完才成立的形状

- **Lock README 另补**：token 推荐改为**类型化 token**（名字会出现在诊断输出里，且消掉字面量撞名）+「幂等 / 嵌套计数」两种模式的小表；`已知限制` 补五条（全局锁无所有者、聚合事件是电平不重播、全局锁边沿 O(全部订阅主体)、异常日志的事件名不区分聚合订阅、容器未池化的理由）；`已评估未采纳` 补四条（`lockObj` 计数语义属**破坏性变更**、`HasAnyLock` 的形状本身语义歧义、`RegisterLockTypeName` 顺序反了、等待原语与 UI README 同型裁定）

- **根 README**：「主要功能」表补 Lock 一行；快速开始的 `LockType.InputBlock` 全仓不存在（没有 `class LockType`）——改为写明锁的三要素（主体 / 类型 / 持有者身份）并单独给出类型定义

- **Lock 文档勘误**：README 标着「推荐」的示例 `public void CastSkill()` 里带 `await`（CS4033）、`ILockable` 的 XML 示例调从未存在的 `this.Acquire(...)`（CS1061）且基类 `EntityNode` 已随节点系统删除（CS0246）——三处都是把示例**逐字**放进测试程序集编译实跑出来的。另清理了 `LockableExtensions` 与 `Global` 哨兵里的同名残留

- **Lock 补写键的语义**：`lockObj` 是**持有者身份**（存 `HashSet<object>`、按值相等判定，键相等 = 同一把锁——两个来源取同一个名字会互相解掉；推荐每个加锁点一枚专用 token）；`lockType` 是**全框架共享的 `int` 命名空间**（两个模块各写 `Movement = 1` 即同一把锁，编译期毫无提示）

- **Lock 补 `已知限制` / `设计取舍` / `审计记录` 三节**（含五项「已评估未采纳」与两项未决）；模块测试从单个 fixture 扩到 9 个（含扩展面 10 个成员的正反两半覆盖、四个容器的回收护栏、以及把文档承诺逐条写成断言的契约用例）

- **补记 `74b3583`**：`LockHandle` 的持锁语义由 `IsValid`（恒真缓存字段）改为 `IsHeld`（逐主体逐句柄实时查询）——该次变更当时未记 CHANGELOG

- **文档分层：模块 README 归使用方，维护记录抽到仓库根的 `维护向记录目录`**。模块 README 是随包发给第三方的东西，但里面长着「下一轮审计需要知道的」内容——`Lock` / `Localization` 的 `## 审计记录` 开头就写着「本节只留**下一轮需要知道的**」。切分线因此画在**收件人**而不是「技术 vs 使用」上：`Input` 的「关闭域重载后第二个播放会话帧脉冲静默失效」、`Save` 的「删除/移动在主线程付同步 IO」语感全是维护者向（带根因、带实测、带「本轮只归档不修」），内容却是使用方判断「这模块能不能用在我的场景」的唯一依据，搬走他们会写出错代码。判据定为一句：**把这段删掉，使用方会不会写错代码或误判行为？** 17 个模块的文件结构、模块版本记录（4 张表用的是**模块独立成包时代的本地版本号**——`Pool 1.3.0` / `Input 2.4.0`，与包 CHANGELOG 的语义化版本不是同一套；`Config` 那张按年月记，且该模块在整个 CHANGELOG 里一条记录都没有）、沿革叙述、审计记忆（含已评估未采纳与未决项）搬进 `<模块> 模块的维护向记录`；`已知限制` / `设计取舍` / `接口承诺` / 线程与性能契约**一律留在 README**。落点选仓库根是因为 `package.json` 无 `files` 字段、目录内无 `.npmignore`，故它天然不随包发布，也不需要 `.meta`。规则落在 `CLAUDE.md` 新节 `## 文档分层` 与 `ModuleAudit.md` §五的落点表（**已评估未采纳从 README 改落技术文档**）；`Workflow.md` 的提交内环加一步「核对文档」——两问、答「不用」是合法结论，并明说**它不是门禁**（三条门禁都机械可判定，这一条不是）。**搬迁顺手修出 README 里的既有损坏**：`Data` 的「目录结构」代码块**没有收尾围栏**（文件在代码块内部就结束了），以及五份文件清单已经腐烂——`Asset` 缺 4 个文件（`AssetBootstrapStage` / `AssetInitReport` / `AssetInitProgressRelay` / `IAssetPoolController`）、`Message` 缺 4 个（四份统计与策略类型）、`Lock` / `Data` / `Serialize` 各缺 1 个；补齐行的描述一律取自各文件自己的 `<summary>`，不臆造。这类清单没有任何门禁能发现它过期，搬迁前的逐树核对才让它现形。搬迁内容零丢失：逐（源 → 目标）配对跑「源侧删除行 ⊆ 目标侧新增行」的行集守恒断言，并做过改前必红（故意删掉技术文档里一行 → 断言精确报出该行）。`Assets/XFramework/` 的公开面、示例与运行时行为零变化

#### Diagnostics

- **诊断窗口文档收口**：新增 `Diagnostics 模块的维护向记录`（文件结构 / 沿革 / 已评估未采纳与未决）；`维护向记录的目录规范` 索引补一行；包 `README.md` 的模块表与可替换点表各补一行；`UI 模块的维护向记录` 里「不给 `IsLayerInteractive` / `IsLayerVisible` 加门面转发」那条**标注为已落地**（不删除，保留沿革）；`Reactive 模块的维护向记录` 的订阅追踪条目更新参照物（`UIStateWindow` → 诊断窗口），并写明「窗口壳不改变该目标」；`当时的候选模块路线图` 的 GM / 调试命令台条目补上「本轮给了一半答案、仍待裁」。`Runtime/Diagnostics/README.md` 增「编辑器窗口与渲染」一节（默认路径 / 复用视图 / 自绘接口三条用法）。

#### 跨模块 / 框架级

- **`CLAUDE.md` 规则改写：静态服务自初始化**——原文「纯静态服务(如 LockManager、MessageManager、UpdateManager)用 `[RuntimeInitializeOnLoadMethod]` 自动初始化，**遵循 `#if UNITY_EDITOR` 分支写 `[InitializeOnLoadMethod]` 的现有惯例**」把本次判为缺陷的形状规定成了惯例，还点名了两个刚改掉的模块——旧形状之所以扩散成四处，机制就在这里。新条文写清：两个特性都挂的理由（关闭域重载后编辑器分支不再执行）、**load type 按需要选、不为统一而统一**（附三档各自适用场景）、`Application.quitting` 订阅必须幂等（订阅跨会话存活、重复 `+=` 累积）、以及新成员必须去族级守卫登记（未登记即红）

- **新增自动初始化族级守卫**（`Tests/Runtime/Architecture/AutoInitTests`，落在 PlayMode）：反射遍历运行程序集里所有挂 `RuntimeInitializeOnLoadMethod` 的方法，与一张「类型 → 方法 → 期望档位」清单比对，**未登记即红**——新成员必须显式回答「我的档位为什么是这个」。放 PlayMode 是因为 `-Filter` 未指定平台时只跑 PlayMode，EditMode 侧的守卫会被最常见的开发回路静默跳过

- **Editor.Tests 的 asmdef 补 `YooAsset` 引用**（新测试用到它的类型）：`autoReferenced` 只对预定义程序集生效，**自定义 asmdef 必须显式列**——漏了它 `dotnet build` 照过（生成的 csproj 不管 asmdef 规则）而 Unity 报 `CS0246`，是一类「dotnet 绿、Unity 红」的假绿

- **补上「怎么把 XFramework 装进项目」**：README 的依赖章节此前只写「配置完成后，再通过 Git URL 或本地路径添加 XFramework」，而**全仓（除本文件外）从没有出现过本仓库自己的 Git URL**——第三方照那句话做不出来。现补 `### 安装 XFramework` 两节：**方式一 Git URL**（`?path=Assets/XFramework` 是必需的，因为仓库根是一个完整 Unity 开发工程、包本体在子目录；这种方式落在 `Library/PackageCache`，只读、改不了也推不回），**方式二 git 子模块 + `file:` 本地路径**（`file:` 的相对路径以工程的 `Packages` 文件夹为基准解析，故 `../` 即工程根；含「不要放 `Assets/` 或 `Packages/` 下」两个反例与各自的后果；以及 `git checkout main` 那个 detached HEAD 的坑——不切分支就提交，新提交没有任何分支指向它）

- **包内 `Tests/` 在导入方不参与编译**（实测：`Library/ScriptAssemblies` 下只出现 Runtime 与 Editor 两个程序集，测试程序集没有产出）——Unity 的规则是包测试要进工程 manifest 的 `testables` 才编译。因此导入方不必为它准备任何东西；要跑这些测试就把包名加进 `testables`

- **`Roadmap.md` §2.4 标为已建成**：立项时六条边界照原样落地；两处与立项时不同（`Version` 计数器实现前砍掉——本仓无先例的新契约面，且主线程同步下「订阅后立即读」已是原子一致点；`Sort` 签名定为 `Sort(IComparer<T> comparer = null)`）记入条目，正文按 §一 搬迁纪律只留指针。

- **`维护向的工作流手册` 记入「零分配断言可能恒真」**（尺子实测——**点名三处空转的旧实测**：`TimerAllocationTests` / `LogAllocationTests` / `MessageStatsTests:342` + 写这类断言的两条纪律：先自检尺子；尺子不够用时优先用结构断言）。**既有 `TimerAllocationTests` / `LogAllocationTests` 的断言在本环境恒真**——其结论未被证实（未必为假，但没被验证），修订方案待定。

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
