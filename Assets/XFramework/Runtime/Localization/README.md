# XFramework / Localization 模块

## 概述

XFramework 本地化模块提供多语言文本管理功能。通过 `ILocalizationManager` 接口抽象，支持 JSON 格式的本地化配置表加载，运行时切换语言，以及通过 `LocalizedString` 组件实现 UI 文本的自动更新。

**命名空间**: `XFramework.XLocalization`

**语言标识**: 使用 `string` 类型（如 `"zh-CN"`, `"en-US"`, `"ja-JP"`），兼容 .NET `CultureInfo.Name` 格式。

## 架构设计

```
Runtime/Localization/
├── ILocalizationManager.cs        # 本地化管理器公共接口
├── LocalizationManager.cs         # 静态外观（全局入口）
├── LocalizationManagerImpl.cs     # 默认实现
├── LocalizedString.cs             # 本地化字符串组件（挂载到 UI）
├── LocalizedText.cs               # 本地化文本组件（旧版，已废弃）
├── LocalizedTextMeshProUGUI.cs    # TextMeshPro 本地化组件（旧版，已废弃）
└── LocalizationBootstrapNode.cs   # 启动节点（注册到 Bootstrap 管线）
```

## 快速使用

### 1. 初始化

```csharp
using XFramework.XLocalization;

// 方式一：通过 Bootstrap 自动初始化（推荐）
// BootstrapNode 添加 LocalizationBootstrapNode，自动处理初始化

// 方式二：手动初始化（需要先注册 JSON 数据源）
await LocalizationManager.InitializeAsync(jsonConfigData);
```

### 2. 获取本地化文本

```csharp
// 通过 key 获取当前语言的文本
string text = LocalizationManager.GetText("ui_main_title");
string text = LocalizationManager.GetText("ui_confirm_button");
string text = LocalizationManager.GetText("error_connection_timeout");

// 带默认值（当 key 不存在时返回默认值）
string text = LocalizationManager.GetText("unknown_key", "未知文本");

// 获取指定语言的文本
string text = LocalizationManager.GetText("ui_play", "en-US");
```

### 3. 切换语言

```csharp
// 切换到英文
await LocalizationManager.SetLanguageAsync("en-US");

// 切换到简体中文
await LocalizationManager.SetLanguageAsync("zh-CN");

// 获取当前语言
string currentLang = LocalizationManager.CurrentLanguage;

// 查询所有可用语言
var languages = LocalizationManager.GetAvailableLanguages();
```

### 4. 检查 Key 是否存在

```csharp
bool exists = LocalizationManager.HasKey("ui_settings_title");
bool enExists = LocalizationManager.HasKey("ui_settings_title", "en-US");
```

### 5. 添加/更新本地化条目

```csharp
// 添加或更新一个本地化条目
LocalizationManager.SetEntry("new_key", new Dictionary<string, string>
{
    { "zh-CN", "新条目" },
    { "en-US", "New Entry" },
    { "ja-JP", "新しいエントリ" }
});
```

### 6. 格式化文本

```csharp
// 带占位符的本地化文本（支持 string.Format 语法）
string text = LocalizationManager.GetFormattedText("ui_player_gold", gold, amount);
// JSON 数据: { "ui_player_gold": { "zh-CN": "金币: {0} / {1}" } }
```

## 本地化数据格式

### JSON 配置表结构

```json
{
  "ui_main_title": {
    "zh-CN": "主菜单",
    "en-US": "Main Menu",
    "ja-JP": "メインメニュー"
  },
  "ui_play_button": {
    "zh-CN": "开始游戏",
    "en-US": "Play",
    "ja-JP": "ゲーム開始"
  },
  "ui_gold_format": {
    "zh-CN": "金币: {0}",
    "en-US": "Gold: {0}",
    "ja-JP": "ゴールド: {0}"
  }
}
```

## UI 绑定

### LocalizedString 组件

将 `LocalizedString` 组件挂载到 UI 元素上，即可自动响应语言切换：

```csharp
// 在 GameObject 上挂载 LocalizedString 组件
var localizedString = uiText.AddComponent<LocalizedString>();
localizedString.Key = "ui_main_title";

// 语言切换时，UI 文本会自动更新
await LocalizationManager.SetLanguageAsync("en-US");
// 组件文本自动变为 "Main Menu"
```

## 节点扩展方法

实现了 `ILocalizable` 接口的节点可以使用扩展方法：

```csharp
public class MyNode : EntityNode, ILocalizable
{
    protected override void OnStart()
    {
        base.OnStart();

        // 获取本地化文本
        string title = this.GetLocalizedText("ui_main_title");
        string button = this.GetLocalizedText("ui_confirm_button");

        // 格式化文本
        string goldText = this.GetFormattedText("ui_player_gold", currentGold, maxGold);
    }
}
```

## 设计原则

- **接口可替换** — 通过 `ILocalizationManager` 接口，可替换底层实现
- **静态外观** — `LocalizationManager` 提供全局入口，任意位置可调用
- **语言标识标准化** — 使用 `string` 类型，兼容 .NET `CultureInfo`
- **UI 自动绑定** — `LocalizedString` 组件自动响应语言切换，无需手动刷新
- **JSON 驱动** — 配置数据以 JSON 格式维护，方便策划编辑和版本管理
- **格式化支持** — 支持带参数的格式化文本（`string.Format` 语法）

## 依赖

- `XFramework.XCore` — 节点系统依赖
- `XFramework.XLoader` — 启动加载流程依赖
- `UniTask`（框架层已提供）