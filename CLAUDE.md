# XFramework 项目规则

## 项目定位

- 本项目是 Unity 游戏框架(内嵌 UPM 包 `com.venusy609.xframework`),以插件形式供其他游戏项目引入,不包含具体游戏内容
- Unity 版本:6000.4.5f1(开发环境);package.json 最低要求 `6000.3`
- API 兼容级别:.NET Standard 2.1;序列化模式:Force Text
- Runtime 全部代码属于单一 asmdef `Venusy609.Xframework`,外部依赖:UniTask、YooAsset、TextMeshPro、Input System。Cinemachine 支持由独立可选扩展包 `com.venusy609.xframework.cinemachine` 提供(独立 asmdef,依赖 Cinemachine 3.1.7),核心包不引用它
- **不预设 GamePlay 架构:** 框架只提供正交的基础设施与编排原语,不为使用方决定实体模型、生命周期树、时间模型或状态管理方式。任何「使用方必须按某种架构组织游戏逻辑」的设计都不属于本框架

## 总体原则

- **原子化:** 每次只完成一个逻辑明确、可独立验证的任务,代码易于 review 和原子提交
- **避免臆测:** 需求不明确或对 API 行为不确定时,先提问而不是猜测
- **单一职责 / 组合优于继承:** 每个类只负责一个核心功能;优先组件组合,避免深继承
- **性能与 GC:** 框架代码供第三方游戏在运行时使用,必须控制 GC 分配(见「性能与 GC 约定」)
- **测试:** 完成逻辑后编写单元测试并提供验证步骤。Claude 用 `Tools/run-tests.ps1` 自测:改完一个模块以 `-Filter <类名片段>` 定向跑,阶段收尾跑一次全量。**门禁是「全量 0 失败」,不写固定例数**——例数随开发增长,写进规则必然定期过期(它的真相来源是 runner 的输出,抄进来就是第二份真相)。**每个 fixture 必须复位它触碰的静态门面**——PlayMode 下所有用例共享一个 player 实例,不复位即互相污染,全量的门禁价值会立刻失效
- **公开 API 基线:** Runtime 程序集 `Venusy609.Xframework` 的公开面由 `Tests/Runtime/Architecture/PublicApiBaselineTests` 锁定(基线 `PublicApiBaseline.txt`,含非 sealed 类上的 `protected` 扩展点)。公开面变了而基线未变即红——**机器只负责说「变了」,判断仍归人**:确属有意时按「提交拆分与授权」单独请用户确认,在 `CHANGELOG.md` 补行——**破坏性变更(命名与签名)**进迁移表,**源兼容新增**归 `### Added`(该表只收编译器抓得到的命名/签名变更),再用 `Tools/run-tests.ps1 -UpdateApiBaseline` 更新基线(更新模式**只写盘、永不给绿灯**,绿灯由不带开关的重跑给出)。Editor 程序集不在射程内
- **文档门禁:** 阶段收尾除全量测试外,再跑 `Tools/check-docs.ps1 -Enforce`(五项都须满足:包内 XML 文档告警为 0、所有源文件都已参与编译、所有资源都有 `.meta`、决策台账结构合规、模块 README 的文件树与磁盘一致)。**台账那项的规范在 `Documentation/Modules/README.md`(条目格式与状态词表),它只守结构——每模块有台账、状态词合法、文件表与 `Runtime/` 双向相等——守不了条目内容属实**。它是独立的一条通道:csproj 未设 `DocumentationFile`,**默认编译根本不检查文档注释**,不开这一枪则写坏文档不会有任何反馈。**哪些码进这一枪以脚本头部「覆盖范围」为唯一真相**(2026-10-13 起十一个码全部纳门;扩容走「先诊断看曲线、清完存量再纳门」)
- **包 README 清单表门禁:** 包 README 的四张清单表(主要功能 / 模块索引 / 可替换的实现一览 / 可配置项一览)由 `Tests/Runtime/Architecture/ReadmeTableTests` 在测试侧守卫(模块集与 `Runtime/` 直接子目录双向相等、文档链接落盘、命名空间列与程序集双向覆盖、功能表登记表未登记即红)。新模块上线要么同步四表、要么在登记处显式豁免;`check-docs.ps1` 对**包内** `.md` 仍零覆盖(它读 `Documentation/` 下三处只为决策台账结构,不是内容检查),两条通道互补
- **模块 README「已知限制」门禁:** 每个 `Runtime/<模块>/README.md` 都必须有「已知限制」小节,由 `Tests/Runtime/Architecture/ReadmeSectionTests` 在测试侧守卫(只认 `##` 与 `###` 级标题;**Reactive 以两个集合级小节给出,天然满足**,故无例外表)。新模块上线要么写该小节、要么在豁免登记处写明理由;`check-docs.ps1` 对**包内** `.md` 仍零覆盖,同「包 README 清单表门禁」

## 架构分层

框架采用**单一路径 + 通用编排**:基础设施是彼此正交的静态服务,启动顺序由显式的引导登记表决定。规划新模块时必须先确定归属:

- **静态服务(无状态):** 以「静态门面 + 接口 + 内部实现」提供。对外只暴露静态门面类和 `IXxxManager`/`IXxxProvider` 接口;实现类 `XxxManagerImpl`/`XxxProvider` 默认 `internal sealed`(仅当需要跨命名空间注入或模式匹配时才 public,如 SaveManagerImpl、DataManagerImpl)
  - 门面模板:`private static IXxxManager _impl` + `Initialize`(注入实现,便于测试)+ `Shutdown/Destroy` + `EnsureInitialized()`(未初始化抛 `InvalidOperationException`,消息带 `[模块]` 前缀和修复提示)
  - 注入形式按需差异化,不必齐备:`Initialize(实例)`(如 Data)、工厂 delegate(如 SaveManagerFactory)、无参 `Initialize` + `SetInstance`(如 Config/Input)均符合模板;`Initialize` 也可以带参数(如 Localization 的 `Initialize(defaultLanguage, data)`、Save 的全可选参 `Initialize(factory, options)`)
  - 懒加载豁免:FileManager.EnsureInitialized 在未初始化时自动 Initialize(零配置有意设计),不抛异常;其余门面的 EnsureInitialized 仍按模板抛 InvalidOperationException
  - 宽容语义豁免:InputManager 未初始化时全部查询空引用安全返回默认值(有意设计,对 UI 提示友好,测试锁定),不提供 EnsureInitialized 抛异常
  - **永不抛豁免:** `LogManager` 未初始化时自动构建默认实现,**不抛 `InvalidOperationException`**——日志是错误路径的最后一张面孔,让「模块没初始化」把它升级成二次故障,代价比一致性大得多。豁免**仅限日志门面**:诊断能力不能成为新的故障源。它的自动初始化走 `SubsystemRegistration`(全量捕获必须早于其它模块的 AutoInit),已在 `AutoInitTests` 族清单登记
  - 纯静态服务(如 LockManager、MessageManager、UpdateManager)的自初始化:**两个特性都要挂**——`#if UNITY_EDITOR [UnityEditor.InitializeOnLoadMethod] #endif` **加**无条件的 `[RuntimeInitializeOnLoadMethod]`。只挂编辑器分支时,**关闭域重载**(Enter Play Mode Options → 取消 Reload Domain)后进入播放不会重新加载程序集,该回调不再执行(Editor 分支的 `#if/#else` 写法即是此坑,已全面清除)。load type **按需要选,不为统一而统一**:要早于首个场景 Awake 用 `BeforeSceneLoad`(如 File 的域根预热——它的价值就在「早于首次使用」),要注入 PlayerLoop 用 `AfterAssembliesLoaded`,只做订阅/注册的用默认档即可。订阅 `Application.quitting` 必须**幂等**(`-=` 后 `+=`):关闭域重载时该订阅跨播放会话存活,重复 `+=` 会逐次累积。族级守卫见 `Tests/Runtime/Architecture/AutoInitTests`(新成员**必须去那里登记**,未登记即红)
  - **门面保持扁平:** 静态门面一律扁平,成员多也不分组——`MessageManager`、`InputManager`、`UIManager`、`AssetManager` 都靠 `#region` 分区(不在此写各自成员数:那是会随开发增长的量,写进来必然定期过期,与「门禁不写固定例数」同一条纪律)。嵌套静态类分组会把「门面名 == 接口名」这条唯一的人工核对手段换掉(转发时必然改名),换来的只是 IntelliSense 分组,而本仓没有任何其它门面认为那值得付这个价(`UIManager` 曾短暂分组,未发布即撤销)
  - **门面转发不变量:** `IXxxManager` 新增成员时**必须同步在门面加转发**,否则它在第三方眼里根本不存在。(实例:`SetLayerVisibility` 曾长期方法完整、文档也有,却只在内部实现上,门面既无转发也无实例属性,第三方实际完全不可达)。**这条不能只靠评审**,扁平门面下十几行就能锁住:接口每个声明成员在门面上存在**同名**的 public static 成员即可。签名不必测——转发体是经 `IXxxManager` 的调用,签名不符编译不过,编译器已经兜住了。见 `UIFacadeCompletenessTests`
- **可选能力接口与装饰器:** 以「可选能力接口 + 门面 `is` 探测」扩展 Provider 能力(`IAtomicFileProvider`、`IDirectoryProvider`;Settings 侧的 `IAsyncSettingsStore` 同构),不给主接口加成员,第三方实现零影响。两条不变量:(1) 装饰器**必须实现**底层的可选能力接口,否则会把底层的能力遮蔽掉——门面探测到的是装饰器本身;(2) **判定权归门面**:门面探测的必须是 `_baseProvider` 而非生效 Provider,因为装饰器恒实现可选能力接口、对它探测必然「通过」,判定会被整个推给装饰器——于是「是否启用加密」这种无关变量就能改变调用方观察到的行为。实例:`CryptoFileProvider` 曾对「底层不支持原子写」**抛** `NotSupportedException`,而门面未加密时对同一情况是**告警 + 降级为普通写**;`GetDirectoriesAsync` 则曾表现为门面告警降级、装饰器静默降级,连那条告警都被吞掉。装饰器侧的同类检查只作兜底:**不重复告警、不改变语义**
- **管线基础设施(通用编排):** 以「接口 + 静态工厂 + internal 实现」提供,非全局单例:`IPipeline`/`IPipelineStage`/`IPhaseStage`/`PipelineProgress` 公开接口 + `Pipeline.Create()` 工厂 + `internal sealed PipelineImpl`。实例即用即弃;阶段经 `PipelineStageContext` 主动写入(事件驱动聚合,管线不轮询、不持有帧泵);阶段串行逐 await、失败/取消即停、三路互斥终局。相位编排:实现 `IPhaseStage` 声明相位号(同相位并行、相位升序串行,数值含义为模块约定),经 `Pipeline.BuildPhaseGroups` 装配为每相位一个 `ParallelStage`(Weight = Σ 子阶段声明权重)。管线对任何具体模块零依赖,可独立使用
- **启动引导(Bootstrap):** 需要异步初始化的服务实现 `IBootstrapStage`(`IPhaseStage` + 同步 `Shutdown`;Phase = 模块约定值、Name = 类型名、Weight = 1),经 `Bootstrap.Register` **显式登记**(不反射发现),`Bootstrap.RunAsync` 用 `Pipeline.BuildPhaseGroups` 装配运行,`Bootstrap.Shutdown` 按登记顺序**逆序**清理。ExecuteAsync 内经 PipelineStageContext 写描述并 await 模块初始化,**禁止吞 OperationCanceledException**(取消经 OCE 传播,契约兜底/取消语义由 StageExecution 单一承担);失败与取消在 RunAsync 处**抛出**而非只留日志。阶段类归各模块自己的目录(如 `AssetBootstrapStage` 在 `Runtime/Asset/`),框架不在 Bootstrap 目录里装具体服务
- **更新调度约定(Update):** 档位语义是**时长**而非帧数——第 k 档 = 2^k 个节拍格(变步长轴按 60Hz 基准计,固定步轴 = 2^k 个固定步),第 0 档为每帧。增删档位只需改 `UpdateTier` 的枚举成员,桶数组尺寸与钳制上限随 `UpdateTier.Max` 推导。**调度器不得直接读 `UnityEngine.Time`**:时间源由驱动方经 `UpdateClock` 成对传入以保持纯函数——单测精确驱动与确定性回放都依赖这一点(注册/启用的定锚同理,不得去猜时刻)
- **新模块清单:** `Runtime/<模块>/` 目录 + 命名空间 `XFramework.X<模块>` + 中文 README.md(使用方向;维护向记录按需写入 `Documentation/Modules/<模块>.md`,见「文档分层」);示例放 `Samples~/<名字>/`(Unity **完全忽略**该目录,经 Package Manager 的 Samples 页签导入;代价是示例**不参与编译**——示例只许使用公开 API,改公开面时手工核对示例);测试放 `Tests/Editor|Runtime/` 的模块子目录——**测试 asmdef 每个 Tests 根一个,不按模块新建**(`optionalUnityReferences: TestAssemblies`),新模块只需在对应根下建子目录
- **模块边界:** 生产代码不得引用其它模块的 `Internal` 命名空间——Runtime 全树共用一个 asmdef,`internal` 不构成编译边界,这条只能靠约定与自查(见 `Tests/Editor/Architecture/ModuleBoundaryTests`)。跨模块共享的基础设施必须抽为独立模块并给公开面(先例:`XFramework.XEvent` ← Message/Reactive/Input/Settings;`XFramework.XPipeline` ← Bootstrap);测试与 Editor 工具经 `InternalsVisibleTo` 不受此限
- **可选包的 Unity 类型不得出各自的 `Default` 命名空间:** Unity Input System 的类型(`InputActionAsset` 等)只允许出现在 `Runtime/Input/Default/`——门面与 `IInputProvider` 保持插件中立,第三方(如 Rewired)整体替换后端时公开面不受影响;别的模块要提供「Inspector 里配输入」的入口,就用该命名空间下的框架类型(先例:`InputSystemOptions` / `InputSystemOptionsAsset`)。**判据是「可选包」而非「Unity 类型」**:`Transform` / `TextAsset` 这类 `UnityEngine` 核心类型不受此限(每个项目都有)。机械守卫:`ModuleBoundaryTests.UnityInputTypes_StayInsideTheDefaultNamespace`(源码扫描);核心包的 Cinemachine 类型同样零容忍——`ModuleBoundaryTests.CoreRuntime_DoesNotReferenceCinemachine`(源码扫描),Cinemachine 实现放独立可选扩展包 `com.venusy609.xframework.cinemachine`,核心包不引用它

## 编码规范

- **命名:** 接口 `I` 前缀;私有/受保护字段 `_camelCase`;常量 PascalCase(如 `SlotFilePrefix`,不全大写);方法 `TryXxx(out T)`、`GetOrCreateXxx`;bool 属性 `IsXxx`
- **缩写大小写:** **两字母缩写全大写**——`UI`、`IO` 而非 `Ui`、`Io`(`IUIManager` / `UIHudItem` / `AudioChannels.UI` 而非 `IUiManager` / `UiHudItem` / `Ui`);**三字母及以上按单词**(PascalCase)——`Bgm` / `Json` / `Hud`。依据 .NET 框架设计指南。这条此前是空白,导致 `IUiHudProvider` 与 `UiDiagnosticPanel` 长期与周围不一致(2026-10-11 统一);机械守卫见 `Tests/Runtime/Architecture/NamingConventionTests`(2026-10-13 订正:此前写成 `Tests/Editor/Architecture/`,实测该文件在 Runtime 侧——它纯扫源码文本、不需要播放模式,放 Runtime 是为了不被「带 `-Filter` 且未指定平台只跑 PlayMode」的开发回路跳过)
- **风格:** Allman 大括号(换行);`#region` 按功能分区(Public API / Private Fields / Lifecycle / Internal);using 按 System → 第三方(Cysharp、UnityEngine)→ XFramework 排序;同一分区内同步方法与它的异步版本成对相邻、同步在前(既有先例 `Exists`/`ExistsAsync`)
- **行尾与文本资源由仓库根 `.gitattributes` 定:** 文本一律 LF(含 Unity 的 Force Text 资源——`*.unity`/`*.prefab`/`*.asset` 不标文本就拿不到可读 diff)、二进制显式标注、`*.bat`/`*.cmd` 保持 CRLF。它**覆盖** Windows 的 `core.autocrlf`,是新克隆与既有工作区共同的真相。**编辑器侧的同一条线在仓库根 `.editorconfig`**(缩进 / 字符集 / 行尾 / 尾空白;`*.md` 与 Unity YAML 关掉尾空白 trim——前者的行尾两空格是 Markdown 硬换行,后者 Unity 自己会写回)。它只管编辑器、不进门禁,也不随包发布
- **注释:** 全中文 XML doc,公开 API 必须带 `<summary>`(必要时 `<para>`/`<example>`;**这条已部分机械化——CS1591 随文档门禁一起判 0,但机器只查「有没有 XML 注释」**:`<inheritdoc/>`、只有 `<param>` 也算有,「必须有 summary、写得对不对」仍是人工判据);接口实现的成员用 `<inheritdoc/>`;行内注释解释「为什么」而非「是什么」
  - **XML 必须能解析:** 泛型尖括号与 `&` 一律转义(`&lt;T&gt;`、`&amp;`),`<code>` 块内也一样。未转义会触发 CS1570 使**整条注释被编译器丢弃**——该成员的文档条目从生成的 XML 中整条消失,且块内 `cref` 一律不再被检查(死链因此隐形:它表现为「**没有**告警」而不是「有告警」)
  - **cref 的四个坑**(按序排查):① 带类型限定的 cref **只找该类型自己声明的成员,不找继承来的**(`RectTransform.position` 要写 `Transform.position`,`YooAsset.AssetHandle.Release` 要写 `YooAsset.HandleBase.Release`);② 参数列表要写全,**带默认值的参数也算**(`CloseAsync(UIPanelBase, bool)` 匹配不上三参的真实签名);③ cref 要**写全形以消二义**:有重载的成员**必须**写参数列表——无参重载写 `Foo()`、带参写全 `Foo(int, float)`;只在该名字没有重载时才能写裸名。写不全就触发 **CS0419**(编译器替你「假设」一个重载,链接可能指向非本意的那个)——旧说法「cref 里不能带 `()`」是过度概括:`Tick()` 合法、`InitializeAsync()`(该方法带参)非法,差别只在参数列表写没写对——**该码已纳 `check-docs.ps1 -Enforce` 门禁**;④ cref 引用的**参数类型**在本文件不可见(缺 using)时把该类型写成全名,不必为此加 using
  - 文档指向**根本不存在的东西**(已改名/已删的成员或类型)是内容问题,先确认真实 API 再改,不要机械替换。两类告警都用 `Tools/check-docs.ps1` 查(默认诊断,`-Enforce` 才是门禁)
- **可见性:** 默认 `internal`;测试通过 `InternalsVisibleTo("Venusy609.Xframework.Tests")` 访问内部实现

## 性能与 GC 约定

- **对象池:** 频繁创建销毁的对象必须走 PoolManager / CollectionPool(ListPool、HashSetPool、DictionaryPool、StringBuilderPool)
- **每帧路径:** 禁止 LINQ,手写 for 循环;避免闭包分配、装箱拆箱、字符串拼接
- **零 GC 结构:** 可复用的轻量句柄设计为 readonly struct(如 LockHandle),存储原始数据而非委托
- **组件引用缓存:** 避免在 Update 中调用 GetComponent、Camera.main、FindObjectOfType,应在 Awake 缓存
- **反射:** 仅允许在运行时 Type 驱动的 API 边界和配置元数据提取处使用,必须缓存结果,禁止出现在每帧路径(参考 ConfigTypeHelper)

## 异步约定

- 异步统一 UniTask(`Cysharp.Threading.Tasks`),不使用 System.Threading.Tasks.Task,不使用 IEnumerator 协程
- 公开异步 API 必须带 `CancellationToken cancellationToken = default` 参数
- `async void` 仅限 Unity 生命周期入口(如 GameLauncher.Start);其余一律返回 UniTask/UniTask<T>
- 订阅随生命周期自动取消:`MonoBehaviour.destroyCancellationToken`,或实现 `XMessage.IDestroyCancellationToken`——`MessageManager` 的订阅扩展方法识别二者,令牌取消即自动退订(见 `TryBindToDestroy`)
- 模块自持进度上报:进度载荷定义为模块内 `readonly struct`(如 `XAsset.AssetInitReport{Progress, Description}`),参数形态 `IProgress<T> progress = null`,置于 options 等配置参数之后、CancellationToken 之前(ct 仍收尾)

## 日志与异常

- 运行时日志统一走 `XFramework.XLog`:分类从 `LogCategories` 取(框架内置全表)或 `LogCategory.Get("名字")`(第三方自定义);消息里**不写** `[模块]` 前缀——它由分类渲染层补(分类名就是渲染出来的标签,两者逐字对应)
- **写模板 + 参数,不要先插值**:`LogManager.Warning(cat, "x {0}", a)` 在档位未启用时**不格式化**(零分配,有测试锁定),`$"x {a}"` 则无论开不开都已经分配过。参数仍在调用方求值(与插值一致),代价高的先用 `IsEnabled` 探测。含**字面花括号**的消息走「现成字符串」重载,或按 `string.Format` 规则写成 `{{`/`}}`
- 级别映射与迁移前对齐:`Debug.Log`→`Info`、`Debug.LogWarning`→`Warning`、`Debug.LogError`→`Error`;Release 默认档是 `Info`,故可见性不变。六档含义、默认值与 JSONL schema 见模块 README
- `LogManager` 自身**永不抛异常、永不记日志**:sink 抛异常即被静默摘除并计入 `DroppedSinkCount`——「因为日志坏了而记一条日志」会递归,且测试把意外 Error 当失败。文件写入失败(磁盘满等)同样静默停用该输出端,不影响控制台通路
- 未初始化访问抛 InvalidOperationException,消息带 `[模块]` 前缀和修复提示(**日志门面除外**,见「架构分层」的第三处豁免)
- 重复 Initialize 打 Warning("... called more than once. Ignoring duplicate.") 后忽略
- Update 循环异常隔离:OnUpdate 抛异常时记 Error 并自动注销,不得打崩整个调度
- 预期内的失败用 Warning 而非抛异常;参数防御用 ArgumentNullException/ArgumentException

## 依赖管理

- package.json 的 dependencies 刻意留空(UPM 不支持 Git URL 依赖);新依赖:
  - UPM 包 → `Packages/manifest.json`(Git URL 或注册表版本号)
- 新增依赖必须同步更新 Assets/XFramework/README.md,引导第三方手动安装(安装顺序:先依赖,后 XFramework)
- **依赖版本只有一份真相:** 第三方依赖的版本/URL 以 `Assets/XFramework/README.md` 的「第三方依赖」表为准;`Editor/XFrameworkDependencyInstaller.cs` 的清单与 `Packages/manifest.json` 必须与之逐字一致,Git URL 必须钉 `#tag`(`?path=` 写在 `#` 之前)。三者由 `Tests/Editor/Architecture/DependencyDeclarationTests` 机械守卫——不钉修订时消费方会取上游默认分支,Asset 模块直接编译不过(实测 51 处 `CS0246/CS0234`)
- **扩展包的依赖是它自己的真相:** `Assets/XFramework.Cinemachine/package.json` 的 Cinemachine 版本、扩展 README、开发 `Packages/manifest.json` 三处一致,由扩展包的 `CinemachineDependencyTests` 守;核心 README / 安装器**不含** Cinemachine(开发 manifest 的 3.1.7 只服务扩展包编译 / 测试,核心 `DependencyDeclarationTests` 不把它当核心依赖)。扩展包不声明核心依赖(UPM 不支持 Git URL 依赖),由 README 的安装顺序与 asmdef 引用兜底

## 文档分层

文档按**收件人**分层,一处只写一份真相:

| 文档 | 收件人 | 写什么 |
|---|---|---|
| `README.md`(仓库根) | GitHub 访客 / 仓库门面 | 只指路不复述:这是什么、包在哪个子目录、两条读者路径(用包的 / 开发本仓的)。**模块清单、可配置项这类会漂移的内容一律指向包 README**,它自己只留稳定的那几句 |
| `Assets/XFramework/README.md` | 引入包的第三方 | 包级入口:设计哲学、快速开始、依赖安装 |
| `Runtime/<模块>/README.md` | 引入包的第三方 | 模块用法:概述、快速使用、模块结构、API 参考、行为契约、线程与性能代价、`已知限制`、`设计取舍`。**骨架、命名唯一写法与文件树分工见 `Documentation/ModuleReadme.md`** |
| `Documentation/Modules/<模块>.md` | 维护者 + 下一轮审计者 | 维护向记录:文件结构、模块版本记录、**决策台账**(原「已完成功能与未做」「已评估未采纳与未决」两节合流)、沿革与已否决形状、迁移指南、审计记忆。**「文件结构」是权威副本**——模块 README 的 `## 模块结构` 是它的使用向子集(见「README 的判据」) |
| `Documentation/ModuleReadme.md` | 维护者(写 / 重整模块 README 时) | 模块 README 的**结构规范**:一级骨架(18 节,5 项必答)、特性节、旧名→新名对照表、文件树两份分工、H1 与措辞、门禁射程 |
| `Documentation/Ledger.md` | 维护者(选型/排期/审计时) | 决策台账:框架级「已做/未做/待裁/暂不做/不做/已推翻」条目 + 已否决项复核记录 + 同类框架对照(快照,只作立项调研的起点) + 模块台账指针。**跨模块**结论归它,**单模块**的边界仍归模块 README 与 `Modules/<模块>.md`。条目格式与状态词表见 `Documentation/Modules/README.md` |
| `Documentation/AuditRound2.md` | 维护者(执行本轮修复时)+ 下一轮审计者 | 第二轮全面审计的**点快照**:范围与方法、P0/P1/P2 发现(位置、证据类型、验证方式)、跨模块主题、逐模块 18 类判据留痕、实施批次与台账同步索引。**只记录本轮发现**;长期结论仍按收件人分流到模块 README 与决策台账,本文件不复制它们、不随开发增长更新 |
| `Documentation/Luban.md` | 维护者(接第三方配置表工具时) | 接入配方:框架侧契约、工具链步骤、已核对事实与版本漂移点。**随第三方工具版本漂移的内容只在这里,不进包** |
| `Documentation/CI.md` | 维护者(排期与重开 CI 时) | CI 的成本要点(Unity 许可形态 / runner / 与测试壳关系,外部事实标核对状态)与裁定:暂不接入 + 三条重开判据。**随外部平台政策漂移的内容只在这里,不进包** |
| `Documentation/Cinemachine.md` | 维护者(升级 Cinemachine / 审计扩展包时) | 扩展包 `com.venusy609.xframework.cinemachine` 的版本、3.x API 实测事实、升级与漂移点。**随 Cinemachine 版本漂移的内容只在这里,不进核心包** |
| `Assets/XFramework/CHANGELOG.md` | 双方 | 变更流水:改了什么、为什么、根因与实测证据 |

- **README 的判据:** 把这段删掉,使用方会不会写错代码或误判行为?会 → 留在 README。`已知限制` / `接口承诺` / `线程契约` 即使语感像开发记录,**不得**搬走
- **文件结构的唯一例外:** 模块 README 的 `## 模块结构` 与 `Documentation/Modules/<模块>.md` 的 `## 文件结构` 并存——两边**收同一批文件**(模块目录下的全部源文件;`internal` 在 README 侧逐个标注),差异只在措辞(维护向那份是职责详述)。两份并存的理由只有一个:`Documentation/` 不随包发布,第三方读不到权威副本。**两边都由磁盘锚定**——加 / 删 / 改一个文件,两处都要跟;README 侧与磁盘的一致性由 `check-docs.ps1` 的报告项守(漏收与残影都抓),维护向那份仍靠评审
- **技术文档的判据:** 答的是「下一轮我要不要重新论证这件事」,而不是「我该怎么用」。**按需创建**,没有内容就不建文件
- **包内不指向包外:** 包内**全部文本**——README、`.md` 正文、源码注释(XML 注释随包发布,第三方在 IDE 里就直接读到)——都不引用 `Documentation/` 下的维护向文档:第三方装了包却打不开它。要指路的场合写成**自足表述**:用「该模块的维护向记录」「框架级决策台账」这类**描述**代替路径,或把结论内联。**判据只有「有没有那条路径」一条**——不再区分「指路」与「历史陈述」(2026-10-16 用户裁定「包内不要引用包外资源」),确需保留路径的走豁免登记处写明理由。机械守卫:`Tests/Runtime/Architecture/PackageBoundaryTests`(`.cs` 与 `.md` 两路,含豁免登记处)。历史:2026-10-15 两轮清出 22 处(其中 2 处在 Runtime 生产代码的 XML 注释里)后上的源码守卫;2026-10-16 扩到 `.md` 时又清出 48 处(`CHANGELOG.md` 47 处 + 包 README 1 处——都随包发布,此前只扫 `.cs` 所以一直没被看见)

## 沟通与工作流

- **任务规划:** 编码前先以 Plan Mode 与用户确认需求与理解
- **代码审查:** 生成修改后主动请求用户审查
- **解释决策:** 影响性能或设计的复杂选择(如 Singleton、反射)必须在注释或对话中说明理由
- **Git 提交:** 提交信息除类名/成员名等代码关键字外一律使用中文,**不加 `Co-Authored-By` 之类的署名尾注**;**不自动推送**,推不推由用户决定
- **提交拆分与授权:** 计划阶段即确定原子提交边界(计划中列出提交拆分)与每个提交的验证命令,实现按提交逐个完成、验证后再提交,避免「先全量实现、提交前再拆分」——混合文件需反复编辑还原,易出错;拆分依据是逻辑边界与可独立编译,同源小改动(如统一消息文案)可合并为一个提交;实现时以提交为执行单元,不得提前实现后续提交内容。**计划批准即授权本阶段的全部提交**:每个提交跑绿计划里写明的验证命令后**直接提交**,不必逐次请示。**两类例外仍须单独请用户确认**——公开 API 变更、破坏性变更(含仅对仓内的);需要用户裁定的设计取舍(API 形状、「不该做」的结论)同样停下
- **规则存档:** 讨论中若产生可固化为长期约定的规则/决策(如 API 行为、架构约定),主动向用户提示拟写入的文本,经用户确认后方可添加到 CLAUDE.md 对应小节;未确认不擅自修改
- **工作流手册:** 提交节奏、三条门禁的具体命令与验证纪律见 `Documentation/Workflow.md`;本文件只写规则,流程细节不在两处重复
- **模块审计:** 对模块做「找潜在问题 + 第三方对照 + 改进建议」这类审计时,判据清单、探针命令与报告格式见 `Documentation/ModuleAudit.md`;结论按性质分流(落点表见该手册 §五):使用方需要知道的边界(违约) → 模块 README `已知限制`;使用方按直觉写就会错的行为(未承诺) → 模块 README `行为契约`;影响使用方怎么用的「为什么是 A 而不是 B」 → 模块 README `设计取舍`;接口承诺 → 模块 README `接口承诺到哪为止`;已评估未采纳与未决项 → `Documentation/Modules/<模块>.md` 的决策台账
