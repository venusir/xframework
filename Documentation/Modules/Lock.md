# Lock —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Lock/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Lock/
├── LockManager.cs                # 静态外观（全局入口）
├── ILockable.cs                  # 可锁标记接口
├── LockableExtensions.cs         # ILockable 扩展方法
├── LockStateSnapshot.cs          # 状态快照（诊断用：锁计数与「永不自动释放」的主体数）
└── LockHandle.cs                 # 锁句柄（读存储，支持 using）
```

## 已评估未采纳与未决

**已评估未采纳**：
- `lockObj` 改引用相等 —— 会推翻既有幂等语义（同一来源重复加锁幂等）。改为文档化 + 推荐专用 token。
- **`lockObj` 改计数语义**（`Dictionary<object,int>`，照 Unreal GAS 的 loose tag 容器）—— 这**不是**「代价大
  一点」，而是推翻已被 README 与测试钉住的幂等语义，属**破坏性变更**；嵌套计数由「每次 acquire 一枚新
  token」零 API 覆盖（见「键的语义」的两行小表）。
- **`HasAnyLock(subject)` / `GetLockedTypes(subject)`** —— 形状本身有语义歧义：含全局锁会把「全局 UI 锁」
  也算进「移动键该灰」，不含又表达不了「加载屏期间整块置灰」，两种选法都有场景给出错答案。GAS 的对应物是
  `HasAny(调用方给的集合)`——**成熟实现没有「任意类型」这个查询**；调用方自持 `static readonly int[]`
  加一次循环即可（零分配、语义明确）。程序化枚举类型域的需求由 `CopyLockedTypes` 覆盖。
- **`RegisterLockTypeName(int, string)`**（让 dump 打出 `Movement` 而不是 `3`）—— 仓内**没有 int→name 注册表
  的先例**（id 域的先例是 `UILayers` 那样「带名字的 const 类」），而 GAS 需要注册表是因为它的 tag 是**数据
  驱动**的。顺序应当是：先用 `DumpState`，再实测「裸数字到底有没有真妨碍排查」，然后才决定。
- `OnGlobalLocked` / `OnGlobalUnlocked` 改名（如 `OnAnyLocked`）—— 非破坏优先，改为把文档写成与实现一致。
- 订阅者异常**逐条**隔离（`GetInvocationList`）—— 每次派发要分配一个委托数组，取舍与 `Pipeline` 一致，
  已记入「已知限制」。
- **等待原语**（`WaitUntilUnlockedAsync`）—— 与 UI README 的同型裁定一致（「框架**没有**内建的『等你回结果』
  通道」）：等一个由使用方拥有的状态翻转，配方式写法见「典型场景 D」，且**必须先查一次**否则会永久挂起。
- 主线程断言（`MainThreadGuard` 式）—— 本轮只写文档，不动行为。
- `SubscriptionTracker` 那句「全仓订阅只有两处登记点」（Lock 是第三处、且不受跟踪）—— 属 Event 侧文档，
  经裁定本轮不动。

**未决**：
- **跨对象订阅的自动退订**：形态是 `OnLocked(subject, handler, IDestroyCancellationToken owner)` 重载
  （arity 不同，无重载二义风险），本轮未做。
- **`ActionDisposable` 池化的 ABA**：可改成「Rent 返回带代号的包装」消除，代价是每次订阅多一次分配。
  仓内另有更彻底的答案可参考：UI 遮罩用**单调令牌 + 条目表**（`UIManagerImpl`），令牌不回收即无 ABA。

## 审计轮次

2026-09-27 按 `../ModuleAudit.md` 审计一轮（本轮修了什么见 `CHANGELOG` 的 `[Unreleased]`）；
本节只留**下一轮需要知道的**。
