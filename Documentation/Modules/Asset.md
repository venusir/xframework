# Asset —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Asset/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Asset/
├── IAssetManager.cs               # 资源管理器公共接口
├── AssetManager.cs                # 静态外观（全局入口）
├── AssetManagerImpl.cs            # 默认实现（对象池 + 生命周期管理）
├── YooAssetManagerImpl.cs         # YooAsset 底层适配器（多包管理）
├── IAssetRemoteServices.cs        # 远端资源地址服务接口
├── IAssetPoolController.cs        # 资源实例池的受控清理能力（可选能力接口）
├── AssetInitOptions.cs            # 初始化配置（包名 / 运行模式 / 低内存回收）
├── AssetInitReport.cs             # 初始化进度载荷（readonly struct，中性，不依赖管线）
├── AssetInitProgressRelay.cs      # 进度直写桥：AssetInitReport → PipelineStageContext
├── AssetBootstrapStage.cs         # 引导阶段：资源管理器初始化（Phase 0，最早）
├── AssetHandle.cs                 # 资源句柄（只读结构体，委托 YooAsset.AssetHandle）
├── AssetDownloaderHandle.cs       # 下载器句柄（事件/控制/等待）
├── SubAssetsHandle.cs             # 子资源句柄（图集/多 Sprite）
├── RawFileHandle.cs               # 原始文件句柄（txt/json/二进制）
└── InstanceTracker.cs             # 实例引用追踪组件（内部）
```

## 模块版本记录

模块内部的时间线（按年月记，非版本号）。部分条目在包 `CHANGELOG.md` 里有对应记录、部分没有，保留本表是为了不丢失引入时间线；包级变更见 CHANGELOG。

| 版本 | 说明 |
| --- | --- |
| 2026-08 | 并发初始化修复（共享任务 + 代际号）、低内存自动回收（AutoReclaimOnLowMemory）、LoadAllAsync 批量持有句柄、未初始化消息统一为 [模块] 前缀中文文案 |
| 2026-08 | 接口扩展：多包架构与初始化配置、卸载控制与存在性查询、热更链路（下载器句柄 + 一键下载）、预加载进度回调、同步加载 / 子资源 / RawFile |
| 2026-08 | 池语义修正（方案 A）：回池保活——回池保留句柄、真正销毁才释放；补齐取消支持与池状态统计 |
| 2026-10 | 审计轮（见下两节）：等待者按代际登记（作废路径不再静默）、非成功路径释放句柄、回池去重、门面完备性守卫（第九个采用者）、池 PlayMode 用例、下载器取消路径退订与未 Begin 拒绝、主线程契约（断言 + README 两节） |

详细变更见 git log。

## 已修（2026-10-01）

- **`AssetInitOptions.PackageName` 此前是静默陷阱**：它只决定「初始化哪个包」（`InitializePackageAsync`），
  而加载族（`LoadAsync` / `InstantiateAsync` / 预载 / 场景）统一解析到硬编码的 `DefaultPackage`。
  于是使用方 `InitializeAsync(new AssetInitOptions { PackageName = "MyPack" })` 的真实结果是
  **初始化了 MyPack、却从一个刚被新建的空 DefaultPackage 上加载**——初始化看着成功、之后每次加载失败
  （DEBUG 下 YooAsset 直接抛）。修法：`InitializeAsync` 用 `ResolveDefaultPackageName` 解析主包名
  （非空即采用），并把解析提成可单测的纯函数。**这是行为修复，非 API 变更**：那条路本来就走不通。

## 已评估未采纳与未决

**覆盖缺口（2026-10-02，下一轮先看这条）**：**真实 YooAsset 初始化路径长期零覆盖**。既有测试全走假实现
（`FakeAssetManager` / `ImplFactory`），于是「离线模式的 `BuildinFileSystemParameters` 从未接线」这个
**默认 PlayMode 直接启动失败**的缺陷活到了 2026-10-02 才被清点发现（修法与证据见 CHANGELOG）。
本轮补上的一半是**参数映射单测**（`AssetDecryptionServicesTests`：`CreatePlayModeParameters` 提成
`internal` 后直测两种模式的产物 + 解密服务是否真的接进 YooAsset 参数，经反射读 `CreateParameters`）；
**仍未覆盖的一半**是端到端初始化（需要构建产物，测试工程没有）——引入 Asset 侧集成测试（哪怕是
最小空包）之前，凡是改动 `YooAssetManagerImpl` 与 YooAsset 的接线，都要**回到 YooAsset 源码逐行核对
它的消费路径**，不要只靠编译通过。

**已评估未采纳**（逐条理由已在使用方 README 的 `## 已知限制` / `## 接口承诺到哪为止`，此处只留「本轮评估过并否决」这一层；**下一轮从这里读起**）：

2026-10-01 一轮（模块审计，判据 A–F 全类扫过）：
- 整体改用 `AsyncLazy` / `UniTask.Preserve()` 重写门面的初始化协调协议（现有语义——同一异常实例、代际作废——
  有用例钉住，重写改动面大于收益；只修正了「promise 只支持单个 continuation」那句理由句的表述范围）
- 合并门面与 `AssetManagerImpl` 的两层同构协议（实现层失败时刻意不销毁 `_managerImpl`，包复用语义保证重试安全）
- 给 `AssetManagerImpl` 的同构协议补代际防护（internal，且经门面使用时已被代际保护，不可达）
- 统一 `[YooAssetManager]` 日志前缀为 `[AssetManager]`（内部适配层的有意区分，信息量更大）
- 给门面的 `_poolCapabilityWarned` 加复位入口（进程级闩锁正是「只告警一次」承诺的实现；测试改用反射复位）
- 优化 `PreloadAllAsync` 的每迭代闭包（低频批量路径，「频度是判据的前提」）
- 把三个句柄从 readonly struct 改成类，或给它们加双 Dispose 守卫（零分配是刻意设计；YooAsset 已兜底）
- 删掉 `GetOrCreatePackage` 调用点的 `package == null` 防御分支（实测不可达，但保留零成本）
- **`UnloadUnusedAssetsAsync` 补状态检查**：审计取证曾报「不检查 `operation.Status`，失败静默」，修复写完被
  **编译器当场拦下**（该操作没有 `LastError`）。读 YooAsset 源码确认：`UnloadUnusedAssetsOperation`
  **没有失败路径**（只置 `Succeed`，从不设 `Error`）。**按纪律撤回修复**，改为就地注释记下已验证的事实
  ——加一个恒真的检查是死代码，还会让人以为「失败会被报告」。

**未决**：
- **`Application.lowMemory` 的回调线程**：Unity 官方文档未声明该回调在哪个线程触发，而 `OnLowMemory` 直接触碰
  GameObject（清池）与 `Resources.UnloadUnusedAssets`——若该回调可能在非主线程触发即为缺陷。**需真机实测**；
  未证实前不写进使用方文档。
- **Asset 加载链路缺 PlayMode 集成用例**：`AssetHandle<T>` 只有一个 internal 构造、必须包真的 YooAsset 句柄，
  替身造不出来。于是几条**行为了修复却无法自动化验证**（类型不匹配释放句柄、失败路径释放句柄、回池保活与
  引用计数端到端）。建一套最小 YooAsset Offline 测试环境是后续选项。**（2026-10-01 部分收口）**
  主包名解析已提出纯函数 `AssetManagerImpl.ResolveDefaultPackageName` 并由 `AssetPackageNameTests` 钉住三态
  ——它此前零覆盖，而它决定的正是「初始化与加载是否同一个包」（见下方「已修」）。
- **主线程断言已出现第三份内联副本**（Message 的 `MainThreadGuard`、Pipeline 的阶段写入断言、Asset 门面）。
  三份同形 → 值得评估抽公共防线（跨模块 API，需单独计划）。
- **会话级复位**：域重载关闭时 `AssetManager._instanceInitialized` 跨播放会话存活，`AssetBootstrapStage`
  见到 `IsInitialized` 为真即跳过初始化，第二个播放会话的 `_instance` 指向上一会话的实例（其 `_managerImpl`
  持有已销毁的 YooAsset 包）。与既有 Input/UI 立项同族，**归入该立项**，本轮未动。

## 审计轮次

2026-10-01 按 `../ModuleAudit.md` 审计一轮（判据 A–F 全类扫过，逐类「扫过、无问题」的留痕在当轮提交信息里；
修了什么见 `CHANGELOG` 的 `[Unreleased]`）。本节只留**下一轮需要知道的**。

**范围**：由「审计 `AssetManager.cs`」起，经裁定扩至整个模块（门面 + impl + YooAsset 适配层 + 三个句柄 +
下载器 + 池 + 引导阶段）。

**本轮最重要的两条方法论回报**：

1. **跨代际广播（高危）改前必红**：`_initWaiters` 是全局列表而广播不区分代际，`Destroy()` 后旧代际收尾会
   放行新代际的加入者（假成功），或把旧代际的异常广播给它。写用例时**实测复现**：5 条红，其中一条**无关**
   用例因旧实现泄漏的异常被 UniTaskScheduler 播出而连带判红——「并发缺陷的冒烟用例绿色不构成证据」的又一例。
2. **一条发现被编译器当场证伪**（`UnloadUnusedAssetsAsync` 的状态检查，见上节）：取证读的是「其余 14 处都有
   检查」这个**模式**，而不是「YooAsset 那个操作能不能失败」这个**事实**。下一轮取证时先问后者。

**新增判据**：B6 共享协调协议代际覆盖面 —— 已写入 `ModuleAudit.md` 第三节。

**本轮未审到的**：YooAsset 适配层的 `CreatePlayModeParameters` 参数组合（Host/Offline 双文件系统的取舍）
只做了「与 README 承诺一致」的核对，未评估参数本身的合理性（如 `AutoUnloadBundleWhenUnused` 默认关闭是否
是最优——它决定了 `PreloadAsync` 的「秒回」承诺成立与否，实测成立）。
