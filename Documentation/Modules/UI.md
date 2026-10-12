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
├── IUIHudProvider.cs          # HUD 提供者接口
├── UIHudManager.cs            # HUD 管理器（Attach/Detach/Update 驱动，internal sealed）
├── UILayers.cs                # 推荐层级常量（Background/Default/Popup/Top/Mask）
├── UISorting.cs               # 排序空间单点定义（层级带、HUD/Tip/系统保留带、钳制）
├── UIPopupPosition.cs         # 弹窗定位纯函数（UIPopupDirection / UIPopupAlign 两个枚举同文件）
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
    ├── UIDialogPanel.cs       # 对话框基类：结果通道（随关闭兑现、回池置白纸）+ internal 结果通道接口
    ├── UIHudItem.cs           # HUD 项基类（3D 世界坐标跟踪、目标丢失自动回收）
    ├── UIRootNode.cs          # 场景 Canvas 载体（初始化 UIManager）
    └── UISafeArea.cs          # 安全区适配（推荐挂在 UIRoot 上）
```

## 沿革与已否决形状

- **`UIManager` 曾短暂分组过**（`467c64d`），未发布即撤销——它是「门面保持扁平」这条规则的直接先例。
- **锚定弹窗（2026-10-11，第二轮）**：`UIManager.ShowPopupAsync` + `UIManager_PopupCatcher` 落地。
  **先跑了探针再动手**——「布局驱动的尺寸在本轮布局跑完之前读到的仍是上一次的值」这条前提
  此前只是推理，而它是本入口存在理由的一半。探针落成了守卫用例（`UIPopupLayoutTests`），
  **实测结论：前提成立**（`ContentSizeFitter` 驱动的宽度设 300 → 改成 500 → 立刻读仍是 300、
  强制布局后才是 500）。用例的失败消息写的是「若这条红了意味着什么、下一步该做什么」，
  而不只是「期望 X 得到 Y」——它红的时候要能直接回答「那这个入口还要不要做」。
  四条取向：摆之前强制布局（且**同步**，故不会先闪一帧在错位置）；可用区域取 `Screen.safeArea`
  而非全屏（桌面无差别、移动端不摆进刘海）；返回**面板**而非结果（与 `ShowDialogAsync` 的不对称
  写进了两边文档）；捕获层与遮罩**同形但独立**。
  **捕获层不复用遮罩的理由**已从源码坐实：`ApplyMaskStyle` 每次 `ShowMask` 都重设排序与颜色、
  **以最后一次为准**（遮罩是单实例、不按持有者分身份），共用会让「模态要 0.5 变暗」与
  「下拉要全透明捕获」互相覆盖。**层级取 `PanelOrder(layer, 0)`**＝`layer × 32`，严格夹在
  「`layer−1` 层最高面板」（`layer×32−1`）与「`layer` 层第一个面板」（`layer×32+1`）之间；
  序号 0 在 `RestampSortingOrders` 里从不分配给面板，那个槽位本就是层容器自己的位置（容器无图形，不争）。
  **一处实现期改动**：计划里捕获层单开一个文件，实际放进 `UIManagerImpl.cs` 的 `#region Popup Click
  Catcher`——它需要「点击→关面板」「面板关闭→释放」「Dispose→销毁」三个接线点，全在 impl 上，
  单开会为 60 行付三处 plumbing；遮罩就是同一位置的先例。
  **顺带发现一处疑似既有缺陷**（遮罩没有 `GraphicRaycaster`），记在下面的「未决」里。
  **本轮不做**：`ShowPopupAsync` 只在打开时摆一次，**不跟随锚点移动**（写进 README 的已知行为边界）；
  实际采用的方向没从入口暴露（`async` 不能带 `out`，但 `CalculatePosition` 是纯函数，调用方重算即可）。
- **对话框（2026-10-11）**：`UIDialogPanel<TResult>` + `UIManager.ShowDialogAsync` 落地。缘起是一轮
  「通用弹窗」评估，它先纠正了一个归类错误——**「弹窗」在本模块其实是两个形态**，此前被当成一个：
  **锚定弹窗**（下拉 / 右键菜单，要贴着控件摆，几何由 `UIPopupPosition` 补上）与**模态对话框**
  （确认框 / 详情面板，要的是遮罩与结果回传）。后者此前能拼出来，但四件必须互相配合的事散在四处文档里，
  且**配错了全都不报错**：遮罩层配错 → 对话框被自己的遮罩盖住；遮罩不释放 → 此后全黑且点不动；
  结果配方放错位置 → 等待方永久挂起。本轮把四件收成一个入口。
  **设计经过两次修订，都是被事实推着走的**：
  ① 初版打算封掉 `OnClose`、另给 `OnCloseCore`，把兜底写在 `OnClose` 里；坐实
  `UIManagerImpl.Dispose` 同步回池全部面板、**刻意不走关闭路径**（不 await 关闭动画）之后推翻，
  改挂回池点——那是关闭与 `Dispose` 唯一的交点。封钩子随之变得不必要，**子类契约得以与
  `UIPanelBase` 完全一致**。
  ② 随后又发现「置白纸」与「兑现」必须分处两个时机：合并到回池点会让**在 `OnOpen` 里自我关闭的面板**
  （仓内既有形状，`SelfClosingOnOpenPanel`）既回收又清空，随后才开始的等待落在一个全新的挂起任务上。
  现为 `BeginDialogCycle`（`InstantiatePanelAsync` 取出实例时）+ `CompleteOnRecycle`（`RecyclePanel` 的
  `finally` 里，与 `UpdateFaulted = false` 同一位置、同一理由）。
  **顺带修了两处已发布文档里的错误用法**（结果配方放在按钮回调里、遮罩层用默认值）——它们独立于本功能，
  是同一个「散着写就会错」的病灶。
- **锚定弹窗（第二轮，未做）**：`ShowPopupAsync<T>`（开面板 + 强制布局 + 量尺寸 + `UIPopupPosition` 定位），
  外加**自建独立不可见捕获层**做「点外部关闭」——**不能用 `ShowMask`**：它是单实例引用计数、
  只管显示与否不管外观，与「模态调暗」会抢同一个 `Image`，`HideMask()` 更是清掉全部持有。
  **前置阻塞**：「开完面板后 `rect.size` 还是旧值」目前是**推理、未实测**，而仓内零
  `ForceRebuildLayoutImmediate` / `LayoutRebuilder` / `ForceUpdateCanvases` 先例——框架从没有过
  「量一个新实例的已布局尺寸」这个需求。**先跑 15 分钟探针**（带 `ContentSizeFitter` 的预制体，
  实例化后立刻读 `rect.size` vs 强制布局后读）再决定做不做；相等则「该建」的论据整个塌掉。
- **`UIPopupPosition`（2026-10-11）来自一轮第三方 UI 方案 `FairyGUI` 的评估**（评估全文见
  `Documentation/FairyGUI.md`——那轮「该不该接成 UI 后端」的结论、共存的两条硬约束、输入系统那道关
  都在那里，本文不复述）。那轮的动作是逐条筛
  「它的控制逻辑有什么值得借鉴到本模块」——**筛完只有弹窗定位一条够格**；同一轮另有六项对照本仓全胜
  （模态遮罩引用计数、`CloseAllAsync` 不关遮罩、纯静态工具不进接口、options 而非全局静态、
  `UpdateManager` 而非自持 LateUpdate、排序空间与 HUD/Tip/特效共享），故都不借鉴。
  **没有照抄它的定位实现**（`GRoot.cs` 的 `GetPoupPosition`，注意上游把方法名拼错了）：它只有右侧收敛、
  间隙硬编码 `-1`、翻上去仍溢出时再做一次 `xx += size.x / 2` 的二次调整且与右对齐叠加。本仓改为
  **两轴都收敛 + `offset` 可配 + 显式分支处理「弹窗大于屏幕」**，并把「锚点贴屏幕左缘 + 宽弹窗」
  写成回归用例。
  同一轮还**重推了「不内建列表虚拟化」**（见下），结论不变，但它给出了两条新理由。
- **「不内建列表虚拟化」的重推（2026-10-11）**：原判据「列表是叶子组件 + 各项目差异极大」**结论仍然成立**，
  但范围比字面窄——它论证的是「不内建**控件**」，没有单独论证「不给**窗口计算**」。补上的理由是：
  **可复用的只有等高情形的几行算术；真正费时的不等高与不定布局无法一般化**。上游给了佐证：FairyGUI
  的虚拟列表文档明写「单行/单列布局**可支持不等高**」（`GList.cs:1428`），Flow 布局按等高算（`:1172`），
  循环列表在 Flow 布局下**直接报错**（`:1456`）——**连它也是靠砍布局空间换来的**，而「砍哪种布局」
  是项目自己的决定。
  **附带两条**：① 循环列表**从未被单独评估过**（本文此前全文没出现过它），它是跟着「列表虚拟化」
  一起被打包放走的——重推结论对它同样适用；② `Roadmap.md` §四 把这条写成「UI **否决**」，与本文的
  「已评估未采纳」、README 的「设计取舍」两处用词不一致（**本文这处是准的**）。
- **遮罩射线缺陷：已实测并修复（2026-10-11）**。原条目记在下方「未决」，判据是「UGUI 源码 + 代码不对称，
  但**跑不了 Unity 验证**」——那一轮**低估了可测性**：`EventSystem.RaycastAll` 遍历的是全局 raycaster 列表，
  **既不需要输入模块也不需要 `EventSystem.current`**，只建一个空 `EventSystem` 就能跑真射线。
  真正的阻碍只有「测试壳没有 `EventSystem`」这一条夹具前提，而它是可以自己搭的。
  **实测结论**：修复前，屏幕正中（遮罩铺满全屏）命中数为 **0**；去掉修复那一行则新用例立刻红、加回即绿。
  **两处根因同时坐实**：既点不关，**也挡不住下方输入**（后者原先只是顺带推测）。
  **留给下一轮的三条**：① 这是 `ModuleAudit` 判据 **D3「测试的假绿」**的教科书样本——`ClickToClose_PopsTopPanel`
  一直绿，因为 `Button.onClick.Invoke()` 绕开了射线管道，而前提说明就写在 `UIPopupTests` 头部；
  **「验接线」与「验管道」是两件事，两者都要有**。② 写射线用例的两个夹具坑：
  **根 `RectTransform` 必须显式摆成屏幕区域 `[0,w]×[0,h]`**（batchmode 下 Overlay 根画布不会自动撑开它，
  不摆则锚点算出的坐标落进负半轴，`GraphicRaycaster` 判越界后静默返回 0 命中），且
  **`FakePanelFactory` 造的面板没有 `Graphic`**（只有 Canvas + raycaster），**对面板做射线断言必然为空**。
  ③ 行为变更已写入 README 与 CHANGELOG：遮罩**从此真正吃点击**，且 `clickToClose` 开启时会有悬停变色
  （`Button` 默认 `ColorTint`）——这是修复带出的、第三方可见的副作用。

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
  > **2026-10-02 更新（本条已落地，不再属于「未采纳」）**：诊断窗口的 UI 页签需要回读层级状态，于是按上面写好的
  > 三步公开——`IUIManager` +2（`IsLayerVisible` / `IsLayerInteractive`，探测型读）、`UIManager` +2 转发、
  > `UIFacadeCompletenessTests` 自动覆盖；`Tests/Runtime/UI/FakeUIManager.cs` 同步补了两个成员（它对接口新增成员
  > 一向是编译期点名）。破坏性变更只对第三方 `IUIManager` 实现者成立，已进 CHANGELOG。
- **不在 `SetInstance` 路径消费 `PanelFactoryFactory`**：该钩子是测试专用，`Initialize` 消费即清、`Destroy`
  兜底复位，两条都有用例；「设了钩子又走 SetInstance」在测试纪律下不会跨 fixture 泄漏（见 `UIPanelFactoryInjectionTests`）。
- **不锁 `Destroy` 一律 Dispose 注入实例的行为**（它是有意为之的终局语义，README「实例所有权」已写明），
  也不再为它加断言——该行为是否要改成「遵循 `_ownsInstance`」仍是未决（见下），先不用测试把它钉死。
- **不给门面加「驱动是否仍在调度器里」的自检**（例如要求 `UpdateManager` 提供 `IsRegistered`）：本轮的最小修法
  是「每次都真的 Register」（`UpdateManager.Clear` 那条），已覆盖同一条失联；引入查询 API 要动 Update 模块的公开面，
  收益与代价不成比例（若将来第二条路径再踩到，这条要重新评估）。

**未决**：

- ~~遮罩接不到点击~~ —— **2026-10-11 已实测并修复**（原条目判「跑不了 Unity 验证」，那一步是错的：
  真射线只需一个空 `EventSystem`）。结论与两条夹具坑见上方「沿革与已否决形状」末条。
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
- ~~`UIDiagnosticsTests` / `UIPanelQueryTests` 的零分配断言用的是恒真尺子~~ —— **2026-10-12 已处置**：
  两处站点改为「结构断言 + 放大粗尺子」两道防线（此前只记在 `Workflow.md` §六 的跨模块清单里，本模块
  自己没有落点）。结构侧：`UIStateSnapshot` 是纯值类型——实例字段全为值类型且 `readonly`、`readonly struct`，
  连 `IUIManager.GetState()` 的返回类型也钉住——**构造它不可能触发堆分配**；`UIManagerImpl._stack` 必须是
  具体 `List<UIPanelBase>`——这保证 `CopyPanels` 的 `buffer.AddRange(_stack)` 在运行期必然命中
  `ICollection<T>` 快速路径（源码注释自述的「不经枚举器，不分配」由此被钉住），声明成接口则连 `foreach`
  都会装箱枚举器。尺子侧：两处各放大 10 万次，实测堆增量均为 **0 B**。红证：注入 `new object()` →
  524,288 / 1,564,672 B；`UIStateSnapshot` 改类、`_stack` 改接口声明 → 两条结构断言各自命中。
  校准方法与模板见 `Documentation/Workflow.md` §六。

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
