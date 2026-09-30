# UI —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/UI/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/UI/
├── IUIManager.cs              # UI 管理器公共接口
├── UIManager.cs               # 静态外观（全局入口）
├── UIManagerImpl.cs           # 默认实现（面板字典、显示栈、资源缓存）
├── IUIPanelFactory.cs         # 面板实例来源接口（抽出来是为了测试不依赖 YooAsset）
├── AssetPanelFactory.cs       # 默认实现：经 AssetManager 加载预制体并复用其对象池
├── IUiHudProvider.cs          # HUD 提供者接口
├── UIHudManager.cs            # HUD 管理器（Attach/Detach/Update 驱动，internal sealed）
├── UILayers.cs                # 推荐层级常量（Background/Default/Popup/Top/Mask）
├── UISorting.cs               # 排序空间单点定义（层级带、HUD/Tip/系统保留带、钳制）
├── UIMaskStyle.cs             # 遮罩样式（层级/颜色/透明度/点击关闭）
├── UIMaskHandle.cs            # 遮罩引用计数句柄（readonly struct）
├── UIStateSnapshot.cs         # 状态快照（readonly struct，读取零分配）
├── README.md                  # 使用说明
├── Controller/
│   ├── IUIController.cs       # 调度控制接口（五阶段生命周期拦截）
│   ├── UIDefaultController.cs # 默认控制器（全部放行）
│   └── PreconditionChain.cs   # 前提条件链（链式组合异步校验条件）
├── Data/
│   ├── IViewModel.cs          # ViewModel 接口
│   ├── ViewModelBase.cs       # ViewModel 抽象基类
│   ├── UIPanelBinding.cs      # UI 绑定组件（挂载在 Panel Prefab 上，约定式绑定）
│   ├── UIBinder.cs            # UI 绑定工具（静态扩展方法，手动精确绑定）
│   ├── PanelOpenedMessage.cs  # 面板打开消息（readonly struct）
│   ├── PanelClosedMessage.cs  # 面板关闭消息（readonly struct）
│   └── AllPanelsClosedMessage.cs  # 全部面板关闭消息（readonly struct）
├── Tip/
│   ├── IUITipProvider.cs      # Tip 提供者接口
│   ├── UITipManager.cs        # Tip 管理器（实例化、容器、回池，internal sealed）
│   ├── UITipItem.cs           # 挂在 Tip 预制体上的组件（每帧推进动画）
│   └── UITipConfig.cs         # 显示配置（位置/颜色/时长/浮动距离/字号）
└── View/
    ├── UIViewBase.cs          # UI 控件抽象基类（Canvas/层级/OnUpdate），UIPanelBase 与 UIHudItem 的公共父类
    ├── UIPanelBase.cs         # 面板基类（所有 UI 面板需继承）
    ├── UIHudItem.cs           # HUD 项基类（3D 世界坐标跟踪、目标丢失自动回收）
    ├── UIRootNode.cs          # 场景 Canvas 载体（初始化 UIManager）
    └── UISafeArea.cs          # 安全区适配（推荐挂在 UIRoot 上）
```

## 沿革与已否决形状

- **`UIManager` 曾短暂分组过**（`467c64d`），未发布即撤销——它是「门面保持扁平」这条规则的直接先例。

## 已完成功能与未做（roadmap）

### 已完成

- [x] ✅ 基础面板管理 — OpenAsync / CloseAsync / IsOpen / GetPanel
- [x] ✅ 显示栈导航 — PushAsync / PopAsync / GoBackAsync / PopToAsync / PopToRootAsync / CanGoBack
- [x] ✅ 模态遮罩 — ShowMask / HideMask 支持透明度与点击关闭
- [x] ✅ 资源预热 — PreloadAsync / ForgetPreload / ClearPreloads（只清记账，不卸载资源）
- [x] ✅ 打开/关闭动画 — PlayOpenAnimation / PlayCloseAnimation 虚拟方法
- [x] ✅ 多语言联动 — OnLanguageChanged 与 XLocalization 集成
- [x] ✅ MVVM 绑定 — 通过 UIPanelBinding（约定式）+ UIBinder（精确式）+ ReactiveProperty 实现 View ↔ ViewModel 绑定
- [x] ✅ 调度控制 — 通过 IUIController + PreconditionChain 实现面板生命周期的 AOP 控制
- [x] ✅ 面板 OnUpdate — 由 UIManager 集中驱动，仅已打开且未暂停的面板执行更新
- [x] ✅ 临时提示 Tip — 扣血提示、浮动文字，支持世界坐标定位、渐隐动画、对象池复用
- [x] ✅ 世界空间 HUD — NPC名/血条/标记，3D坐标跟踪，目标丢失自动回收，独立Canvas渲染

### 未做

- [ ] 列表虚拟化 — 长列表的滚动复用（与 FairyGUI 的 `GList` 虚拟滚动同类能力）
- [ ] 场景切换安全 — 自动检测跨场景引用并处理
- [ ] UI 特效层 — 粒子特效、UI 上叠特效支持
- [ ] UI 引导层 — 新手引导的遮罩挖洞支持

> 「面板资源真释放」那条的**边界**——需显式调 `UnloadPanelAssetAsync`、没有按 LRU 自动卸载——是使用方要知道的，留在 README 的 `## 已知限制`，此处不复述。
