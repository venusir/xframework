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

## 已评估未采纳与未决

（2026-09-30 按 `../ModuleAudit.md` 审计一轮，判据 A–F 全类扫过，**零高危**。已修项见 `CHANGELOG`。）

**已评估未采纳**（逐条理由见使用方 README 的相关章节，此处只留「评估过并否决」这一层）：

- **不给 `ConsoleFileProvider` 写测试派生类**：它锁的是「本仓没有的使用方式」（全仓零派生）。真正该锁的
  「不实现可选能力接口时门面如何降级」已由 `NonAtomicFileProvider` / `NonEnumerableFileProvider` 两个替身覆盖。
- **不给「首次初始化须在主线程」加强制断言**：失败已经响亮（池线程读 `Application.*` 抛 `UnityException`），
  断言只是把同一条错误提前。`MessageManager` 的 `MainThreadGuard` 是「静默出错」才需要的。
- **不动 `Destroy()` 对 Provider 的处置**：`IFileProvider` **不是** `IDisposable`（10 个成员里没有），
  `Destroy` 不调 Dispose 不漏资源。
- **不给同步 API 做「自动切回主线程」**：那会改变 `.AsTask().GetAwaiter().GetResult()` 的阻塞语义，
  而同步面的存在意义就是「调用方自己承担阻塞」。

**未决**：

- **File 缺异步删除原语**：Save 的 `DeleteSlotAsync` / `MoveSlotAsync` 因此被迫在主线程做同步 IO
  （已记在 Save README 的「已知限制」）。属跨模块 API 变更，该单独走一轮计划——本轮只指认归属，不重复立项。
- **`MobileFileProvider.CheckStreamingExists` 的忙等仍未在真机验证**：门面（`FileManager.Exists`）已拒绝
  「移动端 + Streaming」这个组合，但 Provider 是公开类型、可以直接使用，那条 `while (!request.isDone) { }`
  仍在。**未决的是「要不要给 Provider 层也加兜底」**（改成有界等待？），本轮只如实标注风险。
  **桌面环境的实测数据**：审计时的红基线跑完只用了 16.1 秒，即那条自旋在桌面 + `file://` 下**会完成**——
  已知证据只到这里，移动端（Android `jar:file://` / iOS）无路径可验。
- **两处如实登记的覆盖空白**（不是「以后补」，是**测不了**）：
  1. `ConsoleFileProvider` 的 12 个公开方法（5 abstract + 7 virtual）零覆盖——仓内无派生类。
  2. `MobileFileProvider` 的 Streaming 私有路径（`CheckStreamingExists` / `ReadStreamingText` /
     `ReadStreamingBytes` / `GetStreamingUrl`）零覆盖——仓内无移动设备路径，任何替身都会结构性绕开真实链路。
     这正是 `CHANGELOG` 记过的教训：8 个 fixture 全注入 `TempFileProvider`，于是「域根解析是否碰了 Unity API」
     这一整类缺陷在替身上不可见。
- **`FileRootsTimingTests` 的命名空间与同目录其余 fixture 不一致**（`XFramework.XFile.Tests` vs
  `XFramework.XFileManager.Tests`）：纯命名问题，等下次动那批测试时顺手统一。
