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
├── UpdateScheduler.cs            # 纯调度逻辑（档位分桶 + 时间切片 + 双时间轴），internal
└── UpdateManager.cs              # 静态门面（含 PlayerLoop 注入驱动）
```

## 沿革与已否决形状

- **「组」为什么不做成框架 API**：组一旦进入框架，紧接着就要回答「组的生命周期」「组内嵌套」「组之间的顺序」——那就是生命周期树，正是本框架在 2026-09-18 整块删除的节点系统所承担的东西（见 `CHANGELOG.md` 的「删除节点系统」，其中「一套 GamePlay 架构」「一个服务定位器」都被判定为不该由基础框架管）。

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过，**零高危**。该轮全部改动是**文档 + 测试**，未碰任何行为。）

（2026-10-01 **第二轮**，定向复审 `UpdateManager.cs` 及其承载的契约面——四个源文件、模块 README、
五个 fixture 的覆盖现状、四个生产消费方的用法。判据 A–F 逐类扫过，**仍无高危**。新账四处：
① `Enable` 档位口径的三处 XML 注释是上一轮 F1 的另一半（README 已勘误、注释没跟上，本轮补齐）；
② 「固定步 × `ProcessImmediate`」把条目锚到逻辑时刻（本轮改为只定锚）；③ 一条覆盖缺口——
`Pause()` × 墙钟轴零断言且该轴零生产使用者（本轮补用例 + 负向控制）；④ 四条低危项（门面类摘要
指错手动驱动入口、`GetCount`/`IsEnabled`/`Pause` 三处边界未写明、异常日志固定报 `OnUpdate`、
README 缺「注册表持强引用」边界）。**本轮碰了行为**——与上一轮「只改文档 + 测试」不同，
行为变更经用户单独裁定。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层）：

- **不修「相位随增删平移」**：切片档的相位就是**桶内下标对 `2^k` 取模**，而增删（`RemoveAt` / `Insert`）会让其后条目的下标整体平移——于是同桶内任何一个条目的增删都会改变其它条目的派发相位（极端情况要多等将近一整个周期）。彻底修要「**稳定 per-entry 相位**」，代价是每格扫全桶，会丢掉现在的「只访问本切片条目」这条性质——**那正是档位分级省开销的来源**。
  **对照物**：Linux 的分层时间轮（timer wheel）是同一个形状，但节点**稳定留在自己的槽位**、摘除不移位。本模块省掉了那个稳定槽位，代价即此。**已评估并接受**；本轮只归档 + 在 README「已知限制」写明使用方边界。
- **不加线程断言（`MainThreadGuard` 式）**：契约（所有 API 必须在主线程）写进了 README「已知限制」，但不加机械——用户裁定。仓内四个消费方全在主线程，风险主要是第三方的。
- **不给门面加完备性守卫**（UI / Settings / Localization / Input / File 五个先例的那种）：那条约定的形状是「**provider 接口**的每个成员在门面上有同名转发」，而 Update 的三个时机接口是**消费方实现**的契约、门面背后没有 provider——形状不适用，硬套会把模块自有的成员判成缺陷。
- **不改 `ProcessImmediate` 的异常行为**（`Tick` 路径抛异常→隔离 + 注销；`ProcessImmediate` 路径→上抛且不注销）：显式调用该看到失败，与 `Settings.Save<T>()` 的既有裁决同向。本轮补文档，不改行为。
- **不加「加载期硬门控」原语**（`SuspendDispatch` / `ResumeDispatch` 式，冻结含 `Unscaled` 的全部派发）：2026-10-01 评估后否决。动机是「框架加载完成前不派发」，但问题两侧都不需要它——框架内建 ticker 全部「初始化成功后才注册 + 未就绪空转」，使用方回调的正解是 `await Bootstrap.RunAsync()` 后注册；「加载期冻结逻辑轴」的需求现有 `Pause()` / `Resume()` 已覆盖（不冻 `Unscaled` 是既有设计，不是缺口）。新增原语等于在 `timeScale <= 0` 与 `Pause()` 之外再加一种「冻结」语义，加重 API 面，且无仓内消费者。替代写法与边界写进本模块 README「已知限制」。
- **不钳 `ProcessImmediate` 的 `deltaTime`**（2026-10-01）：Tick 路径钳负 delta 是因为时刻来自引擎
  （`timeScale < 0` 倒放会算出负间隔）；`ProcessImmediate` 的 delta 是**显式实参**，钳它等于静默
  改写调用方请求——与该方法「异常直接上抛、不注销」的既有裁决同向。
- **不拆 `UpdateManager.cs`**（门面 + PlayerLoop 注入拆成两个类，2026-10-01）：注入与门面共享
  `_schedulers` 这一份私有状态，拆开要么破坏封装、要么把它变成第二处真相；注入面的覆盖已由
  `UpdatePlayerLoopTests` 提供，拆分换不来新的可测面。
- **不把驱动注入的匹配从「类型名」改成「父链校验」**（2026-10-01）：`TryAppendToDriverTarget` 按
  `system.type.Name` 全树查找，理论上会命中同名系统；但 Unity 默认树里不存在同名，风险纯理论，
  而按名查找换来的跨版本稳健（不引用嵌套类型）是实际收益。已评估并接受。
- **不给 `UpdateClock` 加第三个「固定步时基」字段**（2026-10-01）：为一条只被 `ProcessImmediate`
  读到的通道扩公开结构体的形状，代价大于收益；固定步时机已改为「只定锚」，不再需要该字段
  （若将来出现第二条需要固定步时刻的路径，这条要重新评估）。
- **不调整 `Tick` 的三重重载形状**（2026-10-01，D2 **实测**）：照抄
  `Tick()` / `Tick(float)` / `Tick(in UpdateClock)` 形状的独立复现程序（不依赖本仓代码，带反向控制）
  得出三条与纸面推理**相反**的事实——① 五条既有调用形状（无参、`0f`、`0`、结构体实例、
  `new UpdateClock`）各自唯一匹配，**没有任何形状被两个重载同时接住**；② `Tick(default)`
  **不二义**，静默落到 `Tick(float)`（不是编译错误）；③ `Tick(Time.timeAsDouble)` 是**编译错误**
  CS1503——`double` → `float` 不是隐式转换，故「换了双精度时间 API」不会静默丢精度。三者都不构成
  缺陷，无形状可调；记在这里免得下一轮重新论证（真要动重载形状，先把这三条实测重跑一遍）。

**未决**：

- **「节点被自动注销」对注册方不可观测**：`OnUpdate` 抛异常后调度器记 `LogError` 并把节点摘掉，但注册方没有任何查询能知道「我还在不在调度里」。仓内两个消费方因此各自造了防线——`InputManager._ticker != null`（自己的字段，不是查询）与 `SettingsAutoSaveTicker` 吞异常重试。要补的形态是给门面加一个「该节点是否仍被调度」的查询，属**公开 API 新增**，该单独走计划。
- **`IsDrivingPlayerLoop` 的边界**：它不读 `_schedulers`，故 `OnQuitting()` 之后仍可能返回 `true`（引擎是否在退出时重置 PlayerLoop **未确认**）。已在 XML 文档里注明「它只回答注入状态，不回答有没有东西在派发」。
- **`LateUpdate` / `FixedUpdate` 两个时机在仓内零生产使用者**，**墙钟轴（`Unscaled`）同样零生产
  使用者**（四个消费方——`InputTicker` / `FrameDriver` / `TierDriver` / `SettingsAutoSaveTicker`——
  全在 `Scaled` 轴）：三者的集成面只有测试，`Pause()` × 墙钟轴的组合此前连用例都没有（2026-10-01
  补上 `Pause_LeavesUnscaledAxisRunning`，并以负向控制证明其判别力）；下一轮若要改这几处的语义，
  别以为有生产用例兜底。
