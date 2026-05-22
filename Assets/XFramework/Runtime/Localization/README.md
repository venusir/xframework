# XFramework / Localization 模块

## 概述

XFramework 本地化模块提供多语言文本管理功能。通过 `ILocalizationManager` 接口抽象，支持初始化时注入默认语言数据，运行时按需异步加载目标语言、LRU 缓存管理，以及格式化文本和全局占位符替换。

**命名空间**: `XFramework.XLocalization`

**语言标识**: 使用 `string` 类型（如 `"zh_Hans"`, `"en"`, `"ja"`），可自定义任意标识。

## 架构设计

```
Runtime/Localization/
├── ILocalizationManager.cs        # 本地化管理器公共接口
├── LocalizationManager.cs         # 静态外观（全局入口）
├── LocalizationManagerImpl.cs     # 默认实现（LRU 缓存）
├── LocalizationBootstrapNode.cs   # 启动节点（注册到 Bootstrap 管线）
└── LanguageSwitchNode.cs          # 异步语言切换节点（内部）
```

## 核心机制：LRU 缓存

`LocalizationManagerImpl` 内存中最多缓存 **4 种语言**的数据。当前语言和回退语言始终保留，不被淘汰；其余按 LRU（最近最少使用）淘汰。

- 每次 `SetLanguageData` 或 `SetLanguage` 都会将目标语言标记为"最近使用"
- 调用 `SwitchLanguageAsync` 加载新语言时，如果缓存已满 4 种，淘汰最早加载的非当前/非回退语言
- 缓存命中时语言切换为同步（零 GC），未命中时通过 YooAsset 异步加载 JSON

## 快速使用

### 1. 初始化

初始化时需注入默认语言和对应数据：

```csharp
using XFramework.XLocalization;
using System.Collections.Generic;

// 方式一：通过 Bootstrap 自动初始化（推荐）
// LocalizationBootstrapNode 在 Phase 90 初始化

// 方式二：手动初始化
var defaultData = new Dictionary<string, string>
{
    { "ui_main_title", "主菜单" },
    { "ui_play_button", "开始游戏" },
};
LocalizationManager.Initialize("zh_Hans", defaultData);

// 方式三：注入自定义实现
LocalizationManager.SetInstance(myLocalizationManager);
```

### 2. 获取本地化文本

```csharp
// 通过 key 获取当前语言的文本
string text = LocalizationManager.Get("ui_main_title");

// 格式化文本（内部使用 string.Format）
string goldText = LocalizationManager.GetFormat("ui_player_gold", currentGold, maxGold);

// 判断 key 是否存在
bool exists = LocalizationManager.ContainsKey("ui_settings_title");
```

找不到 key 时先回退回退语言，仍找不到则返回 key 本身，方便调试。

### 3. 切换语言（异步加载）

已缓存的切换是同步的（零等待），未缓存的自动异步加载：

```csharp
using Cysharp.Threading.Tasks;

// 异步切换——已缓存则同步完成，否则加载 JSON 文件
await LocalizationManager.SwitchLanguageAsync("ja");

// 带取消令牌
var cts = new CancellationTokenSource();
await LocalizationManager.SwitchLanguageAsync("en", cts.Token);
```

切换前可通过 `HasLanguage` 检查是否已缓存：

```csharp
if (LocalizationManager.HasLanguage("ja"))
{
    // 已缓存，可直接同步切换
    LocalizationManager.SetLanguage("ja");
}
```

### 4. 同步切换（仅已缓存语言）

当语言数据已在缓存中时，可直接同步切换：

```csharp
// 前提：HasLanguage("en") == true
LocalizationManager.SetLanguage("en");
```

若语言未缓存，`SetLanguage` 会抛出 `InvalidOperationException`。这种情况下应使用 `SwitchLanguageAsync`。

### 5. 语言资产路径配置

语言数据 JSON 文件的 YooAsset 地址模板，默认为 `"localization/lang_{0}"`：

```csharp
// 默认值
// LocalizationManager.LanguageAssetPath == "localization/lang_{0}"

// 自定义路径
LocalizationManager.LanguageAssetPath = "i18n/{0}";

// 切换时自动拼接："i18n/ja" → AssetManager.LoadAsync<TextAsset>("i18n/ja")
await LocalizationManager.SwitchLanguageAsync("ja");
```

### 6. 手动注入语言数据

```csharp
var enData = new Dictionary<string, string>
{
    { "ui_main_title", "Main Menu" },
    { "ui_play_button", "Play" },
};
LocalizationManager.SetLanguageData("en", enData);
```

注入后该语言即进入缓存，`HasLanguage("en")` 返回 `true`。

### 7. 全局占位符

通过全局占位符，可以在所有本地化文本中自动替换命名变量（如玩家名、等级等），无需每次调用 `GetFormat` 传递参数：

```csharp
// 注册全局占位符
LocalizationManager.SetPlaceholder("PlayerName", "张三");
LocalizationManager.SetPlaceholder("GuildName", "传奇公会");

// 本地化文本（JSON 文件中）
// "ui_welcome": "欢迎回来，{PlayerName}！"
// "ui_guild_info": "{GuildName} - 等级 {GuildLevel}"

// Get 时自动替换
string welcome = LocalizationManager.Get("ui_welcome");
// → "欢迎回来，张三！"

// 与 GetFormat 混用：先替换占位符，再执行 string.Format
string info = LocalizationManager.GetFormat("ui_guild_info", "5");
// → "传奇公会 - 等级 5"
```

**占位符替换规则：**
- 语法：`{Key}`（`{` + 占位符名称 + `}`）
- 替换在 `string.Format` 之前执行，两者可安全混用
- 未注册的占位符保持原样输出
- 未设置任何占位符时无 GC 开销（快速路径跳过）

**管理占位符：**

```csharp
// 更新占位符值（如玩家改名）
LocalizationManager.SetPlaceholder("PlayerName", "李四");

// 移除单个占位符
LocalizationManager.RemovePlaceholder("PlayerName");

// 判断占位符是否存在
bool exists = LocalizationManager.HasPlaceholder("PlayerName");

// 清空所有占位符（如退出登录）
LocalizationManager.ClearPlaceholders();
```

### 8. 语言切换事件

```csharp
LocalizationManager.OnLanguageChanged += lang =>
{
    Debug.Log($"语言已切换为: {lang}");
};
```

### 9. 当前状态

```csharp
string current = LocalizationManager.CurrentLanguage;   // 如 "ja"
string fallback = LocalizationManager.FallbackLanguage;  // 如 "zh_Hans"
bool initialized = LocalizationManager.IsInitialized;    // true
```

## 本地化数据格式

### JSON 文件结构

语言数据文件为 `TextAsset`，内容为扁平的 JSON 键值对对象：

```json
{
    "ui_main_title": "メインメニュー",
    "ui_play_button": "ゲーム開始",
    "ui_gold_format": "ゴールド: {0}",
    "error_connection": "接続エラー"
}
```

文件放置路径需匹配 `LanguageAssetPath`（默认为 `Resources` 目录下的 `localization/lang_ja.json` 等）。

### JSON 解析策略

模块内使用自定义的轻量 JSON 解析器（`LanguageSwitchNode.ParseJson`），仅支持 `"string": "string"` 的简单格式，无需引入 Newtonsoft.Json 或其他第三方库。如果 JSON 含嵌套结构或数组，需替换为完整 JSON 库。

## 节点系统集成

### LocalizationBootstrapNode

启动节点，在 Bootstrap 管线 **Phase 90** 执行。使用前需调用 `SetInitData` 注入默认语言和数据：

```csharp
var bootstrapNode = new LocalizationBootstrapNode();
bootstrapNode.SetInitData("zh_Hans", defaultLanguageData);
// 添加到启动队列
```

节点销毁时自动调用 `LocalizationManager.Destroy()` 清理缓存。

### LanguageSwitchNode

内部节点（`LeafNode`, `ILoadable`），由 `LocalizationManager.SwitchLanguageAsync` 创建并执行：

1. 检查缓存——已命中则直接同步切换
2. 缓存未命中——通过 `AssetManager.LoadAsync<TextAsset>` 加载 JSON 文件
3. 解析 JSON → 注入缓存 → 同步切换
4. 支持 `CancellationToken` 取消加载

## 设计原则

- **接口可替换** — 通过 `ILocalizationManager` 接口，可替换底层实现
- **按需加载** — 初始化仅注入默认语言，其他语言首次切换时异步加载
- **LRU 缓存** — 最多 4 种语言驻留内存，当前+回退始终保留
- **零外部依赖** — JSON 解析自实现，不依赖 Newtonsoft.Json
- **静态外观** — `LocalizationManager` 提供全局入口，任意位置可调用
- **格式化支持** — 支持 `string.Format` 语法的参数化文本
- **全局占位符** — 支持 `{Key}` 语法全局替换，减少重复传参

## 依赖

- `XFramework.XCore` — 节点系统（`EntityNode`, `LeafNode`, `IBaseNode`）
- `XFramework.XAsset` — 通过 `AssetManager` 加载语言 JSON 文件
- `XFramework.XLoader` — `ILoadable`, `LoadProgress` 接口
- `UniTask`（框架层已提供）