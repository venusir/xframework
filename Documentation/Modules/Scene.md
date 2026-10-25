# Scene —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Scene/README.md`——已知限制、设计取舍、
> 接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Scene/
├── SceneManager.cs              # 静态门面：加载编排（校验 → 转发 → 激活/回收后处理）+ SetActiveScene / ActiveScene
├── SceneLoadOptions.cs          # 加载选项（readonly struct；全零 = Single / 不激活 / 不回收，有意义）
├── SceneLoadProgress.cs         # 进度载荷（readonly struct）
├── SceneLoadProgressRelay.cs    # internal：Action<float> → IProgress<SceneLoadProgress>（每加载一实例，无闭包无装箱）
└── README.md                    # 使用说明

Tests/Runtime/Scene/             # PlayMode：FakeSceneAssetManager + SceneLoadTests + SceneHandleTests
Tests/Editor/Scene/              # EditMode：SceneLoadOptionsTests（纯结构体值语义）
```

**模块无静态状态**，故三样都不做也不登记：AutoInit、`ResetForNewSession`、Diagnostics 面板。
真正的状态在 `XAsset` 侧——句柄登记表（`Id` → 加载操作）与「能力缺失只告警一次」闩锁都在那里。

**每个源文件都写 `using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;`**，且绝不裸写
`SceneManager`。理由不是风格：本命名空间的自有类型会**静默遮蔽** Unity 那个（C# 判的是类型名），
裸写不报错，错的是运行时行为。

## 沿革与已否决形状

### 立项（2026-10-13）：候选表里最大的一条 → 建成

四条边界由用户逐条裁定，原文随立项轮从候选表搬迁至此（立项记录见 `Ledger.md` 的「场景管理（XScene）建成」条）：

| # | 边界问题 | 结论 |
|---|---|---|
| 1 | 给**场景栈**还是只给**加载原语**？ | **只给加载原语 + 配对句柄**。「谁在栈顶 / 返回上一场景」= 历史 + 顺序 + 归属 = 生命周期树（§四已否决）——与 Update 否决「组」API 是同一条推理链（组 → 组的生命周期 → 组内嵌套 → 组间顺序）。查询只保留 `ActiveScene`，那是 Unity 本就有的事实，不是本模块的账本 |
| 2 | 加载界面归谁？ | **框架不内建 UI**，只给进度契约与调用方自己的 `await` 窗口。「加载期间显示什么」是表现策略；UI 侧 `UISorting.SystemOrder = 32000` 的注释原文就是「新手引导挖洞层、全局加载遮罩」——那一半答案已现成 |
| 3 | 与 UI「切场景框架不做自动检测」如何共存？ | **硬约束，照原样共存**：本模块不碰 UI（不关面板、不扫跨场景引用）。换来的能力是「每场景一根」自动成立（节点随旧场景销毁） |
| 4 | 场景间传参给不给泛型通道？ | **不给**，只文档化配方与时序——参数形状是游戏逻辑，多一个带生命周期的全局通道就多一份「什么时候清、谁清的」纪律，而 `Data` / 常驻服务已经覆盖 |

**「最容易做过头」这个立项风险没有兑现**：四条边界的落点全是「原语 + 文档」，没有一条变成新 API。

### 落点三选一：S1 / S2 / S3（已议，选 **S2**）

场景原语该留在 `IAssetManager` 上、还是移出去？三案在计划阶段逐条对照：

| 案 | 形状 | 判定 |
|---|---|---|
| S1 | Asset 的 `LoadSceneAsync` 改为返回句柄（仍在主接口） | 否——把「产生一个框架句柄」变成每个第三方实现者的**编译级义务** |
| **S2** | 场景原语**移出主接口**，改由能力接口 `IAssetSceneLoader` 承载；门面 `is` 探测转发 | **采纳** |
| S3 | 主接口签名不变，另补 `UnloadSceneAsync(Scene)` | 否——两个入口，且 `Scene` 反查句柄要靠名字猜 |

选 S2 的四条理由：① 场景的存活就是 YooAsset `SceneHandle` 的引用计数，句柄类型只能定义在持有后端的
`XAsset`；② 能力接口不破坏第三方 `IAssetManager` 实现（`IAssetPoolController` 的原话理由原样成立）；
③ 未来的 `suspendLoad` / `physicsMode` 关在一个可选契约里，不必每次再破主接口一次；④ 能力接口可被假件
实现，本模块的用例才驱动得起来。

**一条必须记住的诚实边界**：「`XScene` 是唯一公开场景 API」是**职责与文档层**的结论，**不是编译期封死的**。
`AssetManager` 门面不能暴露 `Instance`（`AssetFacadeCompletenessTests.Facade_DoesNotExposeInstanceProperty`
钉着），而 `XScene` 只能经门面取能力 ⇒ Asset 门面必须保留场景成员，形态与 `ClearPool` 相同
（文档标注「后端能力转发，不面向使用者」）。要真正编译期唯一，只能把那个成员改成明显的后端名
（如 `LoadSceneViaBackendAsync`）——那会再加一次破坏性且读起来更差，**不值得**。

## 迁移指南

破坏性变更的旧 → 新对照在 `Assets/XFramework/CHANGELOG.md` 的「破坏性变更迁移表」里，本文件不复述。
要点：`bool additive` → `LoadSceneMode mode`、返回 `Scene` → `AssetSceneHandle`、
`IAssetManager.LoadSceneAsync` → `IAssetSceneLoader.LoadSceneAsync`。

## 决策台账

- 已做：**加载成功后的回收段不响应调用方取消**（2026-10-17 · `c7edcfa`，SCN-001）——回收调用改用 `CancellationToken.None`：句柄已就绪后再取消不得丢句柄（Additive 场景会留在 Hierarchy 且外部无卸载凭据）；README/XML 已成文。

- 已做：**`sceneUnloaded` 清表在真适配器下生效**（2026-10-19 · R3-SCN-001，回归）——登记项改存**加载时的 `Scene` 快照**，YooAsset 先 `Release` 导致 `SceneObject` 归 default 也不影响匹配；修复落在 Asset 适配器，本模块的「外部卸载感知」契约因此真正成立。
- 已做：**回收段的非取消异常不再吞掉句柄**（2026-10-20 · R3-SCN-002，本轮修复）——`LoadSceneAsync` 只把取消做成「不丢句柄」，回收后端抛非取消异常时 `return handle` 仍不可达（与 SCN-001 同形：场景已进来、外部无卸载凭据）。修法：回收 await 包 try/catch 记 Error，异常不阻止句柄交回；假件补 `UnloadUnusedException` 可编程位；回归 `SceneLoadTests.LoadSceneAsync_UnloadUnusedThrows_StillReturnsHandle`（改前异常外泄）。
- 已做：**日志分类进内置表 `LogCategories.SceneManager`**（2026-10-20 · R3-SCN-003，公开 API 源兼容新增）——此前 `LogCategory.Get("SceneManager")` 现取，不在分类表、也不在第三方的分类发现入口里；模块 README 的 XLog 依赖说明随之从「间接」改为「直接」；基线已同步。

- 已做：**v1 功能面建成**（2026-10-13）——加载（单 / 叠加）、配对卸载（`UnloadAsync` / `Dispose`）、激活与查询；`SceneLoadOptions`（模式 / 叠加后激活 / 切完回收）；`IProgress<SceneLoadProgress>` 进度契约（含地址，失败路径不补发 `1f`）；未初始化 / 空地址的前置校验（带 `[SceneManager]` 前缀与修复提示）。原「已完成（v1）」清单即此四项，逐条证据在 `CHANGELOG`。
- 暂不做：**Diagnostics 面板**（2026-10-13 · v1.1 候选复核）——模块无状态，能列的只有 Unity 本就给的事实（`sceneCount` / 各场景名）。重开判据：将来长出「本模块自己记的账」。
- 暂不做：**加载编排 hook（全局事件）**（2026-10-13 · v1.1 候选复核）——让「不是发起方」的系统（如常驻加载遮罩）也能感知切换；当前判断是「发起方就是最该知道的人」，且事件面会引出「谁在监听、什么时候退订」。重开判据：真实使用方出现并提出该需求。
- 不做：**`IsLoading` 单飞守卫**（2026-10-13 · v1.1 候选复核）——模块不做「同时只允许一次加载」的拦截：并发加载同一场景是 Unity 允许的，拦下来等于替使用方决定策略；真需要时一行信号量就够。无重开判据。
- 暂不做：**`suspendLoad`（YooAsset 的「加载到 90% 挂起」）**（2026-10-13 · 立项轮）——它把「await 返回 = 场景已就绪」改成「需显式 `UnSuspend`」，会在框架里新增一条「谁负责激活」的时序——正是边界 1 要躲的东西。重开判据：出现「必须撤销一次 `Single` 切换」的真实需求。
- 不做：**`priority` / `physicsMode`（YooAsset 的另两个参数）**（2026-10-13 · 立项轮）——无真实使用方；`physicsMode` 面向多物理场景，属专用形状。无重开判据。
- 不做：**场景栈 / 「返回上一场景」**（2026-10-13 · 立项轮）——四条边界中的第 1 条（生命周期树），理由见「沿革」的边界表。无重开判据。
- 不做：**场景间传参通道**（2026-10-13 · 立项轮）——第 4 条边界，理由见「沿革」的边界表。无重开判据。
- 不做：**`ISceneManager` 接口**（2026-10-13 · 立项轮）——本仓「有接口」的分界线是「是否存在可替换的后端」，本模块的后端是 `IAssetSceneLoader`；再套一层只多一份必须长期同步的转发契约 + 第 12 处门面完备性守卫（同 Timer）。无重开判据。
- 不做：**场景生命周期回调（`sceneLoaded` 形状的通知）**（2026-10-13 · 立项轮）——与 `AssetManager.LoadSceneAsync` 的返回值重复；谁发起谁就知道结果。无重开判据。
- 不做：**把 `SceneLoadOptions` 做成 `AssetManager` 侧的类型**（2026-10-13 · 立项轮）——选项是**编排层**的概念（激活与回收都不是资源层的事），放回 Asset 会把它变成「资源加载选项」。无重开判据。

> 本节无 `未做` 条目——v1.1 三个候选经复核都是「条件触发才做」，故状态为 `暂不做`。

- 已做：**激活失败告警守卫无效 Scene**（2026-10-25 · `2852de39`，R4-SCN-001，本轮修复）——自定义 loader 返回「非 default 句柄 + 无效 Scene」时，告警不再读 `Scene.name`，改用请求 location；句柄照常返回。回归 `SceneLoadTests.LoadSceneAsync_ActivateWithInvalidScene_WarnsWithLocationAndReturnsHandle`（改前告警不含 location）。

## 审计轮次

### 实施轮（2026-10-13）

**射程边界（这一轮最该记住的一条）**：本模块自己几乎无逻辑可测——真正的行为在 `XAsset` 的场景路径上，
而 **Runtime 测试程序集不引用 YooAsset，测试环境也没有可运行的资源包**，真路径在用例里跑不起来。
处置是在 `YooAssetManagerImpl` 与 `YooAsset.SceneHandle` 之间抽了一条 internal 缝
（`ISceneLoadSource` / `ISceneLoadOperation`，`Asset` 模块文件）：**缝之上**（轮询、进度、失败释放、
取消善后、取场景对象、句柄登记与卸载）有「改前必红」的用例覆盖；**缝之下**那约 40 行机械转发只有
读源码核对与人工验收。

**实测 vs 推理**（逐条标）：

- **实测**（读 YooAsset 2.3.18 包源码，非真包运行）：`SceneHandle` 有 `SceneObject` / `UnloadAsync` /
  `ActivateScene` / `UnSuspend`；`ResourceManager.cs:55` 订阅 `sceneUnloaded`、`:376-394` 按 `SceneObject`
  匹配后 `Release()`（**成功路径本来就自愈**，失败路径才是真泄漏）；`UnloadSceneOperation` 在
  `_provider.IsDone == false` 时先等加载完成；`TryUnloadBundle` 只在 `AutoUnloadBundleWhenUnused`
  （默认 false）时才动手；`AsyncOperationBase` 实现 `IEnumerator` 而非 `UnityEngine.AsyncOperation`
  （所以 `WithCancellation` 走的是 UniTask 的 `EnumeratorAsyncExtensions`）。
- **推理（未实测，须由人工验收覆盖）**：取消接力在真包里的效果；`Single` 被取消后不释放、只告警的
  残留状态；`SceneHandle.UnloadAsync()` 对「最后一个场景」会 Failed。
- **测试壳里不可达的一处**：「拒绝卸载当前唯一的场景」分支——用例造出来的场景必然是 `sceneCount` 里的
  第二个。该分支只有推理级保证。

**人工验收配方**（脚本表达不了，交付时给使用方）：

1. Offline 模式构建一个含两个场景的内置包；
2. 断言「加载失败时 `YooAssets` 的 provider 列表不增长」（对应失败路径释放）；
3. 断言「取消一次 Additive 加载后，场景不留在 Hierarchy」（对应取消善后）；
4. 断言「`UnloadAsync` 之后 `UnloadUnusedAssetsAsync` 能回收 bundle」——注意
   `AutoUnloadBundleWhenUnused` 默认 **false**，回收口在 `UnloadUnusedAssetsAsync`。

**首轮编译踩到的同族坑**（留给下一轮）：Unity 6 自己也有 `UnityEngine.SceneManagement.SceneHandle`，
与 YooAsset 的同名类型在同一文件里 CS0104，已用 `YooAssetSceneHandle` 别名消解——与
`Message/Event` 的 `DispatchListPool` 是同款处置。
