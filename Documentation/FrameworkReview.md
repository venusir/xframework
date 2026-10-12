# XFramework 框架级评估

> **收件人**：框架作者（选型与排期时）+ 下一轮实施者。**不是**使用文档，**不是**审计判据。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 快照日期：**2026-10-11**。规则在 `CLAUDE.md`，流程与门禁在 `Workflow.md`，审计判据在 `ModuleAudit.md`，候选模块在 `Roadmap.md`——本文不复述它们。

## 一、这份文档怎么用

**它补的是中间那一层。** 此前两种系统性审查都在别的粒度上：

| 文档 | 粒度 | 回答 | 问不出的 |
|---|---|---|---|
| `ModuleAudit.md` | 一次一个模块 | 这个模块内部有没有缺陷 | 「场景管理该不该有」 |
| `Roadmap.md` | 一个候选模块 | 该不该建、边界画在哪 | 「测试覆盖是否失衡」「这个包第三方装不装得上」 |
| **本文** | **整个框架** | **作为一件对外发布的产品，它整体缺什么、该先做什么** | 单个模块的实现细节 |

**本文只做两件事**：① 把框架当成产品体检，列出四个面的缺口与优化项；② 给出**优先级分期**。具体「怎么改」留给每一轮的 Plan Mode，不在本文写施工单（写了必过期）。

**三条纪律**：

- **「不该做」也写成结论**（`ModuleAudit.md` §四.2）。只写「缺什么」的清单会诱导下一轮把已否决的东西重新做一遍。
- **实测与推理分开标**。本文所有**行为性**结论都标了是实测还是读码推理；第三方对照表的来源标了「未一手核对」。
- **时点性 vs 结构性**。上游仓库 HEAD、包版本这类会漂移的结论带日期；结构性结论（某组件缺失、某尺子恒零）可以长期引用。

**读完从哪开始**：要看「下一步做什么」直接跳 §五；要理解某条为什么在清单里，看 §四对应小节。

## 二、现状盘点

**规模**：22 个 Runtime 模块、单一 asmdef `Venusy609.Xframework`。包内约 94,000 行——Runtime 45,595 / Tests 45,591 / Editor 2,185 / Samples 731。9 个月 902 次提交（2026-01-13 → 2026-10-11）。

**测试规模异常地大**：204 个测试文件、152 个 fixture、1963 个 `[Test]`，与 Runtime 行数几乎 1:1。

### 已经领先的地方（对照表的优势列）

不是客套——这些是同类框架普遍没有或做得更浅的：

| 项 | 为什么算领先 |
|---|---|
| **日志系统** | 六档分级 + 分类过滤 + 模板化零分配调用 + 每条一行 JSONL 落盘 + 引擎/第三方/未捕获异常收进同一条时间线。多数框架只包一层 `Debug.Log` |
| **诊断窗口** | 18 个模块面板 + 页签注册契约（`IDiagnosticPanel`）+ 可测的渲染核心。GameFramework 的 Debugger 是同类物，本仓的模块覆盖面更广 |
| **Update 档位时间切片** | `IUpdateable.OnUpdate` 返回 `UpdateTier`，调度器按时长自动升降档。这是 LOD 式更新调度，同行普遍没有 |
| **编排原语** | `Pipeline`（阶段串行/并行/加权进度/失败即停）+ `Bootstrap`（显式登记 + 相位装配 + 逆序清理）。多数框架把启动顺序写死在某个 Manager 里 |
| **模块级文档密度** | 22 个使用方 README + 23 个维护向模块文档 + 18 类审计判据 + 审计技能。这个密度在开源 Unity 框架里罕见 |
| **待办纪律** | 源码里 `TODO/FIXME/HACK` **真标记 0 处**——所有待办与已否决都收敛进文档层，`Roadmap.md` §四 + 各模块「已评估未采纳与未决」 |
| **测试规模** | 45k 行 ≈ Runtime 1:1，含架构守卫（模块边界 / 命名约定 / 自动初始化族清单） |

**这些必须在对照表里显式写出来**——否则本文读起来像一份「到处是坑」的清单，而事实不是。

### 能力矩阵（本仓）

| 面 | 有 |
|---|---|
| 基础设施 | Update / Event / Message / Reactive / Pool / Timer / Lock / File / Serialize / Log / Diagnostics / Pipeline / Bootstrap |
| 资源与内容 | Asset（YooAsset）/ Config（Json·CSV·SO）/ Localization / Audio / UI（面板·HUD·Tip·对话框·弹窗定位）/ Input |
| 存档与设置 | Save / Data / Settings |

## 三、与同类框架的能力对照

> **来源与可信度**：本节对照的框架（GameFramework / QFramework / ET / TEngine / YukiFrameWork / EasyFramework / Scylla / JKFrame / CocKleBurs）信息来自 **2025-10 前后的公开资料汇总**，
> **未逐一到各自仓库一手核对**（标 `[未一手核对]`）。**本仓那一列是一手事实**（零 grep 命中 + 无目录，见 §四.1 的探针）。
> 结论的用途是「支撑该不该建的论证」，不是「谁更强的排名」。

| 能力 | 同类普遍性 `[未一手核对]` | 本仓 | 判定 |
|---|---|---|---|
| 资源管理 | 人人都有 | ✅ Asset（YooAsset） | — |
| UI 管理 | 人人都有 | ✅ UI（含导航栈 / HUD / Tip / 对话框） | — |
| 事件 / 消息 | 人人都有 | ✅ Message + Event + Reactive | — |
| 对象池 | 人人都有 | ✅ Pool（对象 + 集合四件套） | — |
| 配置表 | 人人都有 | ✅ Config（+ Luban 接入配方） | — |
| 本地化 | 多数有 | ✅ Localization | — |
| 音频 | 多数有 | ✅ Audio | — |
| 存档 | 多数有 | ✅ Save + Data + Settings | — |
| 日志 / 调试台 | 多数有 | ✅ Log + Diagnostics（**覆盖面领先**） | — |
| **场景管理** | **几乎人人都有** | ❌ 只有 `AssetManager.LoadSceneAsync` 单次加载 | **缺口，见 §四.1** |
| **热更编排** | 中大型框架都有 | ❌ 只有 Asset 对 YooAsset 的薄封装 | **缺口，见 §四.1** |
| 网络基础层 | 多数有（UGF 有 Network + WebRequest） | ❌ 零命中 | §三 待裁，见 §四.1 |
| 代码热更 | TEngine 等以它为主打 | ❌ 零命中 | §三 待裁 |
| 状态机 / 流程 | UGF 有 FSM + Procedure；QFramework 有 | ❌ | **刻意不做**（§六） |
| 实体 / GameObject 管理 | UGF 有 Entity | ❌ | **刻意不做**（§六） |
| 红点 | 少数（YukiFrameWork 等） | ❌ | §三 待裁 |
| 新手引导 | 少数且多标注「不完善」 | ❌ | **刻意不做**（§六） |
| 画质 / 性能档位 | 少数 | ❌ | §三 待裁 |
| GM 命令台 | 少数 | ❌（诊断窗口给了一半答案） | §三 待裁 |
| 相机管理 | 少（多交给 Cinemachine） | ❌ | 建议不做，见 §四.1 |
| 埋点 / 统计上报 | 少（多属项目侧） | ❌（`ILogSink` 已给框架侧那一半） | §三 待裁 |
| 远程配置下发 | 少（属后端） | ❌ | 建议不做，见 §四.1 |
| 崩溃上报 | 少（属渠道） | ❌（Log 的 JSONL 是框架侧那一半） | 建议不做，见 §四.1 |

**一句话**：**基础设施面本仓不输**（日志与诊断还领先），**缺的是「一局游戏怎么跑起来」那一层的编排**——场景、热更、网络。而后者恰恰是同类框架「开箱即用」口碑的主要来源。

## 四、缺口与优化项

### 4.1 包内能力缺口

> 探针：`grep -rn "<关键字>" Assets/XFramework/Runtime/` 零命中 + 无目录。下表「零命中」均为此探针结果，属**一手事实**。

#### ★ 场景管理（最大缺口）

**现状**：只有 `AssetManager.LoadSceneAsync(string, bool additive, Action<float>, CancellationToken)`（`Runtime/Asset/IAssetManager.cs:153`，实现 `Runtime/Asset/YooAssetManagerImpl.cs:500`）。**没有**场景栈、场景生命周期、加载界面编排、场景间传参、叠加场景的加载/卸载配对管理。`Runtime/` 下无 `Scene/` 目录。

**为什么它不该被「不预设 GamePlay」挡住**：这条否决的是**实体模型 / 生命周期树 / 时间模型**——使用方**怎么组织游戏逻辑**。而「一个场景怎么加载、怎么卸载、加载期间显示什么」是**引擎级基础设施**：Unity 自己的 `SceneManager` 就是引擎 API，`AssetManager.LoadSceneAsync` 也早已存在。补的是**这一层之上的编排**，不是游戏逻辑。

**边界该画在哪**（立项时要逐条回答的）：

- 给**场景栈**还是只给**加载原语**？（栈会引出「谁在栈顶」「返回上一场景」——逼近生命周期树，§六 已否决的形状）
- 加载界面归谁？（框架给回调/hook，UI 内容由项目决定）
- 与已有决定的关系：UI 已定「**切场景框架不做自动检测**」（`Runtime/UI/README.md`），场景模块必须与这条共存而不是推翻它
- 场景间传参：给一个泛型通道，还是让项目自己用 `Data`/`Settings` 传？

**风险**：这是本清单里**最容易做过头**的一项——一旦开始管「场景之间的顺序与归属」，就滑向 §六 已否决的生命周期树。立项时先把上面四条边界写死。

#### ★ 热更编排

**现状**：`HotUpdate` / `Patch` 零命中。只有 Asset 对 YooAsset 的薄封装 5 个方法（`RequestPackageVersionAsync` / `PreDownloadContentAsync` / `DownloadAssetsAsync` / `CreateDownloader` / `UpdatePackageManifestAsync`）。

**缺的是决策层**：版本对比 → 要不要更新 / 强制还是可选 → 下载队列与并发 → 断点续传与失败重试 → CDN 切换 → 更新公告与进度 UI 的接线点 → 与 `Bootstrap` 相位的配合。

**边界问题**：「更新策略」高度依赖运营与渠道（强制更新的门槛、公告文案、灰度），**多半与代码热更（HybridCLR）一样该是「Editor 工具 + 文档」而不是 Runtime 模块**。但「下载编排」这一半（队列/重试/续传/进度）是纯基础设施，可以进框架。

> 本条**原先不在 `Roadmap.md` 里**——本轮新识别。

#### 其余能力缺口（结论与出处）

| 项 | 本仓现状 | 建议 |
|---|---|---|
| 网络基础层 | `HttpClient`/`Socket` 零命中；唯一网络代码是 `File/MobileFileProvider.cs` 的 `UnityWebRequest` | **§三 待裁**，边界问题见 `Roadmap.md` §三（原样保留） |
| 红点 | 零命中 | **§三 待裁**（原样保留） |
| 画质 / 性能档位 | 无 | **§三 待裁**（原样保留） |
| GM / 调试命令台 | 无 | **§三 待裁**（诊断窗口给了「Editor 工具 + Runtime 数据契约」这一半答案） |
| 埋点 / 统计上报 | 无 | **已半解决**：`XLog.ILogSink` 就是框架侧那一半，追加输出端即可，**不必单独立项**（`Roadmap.md` §三 原判，维持） |
| **相机管理** | 无 `ICameraManager`；仅 UI 的 `UITipItem.cs:76` 与 `UIHudItem.cs:80` 各自缓存 `Camera.main` | **建议不做**：Cinemachine 已覆盖相机编排，而框架内建只会与之竞争（与 §六「Tween」同一理由）。UI 那两处 `Camera.main` 是**缓存后的**，不构成每帧开销，不值得为它开模块 |
| **远程配置下发** | `RemoteConfig` 零命中 | **建议不做**：与「平台 SDK 聚合」同性质——后端与渠道相关，适合项目侧包裹。框架最多给一个取配置的接口，而 `Config` 的 `IConfigLoader` 已经是那个接口 |
| **崩溃上报** | `Crash` 零命中 | **建议不做**：渠道相关（Bugly / Crashlytics / 自建）。**框架侧那一半已经做了**——Log 把引擎 / 第三方 / 未捕获异常收进同一条 JSONL 时间线，正是上报系统要吃的输入 |

### 4.2 已有模块的优化项

这一层的**绝大多数已经在册**，本节不复制它们，只做三件事：给指针、标优先级、补本轮新识别的。

**已在册、不重复论证**（真相在各处原文）：

- `Roadmap.md` §五 的 **5 条跨模块未决**：会话级复位 / File 异步删除原语 / `Data.CreateSnapshot(bool clearDirty)` / 主线程断言抽公共防线 / Update 节点注销不可观测
- 各 `Documentation/Modules/<模块>.md` 的「**已评估未采纳与未决**」——22 个模块各自的清单

**本轮新识别或新升级的**：

| 项 | 事实 | 为什么重要 |
|---|---|---|
| ~~**零分配断言恒真**~~ ✅ **2026-10-12 已处置** | 原事实：两把分配尺子恒返回 0，5 个文件 / 11 处断言恒真（`LogAllocationTests.cs:12` 自述照抄 `TimerAllocationTests`，坏形态靠模仿扩散）。处置：8 处改成**结构断言**（句柄/快照/统计行是值类型、状态槽位按类型参数泛化、表声明为具体字典…）、3 处（Log 的关档不格式化）改成**直接观测**（探针 `ToString()` 计数 + 计数 sink + 正对照）、全部保留放大粗尺子兜底。实测：**11 处站点的堆增量全为 0 B**——零 GC 卖点第一次被真实测量 | 新增共享尺子 `AllocationRuler`（唯一实现）+ 三条**尺子自检** + 两条**源码守卫**（两把恒零尺子全包零命中、可用尺子只许一个读者）。改造中的一条发现：粗尺子对**瞬时垃圾**型回归灵敏度会被窗口内 GC 削掉一截（格式化注入只读到 417 KB，持有式注入读到 1.5–3.2 MB）——那类回归以直接观测为准 |
| **遮罩射线缺陷** | 2026-10-11 **已实测并修复**（`2e7951b`）。此前遮罩既点不关也挡不住输入 | 见 §四.4 的教训；已闭环，列在此处仅为完整 |
| ~~**测试覆盖失衡**~~（四模块已补，其余仍在） | ~~Config / Localization / Pipeline / Pool **零 PlayMode 侧**~~ → **2026-10-12 已补**（各一个 PlayMode fixture，共 11 例；验的都是 EditMode 结构上测不到的：真跨帧、真墙钟超时、真销毁令牌）。**仍失衡的**：Serialize(1) / Event(3) / Save(3) / Reactive(4) / Asset·Bootstrap 的 Runtime 侧各 1 | EditMode 覆盖不能替代 PlayMode：**跨帧、跨生命周期、跨静态状态**的问题只在 PlayMode 显形（会话级复位就是例证）。本轮四模块的补法可直接照抄：先去 README 的「已知限制」里找「只有 PlayMode 才验得了」的那条 |
| **大文件** | `UIManagerImpl.cs` 2038 行 / `UpdateScheduler.cs` 1420 / `SaveManagerImpl.cs` 1314 / `LockManager.cs` 1125 / `MessageBroker.cs` 1072 | 维护成本，非缺陷。**不建议为拆而拆**——Update 已明确否决过「拆 `UpdateManager.cs`」（`Modules/Update.md`），同一判据适用于其余 |

### 4.3 仓库工程化与发布形态

> 以「对外发布的通用框架」为标准。**本节的每一项都是「第三方会直接撞上」或「发布流程会直接卡住」的**，不是洁癖。

#### 高：会让第三方拿到坏结果

1. **一键依赖安装器装出的版本与仓内不是同一套**（**已实测**）。
   `Editor/XFrameworkDependencyInstaller.cs:33-34` 写出的 UniTask / YooAsset 是**不带 `#tag`** 的 Git URL；而 `Packages/manifest.json` 钉的是 `#2.5.11` / `#2.3.18`。
   **后果已由本仓自己实测过**：`README.md` 的「第三方依赖」表明写「不写修订时包管理器会取默认分支的最新提交……Asset 模块会直接编译不过（**实测：不写修订时 51 处 `error CS0246/CS0234`，钉住后退出码 0**）」。
   **当前上游状态**（2026-10-11 实测）：YooAsset 默认分支 HEAD 的 `package.json` 已是 **3.0.6**，其兼容类型整文件包在 `#if YOOASSET_LEGACY_API` 内、默认不编译；而本仓引用了其中被移走的 `RawFileHandle`。**三个条件同时成立 ⇒ 今天点一下菜单，装出来的工程必然编译不过。**
   **第二个缺陷**：`:71` 用 `json.Contains(packageId)` 判存在性，**已存在就永不升级**——照旧 README 装过裸 URL 的消费方会**永久**停在裸 URL 上，连手工改成钉 tag 后再点菜单也不会被纠正。
   **附带**：安装器末尾无条件打印「依赖安装完成！」——一次假成功信号。
2. **无 LICENSE**。仓库根与包内都没有，`package.json` 也无 `license` 字段。**对外发布的阻塞项**：没有许可证，第三方在法律上无权使用。

#### 中：发布与升级流程

3. **无 git tag、无发布流程**。`CHANGELOG` 的 `[Unreleased]` 自 2026-08-20（`0.2.0`）起累积 **~660 行**、且内部 `###` 子节**按轮次重复出现 50+ 次**——不是 Keep-a-Changelog 的可读形态。`package.json` 的 `version: 0.2.0` 全手工维护。
4. ~~**无 API 兼容性检查**~~ —— **2026-10-13 已落地**。原事实：没有 `PublicAPI.Shipped.txt` 分析器、没有 ApiCompat，而 `[Unreleased]` 里已攒了 **6 处破坏性改名**，全靠手工记——「与门禁文化格格不入，最适合机械化的一条」。
   现在的形态（**不是**分析器，也不是 ApiCompat）：`Tests/Runtime/Architecture/PublicApiBaselineTests` 反射渲染 Runtime 程序集的公开面成扁平行集合，与 `PublicApiBaseline.txt`（1961 行 / 222 个公开类型）逐行比对；差异天然两分——「已消失/已变更」（破坏性）与「新增」（源兼容）。更新走 `Tools/run-tests.ps1 -UpdateApiBaseline`（**只写盘、永不给绿灯**，绿灯由不带开关的重跑给出）。
   **为什么没用现成分析器**：Roslyn 分析器在 Unity 侧没有可提交的 csproj 挂载点（Unity 生成的 csproj 被 git 忽略），且 `PublicAPI.Shipped.txt` 要经 `AdditionalFiles` 传给分析器——那条路本仓走不通（详见实施轮的探针结论）。**Editor 程序集（5 类型 / 10 成员）不在射程内**。
5. **无 CI**。三条门禁全靠人工在阶段收尾时跑（`Workflow.md:122` 自述）。对外发布的项目，门禁应该挂在 push/PR 上而不是人脑里。
6. **`package.json` 缺 6 个字段**：`keywords` / `samples` / `author` / `license` / `documentationUrl` / `changelogUrl`。UPM 列表页与详情页会因此残缺。
7. **Samples 上不了架**。目录是 `Samples/Example/`（**非 `Samples~`**），包内有 `.sample.json` 写了 `displayName`/`description`，但 **`package.json` 没有 `samples` 数组** ⇒ Package Manager 的 Samples 页签里看不到它，反而**作为普通包资源被导入**到每个消费方工程。示例只有 7 个，覆盖 22 模块中的 6 个。
8. **模板残留随包分发**：`Editor/EditorExample.cs`、`Tests/Editor/EditorExampleTest.cs`、`Tests/Runtime/RuntimeExampleTest.cs`、`Samples/Example/SampleExample.cs`。
9. **无 `.gitattributes`、无行尾归一化**。Force Text 只是**工程设置**（`ProjectSettings/EditorSettings.asset:7`），不是仓库保证；`m_LineEndingsForNewScripts: 0`（OS Native）。**实测旁证**：本轮提交时 git 对 7 个文件全部报了 `LF will be replaced by CRLF` 警告——说明换行符正在被运行环境悄悄改写。
10. **`Tests.Native/` 死残留**：目录里只有 `Tests.Native.csproj`，**未入版本控制**（`git ls-files` 为空，被 `.gitignore` 的 `*.csproj` 排除），而它 `<Compile Include>` 引用的 `Runtime/Node/`、`Runtime/Loader/` **早已删除**。纯本地孤儿。

#### 低：长期形态

11. **`Tests/` 随包分发**（45k 行进每个消费方工程）。Unity 惯例可用 `Tests~` 让包管理器跳过导入。
12. **包在子目录**：引入必须带 `?path=Assets/XFramework`，因此**不能直接当子模块包用**。
13. 无 `.editorconfig`（`CLAUDE.md` 有大段编码规范，但机器不强制）、无性能基准套件、无 Git 钩子、无 Issue/PR 模板、无 `CONTRIBUTING`。
14. **依赖声明碎片化**：`package.json` 空 → 版本真相分散在 `Packages/manifest.json`、`Editor/XFrameworkDependencyInstaller.cs`、`README.md` 三处，安装器注释自承「两处必须一起长」。第 1 条就是这个碎片化的直接后果。
15. **`ProjectSettings/ProjectVersion.txt` = `6000.4.5f1`，而 `package.json` 声明 `unity: 6000.3`**。这是**有意**的（最低要求 ≠ 开发版本），但两处不一致容易被误读为漂移，值得在 README 写明。

### 4.4 文档与开发者体验

1. **包 README 的三张清单表无门禁**。功能表（`README.md:118`）、模块索引表（`:138`）、可配置项表（`:197`）全靠手改，而 `check-docs.ps1` 对 `.md` **零覆盖**。**已实测漏过一整轮**（ReactiveList 建成时三张表全漏，跨了一轮才被补）。
2. **「已知限制」小节缺 5 个模块**：Event / Message / Pipeline / Pool / Serialize。其余 17 个都有，且开场白逐字一致（「知道边界比以为没有边界安全……」）。**这 5 个恰是第三方最容易踩坑的底层件**（Pool 的 `Configure` 只在建池前生效、Event 的订阅语义、Message 的缓冲重放）。
3. **`check-docs.ps1 -Enforce` 只统计 CS1570 / CS1574**。CS0419 / CS1591 / CS1573 / CS1734 / CS1735 / CS1584 / CS1658 有存量、未纳门（`ModuleAudit.md` §八 已列）。**注意 CS0419 的隐蔽性**：它表现为「**没有**告警」——cref 写不全时编译器替你假设一个重载。
4. ~~**门面完备性守卫只覆盖 UI / Settings**~~ —— **2026-10-13 核对后订正：这句话是失实的。** 守卫共 **11 处**
   （Asset / Audio / Config / Data / File / Input / Localization / Log / Save / Settings / UI），恰是全部
   「静态门面转发到可替换接口」的模块（9 个 `I*Manager` + File 的 `IFileProvider` + Input 的 `IInputProvider`）。
   原句的「2 个」与「Message 尚无」都不成立：**Message 不适用这条不变量**（`MessageManager` 是
   `IMessagePublisher` / `IMessageSubscriber` 的扩展方法宿主，不是转发门面）。
   **仍开着的是更细的一问**（本条修订后的真实内容）：多接口模块只守了**主**接口——UI 没覆盖
   `IUIHudProvider` / `IUITipProvider`，Settings 没覆盖 `ISettingsStore` / `IAsyncSettingsStore`，
   这些 provider / store 型接口要不要同名转发守卫需要一次裁定。详见 `ModuleAudit.md` §八 该行。
5. **`ModuleAudit.md` 的判据源自 CHANGELOG，存在已知盲区**。Config 模块 31 条提交、两次破坏性重构，而 CHANGELOG 里出现 **0 次**（`Documentation/Modules/Config.md:54`）——「手册从本模块的缺陷史里学到的是零」。**这是判据生成方式的系统性缺陷**，不只是 Config 一个模块的问题。
6. **示例覆盖面窄**（6/22 模块），且上不了架（§4.3 第 7 条）。第三方上手路径目前只有 README。

**本轮新得到的一条 DX 教训（实测）**：`UIMaskTests.ClickToClose_PopsTopPanel` 绿了九个月，而它验的是**接线**（`Button.onClick.Invoke()`）不是**射线管道**——缺陷因此隐形。这是 `ModuleAudit.md` 判据 **D3「测试的假绿」**的教科书样本。**可复用的判据**：当一个特性跨「接线」与「引擎管道」两层时，只测接线等于不测。

## 五、优先级分期

> 排序依据是「**对外发布**」标准：先清会让第三方拿到坏结果或无法合法使用的，再清正确性，最后补能力。
> **「近」里的每一项都是小改动**——它们卡住的却是整个发布。

### 近期 A：可发布阻塞项（全是小改动，可一轮做完）

| # | 项 | 验证方式 |
|---|---|---|
| 1 | 依赖安装器钉 `#tag` + 与 README 表对齐；**并处理已存在裸 URL 的消费方**（`:71` 的存在性判断要不要放宽） | 新增 EditMode 守卫：反射读 `RequiredUPMDependencies`，断言每条 Git URL 都含 `#` 且与 README 表逐字一致——**这正是安装器注释自己求的「两处必须一起长」** |
| 2 | LICENSE（仓库根 + 包内）+ `package.json` 的 `license` 字段 | — |
| 3 | `package.json` 补齐其余 5 字段（`keywords`/`samples`/`author`/`documentationUrl`/`changelogUrl`） | — |
| 4 | Samples 上架（`samples` 数组 + 目录改 `Samples~`）或明确登记为「随包资源」 | 在 Package Manager 里能看到 Samples 页签 |
| 5 | 清理 4 个模板残留 | `grep -ri example Assets/XFramework` 无残留 |
| 6 | `.gitattributes`（行尾 + `*.unity`/`*.asset` 文本序列化标记） | 提交不再报 `LF will be replaced by CRLF` |
| 7 | 删 `Tests.Native/` 死残留 | 目录不存在 |
| 8 | 把 660 行 `[Unreleased]` 落成 `0.3.0`：`###` 子节按 Keep-a-Changelog 归并，6 处破坏性变更写迁移指南 | 新 tag + CHANGELOG 边界推进 |
| 9 | 包 README 三张清单表补齐并核对 | 逐表对照 `Runtime/*/`

### 近期 B：正确性

| # | 项 | 验证方式 |
|---|---|---|
| 10 | ~~遮罩射线~~ | ✅ **已完成**（`2e7951b`，含红证） |
| 11 | ~~零分配尺子处置~~ ✅ **已完成**（2026-10-12，6 提交） | 尺子自检三条绿；11 处站点改造后实测**全为 0 B**（真红检查逐站点做过：注入式 + 结构改型） |
| 12 | ~~会话级复位~~ ✅ **已完成**（2026-10-12，6 提交） | 六门面 + `Bootstrap.StageList` + `InstanceTracker` 全部处置，各带改前必红的回归用例（`SessionResetTests` 10 例）；**手工验收仍待做**（关域重载连跑两遍，脚本表达不了） |
| 13 | ~~补 Config / Localization / Pipeline / Pool 的 PlayMode 侧覆盖~~ ✅ **已完成**（2026-10-12，4 提交） | 四模块各补一个 PlayMode fixture（3+2+3+3 = 11 例），验的都是 EditMode 结构上测不到的东西（真跨帧、真墙钟、真销毁令牌） |

### 中期：能力缺口与机械化

| # | 项 | 备注 |
|---|---|---|
| 14 | **场景管理立项** | 最大缺口；立项前先回答 §4.1 的四条边界 |
| 15 | 网络基础层 / 热更编排的**边界裁定** | 热更编排的「下载编排」一半可进框架，「更新策略」一半大概率是 Editor 工具 + 文档 |
| 16 | ~~API 兼容性检查~~ ✅ **已完成**（2026-10-13，1 提交 + 文档 1 提交） | 落成形态是**仓库自有的基线比对**（反射渲染 + 行集合差 + 显式更新开关），不是 `PublicAPI.Shipped.txt` 分析器也不是 ApiCompat——后两者在 Unity 侧没有挂载点。实测：基线 1961 行 / 222 类型 / 34 个 protected 扩展点；红证用真实历史样本（`Subscribe` 形参 `handler`→`onNext`） |
| 17 | **CI**（push/PR 上跑三条门禁） | 需要 Unity 许可与自托管 runner，成本最高的一项 |
| 18 | `check-docs.ps1` 收纳其余告警码（先清存量） | CS0419 优先——它隐形 |
| 19 | ~~门面完备性守卫推广到其余模块~~ → 缩成**一问**（2026-10-13 核对：守卫已有 11 处，覆盖全部适用模块） | 剩下的是「多接口模块只守主接口」——UI 的 `IUIHudProvider`/`IUITipProvider`、Settings 的 `ISettingsStore` 要不要也守。需先裁定再动手，代价仍是一个模块十几行 |

### 远期

| # | 项 |
|---|---|
| 20 | 红点 / GM 命令台 / 画质档位 / 代码热更（§三 待裁，逐条走边界论证） |
| 21 | 「已知限制」补齐 5 个模块（Event/Message/Pipeline/Pool/Serialize） |
| 22 | 包 README 三张表加门禁（与第 9 条配套，否则会再漏） |
| 23 | 相机 / 远程配置 / 崩溃上报 —— **建议维持「不做」**，见 §4.1 |
| 24 | `Tests~` / 包在子目录的形态问题（§4.3 第 11–12 条） |

## 六、已否决项复核

> 依据用户裁定的口径：**逐条复核，有新证据才重开**。复核方式 = 重读否决理由 + 检查本轮探索是否产生了推翻它的事实。
> **结论：10 条全部维持，0 条重开。** 逐条依据如下（原文与完整理由见 `Roadmap.md` §四，此处只记复核动作与结论）。

| 项 | 复核结论 | 依据 |
|---|---|---|
| 状态机 / 行为树 / ECS / 时间模型 / 生命周期树 | **维持** | 本轮无新事实。§4.1 的「场景管理」候选**特意做了对照**：它通过「是否要求使用方按某种架构组织游戏逻辑」这一判据，而本条各项都不通过 |
| Update 的「组」API | **维持** | 「组 → 组的生命周期 → 组内嵌套 → 组间顺序」这条推理链未被任何新事实动摇 |
| DI 容器（VContainer / Extenject） | **维持** | 本轮确认「可替换点」表（包 README）里的注入形态**刻意差异化**（`Initialize(实例)` / 工厂委托 / `SetInstance` / 无注入点四种并存），与容器统一化方向相反 |
| Tween / 缓动 / 动画 | **维持** | 无新事实 |
| Addressables | **维持** | 已选 YooAsset；本轮确认 `Asset` 的场景加载也走 YooAsset（`YooAssetManagerImpl.cs:500`） |
| 新手引导 | **维持** | 本轮确认 UI 侧「只做原语（遮罩挖洞），不做引导编排」的落地情况一致（`Modules/UI.md` 的「未做」仅剩此项，且形态就是「只给原语」） |
| 平台 SDK 聚合 | **维持** | 本轮新增的「远程配置 / 崩溃上报」两个候选**用的是同一条判据**（渠道相关 → 项目侧），该判据因此得到一次独立印证 |
| 网络同步框架 | **维持** | 与 §三 待裁的「网络基础层」**不是同一件事**：否决的是**同步模型**（GamePlay 本体），待裁的是**请求/重试/超时/主线程回调**（基础设施）。两者不要折叠 |
| 统一框架 Profile | **维持** | 三条重开判据逐条对照：①「只装编译期决定不了且与 Asset 引导无关的值」——本轮探索未发现这样的值；②「不替代模块 Options」——未满足；③「先回答引导顺序」——**2026-10-02 已满足，但原文已明写这不构成重开理由**（第二条判据排除的正是「中心化」这个形状） |
| 把模块内部策略接口化 | **维持** | 本轮新增一条**支持**该判据的实例：遮罩缺 `GraphicRaycaster` 是**缺陷**而非「模块不变量」，补上它不需要接口化任何东西——「不变量」与「漏做」的区分标准（`ModuleAudit.md` 的三档判据）在本轮经受住了一次实战检验 |

**另有一条本轮明确「不是重开理由」的**：`Roadmap.md` §三 的「App 生命周期」维持「暂不做」。它的升级判据是**可判定的**（想接 `OnApplicationPause` 的地方达到 2~3 处，或有真实使用方提出）——本轮探索**未发现**新的消费者（仍是 `Settings` 自持宿主那一处）。**判据未触发，不做。**

## 七、落点表

按 `ModuleAudit.md` §五 的收件人分流纪律，本轮结论的去向：

| 结论性质 | 落点 | 本轮是否已落 |
|---|---|---|
| 新模块候选（场景管理 / 热更编排） | `Roadmap.md` §三（该不该建、边界画在哪是它的职责） | ✅ |
| 已否决项复核结论 | `Roadmap.md` §四 | ✅ |
| 建议「不做」的新项（相机 / 远程配置 / 崩溃上报） | `Roadmap.md` §三，标「本轮建议不做，待裁定」——**「不该做」是设计取舍，需用户确认后才进 §四** | ✅ |
| 本轮的实测结论（遮罩 / 尺子 / 安装器） | 遮罩 → 已闭环（CHANGELOG + `Modules/UI.md` + README）；尺子 → `Modules/{Log,Timer,Message,UI,Reactive}.md` + `Workflow.md` §六（**2026-10-12 处置轮已全部落地**）；安装器 → 本文 §4.3 + 实施轮 | ✅ |
| 2026-10-12 处置轮的结论（尺子 / 会话复位 / 四模块 PlayMode） | 尺子与四模块 → `Workflow.md` §六 + 各模块文档 + `CHANGELOG`；会话复位 → `Modules/{Input,Asset,Bootstrap,Config,UI,Localization}.md` + `Roadmap.md`（族清单与失实句订正）+ `Runtime/{Input,UI}/README.md` 的已知限制 | ✅ |
| 框架级评估这份文档本身的存在 | `CLAUDE.md` 文档分层表 | ⏳ 待用户确认拟写文本 |

**本文不复制 `Roadmap.md` 与各模块文档的内容**——那些是它们的真相，重复一份就会各自漂移。
