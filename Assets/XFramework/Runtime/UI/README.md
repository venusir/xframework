# XFramework / UI 模块

## 概述

XFramework UI 模块提供完整的 UI 面板管理功能。通过 `IUIManager` 接口抽象，支持面板打开/关闭、导航堆栈、模态遮罩、资源预加载缓存、层级排序以及与本地化模块的联动。所有面板预制体通过 YooAsset（`AssetManager`）异步加载，支持打开/关闭动画。

**命名空间**: `XFramework.XUI`

## 架构设计

```
Runtime/UI/
├── IUIManager.cs          # UI 管理器公共接口
├── UIManager.cs           # 静态外观（全局入口）
├── UIManagerImpl.cs       # 默认实现（面板字典、导航堆栈、资源缓存）
├── UIPanelBase.cs         # 面板基类（所有 UI 面板需继承）
├── UIRootNode.cs          # 场景 Canvas 载体（初始化 UIManager）
└── README.md              # 使用说明
```

## 核心概念

### 层级系统

使用 `int` 类型表示层级，数值越大越靠前。通过 `UIRootNode` 提供预设常量，也可自定义：

```
Background (0)   — 背景层（主界面背景、HUD）
Default (100)    — 默认层（大部分面板）
Popup (200)      — 弹出层（弹窗、确认框）
Top (300)        — 顶层（Toast、加载提示、系统消息）
Mask (500)       — 模态遮罩层
```

每个层级内部的面板通过 `sortingOrder` 自动排序（`sortingOrder = layer × 1000 + 序号`），后打开的面板排序值更大。

### 导航堆栈

支持 `PushAsync` / `PopAsync` / `BackToAsync` 导航模式：

- `PushAsync` — 压入新面板，当前面板失焦（OnBlur），新面板获得焦点（OnOpen）
- `PopAsync` — 弹出栈顶面板并关闭，恢复上一个面板焦点（OnFocus）
- `BackToAsync` — 回到指定类型面板，中间经过的面板依次关闭
- `HasPrevious` — 导航堆栈中是否还有上一个面板

### 模态遮罩

通过 `ShowMask` / `HideMask` 创建全屏半透明遮罩，阻止下方 UI 交互：

- 支持设置透明度（alpha 0-1）
- 支持点击关闭（clickToClose）—— 点击遮罩自动 Pop 栈顶面板
- 遮罩位于独立层级（默认 Mask 层），不影响面板排序

### 资源缓存

预加载面板预制体到内存缓存，后续 `OpenAsync` 时直接从缓存实例化：

```csharp
// 预加载——后续打开时不卡顿
await UIManager.PreloadAsync<SettingsPanel>("ui/panels/settings");

// 移除指定缓存
UIManager.UnloadAsset<SettingsPanel>();

// 清空所有缓存（切换场景时）
UIManager.ClearAssetCache();
```

## 快速使用

### 1. 场景设置

在场景中创建一个 `UIRootNode`：

1. 右键 → `GameObject` → `UI` → `Canvas` 创建 Canvas
2. 向 Canvas 添加 `UIRootNode` 组件（`Add Component → UIRootNode`）
3. Canvas 的 `Render Mode` 自动设为 `Screen Space - Overlay`

`UIRootNode` 的 `Awake` 中自动调用 `UIManager.Initialize(transform)`，`OnDestroy` 中自动清理所有资源。

也可以在代码中手动初始化：

```csharp
UIManager.Initialize(uiRootTransform);
```

### 2. 创建面板

所有 UI 面板需继承 `UIPanelBase`：

```csharp
using XFramework.XUI;

public class MainMenuPanel : UIPanelBase
{
    protected override async UniTask OnOpen(object userData)
    {
        // 面板打开逻辑：绑定按钮事件、初始化文本等
        var title = transform.Find("Title").GetComponent<TMP_Text>();
        title.text = "主菜单";
    }

    protected override async UniTask OnClose()
    {
        // 面板关闭逻辑：清理事件绑定、释放资源等
    }

    // 可选：打开动画
    protected override async UniTask PlayOpenAnimation()
    {
        var canvasGroup = GetComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        await canvasGroup.FadeIn(0.3f); // DOTween 或自定义
    }

    // 可选：关闭动画
    protected override async UniTask PlayCloseAnimation()
    {
        var canvasGroup = GetComponent<CanvasGroup>();
        await canvasGroup.FadeOut(0.2f);
    }

    // 可选：语言切换回调
    protected override void OnLanguageChanged(string lang)
    {
        // 刷新面板文本
    }
}
```

### 3. 打开/关闭面板

```csharp
using XFramework.XUI;

// 打开面板
var mainMenu = await UIManager.OpenAsync<MainMenuPanel>(
    "ui/panels/mainmenu",    // YooAsset 预制体地址
    layerDefault,             // 层级（可选，默认 100）
    userData                  // 自定义数据（可选，默认 null）
);

// 关闭面板（通过类型）
await UIManager.CloseAsync<MainMenuPanel>();

// 关闭面板（带关闭动画）
await UIManager.CloseAsync<MainMenuPanel>(immediate: false);

// 关闭面板（立即销毁，跳过动画）
await UIManager.CloseAsync<MainMenuPanel>(immediate: true);

// 面板关闭自身
await this.CloseSelfAsync();

// 查询面板状态
bool isOpen = UIManager.IsOpen<MainMenuPanel>();
var panel = UIManager.GetPanel<MainMenuPanel>(); // 未打开返回 null
```

### 4. 导航堆栈

```csharp
// 从主菜单进入设置——主菜单失焦，设置面板获得焦点
var settings = await UIManager.PushAsync<SettingsPanel>(
    "ui/panels/settings",
    layerDefault
);

// 从设置进入音效子面板
await UIManager.PushAsync<SoundPanel>("ui/panels/sound", layerDefault);

// 返回上一面板（关闭音效面板，恢复设置面板）
await UIManager.GoBackAsync();

// 或使用 PopAsync（等价于 GoBackAsync）
await UIManager.PopAsync();

// 直接从音效回到主菜单（中间的面板依次关闭）
await UIManager.BackToAsync<MainMenuPanel>();

// 检查是否可以返回
if (UIManager.HasPrevious)
{
    await UIManager.GoBackAsync();
}
```

### 5. 模态遮罩

```csharp
// 显示遮罩（半透明，不支持点击关闭）
UIManager.ShowMask(alpha: 0.5f);

// 显示遮罩（支持点击关闭——自动 Pop 栈顶）
UIManager.ShowMask(alpha: 0.3f, clickToClose: true);

// 隐藏遮罩
UIManager.HideMask();

// 查询遮罩状态
bool showing = UIManager.IsMaskShowing;
```

### 6. 关闭指定层级

```csharp
// 关闭 Default 层的所有面板
await UIManager.CloseLayerAsync(layerDefault);

// 关闭所有面板
await UIManager.CloseAllAsync();
```

### 7. 资源预加载

```csharp
// 游戏启动后预加载所有常用面板，后续打开零延迟
await UIManager.PreloadAsync<MainMenuPanel>("ui/panels/mainmenu");
await UIManager.PreloadAsync<SettingsPanel>("ui/panels/settings");
await UIManager.PreloadAsync<DialogPanel>("ui/panels/dialog");

// 场景切换时清理不用的缓存
UIManager.ClearAssetCache();
```

### 8. 语言切换联动

与 `LocalizationManager` 联动，语言切换时自动通知所有已打开面板刷新：

```csharp
// 注册语言切换回调
XLocalization.LocalizationManager.OnLanguageChanged += UIManager.OnLanguageChanged;

// 或手动触发刷新（不经过 LocalizationManager）
UIManager.OnLanguageChanged("ja");
```

面板中重写 `OnLanguageChanged` 方法：

```csharp
public class MainMenuPanel : UIPanelBase
{
    public TMP_Text titleText;

    protected override void OnLanguageChanged(string lang)
    {
        titleText.text = LocalizationManager.Get("ui_main_title");
    }
}
```

### 9. 事件监听

```csharp
// 面板打开事件
UIManager.OnPanelOpened += type => {
    Debug.Log($"Panel opened: {type.Name}");
};

// 面板关闭事件
UIManager.OnPanelClosed += type => {
    Debug.Log($"Panel closed: {type.Name}");
};

// 所有面板关闭事件
UIManager.OnAllPanelsClosed += () => {
    Debug.Log("All panels closed.");
};
```

### 10. 依赖注入

支持注入自定义 `IUIManager` 实现：

```csharp
// 注入自定义实现（可用于单元测试）
var mockManager = new MockUIManager();
UIManager.SetInstance(mockManager);
```

## 设计原则

- **接口可替换** — 通过 `IUIManager` 接口，可替换底层实现
- **静态外观** — `UIManager` 提供全局入口，任意位置可直接调用
- **层级灵活** — `int` 类型表示层级，第三方项目可自由定义常量扩展
- **导航堆栈** — 支持 Push/Pop/BackTo 导航，Blur/Focus 焦点管理
- **资源缓存** — 预加载面板预制体到缓存，后续打开时零加载延迟
- **动画支持** — `PlayOpenAnimation` / `PlayCloseAnimation` 可重写，支持 DOTween 等
- **多语言联动** — `OnLanguageChanged` 与 `LocalizationManager` 无缝集成
- **避免 GC** — 使用固定字典容量（8/4）、值类型遍历、List 复用，减少 GC 分配

## 依赖

- `XFramework.XAsset` — 通过 `AssetManager.InstantiateAsync` 加载面板预制体
- `UniTask`（框架层已提供）
- UGUI（`UnityEngine.Canvas`、`UnityEngine.UI.GraphicRaycaster`）
- 可选：`XFramework.XLocalization`（语言切换联动）