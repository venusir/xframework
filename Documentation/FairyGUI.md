# FairyGUI —— 评估与接入配方（维护侧）

> **收件人**：框架维护者（含「下一轮要不要重新论证这个」这个问题的回答者）。**不是**随包发布的使用文档。
> **使用方契约**在 `Assets/XFramework/Runtime/UI/README.md` 与本文件无关——本文件回答的是**框架侧要不要动**，
> 以及**一个项目若仍要用它，接缝在哪、坑在哪**。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。需要时整篇转发给使用方。

## 〇、核对状态（先读这一节）

本文件每条事实都带标记，**不要把 `[未实测]` 当成已验证**：

| 标记 | 含义 |
| --- | --- |
| `[上游源码核对]` | 读 `fairygui/FairyGUI-unity` 在**固定 tag / commit** 下的源码、`.asmdef`、`Packages/manifest.json` 得出。读的是**构建产物本身**（非文档、非二手描述），但本轮经 GitHub 读取、**无本地副本**；`git clone --branch 5.2.0 --depth 1` 即可复现 |
| `[本仓核对]` | 读本仓文件得出，可当场复现 |
| `[未实测]` | 只有文档或推理支撑，**没跑通过** |

**核对基准**：FairyGUI-unity **tag `5.2.0`**（2025-05-11）；`master` 末次提交 2026-02-03。
仓库 MIT、未归档、约 3.0k star。**其自身开发工程是 Unity `2022.3.17f1c1`**（`ProjectSettings/ProjectVersion.txt`），
README 只承诺「设计支持 5.6+，当前仅在 2018+ 测试过」。

> **一行都没在 Unity 里跑过 `[未实测]`。** 本文件全部结论来自读源码与本仓对照，没有做过探针工程。
> 唯一可能直接推翻结论的是 §3.1 的输入系统那道关——它**只要 30 分钟就能验掉**，且不验就不该往下走。

---

## 一、结论：边界画在哪

**框架侧零改动。不接成 `IUIManager` 的后端，也不做平级的管理层包。**

两条，理由不同：

1. **接成 `IUIManager` 后端 —— 没有接缝**（§2.1）。这不是工作量问题，是切分位置冲突：
   `UIManager` 的编排原语**每一条都是用 UGUI 对象实现的**，不是抽象。硬接的唯一办法是给每个
   FairyGUI 面板套一个带 Canvas 的 MonoBehaviour 空壳——**付了 UGUI 的全部代价，只为了满足类型约束**。
2. **平级写一层 FairyGUI 管理层 —— 技术可行，但不划算**（§2.3）。代价是 800–1500 行 + 永久维护第二套
   UI 栈 + 放弃 `UIManager` 已解的一堆问题，而换来的是 FairyGUI 相对 UGUI **唯一不可替代**的好处——
   「策划可以脱离 Unity 编辑 UI」。**用一个可替代的好处换一笔不可替代的债，不划算。**

**这条边界的具体收益**：本文件**可以被 FairyGUI 的版本演化淘汰**（整篇重写即可），而包内那份契约不会——
因为框架一个字都没为它改。

---

## 二、它到底是什么

### 2.1 不是「另一种面板」，是另一套完整栈 `[上游源码核对]`

| 轴 | 本仓 `UIManager` + UGUI | FairyGUI |
| --- | --- | --- |
| 渲染 | UGUI `Canvas` + `CanvasRenderer` | 逐对象 `MeshFilter` + `MeshRenderer`（`Core/NGraphics.cs:105-106`）+ 自建正交相机 `StageCamera`（`Core/StageCamera.cs:185-196`，仅渲染 `UI` 层、`depth = 1`、`clearFlags = Depth`） |
| 输入 | UGUI `GraphicRaycaster` | `Stage` 自行命中测试（`Core/Stage.cs:775-776` 经 `HitTestContext.cachedMainCamera` 射线） |
| 调度 | `UpdateManager` + 档位分桶 | `StageEngine : MonoBehaviour.LateUpdate()` → `Stage.inst.InternalUpdate()`（`Core/StageEngine.cs:19-20`），另有 `OnGUI`（文本输入） |
| 资源 | 预制体（`AssetManager.InstantiateAsync` → `GetComponent<T>()`） | **`.fui` 二进制包** + 图集（`UIPackage` 经 `ByteBuffer` 解析），**不是 GameObject** |
| 编辑器 | Unity 内置 | **独立 App**（美术 / 策划无需开 Unity） |
| 分发 | `com.unity.ugui`（Unity 内置） | **无 `package.json`，不是 UPM 包**；`Assets/Scripts` 需整目录 vendored 进使用方的 `Assets/` |

**为什么接不上**——`UIManager` 的每个编排动作都盯着 UGUI 对象 `[本仓核对]`：

| 编排动作 | 它实际做的事 | 换到 FairyGUI |
| --- | --- | --- |
| 加载面板 | `AssetManager.InstantiateAsync(path, parent)` → `go.GetComponent<T>()` | 面板是 `UIPackage.CreateObject(pkg, res)` 出的 `GObject`，**不是预制体实例** |
| 挂到层下 | `SetParent(Layer_{int} 的 Transform)` | 得挂成 `GComponent.AddChild` |
| 排序 | 写 `Canvas.overrideSorting` / `sortingOrder`（`RestampSortingOrders`） | **那个 Canvas 根本不存在** |
| 输入阻塞 | UGUI `GraphicRaycaster` + 全屏 `Image` | FairyGUI 自行命中测试，看不见 UGUI 的阻挡 |
| 回池 | `gameObject.SetActive(false)` → `AssetManager.DestroyInstance` | 得走 FairyGUI 的 `Dispose` / `GObjectPool` |
| 类型约束 | `OpenAsync<T>() where T : UIPanelBase` | `GComponent` 不是 MonoBehaviour，**没有共同表示** |

本仓已有定论可引：UI README 的「已知限制」写着换排序方案「**等于整体替换 `IUIManager`**」——
那是重写一个 UI 模块（现规模 6.5k 运行时 + 6.7k 测试行），不是接后端。

### 2.2 与 Audio → Wwise / Input → Rewired 不可类比（最容易被反问的一条）

那两个模块的抽象**刻意保持引擎中立**——`IInputProvider` 只用 `string` 与数值；`IAudioClipLoader` 的
注释甚至专门写过「避免出现『只换资源来源、不换播放引擎』这条不完整、也无人验证的中间道路」。

**UI 的抽象没有、也不可能有这个中立性**：`UIPanelBase : MonoBehaviour` 带
`[RequireComponent(typeof(Canvas))]`（`View/UIViewBase.cs:15-17`）、`IUIManager.UIRoot` 是 `Transform`、
`UIBinder` 绑的是 `UnityEngine.UI.Image/Slider/Toggle/Text`。
⇒ FairyGUI **不可能「落在 `IUIManager` 后面」，只能「取代它」**。

### 2.3 它强的是制作层，管理层并不比本仓强

管理层逐项对照（`GRoot` / `Window` vs `IUIManager`）`[上游源码核对]`：

| 能力 | FairyGUI | 本仓 |
| --- | --- | --- |
| 打开 | `ShowWindow(Window)` **同步 `void`**；包要自己先 `AddPackage` | `OpenAsync<T>()` → `UniTask<T>`，**可取消，含资源加载** |
| 显示栈 | **无退栈语义**（只有 `GetTopWindow`） | `Push`/`Pop`/`PopTo`/`PopToRoot`/`GoBack`/`CanGoBack` |
| 层级可见性 / 交互性 | 无（只有 `sortingOrder`） | `SetLayerVisibility` / `SetLayerInteractive` |
| 模态遮罩 | `ShowModalWait` / `CloseModalWait`（**无引用计数**） | `ShowMask` → 引用计数句柄 `UIMaskHandle` |
| 「关掉除模态外的一切」 | 需专门提供 `CloseAllExceptModals`（`GRoot.cs:232`） | `CloseAllAsync` **本来就不关遮罩**——已等价具备 |
| **运行时数据绑定** | **无**（`Gears` 是编辑器期属性联动，不是绑定） | `UIBinder` 12 个 `BindXxx` + `IReactiveProperty` 双向 |
| 每帧调度 | 自持 `LateUpdate` + `Timers.inst`，无档位概念 | `UpdateManager` + **每面板可选 `UpdateTier`** |
| **排序空间** | 显示树的子节点顺序，**只在自己树内有序** | `UISorting` 预留数值带，**与 HUD / Tip / 特效层共享同一空间** |
| 全局配置 / 单例 | `UIConfig` 一屏 `public static` / `GRoot.inst` | options 对象 / 静态门面 + `SetInstance` |
| 安全区 | 不在其内（`UIContentScaler` 是分辨率适配，另一回事） | `UISafeArea` |

**它强的是制作层**：虚拟列表 / 循环列表、富文本（图文与动画混排）、`GTree`、`GComboBox`、
`PopupMenu`、`DragDropManager`、手势、`Transition`、`GTween`、`GLoader3D`、粒子与 3D 混排、
以及**独立编辑器**。

> **别把「虚拟列表开箱即有」当主理由**。它值多少钱完全取决于列表有多长——最长列表只有几十条的话，
> 这条优势基本不成立。而且它有前提：`[上游源码核对]` 文档明写「**单行/单列**布局**可支持不等高**」
> （`GList.cs:1428`），Flow 布局的内容高度按**等高**算（`ResizeToFit`，`:1172`），
> **循环列表在 Flow 布局下直接报错**（`:1456`）——连它也是靠砍布局空间换来的。

### 2.4 「策划脱离 Unity」这个好处要拆成两半

| 目标 | 谁给的 |
| --- | --- |
| **策划脱离 Unity 编辑 UI** | **FairyGUI 编辑器**。真实，且**只有它给得了**——UGUI 的预制体必须在 Unity 里编 |
| 策划脱离游戏数据逻辑 | **与 FairyGUI 无关**。那是「命名约定 + 绑定层」的效果，本仓 `UIBinder` + `UIPanelBase.BindByConvention<T>` 现在就有 |

⇒ 真正只有它兑现的是**第一条**。而第一条的代价是 §2.3 那笔债。
**反方论据**（应当摆出来）：UGUI 的 `RectTransform` 锚点对非程序确实难学，FairyGUI 的相对定位 +
关联更贴策划直觉。**但解法不是换 UI 栈**——是给策划受限的预制体模板（`Prefab Variant` 固定结构 +
只暴露该改的字段）。

**一条同向的硬事实** `[上游源码核对]`：UI 资源是 **`.fui` 二进制包**，对 review / diff / 回滚是纯负债，
而本仓是 **Force Text** 序列化。

---

## 三、若一个项目仍要用它：接缝与坑

**这一节是给「不做框架集成、但项目自己要用」准备的。** 三件事按顺序，第一件不过就免谈。

### 3.1 输入系统 —— 唯一可能直接堵死的关 `[未实测]`

`[本仓核对]` 本项目 `ProjectSettings/ProjectSettings.asset` 是 `activeInputHandler: 1` = **仅新输入系统**。

`[上游源码核对]` FairyGUI 的默认路径走 **legacy `Input.*`**——`Stage.cs:916` `Input.mousePosition`、
`:1169` `Input.GetMouseButtonDown`、`:868` `Input.GetTouch`、`:236` `Input.touchSupported`。
**在该设置下这些调用会抛异常。**

它的新输入分支存在，但**三重受限**：

1. 需**手工定义** `FAIRYGUI_INPUT_SYSTEM`——`Stage.cs:6` 的 `#if` 看它，但上游 asmdef 的
   `versionDefines` 是空的、仓内无 `.rsp`、本项目 `scriptingDefineSymbols: {}`
2. 开了 define 还得**手改 vendored 的 `FairyGUI.asmdef`** 补 `"Unity.InputSystem"`——
   上游那份**只引用了 `Unity.TextMeshPro`**
3. **上游自己的 `Packages/manifest.json` 里根本没有 `com.unity.inputsystem`** ⇒
   **那个分支上游从未编译过**，你是第一个测它的人

**三条出路**（按推荐度）：

| 出路 | 代价 |
| --- | --- |
| **先做隔离探针**（30 分钟，仓外工程） | 见下方判据。**不验就往下走是在赌一个上游从未编译过的代码路径** |
| 定义 define + 手改 asmdef | 每次升级 FairyGUI 都要重做这两步；且改的是 vendored 源码 |
| 把 Active Input Handling 改成 Both | 与本仓对 `Unity.InputSystem` 的硬引用（`Venusy609.Xframework.asmdef`）取反，是项目级回退 |

**探针判据（先写死再跑）**：空白 Unity 6000.x 工程 + `com.unity.inputsystem` + `activeInputHandler: 1`
→ vendored `Assets/Scripts` → asmdef 补引用 + 定义 define → 跑一个含**按钮点击**与**文本输入**的最小
`.fui` 包。三种结果：**通过**（共存路线可推进）／**编译不过**（本节结论更硬）／**能编译但输入不准**
（记进坑清单）。

### 3.2 分层与输入不可跨栈 `[本仓核对]`

共存可行，但有两条框架强制的硬约束，且都**不会崩、只会静默失效**：

- **分层不可交错**：`View/UIRootNode.cs:59` 强制 `canvas.renderMode = RenderMode.ScreenSpaceOverlay`，
  而 UGUI Overlay **恒绘于所有相机之上**（不受 `depth` 影响）；FairyGUI 走自己的相机。
  ⇒ **UGUI 永远盖在 FairyGUI 上，排序号无解。** 要做到「FairyGUI 背景 → UGUI 弹窗 → FairyGUI tooltip」
  必须放弃 `UIRootNode` 自建 ScreenSpaceCamera 根，代价是丢掉成对生命周期
  （UI README「已知限制」第 1 条：绕过 `UIRootNode` 手动 `Initialize` 的项目在第二个播放会话里无法自救）。
- **模态遮罩不跨栈**：`ShowMask` 是一个 UGUI `Image` 射线阻挡，FairyGUI 自行命中测试、看不见它。
  ⇒ **UGUI 的模态遮罩挡不住 FairyGUI 的点击**，反向亦然。
  「弹窗打开时锁住整个界面」这个最常用的语义，在两栈共存下**默认不成立**，要项目自己补。

**相机不劫持主相机**（一个容易担心的点）：`StageCamera.CheckMainCamera` 建的是名为 `Stage Camera` 的
独立对象（`Core/StageCamera.cs:154-161`），不是复用 `Camera.main`。但它依赖一个名为 `UI` 的 Unity 层，
且碰撞/命中测试仍经 `Camera.main`（`Stage.cs:775`）。

### 3.3 资源接入 —— 这块接缝是干净的 `[上游源码核对]`

`UIPackage` 的加载口设计得就是给外部资源系统的：

```csharp
public delegate object LoadResource(string name, string extension, System.Type type, out DestroyMethod destroyMethod);   // UIPackage.cs:47
public delegate void LoadResourceAsync(string name, string extension, System.Type type, PackageItem item);                // UIPackage.cs:57

public static UIPackage AddPackage(byte[] descData, string assetNamePrefix, LoadResource loadFunc);          // :340
public static UIPackage AddPackage(byte[] descData, string assetNamePrefix, LoadResourceAsync loadFunc);     // :363
```

⇒ 用 `AssetManager` 把 `.fui` 描述与图集字节取回，再交给 `AddPackage(byte[], string, LoadResourceAsync)` 即可。
AssetBundle 重载也已具备（`:201-224`）。**这是全部接缝里唯一不用打补丁的一处。**

---

## 四、从它筛出的借鉴点（已评估）

2026-10-11 逐条筛过一遍：**判据是「正交原语 / 填补真实空白 / 不与既有裁定冲突」**（不违反
`Ledger.md` 不做组与各模块 README 的既有结论）。**筛完只有一条够格。**

### 4.1 采纳：弹窗定位原语

**已落地**（`Runtime/UI/UIPopupPosition.cs`，4 提交）。来源与取舍记在
`Documentation/Modules/UI.md` 的沿革里，此处不复制。

**要点**：`GRoot.ShowPopup(popup, target, PopupDirection, closeUntilUpEvent)` 与
`GetPoupPosition`（`:474`——**上游把方法名拼错了，`GetPoup` 少一个 `p`**）确立了两条值得保留的语义
（Auto 优先下方、放不下翻上方；默认左对齐），但实现有三个可指认的边界缺陷，**没有复制**：

```csharp
xx = pos.x;
if (xx + popup.width > this.width)
    xx = pos.x + size.x - popup.width;      // ① 只有右侧收敛，没有左侧收敛
yy = pos.y + size.y;
if ((Auto && yy + popup.height > this.height) || dir == Up) {
    yy = pos.y - popup.height - 1;          // ② -1 硬编码，无法配置
    if (yy < 0) { yy = 0; xx += size.x / 2; }  // ③ 二次调整且与 ① 叠加；无「弹窗比屏大」处理
}
```

① 的后果：锚点贴屏幕左缘且弹窗比锚点宽时（屏宽 150 / 锚点 `x=0` 宽 50 / 弹窗宽 200）→ `xx = -150`，
**弹窗整个出屏**。本仓实现改为**两轴都收敛 + `offset` 可配 + 显式分支处理超大弹窗**，
并把该组合写成回归用例。

### 4.2 已评估未采纳（都**不是**「它不好」，是不该由本仓做）

| 候选 | 处置与理由 |
| --- | --- |
| **列表虚拟化 / 循环列表** | **不内建，结论经重推后不变**——完整论证（含「可复用的只有等高算术」与「循环列表从未被单独评估过」）记在 `Documentation/Modules/UI.md` 的沿革里，不在此复制。这里只留 **FairyGUI 侧的证据**：连它也是靠砍布局空间换来的（单行/单列才支持不等高、Flow 按等高算、循环列表在 Flow 下直接报错，见 §2.3 末）——这独立佐证了「难的部分无法一般化」 |
| **`DragDropManager`（跨面板拖放）** | 按本仓自己的判据（Tip / HUD / 遮罩是**跨切面**所以内建，列表是**叶子**所以不内建），拖放**其实够格**——源与目标常在不同面板，需要全局的「当前拖拽物 + 跟随代理 + 落点裁决」。**但那是新模块的量级，不是借鉴一条控制逻辑**。要走正常立项流程 |
| **`GTween`** | `Ledger.md` 已否决「Tween / 缓动 / 动画」（DOTween 等已覆盖） |
| **`Timers.inst`** | `Timer` 模块已建成且更强（可查询 `Remaining`、不漂移、与 `UpdateTier` 联动） |
| **`Gears` / `Relations` / `Controller`** | **编辑器期数据**（状态 → 属性/显隐的联动表），不是运行时控制逻辑；且属制作层 |
| **`UIContentScaler`** | 分辨率适配在 UGUI 侧归项目（`CanvasScaler` 挂在 UIRoot 上），不是模块缺口 |

### 4.3 看起来像借鉴、其实是**反向**的（写下来免得下轮重提）

模态遮罩引用计数（它没有，本仓 `UIMaskHandle` 有）／「除模态外全关」（本仓 `CloseAllAsync` 本就不关
遮罩，已等价具备）／`UIConfig` 一屏静态 vs options／`GRoot.inst` vs 静态门面 + `SetInstance`／
自持 `LateUpdate` vs `UpdateManager` + 档位／**排序空间只在自己树内有序 vs 与 HUD·Tip·特效共享**。

---

## 五、边界与重开判据

- **不重建内建适配层**（§一）。理由：没有接缝（§2.1），且与「UI 排序分层是模块不变量」的既有裁定同向
  （`Ledger.md` 不做组的「把模块内部策略接口化」条）。
- **不做平级管理层包**（§一·2）。理由：唯一不可替代的好处撑不起第二套 UI 栈。
- **包内 README 不指向本文件**。规则「包内不指向包外」：第三方装了包却打不开仓库。
  使用方需要知道的东西在包内 README 里已经写全（**不必提 FairyGUI 一个字**——`UIRootNode` 强制 Overlay
  与遮罩只挡 UGUI 这两条是本模块自身的行为，对任何走自有相机的 UI 方案都成立）。
- **本文件不提供探针脚本**。与 `Luban.md` §4.3 同一取向：`<工程路径>` / `<Unity 版本>` 逐项目不同，
  写死一份只会腐化；本仓没有 CI，脚本不会被任何东西执行到。真要做漂移探测，正确的形状是
  **一次性探针、跑完即弃**。

**重开判据（可判定，不是「想起来就做」）**：两条，**都**成立才值得重新评估——

1. **上游那条新输入分支 + Unity 6 实测通过**（§3.1 的探针）。可在任何后续轮次单独立项，约 30 分钟。
2. **出现真实使用方**明确要用 FairyGUI 作为其 UI 栈，且确认存在「美术 / 策划独立编辑 UI」的**真实**需求。

> **「没人要」是最弱的论据。** 上一条不是「因为没人用所以不做」——§一 给出的两条理由是**结构性的**
> （没有接缝 / 收益撑不起成本），与有没有人用无关。判据 2 的作用是回答「什么情况下该重新看这件事」，
> 不是「现在不做的理由」。
