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

> 本清单由模块 README 搬入（`e613867`，2026-09-30 文档分层第五批）：README 只留使用方要知道的边界
> （「面板资源不会自动卸载」→ 其 `## 已知限制`），「建了什么 / 还差什么」属维护向。四项未做于
> 2026-10-01 逐项复核——三项判不纳入框架（转入下方「已评估未采纳与未决」），一项拆分后保留。

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

- [ ] **遮罩挖洞（引导 / 聚焦用）** —— 给 `UIMaskStyle` 加区域语义（如 `Rect? Cutout` + 要不要射线
  穿透），把 `EnsureMaskInstance` 的「单张满屏 `Image`」改成可留洞的构成（多图拼接，或自绘
  `Graphic` 重写 `OnPopulateMesh`；**不用 shader**——仓内零 shader / 材质资产，UPM 包引入 shader 会
  加重资产面），复用现有的引用计数句柄与 `UISorting.SystemOrder = 32000` 预留带（它的注释本来就写着
  「新手引导挖洞层」）。
  **只做原语，不做引导编排**——步骤机 / 触发器 / 进度持久化是 GamePlay 架构，按 `CLAUDE.md`
  「不预设 GamePlay 架构」不属于本框架（2026-10-01 复核时从原「UI 引导层」拆出，编排那半转「不做」）。

> 「面板资源真释放」那条的**边界**——需显式调 `UnloadPanelAssetAsync`、没有按 LRU 自动卸载——是使用方要知道的，留在 README 的 `## 已知限制`，此处不复述。

## 已评估未采纳与未决

（2026-10-01 **第一轮**，定向审计 `UIManager.cs`（静态门面）及其生命周期相邻面——`IUIManager` 契约、
`UIManagerImpl` 的派发/回收路径、消费方（Editor 工具 / Sample / 模块自引用）与 24 个 fixture 的覆盖现状。
判据 A–F 逐类扫过，**1 条高危 + 4 条中高**：① 帧通路无异常隔离（一个坏面板打死整条每帧通路，见 CHANGELOG）；
② `Time.timeScale = 0` 静默冻结整条 UI 通路且模块内零文档；③ 门面记账与调度器实况脱节（`UpdateManager.Clear()`
之后永久失联）；④ `Dispose` 半拆窗口的注释承诺只兑现一半；⑤ `Destroy` 一律 Dispose 实例与 `SetInstance`
的所有权声明相反。本轮**碰了行为**（①②③ 已修，经用户裁定；隔离语义取「停更该面板」；② 只补文档）。
第三方对照**本轮未做**（外部对标物路径未定位），改用仓内对照：帧驱动异常隔离 ↔ `InputManager.PulseFrame`；
`Destroy` 与注入实例 ↔ Config（不 Dispose）/ Localization、Asset（一律 Dispose）；记账脱节 ↔ Asset 的「代际号 +
作废在途」。D2 的重载决议结论已按纪律**写复现程序编译验证**（见下最后一条）。

**已评估未采纳**（理由在本文件；使用方要知道的边界同时写进了模块 README 的对应小节）：

- **不改门面的探测豁免政策本身**：`IsOpen<T>` / `GetPanel<T>` / `GetTopPanel` / `CopyPanels` / `CopyPanelsInLayer`
  未初始化时照抛是**有意**的（实现在取之前要先剪枝，属操作而非纯读），`UIFacadeProbeTests` 把这条分界钉住。
  本轮只订正了把它描述成「Query 区一律不调守卫」的注释——错的是注释，不是代码。
- **不给 `IsLayerInteractive` / `IsLayerVisible` 加门面转发**：它们不在 `IUIManager` 里，属未公开的诊断面，
  不是 A2 缺口（判据是「接口声明了但门面够不到」）。将来若要公开，走完整的「接口 + 转发 + 完备性测试」三步。
- **不在 `SetInstance` 路径消费 `PanelFactoryFactory`**：该钩子是测试专用，`Initialize` 消费即清、`Destroy`
  兜底复位，两条都有用例；「设了钩子又走 SetInstance」在测试纪律下不会跨 fixture 泄漏（见 `UIPanelFactoryInjectionTests`）。
- **不锁 `Destroy` 一律 Dispose 注入实例的行为**（它是有意为之的终局语义，README「实例所有权」已写明），
  也不再为它加断言——该行为是否要改成「遵循 `_ownsInstance`」仍是未决（见下），先不用测试把它钉死。
- **不给门面加「驱动是否仍在调度器里」的自检**（例如要求 `UpdateManager` 提供 `IsRegistered`）：本轮的最小修法
  是「每次都真的 Register」（`UpdateManager.Clear` 那条），已覆盖同一条失联；引入查询 API 要动 Update 模块的公开面，
  收益与代价不成比例（若将来第二条路径再踩到，这条要重新评估）。

**未决**：

- **`IUIManager : IDisposable` 可被第三方绕过门面直接 `Dispose()`**：门面记账随之脱节（`_instance` 仍指向已释放的
  实现）。改接口形状（去掉 `IDisposable` 或改名）对第三方实现是**源码破坏性变更**，本轮只记文档，不动。
- **自定义 Tip / HUD provider 运行期注入拿不到 `SetUIRoot`**：`IUITipProvider.SetUIRoot` 的文档写着「在
  `UIManager.Initialize` 时自动调用」，而经 `SetTipProvider` / `SetHudProvider` 在初始化**之后**注入的实现不会收到
  该调用（只有门面自建的默认实现会）。两条修法——注入时补调 `SetUIRoot(UIRoot)`（更贴合接口意图）或文档写明
  「自行读 `UIManager.UIRoot`」——都需要先裁定，且第一条是行为变更。
- **`AllPanelsClosedMessage` 无条件发布**：控制器拦下部分关闭（或根本没有面板）时它照发，于是「事件面」说
  「全关了」而「查询面」（`OpenCount`）显示还有面板。消息名表达的是「全部关闭这一次动作完成」还是「现在一个
  都没有了」，需要一次语义裁定——本轮只把它记在这里。
- **`Dispose` 半拆窗口的语义**：注释声明窗口内的门面调用「按半拆状态执行」，实际前半段是**全量放行**
  （重入 `OpenAsync` 会造出不被回收的孤儿面板），尾部两行才转为抛「尚未初始化」。真正的「半拆」需要一个
  `_disposing` 状态位（与「未初始化」区分开），代价是每个入口都要多一个分支；本轮只如实记录。

**已实测的重载决议结论**（防下一轮重新论证）：`UIManager.Subscribe` 的三个 `Action<T>` 重载下，
**无类型 lambda**（`Subscribe(m => { })`）是编译错误 CS0121（不是静默挑一个），带显式参数类型的
`Subscribe((PanelOpenedMessage m) => { })` 正常编译——该结论由临时探针文件在本仓真实程序集上编译得出
（带负向控制：同一文件里的带类型调用无告警），**不是纸面推理**。故三条重载的并存不构成 D2 缺陷。

（2026-10-01 **同日追加：四项「未做」复核**。起因是用户找不到 README 里那几项待做（它们随 `e613867`
搬进了本文件）——借机逐项判定「仍缺能力 / 已有原语可表达 / 不该由本框架做」，三项据此转「不纳入」，
一项拆分后留在上面 roadmap 的 `### 未做`。使用方要知道的做法与边界写进 README 新增的 `## 设计取舍`。）

- **不内建列表虚拟化**：`UIBinder` / `UIPanelBinding` 都是「一属性 → 一控件」，无任何集合 / 索引绑定；
  对象池的池化对象是面板 / HUD / Tip 实例而非列表条目（`AssetPanelFactory.cs:23-47`），仓内零滚动 / 列表代码。
  判据：列表是**叶子组件**，与面板生命周期、统一调度、排序空间、遮罩没有一处耦合（对照 Tip / HUD / 遮罩
  都是跨切面基础设施，故内建），而各项目的条目类型 / 分页 / 选中 / 布局差异极大。替代路径见 README
  `## 设计取舍 → 列表虚拟化由项目自建`。
- **不做「跨场景引用的自动检测」**：原「场景切换安全」项只有这一句描述，且方向与本仓约定冲突——检测只能
  靠反射扫用户对象图（本仓反射仅用于 Type 驱动的 API 边界与配置元数据提取），「自动处理」更是替项目决定
  生命周期策略（架构，不是基础设施）。切场景的两条正解（UIRoot 常驻 + `CloseAllAsync()` 收口 / 每场景一根
  走 `UIRootNode` 成对生命周期）写进 README `## 设计取舍`；「多根不预设」是既有裁定（`UIRootNode.cs:80-82`）。
- **不内建 UI 特效层**：`UIManager.UIRoot` 与 `UISorting.SystemOrder` 都是公开 API，`Layer_HUD` / `Layer_Tip`
  建容器的写法就在仓内（`UIHudManager.cs:365-379`、`Tip/UITipManager.cs:187-199`）——自建特效 Canvas 是十几行
  的事（配方在 README `## 设计取舍`）。内建必须替项目决定相机类型 / `RenderTexture` / 是否挡输入的结构，
  拆掉的概率大于用上的概率。
