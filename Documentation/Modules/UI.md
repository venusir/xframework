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
