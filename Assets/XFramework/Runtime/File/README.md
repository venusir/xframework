# XFileManager — 跨平台文件系统

## 概述

`XFileManager` 是 XFramework 的跨平台文件系统模块，提供统一的 API 屏蔽不同平台以及硬件的物理路径差异。通过 **路径域枚举** 抽象不同平台的文件读写，自动选择最合适的平台实现。

**支持平台：**

| 平台                    | 内置实现                          | 说明                                                               |
| ----------------------- | --------------------------------- | ------------------------------------------------------------------ |
| Windows / Linux / macOS | `DesktopFileProvider`             | 使用 `System.IO`，性能最优                                         |
| iOS / Android           | `MobileFileProvider`              | StreamingAssets 读取通过 `UnityWebRequest`，其他域使用 `System.IO` |
| Xbox / PS5 / Switch     | `ConsoleFileProvider`（抽象基类） | 第三方需继承并实现平台 SDK API                                     |
| WebGL                   | 需自定义 `IFileProvider`          | 无内置实现                                                         |

## 核心设计理念

本模块借鉴了以下成熟框架的设计思想，并进行了整合与进一步优化：

| 框架/引擎         | 借鉴点                                      | 本模块对应                                                     |
| ----------------- | ------------------------------------------- | -------------------------------------------------------------- |
| **Godot**         | `user://` / `res://` 虚拟路径概念           | `FileDomain` 枚举：`AppData`、`Streaming`、`Cache`、`SaveData` |
| **Unreal Engine** | `IPlatformFile` 接口抽象、`FPaths` 路径工具 | `IFileProvider` + `GetPhysicalPath()`                          |
| **LÖVE2D**        | `love.filesystem` 极简 API 设计             | `FileManager` 静态外观——4 个核心方法                           |
| **YooAsset**      | 不同平台读取方式策略封装                    | `DesktopFileProvider` vs `MobileFileProvider`                  |
| **自定义增强**    | 可插拔加解密层、Console SaveData 独立域     | `ICryptoProvider` + `FileDomain.SaveData`                      |

---

## 快速开始

### 1. 依赖项

本模块依赖 [UniTask](https://github.com/Cysharp/UniTask)。如果项目尚未安装，请手动添加：

```
// 通过 Package Manager → Add package from git URL：
https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask
```

### 2. 初始化

```csharp
using XFramework.XFileManager;

// 可选：使用自定义 Provider（Console 等平台必须手动传入）
FileManager.Initialize();  // 桌面/IOS/Android 可自动选择

// 可选：启用加解密保护
FileManager.SetCryptoProvider(new XorCryptoProvider());
```

> **注意：** 未调用 `Initialize()` 时，首次调用任何文件操作将自动选择默认实现并初始化。Console 平台除外——因为 Console 没有内置实现，必须手动传入自定义 `IFileProvider`。

### 3. 基本使用

```csharp
// 写入文本文件
await FileManager.WriteAllTextAsync(FileDomain.AppData, "saves/player.json", jsonContent);

// 读取文本文件
string json = await FileManager.ReadAllTextAsync(FileDomain.AppData, "saves/player.json");

// 检查文件是否存在
if (FileManager.Exists(FileDomain.Streaming, "config/game_settings.csv"))
{
    var bytes = await FileManager.ReadAllBytesAsync(FileDomain.Streaming, "config/game_settings.csv");
}
```

---

## 路径域说明 (`FileDomain`)

`FileDomain` 是本模块最核心的概念，它通过四个枚举值屏蔽了不同平台、不同硬件的物理路径差异。**游戏业务代码只需关心"数据类型"，而无需关心"数据在哪个路径"。**

> **设计灵感**：借鉴 Godot 引擎的虚拟路径机制（`user://` / `res://`），使用枚举提供编译期类型安全，避免字符串路径混淆。

### 总览表

| 枚举值      | 映射的 Unity API                                           | 读写权限   | 生命周期              | 典型用途                              |
| ----------- | ---------------------------------------------------------- | ---------- | --------------------- | ------------------------------------- |
| `AppData`   | `Application.persistentDataPath`                           | ✅ 可读可写 | 应用安装期间持久      | 机器级配置、着色器缓存、崩溃日志      |
| `Streaming` | `Application.streamingAssetsPath`                          | 📖 只读     | 随包发布，不可修改    | JSON/CSV 配置表、Lua 脚本、初始数据库 |
| `Cache`     | `Application.temporaryCachePath`                           | ✅ 可读可写 | ⚠️ 系统可随时清理      | HTTP 响应缓存、临时资源、调试截图     |
| `SaveData`  | 桌面/移动 → `persistentDataPath`<br>Console → 平台存档 API | ✅ 可读可写 | 卸载后保留（Console） | 玩家存档、进度、内购记录              |

---

### `AppData` — 应用持久化数据

**映射**：`Application.persistentDataPath`

这是应用程序的**本地可读写数据目录**，适合存放无需云同步、无需账号绑定的数据。

**各平台物理路径示例**：

| 平台                | 物理路径                                                   |
| ------------------- | ---------------------------------------------------------- |
| Windows             | `C:\Users\<用户名>\AppData\LocalLow\<公司名>\<产品名>`     |
| macOS               | `~/Library/Application Support/<公司名>/<产品名>`          |
| Linux               | `~/.config/unity3d/<公司名>/<产品名>`                      |
| iOS                 | `/var/mobile/Containers/Data/Application/<UUID>/Documents` |
| Android             | `/data/data/<bundle-id>/files`                             |
| Xbox / PS5 / Switch | 本地应用数据目录（非存档专用路径）                         |

```csharp
// ✅ 适合放这里
await FileManager.WriteAllBytesAsync(FileDomain.AppData, "shader_cache/main_v3.bin", cacheData);
await FileManager.WriteAllTextAsync(FileDomain.AppData, "crash_logs/latest.log", logText);
await FileManager.WriteAllBytesAsync(FileDomain.AppData, "local_config.dat", config);

// ❌ 不适合放这里（Console 上卸载即丢，且无法云同步）
// await FileManager.WriteAllBytesAsync(FileDomain.AppData, "saves/slot0.dat", saveData);
```

> ⚠️ **重要**：在控制台平台（Xbox/PS5/Switch），`AppData` 依然有物理存储可用，但 **卸载应用时会被清除，且不会云同步**。需要跨设备持久化的数据请使用 `SaveData`。

---

### `Streaming` — 只读包内资源

**映射**：`Application.streamingAssetsPath`  
**权限**：所有平台**只读**

这是随应用包一同发布的资源目录，适合放置策划数据、Lua/Python 脚本、初始 SQLite 数据库等不需要运行时更新的静态文件。

**各平台读取方式**：

| 平台                    | 读取方式                            | 说明                                                                 |
| ----------------------- | ----------------------------------- | -------------------------------------------------------------------- |
| Windows / Linux / macOS | `System.IO.File` 直接读取           | 性能最优，无额外开销                                                 |
| iOS / Android           | `UnityWebRequest`                   | 包内文件无法用 `System.IO` 访问，**`MobileFileProvider` 已自动处理** |
| Xbox / PS5 / Switch     | 由第三方 `ConsoleFileProvider` 实现 | 通常需映射到 RomFS / Content 区域                                    |

```csharp
// ✅ 适合放这里（所有平台只读，框架自动适配读取方式）
var tableCsv = await FileManager.ReadAllBytesAsync(FileDomain.Streaming, "config/enemy_table.csv");
var luaCode = await FileManager.ReadAllTextAsync(FileDomain.Streaming, "scripts/init.lua");
var dbBytes = await FileManager.ReadAllBytesAsync(FileDomain.Streaming, "db/initial.sqlite");

// ❌ 不适合放这里（写入会失败）
// await FileManager.WriteAllTextAsync(FileDomain.Streaming, "config/new.csv", data);
```

> ⚠️ **常见误区**：`Streaming` 不是 "StreamingAssets" 的缩写——它的语义是"随包流式发布的内容"，不是"可流式写入的内容"。所有平台对此目录都是只读的。

---

### `Cache` — 临时缓存

**映射**：`Application.temporaryCachePath`  
**权限**：可读可写  
**⚠️ 警告**：此目录的文件**随时可能被操作系统清理**。

这是应用程序的临时工作目录，适合存放可以随时丢弃的中间结果。不要在此处存储任何需要持久化的重要数据。

```csharp
// ✅ 适合放这里（丢了没关系）
await FileManager.WriteAllBytesAsync(FileDomain.Cache, "http_cache/avatar_123.jpg", downloadedData);
await FileManager.WriteAllBytesAsync(FileDomain.Cache, "temp/screenshot_001.png", pngData);

// ❌ 不适合放这里（系统清理后就没了）
// await FileManager.WriteAllTextAsync(FileDomain.Cache, "player_progress.json", saveData);
```

> ⚠️ **为什么不能依赖 `Cache` 做持久化？**  
> 操作系统在磁盘空间不足时，或应用切换前台/后台时，可能会清理此目录。iOS 的 Caches 目录尤其不可靠——iCloud 备份不会备份 `temporaryCachePath` 的内容，但可能会在同步流程中间接清理它。

---

### `SaveData` — 跨平台存档专用域

这是本模块最重要的设计决策之一。**`SaveData` 在桌面/移动平台上等同于 `AppData`，但在控制台平台上映射到平台专用的存档存储 API**。

**各平台的实际映射**：

| 平台                    | 实际存储                                         | 关键特性                                      |
| ----------------------- | ------------------------------------------------ | --------------------------------------------- |
| Windows / Linux / macOS | `Application.persistentDataPath`（同 `AppData`） | 普通文件，无特殊行为                          |
| iOS / Android           | `Application.persistentDataPath`（同 `AppData`） | 普通文件，无特殊行为                          |
| **Xbox**                | XGameSave / Connected Storage                    | ✅ 云同步 ✅ Xbox Live 账号绑定 ✅ 卸载保留      |
| **PS5**                 | `sceSaveData` API                                | ✅ 云同步 ✅ PSN 账号绑定 ✅ 配额限制 ✅ 卸载保留 |
| **Switch**              | `nn::fs` 托管保存目录                            | ✅ 账号隔离 ✅ 系统级备份 ✅ 卸载保留            |

```csharp
// ✅ 所有平台统一的存档代码——控制台自动走平台 API
await FileManager.WriteAllTextAsync(FileDomain.SaveData, "saves/slot0.json", saveData);
await FileManager.WriteAllTextAsync(FileDomain.SaveData, "saves/autosave.json", autoSaveData);
await FileManager.WriteAllTextAsync(FileDomain.SaveData, "player_settings.json", settings);

// 多存档槽
for (int i = 0; i < 3; i++)
{
    string slot = $"saves/slot{i}.dat";
    if (FileManager.Exists(FileDomain.SaveData, slot))
    {
        var data = await FileManager.ReadAllBytesAsync(FileDomain.SaveData, slot);
        // 展示存档元信息……
    }
}
```

#### `SaveData` 与 `AppData` 的核心区别（Console 平台）

| 特性         | `AppData`（本地持久化） | `SaveData`（平台存档 API）     |
| ------------ | ----------------------- | ------------------------------ |
| **云同步**   | ❌ 不会自动同步          | ✅ Xbox Live / PSN 云端同步     |
| **账号隔离** | 可能共享                | 独立（每个账号独立存档）       |
| **系统备份** | ❌ 不会                  | ✅ 主机系统级备份               |
| **应用卸载** | ❌ 数据可能被清除        | ✅ 数据保留                     |
| **跨设备**   | ❌                       | ✅ 登录同一账号即可恢复         |
| **认证合规** | 无限制                  | ✅ 满足 Console TRC/XR 认证要求 |

> ⚠️ **为什么必须用 `SaveData` 而不是 `AppData` 存储档？**  
> 如果在 Console 上把存档写入 `AppData`，数据能正常读写、游戏不会崩溃——但玩家换一台主机或卸载重装后，存档就丢了。更严重的是，Console 认证（TRC/XR）会检查是否使用了平台存档 API，如果不使用可能会被拒绝上架。

---

### 选择决策树

```
开始
 │
 ├─ 数据是随包发布的静态文件？
 │   └─ 是 → FileDomain.Streaming（只读）
 │
 ├─ 数据需要跨平台存档 / 云同步 / 账号绑定？
 │   └─ 是 → FileDomain.SaveData
 │
 ├─ 数据丢失后不影响用户体验？（网络缓存、临时文件）
 │   └─ 是 → FileDomain.Cache
 │
 └─ 都不是（机器级配置、缓存、日志）
     └─ FileDomain.AppData
```

### 常见误区

| 误区                          | 后果                                               | 正确做法                                        |
| ----------------------------- | -------------------------------------------------- | ----------------------------------------------- |
| ❌ 把玩家存档放到 `AppData`    | Console 上卸载即丢、无法云同步、可能被认证拒绝     | 使用 `SaveData`                                 |
| ❌ 把 `Streaming` 当作可写目录 | 移动/Console 上写入失败                            | 只读取 `Streaming`，写入用 `AppData` 或 `Cache` |
| ❌ 用 `Cache` 存重要数据       | 系统清理后数据丢失                                 | 使用 `AppData` 或 `SaveData`                    |
| ❌ 所有数据都放 `SaveData`     | Console 上 `SaveData` 有配额限制（通常 100MB~1GB） | 只把真正的存档放 `SaveData`                     |

---

## 加密支持

模块内置了基于 XOR 的轻量加解密实现，适合存档防篡改等场景。

```csharp
// 启用默认加密
FileManager.SetCryptoProvider(new XorCryptoProvider());

// 使用自定义密钥
FileManager.SetCryptoProvider(new XorCryptoProvider("my-complex-key-2024"));

// 禁用加密
FileManager.SetCryptoProvider(null);
```

如需强加密（AES 等），实现 `ICryptoProvider` 接口即可：

```csharp
public class AesCryptoProvider : ICryptoProvider
{
    public byte[] Encrypt(byte[] plainData) { /* AES 加密 */ }
    public byte[] Decrypt(byte[] cipherData) { /* AES 解密 */ }
}
```

---

## 架构概览

```
┌──────────────────────────────────────────────┐
│              FileManager (静态外观)             │
│  ReadAllTextAsync / WriteAllBytesAsync / ...  │
├──────────────────────────────────────────────┤
│  ICryptoProvider (可选加解密层)                 │
│  Encrypt() / Decrypt()                        │
├──────────────────────────────────────────────┤
│  IFileProvider (平台抽象层)                     │
│  ┌───────────────┬──────────────┬───────────┐ │
│  │DesktopProvider│MobileProvider│ConsolePro..│ │
│  └───────────────┴──────────────┴───────────┘ │
├──────────────────────────────────────────────┤
│  FileDomain 枚举 (路径域，对标 Godot 虚拟路径)   │
│  AppData / Streaming / Cache / SaveData       │
└──────────────────────────────────────────────┘
```

---

## 接入 Console 平台（Xbox / PS5 / Switch）

1. 继承 `ConsoleFileProvider` 并实现抽象方法：

```csharp
public class XboxFileProvider : ConsoleFileProvider
{
    public override byte[] ReadAllBytes(FileDomain domain, string relativePath) { /* Xbox GDK API */ }
    public override void WriteAllBytes(FileDomain domain, string relativePath, byte[] data) { /* Xbox GDK API */ }
    public override bool Exists(FileDomain domain, string relativePath) { /* Xbox GDK API */ }
    public override void Delete(FileDomain domain, string relativePath) { /* Xbox GDK API */ }
    public override string GetPhysicalPath(FileDomain domain, string relativePath) { /* 平台路径映射 */ }
}
```

2. 在应用启动时传入：

```csharp
#if UNITY_XBOX
FileManager.Initialize(new XboxFileProvider());
#endif
```

---

## 多平台账户隔离

`FileManager` 的默认实现在桌面/移动平台使用 `Application.persistentDataPath` 作为存储根目录，该路径按 **OS 登录用户** 隔离（如 `C:\Users\Lunta\...` vs `C:\Users\XiaoMing\...`）。

如需按 **平台账户**（Steam、PSN、Xbox Live 等）进一步隔离，第三方可通过自定义 `IFileProvider` 在 `GetPhysicalPath` 中注入平台账户 ID 子目录，对上层调用完全透明：

```csharp
public class SteamFileProvider : DesktopFileProvider
{
    public override string GetPhysicalPath(FileDomain domain, string relativePath)
    {
        var defaultPath = base.GetPhysicalPath(domain, relativePath);

        if (domain != FileDomain.SaveData)
            return defaultPath;

        // 在 SaveData 域下注入 Steam 账户子目录
        var steamId = SteamUser.GetSteamID().ToString();
        var root = base.GetPhysicalPath(domain, null);
        var relWithSteam = $"{steamId}/{relativePath?.TrimStart('/') ?? ""}";
        return Path.Combine(root, relWithSteam.TrimEnd('/'));
    }
}
```

初始化时替换默认 Provider：

```csharp
#if STEAMWORKS_ENABLED
FileManager.Initialize(new SteamFileProvider());
#else
FileManager.Initialize();
#endif
```

不同平台账户的文件将自动隔离到各自子目录，无需业务代码修改。

---

## 文件清单

| 文件                       | 说明                      |
| -------------------------- | ------------------------- |
| `FileDomain.cs`            | 路径域枚举定义            |
| `IFileProvider.cs`         | 平台文件提供者接口        |
| `ICryptoProvider.cs`       | 加解密提供者接口          |
| `XorCryptoProvider.cs`     | 基于 XOR 的轻量加解密实现 |
| `DesktopFileProvider.cs`   | 桌面平台文件提供者实现    |
| `MobileFileProvider.cs`    | 移动平台文件提供者实现    |
| `ConsoleFileProvider.cs`   | 控制台平台抽象基类        |
| `FileManager.cs`           | 跨平台文件管理器静态外观  |
| `FileManagerExtensions.cs` | 扩展方法（同步 API 等）   |
| `README.md`                | 本文件                    |

---

## 设计决策与性能考量

- **避免 Main Thread 卡顿：** 所有 IO 操作通过 `UniTask.RunOnThreadPool` 在子线程执行。
- **无 GC 分配热路径：** 枚举传递 `FileDomain`（值类型），`ICryptoProvider` 默认不启用，不加载时无额外开销。
- **Console 安全：** `ConsoleFileProvider` 为抽象类而非接口，便于未来在基类中添加通用实现而不破坏第三方子类。
- **扩展性：** 第三方可通过 `IFileProvider` 接口完全替换文件系统后端（如自定义加密 VFS 或远程存储后端）。