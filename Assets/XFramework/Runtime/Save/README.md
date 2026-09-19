# Save 存档模块

## 概述

`Save` 是 XFramework 的存档调度层，负责将数据快照（由 `DataManager.CreateSnapshot()` 生成）序列化后持久化到文件系统，以及从文件反序列化恢复到 `DataManager`。

**架构链：**

```
SaveManager (静态门面)
  └→ SaveManagerImpl (默认实现)
       ├→ DataManager.CreateSnapshot() / ApplySnapshot()   # 快照收集与恢复
       ├→ Serializer.Serialize() / Deserialize()            # 载荷序列化（线程池上执行）
       └→ FileManager.WriteAllBytesAtomicAsync() / ...      # 落盘（原子替换 + 一代备份）
```

加载路径上还有两道额外防线：**元数据侧车**（免去枚举时的全量反序列化）与**快照级版本门禁**（拒绝比当前客户端更新的存档）。

## 命名空间

`XFramework.XSave`

## 初始化

```csharp
// 使用默认实现（本地文件存储）
SaveManager.Initialize();

// 或传入自定义实现（如 Steam Cloud、PS5 SaveData API）
SaveManager.Initialize(() => new MySteamCloudSaveManager());

// 或带选项
SaveManager.Initialize(null, new SaveOptions
{
    CurrentVersion = 3,                              // 存档格式版本上限
    CryptoProvider = new XorCryptoProvider("my-key"),// 只加密 SaveData 域；见「加密」
});
```

需要自定义选项时改用引导阶段登记：`Bootstrap.Register(new SaveBootstrapStage(saveOptions))`
（登记顺序与默认组合的说明见 [Bootstrap 模块](../Bootstrap/README.md)）。
`SaveBootstrapStage` 在初始化后会执行一轮**恢复扫描**（见下）。

## 核心 API

门面成员与 `ISaveManager` 逐字对应；所有涉及 IO 的成员一律异步并以 `Async` 后缀结尾，**不提供同步版本**（同步 IO 会阻塞主线程）。

| 方法                                                                              | 说明                                             |
| --------------------------------------------------------------------------------- | ------------------------------------------------ |
| `SaveAsync(int slot)`                                                             | 保存当前游戏状态到指定槽位                       |
| `LoadAsync(int slot)`                                                             | 加载存档；失败抛异常                             |
| `TryLoadAsync(int slot)`                                                          | 加载存档；失败以 `SaveLoadResult` 状态回报       |
| `GetSlotMetasAsync()` / `GetSlotMetaAsync(int slot)`                              | 槽位元数据列表 / 单个槽位元数据                  |
| `DeleteSlotAsync(int slot)`                                                       | 删除指定槽位，返回是否确有删除                   |
| `DeleteAllSlotsAsync()`                                                           | 删除当前玩家上下文下的全部槽位，返回删除数量     |
| `SlotExistsAsync(int slot)`                                                       | 检查槽位是否存在（谓词语义，非法槽位返回 false） |
| `CopySlotAsync(int from, int to, bool overwrite)` / `MoveSlotAsync(...)`          | 槽位复制 / 移动                                  |
| `SetCurrentPlayer(string)` / `ClearCurrentPlayer()` / `CurrentPlayerId`           | 玩家上下文                                       |
| `GetAllPlayerIdsAsync()`                                                          | 列出所有含存档的玩家                             |
| `GetPlayerSlotMetasAsync(string playerId)`                                        | 查指定玩家的槽位（不切换当前上下文）             |
| `DeletePlayerAsync(string playerId)`                                              | 删除指定玩家的全部存档，返回删除数量             |
| `CurrentVersion` / `SetCurrentVersion(int)`                                       | 存档格式版本上限（见「版本门禁」）               |
| `IsBusy`                                                                          | 是否有写操作在进行                               |

带进度的重载：`GetSlotMetasAsync` / `GetPlayerSlotMetasAsync` / `DeleteAllSlotsAsync` / `DeletePlayerAsync` 各有一个接受 `IProgress<SaveReport>` 的重载（载荷为 `readonly struct`，`Progress` + `Description`）。

## 盘上文件布局

存档位于 `FileDomain.SaveData` 下；启用玩家隔离时在 `{playerId}/` 子目录中。一个槽位最多涉及四类文件：

| 文件                    | 内容                       | 生命周期                                                                       |
| ----------------------- | -------------------------- | ------------------------------------------------------------------------------ |
| `slot_N.save`           | 载荷（JSON）               | 槽位本体；由 `SaveAsync` 原子写入                                               |
| `slot_N.save.meta`      | 元数据侧车 + 载荷校验和    | 随载荷写入；**缺失时枚举自动回退全量解析**，并由恢复扫描或下次保存重建           |
| `slot_N.save.bak`       | 一代备份（上一次的载荷）   | 每次覆盖保存自然产生；用于加载时损坏回退与崩溃恢复                             |
| `slot_N.save.tmp`       | 原子写的中间文件           | 瞬态；只在替换过程中存在，残留即崩溃证据，由恢复扫描或删除路径清理             |

**删除语义**：删除一个槽位会连同它的全部配套文件一并清除（门面按 `slot_*` 一次枚举），所以备份只在「该槽位存在」期间有效。

### 启动恢复扫描

`SaveBootstrapStage` 初始化后执行一轮扫描，把存档目录收敛到一致状态：

- 载荷缺失但备份在 → 用备份还原载荷（替换流程崩溃后的最坏情况）
- 载荷存在 → 清掉 `.tmp` 残留
- 侧车缺失或不可解析 → 由载荷重建
- 载荷与备份都不在 → 清掉孤儿配套文件

**侧车校验和不符时刻意不重建**：那是「载荷可能已损坏」的信号，重建等于把它抹掉；此时保留原侧车，交给加载路径按损坏处理并回退到备份。

## 线程约定

- 文件 IO 由 Provider 在**线程池**上执行；载荷的序列化与反序列化同样在池线程上完成。
- 本模块的公开异步 API **在返回前都会切回主线程**，因此调用方在 `await` 之后可以安全地访问 Unity API 与 `DataManager`。
- 代价是这些方法依赖 PlayerLoop 泵，**禁止在主线程用 `.GetAwaiter().GetResult()` 同步阻塞等待**，否则会死锁。
- 唯一例外是内部的 `SaveManager.RecoverAsync`：它全程只调用文件系统原语与线程安全的 `Debug.Log`，**刻意不切回主线程**，因此启动管线中同步阻塞等待它也不会死锁。调用方若要写 `PipelineStageContext` 这类要求主线程的对象，须自行切回。
  - 这条成立的前提是文件原语**可从任意线程调用**（域根在主线程解析并缓存，见 `File/README.md` 的「线程契约」）——它曾经不成立：Provider 在池线程上解析域根会撞 Unity 的主线程限定，恢复扫描因此必崩。

## 加载结果与失败处理

`TryLoadAsync` 以状态回报而不抛异常——存档界面需要区分这些情况并给出不同反馈，把它们做成异常会迫使调用方用 try 表达正常分支：

| 状态                | 含义                                                         |
| ------------------- | ------------------------------------------------------------ |
| `Loaded`            | 主文件正常加载                                               |
| `LoadedFromBackup`  | 主文件不可用，已从一代备份恢复                               |
| `Migrated`          | 存档版本低于当前客户端，已按逐块迁移链升级后加载             |
| `Missing`           | 槽位不存在                                                   |
| `Corrupt`           | 主文件与备份均不可用（损坏、校验和不符、或数据块未全部恢复） |
| `VersionTooNew`     | 存档版本高于当前客户端，已整份拒绝                           |

`LoadAsync` 保留抛异常语义，两者共用同一实现。

**不完整加载会被拒绝**：只要有数据块未能恢复，整次加载即判定为失败并回滚内存——「部分块没恢复」等价于内存里少了一半数据，报告成功是危险的。

## 版本门禁

`SaveOptions.CurrentVersion`（或 `SaveManager.SetCurrentVersion`）声明当前客户端支持的存档格式版本上限：

- `SaveAsync` 把该值写进快照
- 加载时**高于**该值 → 整份拒绝并回报 `VersionTooNew`，且不退到备份（备份多半同样是新版本，而静默加载更旧的备份会无声丢掉玩家的新进度）
- **低于**该值 → 正常加载并回报 `Migrated`

数据块级别的迁移链由 Data 模块负责：`IDataBlock.DataVersion` + `IDataBlock.OnMigrate(object, int)`，在 `ApplySnapshot` 内自动执行。**快照级门禁与块级迁移互补**——前者挡住整体更新的存档，后者负责旧版本块的升级。

## 扩展点

### 自定义存档元数据

通过替换 `DataSnapshot.Factory` 并重写 `CreateMeta()`，可扩展 `SaveMeta` 和 `DataSnapshot` 配对字段：

```csharp
[Serializable]
public class MySnapshot : DataSnapshot
{
    public byte[] thumbnailPng;

    public override SaveMeta CreateMeta()
    {
        var meta = new MySaveMeta { thumbnailPng = this.thumbnailPng };
        meta.version = version;
        meta.timestamp = timestamp;
        return meta;
    }
}

public class MySaveMeta : SaveMeta
{
    public byte[] thumbnailPng;
}

// 初始化（一行，在首次调用 SaveManager 之前执行）
DataSnapshot.Factory = () => new MySnapshot();
```

`Factory` 自动提供反序列化类型推导（`Factory().GetType()`）、Meta 实例创建（`CreateMeta()`）与**侧车类型**（侧车经 `Factory().CreateMeta()` 取类型后序列化），无需额外配置。

### 自定义存储后端

实现 `ISaveManager` 接口并注册：

```csharp
public class MyCloudSaveManager : ISaveManager
{
    // 实现各方法，可接入 Steam Cloud、PlayFab 等
}

SaveManager.Initialize(() => new MyCloudSaveManager());
```

### 自定义序列化格式

通过 `Serializer.Register()` 注册自定义序列化器，`DataSnapshot` 的 `defaultFormat` 和 `DataBlockSnapshot` 的 `format` 字段已预留格式选择能力。

### 加密

`SaveOptions.CryptoProvider` 非空时经 `FileManager.SetCryptoProvider` 接线，**只作用于 `FileDomain.SaveData`**，不会连带加密 AppData / Cache。加密不削弱其他保障：`CryptoFileProvider` 本身实现了 `IAtomicFileProvider` 与 `IDirectoryProvider`，原子写与目录枚举照常可用。

两个必须知道的陷阱：

1. **切换加密状态会让存量存档立即不可读。** `XorCryptoProvider` 这类对称实现面对明文同样会「解密成功」而不抛异常，只是产出垃圾字节，最终表现为「存档已损坏」。开启加密或更换密钥前必须先迁移存量存档。
2. **XOR 不是可靠加密。** 框架自带的 `XorCryptoProvider` 只做混淆，能挡住随手改存档，挡不住有心人。防篡改应换成实现 `ICryptoProvider` 的真正算法（AES 等）。

## 依赖

- `XFramework.XData` - 数据块管理
- `XFramework.XSerialize` - 序列化
- `XFramework.XFileManager` - 文件读写
- `UniTask` - 异步操作

## 存档兼容建议

序列化器本身支持字段新增/删除的向前兼容（新字段取默认值，旧字段自动忽略）。当字段**语义变化**（改名、类型变更、默认值不适用）时，用 Data 模块的迁移链，而不是在 `OnLoad` 里手写：

```csharp
public class PlayerData : IDataBlock
{
    public int DataVersion => 2;      // 递增即触发迁移

    [Obsolete] public int gold;       // 旧字段保留，便于旧存档读入后迁移
    public int currency;

    public object OnMigrate(object saveData, int fromVersion)
    {
        // 按 fromVersion 逐级升级；返回值会传给下一级或最终 OnLoad
        if (fromVersion < 2)
        {
            gold = (int)saveData;
            return currency > 0 ? currency : gold;
        }
        return saveData;
    }

    public void OnLoad(object saveObj) { currency = (int)saveObj; }
}
```

存档**格式整体变更**（新增/删除数据块、改变快照结构）时，递增 `SaveOptions.CurrentVersion`：旧客户端会干净地拒绝新存档，而不是按块跳过导致「一半新数据、一半空着」的半加载。

避免在框架层面操作原始序列化数据（JSON/bytes），业务层在迁移链中自行兼容是最可靠的方式。

## 目录结构

```
Runtime/Save/
├── ISaveManager.cs        # 接口 + 工厂委托
├── SaveManager.cs         # 静态门面
├── SaveManagerImpl.cs     # 默认实现
├── SaveMeta.cs            # 存档元数据（含侧车字段与校验和）
├── SaveOptions.cs         # 初始化选项（格式版本、加密）
├── SaveReport.cs          # 进度载荷
├── SaveLoadResult.cs      # 加载状态与结果
├── SavePathUtility.cs     # 路径构造、槽位解析与标识校验
├── SaveIntegrity.cs       # 载荷校验和（FNV-1a 64）
└── README.md
```
