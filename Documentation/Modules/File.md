# File —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/File/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

| 文件                       | 说明                          |
| -------------------------- | ----------------------------- |
| `FileDomain.cs`            | 路径域枚举定义                |
| `FilePathUtility.cs`       | 路径工具（归一化 / 文件名提取 / 路径沙箱校验） |
| `IFileProvider.cs`         | 平台文件提供者接口（含读失败契约） |
| `IAtomicFileProvider.cs`   | 原子写入能力契约（可选）      |
| `IDirectoryProvider.cs`    | 子目录枚举能力契约（可选）    |
| `ICryptoProvider.cs`       | 加解密提供者接口              |
| `XorCryptoProvider.cs`     | 基于 XOR 的轻量加解密实现     |
| `CryptoFileProvider.cs`    | 加解密装饰器（内部）          |
| `DesktopFileProvider.cs`   | 桌面平台文件提供者实现        |
| `MobileFileProvider.cs`    | 移动平台文件提供者实现        |
| `ConsoleFileProvider.cs`   | 控制台平台抽象基类            |
| `FileManager.cs`           | 跨平台文件管理器静态外观      |
| `FileManagerExtensions.cs` | 兼容面：同名同步方法的一行委托（同步 API 已收敛到门面） |
| `README.md`                | 本文件                        |

## 决策台账

- 已做：**解密失败统一抛 `IOException`**（2026-10-17 · `db2e3e1`，FILE-001）——`CryptoFileProvider` 把 `Decrypt` 返回 null 转成 IO 失败，字节面与文本面一致；内置 XOR 永不返回 null，默认路径不变。

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过，**零高危**；逐条理由的展开见使用方 README 的相关章节，此处只留「评估过并否决」这一层。已修项见 `CHANGELOG`。）

- 不做：**不给 `ConsoleFileProvider` 写测试派生类**（2026-09-30 · 审计轮）——它锁的是「本仓没有的使用方式」（全仓零派生）；真正该锁的「不实现可选能力接口时门面如何降级」已由 `NonAtomicFileProvider` / `NonEnumerableFileProvider` 两个替身覆盖。无重开判据。
- 不做：**不给「首次初始化须在主线程」加强制断言**（2026-09-30 · 审计轮）——失败已经响亮（池线程读 `Application.*` 抛 `UnityException`），断言只是把同一条错误提前；`MainThreadGuard` 是「静默出错」才需要的。无重开判据。
- 不做：**不动 `Destroy()` 对 Provider 的处置**（2026-09-30 · 审计轮）——`IFileProvider` **不是** `IDisposable`（接口成员里没有），`Destroy` 不调 Dispose 不漏资源。无重开判据。
- 不做：**不给同步 API 做「自动切回主线程」**（2026-09-30 · 审计轮）——那会改变 `.AsTask().GetAwaiter().GetResult()` 的阻塞语义，而同步面的存在意义就是「调用方自己承担阻塞」。无重开判据。
- 不做：**两处覆盖空白按「测不了」登记、不按「以后补」**（2026-09-30 · 审计轮）——① `ConsoleFileProvider` 的公开方法（abstract + virtual）零覆盖（仓内无派生类）；② `MobileFileProvider` 的 Streaming 私有路径（`CheckStreamingExists` / `ReadStreamingText` / `ReadStreamingBytes` / `GetStreamingUrl`）零覆盖（仓内无移动设备路径，任何替身都会结构性绕开真实链路）。这正是 `CHANGELOG` 记过的教训：文件测试 fixture 全注入 `TempFileProvider`，于是「域根解析是否碰了 Unity API」这一整类缺陷在替身上不可见。重开判据：仓内出现真机或派生类的验证通道。
- 未做：**`MobileFileProvider.CheckStreamingExists` 的忙等未在真机验证**（2026-09-30 · 审计轮）——门面（`FileManager.Exists`）已拒绝「移动端 + Streaming」组合，但 Provider 是公开类型、可直接使用，那条 `while (!request.isDone) { }` 仍在；**未决的是「要不要给 Provider 层也加兜底」**（改成有界等待？），本轮只如实标注风险。已知证据：桌面 + `file://` 下自旋会完成（红基线 16.1 秒跑完）；移动端（Android `jar:file://` / iOS）无路径可验。
- 未做：**`FileRootsTimingTests` 的命名空间与同目录其余 fixture 不一致**（2026-09-30）——`XFramework.XFile.Tests` vs `XFramework.XFileManager.Tests`，纯命名问题，等下次动那批测试时顺手统一。
- 已做：**门面新增 `DeleteAsync`，补齐异步删除原语**（2026-10-15 · `61111e0`）——把 Provider 的同步 `Delete` 放进 `RunOnThreadPool` 执行，是**门面唯一一处自己决定 IO 线程**的原语；基线 +1 行、**零接口变更**。Save 的四个删除 API 随之迁移（`SaveManagerImpl` 5 处 + 每处补 `ReturnToMainThread`），删除不再压在主线程。回归锁：`FileManagerDeleteTests` 5 例（线程断言 + 同步对照 + 幂等 + 取消 + 加密）、`SaveManagerImplTests` 四条线程用例（改前必红）。
- 不做：**不新增「异步删除」可选能力接口**（2026-10-15 · 同轮单议）——降级路径恰好就是门面的线程池包装、且**结果完全相同**（对比原子写丢崩溃防护、目录枚举丢结果——那两条的降级都要告警）。通用判据：**可选能力接口只用在「降级会产生不同结果、需要告警」处**。重开判据：真出现「其 `Delete` 不能离开特定线程」的 Provider（典型如 WebGL 的 JS 桥）⇒ 补能力接口（形状与 `IAsyncSettingsStore` 同构）。
- 不做：**不给 `IFileProvider` 加 `DeleteAsync` 成员**（2026-10-15）——会让所有第三方实现编译不过；本仓铁律是「不给主接口加成员，第三方实现零影响」。无重开判据。
- 不做：**不把 `Delete` 改成 `DeleteAsync(...).AsTask().GetAwaiter().GetResult()` 的阻塞包装**（2026-10-15）——调用方**照样阻塞**，只多付一次线程池往返，还会把 Provider 的执行线程从调用线程悄悄改掉。README 那句「同步面是异步面的一层包装」据此**收窄**为「大多只是包装，`Delete` 是直通原语」——改文档而不是改代码迁就它。无重开判据。
- 不做：**不新增 `MoveAsync` / `CopyAsync`**（2026-10-15）——Save 的「移动」= 复制 + 删源（刻意非原子，见 Save README）；同盘 `File.Move` 在 WebGL / Console 无可用物理路径，加了也没有消费者。原子替换语义已有 `FilePathUtility.ReplaceFileAtomically`（原子写走它）。无重开判据。
- 已做：**补 `OSXServer` 平台分支**（2026-10-20 · R3-FILE-001，本轮修复）——macOS Server 构建落 default 抛 `PlatformNotSupportedException`，本可用 `DesktopFileProvider`；加 case。运行期需 macOS Server 构建实测（R3-PRB-04 登记 `未做`）。
- 已做：**移动 Streaming 路径归一与沙箱**（2026-10-20 · R3-FILE-002，本轮修复）——`GetStreamingUrl` 绕过 `GetPhysicalPath`，反斜杠不归一、`../` 可原样拼进 URL；改为与桌面实现共用 `TryNormalizeRelativePath`，非法路径抛 ANE。回归 `FileManagerSyncTests.MobileStreaming_RejectsTraversalPath`；Android/iOS 真机行为登记探针 `未做`。
- 已做：**Destroy 后的 crypto 设置闩锁**（2026-10-20 · R3-FILE-004，本轮修复）——`SetCryptoProvider`/`GetCryptoProvider` 不查 `_destroyed`，销毁期间的设置会被下一次 `Initialize` 悄悄采纳；两入口补 `ThrowIfDestroyed`。回归 `FileManagerCryptoTests.CryptoProvider_AfterDestroy_ThrowsObjectDisposed`。
