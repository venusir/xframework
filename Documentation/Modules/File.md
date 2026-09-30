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
