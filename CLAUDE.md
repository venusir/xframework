# XFramework 项目规则

## 项目定位

- 本项目是 Unity 游戏框架(内嵌 UPM 包 `com.venusy609.xframework`),以插件形式供其他游戏项目引入,不包含具体游戏内容
- Unity 版本:6000.4.5f1(开发环境);package.json 最低要求 `6000.3`
- API 兼容级别:.NET Standard 2.1;序列化模式:Force Text
- Runtime 全部代码属于单一 asmdef `Venusy609.Xframework`,外部依赖:UniTask、YooAsset、TextMeshPro、Input System

## 总体原则

- **原子化:** 每次只完成一个逻辑明确、可独立验证的任务,代码易于 review 和原子提交
- **避免臆测:** 需求不明确或对 API 行为不确定时,先提问而不是猜测
- **单一职责 / 组合优于继承:** 每个类只负责一个核心功能;优先组件组合(如 EntityNode 的 GetComponent 模式),避免深继承
- **性能与 GC:** 框架代码供第三方游戏在运行时使用,必须控制 GC 分配(见「性能与 GC 约定」)
- **测试:** 完成逻辑后编写单元测试并提供验证步骤;测试不自动运行(不用命令行执行),由用户在 Unity 编辑器 Test Runner 中手动启动

## 架构分层

框架采用双路径架构,规划新模块时必须先确定归属:

- **静态服务(无状态):** 以「静态门面 + 接口 + 内部实现」提供。对外只暴露静态门面类和 `IXxxManager`/`IXxxProvider` 接口;实现类 `XxxManagerImpl`/`XxxProvider` 默认 `internal sealed`(仅当需要跨命名空间注入或模式匹配时才 public,如 SaveManagerImpl、DataManagerImpl)
  - 门面模板:`private static IXxxManager _impl` + `Initialize`(注入实现,便于测试)+ `Shutdown/Destroy` + `EnsureInitialized()`(未初始化抛 `InvalidOperationException`,消息带 `[模块]` 前缀和修复提示)
  - 注入形式按需差异化,不必齐备:`Initialize(实例)`(如 Data)、工厂 delegate(如 SaveManagerFactory)、无参 `Initialize` + `SetInstance`(如 Config/Asset/Localization)均符合模板
  - 懒加载豁免:FileManager.EnsureInitialized 在未初始化时自动 Initialize(零配置有意设计),不抛异常;其余门面的 EnsureInitialized 仍按模板抛 InvalidOperationException
  - 宽容语义豁免:InputManager 未初始化时全部查询空引用安全返回默认值(有意设计,对 UI 提示友好,测试锁定),不提供 EnsureInitialized 抛异常
  - 纯静态服务(如 LockManager、MessageManager、UpdateManager)用 `[RuntimeInitializeOnLoadMethod]` 自动初始化,遵循 `#if UNITY_EDITOR` 分支写 `[InitializeOnLoadMethod]` 的现有惯例
- **节点树(有状态 GamePlay):** `XFramework.XNode` 命名空间。BaseNode → ParentNode → ContainerNode/EntityNode → RootNode,另有 LeafNode、DictionaryNode;承载需要生命周期或加载管线的服务
- **依赖方向单向:** 节点树可以依赖并启动静态服务;静态服务绝不能引用节点树对象
- **管线基础设施(通用编排):** 以「接口 + 静态工厂 + internal 实现」提供,非全局单例:`IPipeline`/`IPipelineStage`/`IPhaseStage`/`PipelineProgress` 公开接口 + `Pipeline.Create()` 工厂 + `internal sealed PipelineImpl`。实例即用即弃;阶段经 `PipelineStageContext` 主动写入(事件驱动聚合,管线不轮询、不持有帧泵);阶段串行逐 await、失败/取消即停、三路互斥终局。相位编排:实现 `IPhaseStage` 声明相位号(同相位并行、相位升序串行,数值含义为模块约定),经 `Pipeline.BuildPhaseGroups` 装配为每相位一个 `ParallelStage`(Weight = Σ 子阶段声明权重)。节点树可依赖并启动管线(依赖方向 Node → Pipeline);StartupAsync 装配预置阶段:收集(Weight 0)+ 每相位一个 ParallelStage + 启动(Weight 0),全局进度恒等于相位阶段进度
- **引导阶段服务:** 需要异步初始化的服务(如 Asset、Data、Localization)包装为 `internal sealed XxxBootstrapNode : LeafNode, IPhaseStage`(Phase = 模块约定值、Name = 类型名、Weight = 1),由 ServiceInitializerNode 挂载;ExecuteAsync 内经 PipelineStageContext 写描述并 await 模块初始化,**禁止吞 OperationCanceledException**(取消经 OCE 传播,契约兜底/取消语义由 StageExecution 单一承担);OnDestroy 反向 Shutdown
- **节点类模板:** override `OnAwake/OnStart/OnDestroy` 且必须调 base;不用构造函数初始化,参数走 `OnInit(object)`;需要帧更新的节点实现 `IUpdateable` 并返回 `UpdateLOD`(UpdateNode 自动注册进 UpdateManager),不写 MonoBehaviour.Update;Disposable 订阅用 `AddToNode(this)` 绑定生命周期;节点一律经 `NodeFactory`/`AddNode<T>` 创建(自动回池)
- **新模块清单:** `Runtime/<模块>/` 目录 + 命名空间 `XFramework.X<模块>` + 中文 README.md;示例放 `Samples/`;测试放 `Tests/Editor|Runtime/` 并新建对应 asmdef(`optionalUnityReferences: TestAssemblies`)

## 编码规范

- **命名:** 接口 `I` 前缀;私有/受保护字段 `_camelCase`;常量 PascalCase(如 `SlotFilePrefix`,不全大写);方法 `TryXxx(out T)`、`GetOrCreateXxx`;bool 属性 `IsXxx`
- **风格:** Allman 大括号(换行);`#region` 按功能分区(Public API / Private Fields / Lifecycle / Internal);using 按 System → 第三方(Cysharp、UnityEngine)→ XFramework 排序
- **注释:** 全中文 XML doc,公开 API 必须带 `<summary>`(必要时 `<para>`/`<example>`);接口实现的成员用 `<inheritdoc/>`;行内注释解释「为什么」而非「是什么」
- **可见性:** 默认 `internal`;测试通过 `InternalsVisibleTo("Venusy609.Xframework.Tests")` 访问内部实现

## 性能与 GC 约定

- **对象池:** 频繁创建销毁的对象必须走 PoolManager / CollectionPool(ListPool、HashSetPool、DictionaryPool、StringBuilderPool);节点销毁经 NodePool 自动回池
- **每帧路径:** 禁止 LINQ,手写 for 循环;避免闭包分配、装箱拆箱、字符串拼接
- **零 GC 结构:** 可复用的轻量句柄设计为 readonly struct(如 LockHandle),存储原始数据而非委托
- **组件引用缓存:** 避免在 Update 中调用 GetComponent、Camera.main、FindObjectOfType,应在 Awake 缓存
- **反射:** 仅允许在运行时 Type 驱动的 API 边界和配置元数据提取处使用,必须缓存结果,禁止出现在每帧路径(参考 ConfigTypeHelper、NodeFactory)

## 异步约定

- 异步统一 UniTask(`Cysharp.Threading.Tasks`),不使用 System.Threading.Tasks.Task,不使用 IEnumerator 协程
- 公开异步 API 必须带 `CancellationToken cancellationToken = default` 参数
- `async void` 仅限 Unity 生命周期入口(如 GameLauncher.Start);其余一律返回 UniTask/UniTask<T>
- 订阅随生命周期自动取消:`IDestroyCancellationToken` + `AddTo`/`AddToNode`
- 模块自持进度上报:进度载荷定义为模块内 `readonly struct`(如 `XAsset.AssetInitReport{Progress, Description}`),参数形态 `IProgress<T> progress = null`,置于 options 等配置参数之后、CancellationToken 之前(ct 仍收尾)

## 日志与异常

- 日志统一 UnityEngine.Debug.Log/Warning/Error,消息带 `[模块]` 前缀(如 `[Save]`、`[ConfigManager]`)
- 未初始化访问抛 InvalidOperationException,消息带 `[模块]` 前缀和修复提示
- 重复 Initialize 打 LogWarning("... called more than once. Ignoring duplicate.") 后忽略
- Update 循环异常隔离:节点 OnUpdate 抛异常时 LogError 并自动注销,不得打崩整个调度
- 预期内的失败用 LogWarning 而非抛异常;参数防御用 ArgumentNullException/ArgumentException

## 依赖管理

- package.json 的 dependencies 刻意留空(UPM 不支持 Git URL 依赖);新依赖:
  - UPM 包 → `Packages/manifest.json`(Git URL)
  - NuGet 包 → `Assets/packages.config` + NuGetForUnity(当前:无)
- 新增依赖必须同步更新 Assets/XFramework/README.md,引导第三方手动安装(安装顺序:先依赖,后 XFramework)
- 各模块 README 是文档的一部分,新增模块/功能需同步维护

## 沟通与工作流

- **任务规划:** 编码前先以 Plan Mode 与用户确认需求与理解
- **代码审查:** 生成修改后主动请求用户审查
- **解释决策:** 影响性能或设计的复杂选择(如 Singleton、反射)必须在注释或对话中说明理由
- **Git 提交:** 提交信息除类名/成员名等代码关键字外一律使用中文;完成任务后不自动提交、推送,由用户决定
- **提交拆分:** 计划阶段即确定原子提交边界(计划中列出提交拆分),实现按提交逐个完成、验证后再提交,避免「先全量实现、提交前再拆分」——混合文件需反复编辑还原,易出错;拆分依据是逻辑边界与可独立编译,同源小改动(如统一消息文案)可合并为一个提交;实现时以提交为执行单元:完成当前提交的全部改动并验证后即停,经提交后再实现下一提交,不得提前实现后续提交内容
- **规则存档:** 讨论中若产生可固化为长期约定的规则/决策(如 API 行为、架构约定),主动向用户提示拟写入的文本,经用户确认后方可添加到 CLAUDE.md 对应小节;未确认不擅自修改
