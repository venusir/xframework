# Save 存档模块

## 概述

`Save` 是 XFramework 的存档调度层，负责将数据快照（由 `DataManager.CreateSnapshot()` 生成）序列化后持久化到文件系统，以及从文件反序列化恢复到 `DataManager`。

**架构链：**
```
SaveManager (静态门面)
  └→ SaveManagerImpl (默认实现)
       ├→ DataManager.CreateSnapshot() / ApplySnapshot()
       ├→ Serializer.Serialize() / Deserialize()
       └→ FileManager.WriteAllBytesAsync() / ReadAllBytesAsync()
```

## 命名空间

`XFramework.XSave`

## 初始化

```csharp
// 使用默认实现（本地文件存储）
SaveManager.Initialize();

// 或传入自定义实现（如 Steam Cloud、PS5 SaveData API）
SaveManager.Initialize(() => new MySteamCloudSaveManager());
```

## 核心 API

| 方法                    | 说明                       |
| ----------------------- | -------------------------- |
| `SaveAsync(int slot)`   | 保存当前游戏状态到指定槽位 |
| `LoadAsync(int slot)`   | 从指定槽位加载存档         |
| `GetSlotMetas()`        | 获取所有存档的元数据列表   |
| `GetSlotMeta(int slot)` | 获取单个槽位元数据         |
| `DeleteSlot(int slot)`  | 删除指定槽位               |
| `DeleteAllSlots()`      | 删除所有槽位               |
| `SlotExists(int slot)`  | 检查槽位是否存在           |

## 存档文件位置

存档文件存储在 `FileDomain.SaveData` 下，文件名格式：`slot_{index}.save`

## 扩展点

### 自定义存储后端

实现 `ISaveManager` 接口并注册：

```csharp
public class MyCloudSaveManager : ISaveManager
{
    // 实现各方法，可接入 Steam Cloud、PlayFab 等
}

// 注册
SaveManager.Initialize(() => new MyCloudSaveManager());
```

### 自定义序列化格式

通过 `Serializer.Register()` 注册自定义序列化器，`DataSnapshot` 的 `defaultFormat` 和 `DataBlockSnapshot` 的 `format` 字段已预留格式选择能力。

## 依赖

- `XFramework.XData` - 数据块管理
- `XFramework.XSerialize` - 序列化
- `XFramework.XFileManager` - 文件读写
- `UniTask` - 异步操作

## 目录结构

```
Runtime/Save/
├── ISaveManager.cs        # 接口 + 工厂委托
├── SaveManager.cs         # 静态门面
├── SaveManagerImpl.cs     # 默认实现
├── SaveMeta.cs            # 存档元数据
└── README.md