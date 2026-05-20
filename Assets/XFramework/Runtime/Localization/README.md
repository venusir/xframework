# XFramework / Localization 模块

## 概述

XFramework 本地化模块提供多语言文本管理能力。本模块**只提供框架层**，不关心数据来源，使用者可通过任意方式（Luban、JSON、CSV 等）生成数据字典后注入。

**语言标识使用 `string`**，无限制扩展。常见约定值：`"zh_Hans"`, `"zh_Hant"`, `"en"`, `"ja"`, `"ko"`，也可自定义如 `"vi"`, `"fr"`, `"th"`。

## 架构设计

```
Runtime/Localization/
├── ILocalizationManager.cs           # 接口（可替换实现）
├── LocalizationManager.cs            # 静态外观（常用入口）
├── LocalizationManagerImpl.cs        # 默认实现
├── LocalizationBootstrapNode.cs      # 启动加载节点
└── README.md
```

## 快速使用

### 1. 初始化

```csharp
var data = new Dictionary<string, string>
{
    ["ui_btn_start"] = "开始游戏",
    ["ui_btn_exit"]  = "退出游戏",
};
LocalizationManager.Initialize("zh_Hans", data);
```

### 2. 获取文本

```csharp
string text = LocalizationManager.Get("ui_btn_start");         // "开始游戏"
string fmt  = LocalizationManager.GetFormat("ui_damage", 50);  // 带参数格式化
```

### 3. 切换语言

```csharp
// 注入其他语言数据
LocalizationManager.SetLanguageData("en", englishData);
LocalizationManager.SetLanguageData("ja", japaneseData);

// 使用者可自定义任意语言标识
LocalizationManager.SetLanguageData("vi", vietnameseData);
LocalizationManager.SetLanguageData("fr", frenchData);

// 切换语言
LocalizationManager.SetLanguage("en");
```

### 4. 语言切换事件

```csharp
LocalizationManager.OnLanguageChanged += lang =>
{
    Debug.Log($"语言已切换至: {lang}");
};
```

### 5. UI 自动刷新（建议在项目层实现）

```csharp
using UnityEngine;
using UnityEngine.UI;
using XFramework.XLocalization;

/// <summary>
/// 项目层实现的本地化 Text 组件。
/// 挂载到带 Text 组件的 GameObject 上，设置 Key 后自动在语言切换时刷新。
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Text))]
public class LocalizedText : MonoBehaviour
{
    [SerializeField] private string _key;
    private Text _text;

    private void Awake() => _text = GetComponent<Text>();

    private void OnEnable()
    {
        LocalizationManager.OnLanguageChanged += OnLanguageChanged;
        UpdateText();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(string lang) => UpdateText();

    private void UpdateText()
    {
        if (_text != null && !string.IsNullOrEmpty(_key))
            _text.text = LocalizationManager.Get(_key);
    }
}
```

> **说明**：XFramework 不内置 UI 绑定组件，以此避免 TMPro 等不必要依赖，使用者按需在项目层实现。

---

## 与 Luban 配合使用

> Luban 是独立的配置生成工具，**不属于 XFramework 的一部分**，需由使用方在项目层自行安装和配置。

### 安装 Luban

```bash
dotnet tool install -g luban
```

### 桥接代码示例

```csharp
public static class LubanLocalizationBridge
{
    public static void Load(string lang, Func<string, string> valueGetter)
    {
        // 从 Luban 生成的表读取数据后注入
        // LocalizationManager.SetLanguageData(lang, data);
    }
}
```

### 与 Bootstrap 集成

```csharp
var node = NodeFactory.Create<LocalizationBootstrapNode>();
node.SetInitData("zh_Hans", chineseData);
// 或直接调用 LocalizationManager.Initialize("zh_Hans", data);
```

---

## 设计原则

- **数据源无关** — 不强制绑定任何配置工具（Luban、CSV、JSON 等）
- **语言无限制** — `string` 标识，使用者可自由扩展
- **轻量级** — 纯 Dictionary 查找，无外部依赖
- **可替换** — 通过 `ILocalizationManager` 接口可替换为自定义实现
- **零侵入** — 不使用本模块的项目零负担

## 依赖

- 无外部依赖（`LocalizationBootstrapNode` 依赖 `UniTask`，框架层已提供）