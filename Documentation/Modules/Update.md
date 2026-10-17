# Update —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Update/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Update/
├── IUpdateable.cs                # 契约：IUpdateLifecycle / IUpdateable / ILateUpdateable /
│                                 #       IFixedUpdateable / UpdateTier
├── UpdateClock.cs                # 时间基：UpdateClock（time + unscaledTime + isPaused）/ UpdateTimeMode
├── UpdateNodeInfo.cs             # 诊断回读载荷：CopyNodes 的列表元素（readonly struct；2026-10-16 补录——它一直是公开面，此前漏在本树之外）
├── UpdateScheduler.cs            # 纯调度逻辑（档位分桶 + 时间切片 + 双时间轴），internal
└── UpdateManager.cs              # 静态门面（含 PlayerLoop 注入驱动）
```

## 沿革与已否决形状

- **「组」为什么不做成框架 API**：组一旦进入框架，紧接着就要回答「组的生命周期」「组内嵌套」「组之间的顺序」——那就是生命周期树，正是本框架在 2026-09-18 整块删除的节点系统所承担的东西（见 `CHANGELOG.md` 的「删除节点系统」，其中「一套 GamePlay 架构」「一个服务定位器」都被判定为不该由基础框架管）。

## 决策台账

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过，**零高危**。该轮全部改动是**文档 + 测试**，未碰任何行为。逐条理由的展开见使用方 README 的相关章节，此处只留「评估过并否决」这一层。）

（2026-10-01 **第二轮**，定向复审 `UpdateManager.cs` 及其承载的契约面——四个源文件、模块 README、
五个 fixture 的覆盖现状、四个生产消费方的用法。判据 A–F 逐类扫过，**仍无高危**。新账四处：
① `Enable` 档位口径的三处 XML 注释是上一轮 F1 的另一半（README 已勘误、注释没跟上，本轮补齐）；
② 「固定步 × `ProcessImmediate`」把条目锚到逻辑时刻（本轮改为只定锚）；③ 一条覆盖缺口——
`Pause()` × 墙钟轴零断言且该轴零生产使用者（本轮补用例 + 负向控制）；④ 四条低危项（门面类摘要
指错手动驱动入口、`GetCount`/`IsEnabled`/`Pause` 三处边界未写明、异常日志固定报 `OnUpdate`、
README 缺「注册表持强引用」边界）。**本轮碰了行为**——与上一轮「只改文档 + 测试」不同，
行为变更经用户单独裁定。）

- 不做：**不修「相位随增删平移」**（2026-09-30 · 审计轮）——切片档的相位就是**桶内下标对 `2^k` 取模**，而增删（`RemoveAt` / `Insert`）会让其后条目的下标整体平移——于是同桶内任何一个条目的增删都会改变其它条目的派发相位（极端情况要多等将近一整个周期）。彻底修要「**稳定 per-entry 相位**」，代价是每格扫全桶，会丢掉现在的「只访问本切片条目」这条性质——**那正是档位分级省开销的来源**。
  **对照物**：Linux 的分层时间轮（timer wheel）是同一个形状，但节点**稳定留在自己的槽位**、摘除不移位。本模块省掉了那个稳定槽位，代价即此。**已评估并接受**；本轮只归档 + 在 README「已知限制」写明使用方边界。无重开判据。
- 不做：**不加线程断言（`MainThreadGuard` 式）**（2026-09-30 · 用户裁定）——契约（所有 API 必须在主线程）写进了 README「已知限制」，但不加机械。仓内四个消费方全在主线程，风险主要是第三方的。无重开判据。
- 不做：**不给门面加完备性守卫**（2026-09-30 · 审计轮）——UI / Settings / Localization / Input / File 五个先例的那种守卫，其形状是「**provider 接口**的每个成员在门面上有同名转发」，而 Update 的三个时机接口是**消费方实现**的契约、门面背后没有 provider——形状不适用，硬套会把模块自有的成员判成缺陷。无重开判据。
- 不做：**不改 `ProcessImmediate` 的异常行为**（2026-09-30 · 审计轮）——`Tick` 路径抛异常→隔离 + 注销，`ProcessImmediate` 路径→上抛且不注销：显式调用该看到失败，与 `Settings.Save<T>()` 的既有裁决同向。本轮补文档，不改行为。无重开判据。
- 不做：**不加「加载期硬门控」原语**（`SuspendDispatch` / `ResumeDispatch` 式，冻结含 `Unscaled` 的全部派发）（2026-10-01 · 专项评估后否决）——动机是「框架加载完成前不派发」，但问题两侧都不需要它——框架内建 ticker 全部「初始化成功后才注册 + 未就绪空转」，使用方回调的正解是 `await Bootstrap.RunAsync()` 后注册；「加载期冻结逻辑轴」的需求现有 `Pause()` / `Resume()` 已覆盖（不冻 `Unscaled` 是既有设计，不是缺口）。新增原语等于在 `timeScale <= 0` 与 `Pause()` 之外再加一种「冻结」语义，加重 API 面，且无仓内消费者。替代写法与边界写进本模块 README「已知限制」。无重开判据。
- 不做：**不钳 `ProcessImmediate` 的 `deltaTime`**（2026-10-01）——Tick 路径钳负 delta 是因为时刻来自引擎
  （`timeScale < 0` 倒放会算出负间隔）；`ProcessImmediate` 的 delta 是**显式实参**，钳它等于静默
  改写调用方请求——与该方法「异常直接上抛、不注销」的既有裁决同向。无重开判据。
- 不做：**不拆 `UpdateManager.cs`**（门面 + PlayerLoop 注入拆成两个类）（2026-10-01）——注入与门面共享
  `_schedulers` 这一份私有状态，拆开要么破坏封装、要么把它变成第二处真相；注入面的覆盖已由
  `UpdatePlayerLoopTests` 提供，拆分换不来新的可测面。无重开判据。
- 不做：**不把驱动注入的匹配从「类型名」改成「父链校验」**（2026-10-01）——`TryAppendToDriverTarget` 按
  `system.type.Name` 全树查找，理论上会命中同名系统；但 Unity 默认树里不存在同名，风险纯理论，
  而按名查找换来的跨版本稳健（不引用嵌套类型）是实际收益。已评估并接受。无重开判据。
- 暂不做：**不给 `UpdateClock` 加第三个「固定步时基」字段**（2026-10-01）——为一条只被 `ProcessImmediate`
  读到的通道扩公开结构体的形状，代价大于收益；固定步时机已改为「只定锚」，不再需要该字段。重开判据：出现第二条需要固定步时刻的路径。
- 已做：**`Tick` 三重重载并存不构成缺陷、无形状可调（D2 结论）**（2026-10-01 · D2 探针**实测**：照抄
  `Tick()` / `Tick(float)` / `Tick(in UpdateClock)` 形状的独立复现程序，不依赖本仓代码、带反向控制）
  ——三条与纸面推理**相反**的事实：① 五条既有调用形状（无参、`0f`、`0`、结构体实例、
  `new UpdateClock`）各自唯一匹配，**没有任何形状被两个重载同时接住**；② `Tick(default)`
  **不二义**，静默落到 `Tick(float)`（不是编译错误）；③ `Tick(Time.timeAsDouble)` 是**编译错误**
  CS1503——`double` → `float` 不是隐式转换，故「换了双精度时间 API」不会静默丢精度。记在这里免得下一轮重新论证（真要动重载形状，先把这三条实测重跑一遍）。
- 已做：**「节点被自动注销」对注册方由此可观测**（2026-10-14 · `81e27a8` / `7a63231`，用户裁定「甲＋乙」）——两条一起做：① **补发 `OnDisable` 边沿**（自动注销是「离开派发集合」，宣告在那帧收尾落地；零公开 API）——它顺带**修回**了 README 明写、而当时正被打破的不变量「两者严格交替、不会出现没有配对的 `OnDisable`」，也让节点的按时机计数不再永不归零；② **`UpdateManager.IsRegistered(node)`**（登记里写的形状，公开 API 新增一行）给「外部观察者查别人的注册态」，与 `IsEnabled` 分工写在 XML doc 里（两谓词方向相反：未注册时一个为 false、一个为 true）。回归锁：`UpdateSchedulerTests` 9 条自动注销用例 + 3 条不变量锁、`UpdateCrossTimingTests` 1 条、`UpdateManagerTests` 4 条（反向注入 9 条全红、锁与既有全绿已取证）。**被否形状**：catch 里改发 `Disable`（会让 `IsEnabled` 一词两义、多出永不释放的中间态、污染 `DisabledCount` 诊断语义）、可选能力接口 `IUpdateFailureAware`（异常本就是节点自己抛的、日志已记录原因）。
- 未做：**迁移仓内两个消费方的防线**（`InputManager._ticker != null` / `SettingsAutoSaveTicker` 吞异常重试）（2026-10-14 · 上条闭合的遗留半）——它们的自记账仍有效，仓内 6 个 `OnDisable` 实现全空 ⇒ 边沿对仓内零影响，迁移只是可选清理。
- 未做：**`IsDrivingPlayerLoop` 的边界**（2026-09-30 · 审计轮）——它不读 `_schedulers`，故 `OnQuitting()` 之后仍可能返回 `true`（引擎是否在退出时重置 PlayerLoop **未确认**）。已在 XML 文档里注明「它只回答注入状态，不回答有没有东西在派发」。
- 未做：**`LateUpdate` / `FixedUpdate` 两个时机与墙钟轴（`Unscaled`）在仓内零生产使用者**（2026-09-30；2026-10-01 扩写）——四个消费方（`InputTicker` / `FrameDriver` / `TierDriver` / `SettingsAutoSaveTicker`）全在 `Scaled` 轴：三者的集成面只有测试，`Pause()` × 墙钟轴的组合此前连用例都没有（2026-10-01 补上 `Pause_LeavesUnscaledAxisRunning`，并以负向控制证明其判别力）；下一轮若要改这几处的语义，别以为有生产用例兜底。
- 待裁：**`Clear()` 是否改成有序 `PendingOp`**（2026-10-17 · 第二轮审计）——派发回调里 `Clear(); Register(x);` 会先插入并宣告 `OnEnable`、再被清掉（UPD-002）；维持现状则文档写明「Clear 恒压过同窗口操作」。重开判据：出现重置后立即重建的真实使用方。
- 待裁：**门面 `ProcessImmediate` 形参是否放宽为 `IUpdateLifecycle`**（2026-10-17 · 第二轮审计）——当前形参 `IUpdateable` 使纯 `ILateUpdateable`/`IFixedUpdateable` 节点够不到（UPD-003），属公开 API 变更。重开判据：出现纯 Late/Fixed 节点需要立即响应的使用方。
- 待裁：**`AutoDriveEnabled` 会话复位 vs 保留用户选择**（2026-10-17 · 第二轮审计）——跨会话存活且 `AutoInit` 不复位，第二会话可能继承 false 导致自动派发静默停摆（UPD-008）。重开判据：关闭域重载两遍 PlayMode 复现停摆。

