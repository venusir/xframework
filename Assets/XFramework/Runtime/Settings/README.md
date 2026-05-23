# SettingsManager — 强类型游戏设置管理器

## 概述

`XFramework.XSettings` 提供了一套以**强类型 + JSON 文件持久化 + 响应式通知**为核心的游戏设置管理方案。第三方项目引入后，只需定义自己的 `[Serializable]` 设置类，即可获得完整的加载/保存/重置/订阅能力。

## 设计理念

| 原则           | 说明                                                                           |
| -------------- | ------------------------------------------------------------------------------ |
| **强类型**     | 编译期类型安全，IDE 智能提示，告别 `GetFloat("key")` 的魔法字符串              |
| **JSON 文件**  | 基于 Unity 内置 `JsonUtility`，可读可调试，天然支持版本迁移                    |
| **不自动保存** | 调用方显式调用 `Save()`，避免频繁 I/O——适合「设置面板关闭时一次性保存」的场景  |
| **响应式通知** | 通过 `Observe` / `ObserveField` + R3 的 `DistinctUntilChanged`，字段不变不刷新 |
| **可替换后端** | `ISettingsStore` 接口允许替换为加密存储、PlayerPrefs 或远程云存档              |
| **多类型共存** | 内部按 `Type` 索引，支持同时管理 `GameSettings`、`EditorSettings` 等           |

## 快速开始

### 1. 定义设置类

```csharp
using System;
using UnityEngine;

[Serializable]
public class GameSettings
{
    public AudioSettings audio = new();
    public GraphicsSettings graphics = new();
}

[Serializable]
public class AudioSettings
{
    public float masterVolume = 1f;
    public float musicVolume = 0.8f;
    public float sfxVolume = 1f;
}

[Serializable]
public class GraphicsSettings
{
    public int resolutionWidth = 1920;
    public int resolutionHeight = 1080;
    public bool fullscreen = true;
    public int qualityLevel = 2;
}
```

### 2. 初始化

```csharp
using XFramework.XSettings;
using UnityEngine;

public class GameBootstrap : MonoBehaviour
{
    void Awake()
    {
        SettingsManager.Initialize<GameSettings>(
            Application.persistentDataPath + "/settings.json"
        );
    }
}
```

### 3. 读写设置

```csharp
// 读取
var settings = SettingsManager.Settings<GameSettings>();
float volume = settings.audio.masterVolume;

// 修改
settings.audio.masterVolume = 0.5f;

// 持久化
SettingsManager.Save<GameSettings>();
```

### 4. UI 响应式绑定

```csharp
// 监听整个设置对象
SettingsManager.Observe<GameSettings>(s =>
{
    musicSlider.value = s.audio.musicVolume;
});

// 精确监听单个字段（DistinctUntilChanged 保证值不变不触发）
SettingsManager.ObserveField<GameSettings, float>(
    s => s.audio.masterVolume,
    volume => audioMixer.SetFloat("MasterVolume", Mathf.Lerp(-80f, 0f, volume))
);
```

### 5. 全局消息订阅

```csharp
using XFramework.XReactive;

MessageManager.Subscribe<SettingsChangedMessage>(msg =>
{
    if (msg.SettingsType == typeof(GameSettings))
        Debug.Log("GameSettings 已变更");
});
```

## API 参考

### SettingsManager（静态外观）

| 方法                                                           | 说明                   |
| -------------------------------------------------------------- | ---------------------- |
| `Initialize<T>(string filePath, Func<T> defaultFactory?)`      | 用 JSON 文件路径初始化 |
| `Initialize<T>(ISettingsStore store, Func<T> defaultFactory?)` | 用自定义存储后端初始化 |
| `Destroy()`                                                    | 释放所有管理器         |
| `Settings<T>()`                                                | 获取当前设置对象引用   |
| `Apply<T>(T settings)`                                         | 替换整个设置并通知     |
| `Save<T>()`                                                    | 保存到持久层           |
| `Load<T>()`                                                    | 从持久层重新加载       |
| `Reset<T>()`                                                   | 重置为默认值并删除文件 |
| `Observe<T>(Action<T>)`                                        | 订阅整个设置变更       |
| `ObserveField<T, TField>(Func<T,TField>, Action<TField>)`      | 订阅单个字段变更       |
| `GetStore<T>()` / `SetStore<T>(store)`                         | 获取/替换存储后端      |

### ISettingsStore（存储后端接口）

| 方法                       | 说明             |
| -------------------------- | ---------------- |
| `T Load<T>()`              | 从持久层加载     |
| `void Save<T>(T settings)` | 保存到持久层     |
| `bool Exists()`            | 是否有已保存数据 |
| `void Delete()`            | 删除持久化数据   |

内置实现：`JsonFileStore(string filePath)`

### ISettingsManager\<T\>（管理器接口）

与 `SettingsManager` 静态方法一一对应，可用于依赖注入场景。

### SettingsChangedMessage

`readonly struct` 消息，通过 `MessageManager` 发布。包含 `SettingsType`（发生变更的设置类型）。

## 高级用法

### 自定义默认值

```csharp
SettingsManager.Initialize<GameSettings>(
    Application.persistentDataPath + "/settings.json",
    () =>
    {
        // 根据设备性能动态决定默认画质
        int defaultQuality = SystemInfo.graphicsMemorySize > 4096 ? 3 : 1;
        return new GameSettings
        {
            graphics = new GraphicsSettings { qualityLevel = defaultQuality }
        };
    }
);
```

### 自定义存储后端

```csharp
// 加密存储示例（示意，需自行实现加密逻辑）
public class EncryptedFileStore : ISettingsStore { /* ... */ }

SettingsManager.Initialize<GameSettings>(
    new EncryptedFileStore(path, encryptionKey)
);
```

### 运行时切换存储后端

```csharp
// 从 JSON 文件切换到 PlayerPrefs
SettingsManager.SetStore<GameSettings>(new PlayerPrefsStore());
SettingsManager.Save<GameSettings>();
```

## 文件结构

```
Runtime/Settings/
├── ISettingsStore.cs              # 存储后端接口
├── JsonFileStore.cs               # 默认 JSON 文件存储
├── ISettingsManager.cs            # 管理器接口
├── SettingsManagerImpl.cs         # 默认实现
├── SettingsManager.cs             # 全局静态外观
├── Messages/
│   └── SettingsChangedMessage.cs  # 变更消息
└── README.md
```

## 避免 GC

- `SettingsChangedMessage` 使用 `readonly struct`，避免堆分配
- `ObserveField` 内部使用 R3 的 `DistinctUntilChanged()` 避免无效回调
- 不自动保存，避免不必要的字符串分配与 I/O
- 静态外观方法全为值类型或引用传递，无装箱

## 与项目其他模块的对比

| 特性     | LocalizationManager      | InputManager                | SettingsManager            |
| -------- | ------------------------ | --------------------------- | -------------------------- |
| 模式     | 静态外观 + 接口 + 实现   | 静态外观 + 接口 + 实现      | ✅ 一致                     |
| 初始化   | `Initialize(data)`       | `Initialize()`              | `Initialize<T>(path)`      |
| R3 封装  | N/A                      | `ObserveXxx`                | `Observe` / `ObserveField` |
| 消息通知 | `LanguageChangedMessage` | `DeviceConnectedMessage` 等 | `SettingsChangedMessage`   |