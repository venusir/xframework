# XDiagnostics 运行时诊断

给框架与游戏自己的运行时状态提供**一条统一出口**：一个页签注册表（谁能被看到）+ 一份报告采集契约（看到什么）。

本模块只有**纯数据**——零 GUI 依赖、不引用任何渲染 API。渲染方可以由任何东西充当：包内附带的编辑器窗口、你自己的工具窗口，或将来可能有的运行时覆盖层。它们消费的都是同一份 `DiagnosticReport`。

## 快速开始：写一个自己的页签

```csharp
using System.Collections.Generic;
using UnityEngine;
using XFramework.XDiagnostics;

internal sealed class MyGamePanel : IDiagnosticPanel
{
    public string Title => "My Game";
    public int Order => DiagnosticOrders.User;

    public void Collect(IDiagnosticReport report)
    {
        report.KeyValue("玩家等级", Player.Level.ToString());
        report.KeyValue("金币", Player.Coins.ToString());

        var table = report.BeginTable("背包", "物品", "数量");
        foreach (var slot in Player.Inventory)
            table.AddRow(slot.Name, slot.Count.ToString());
    }
}

internal static class MyGamePanelRegistration
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Register()
    {
        DiagnosticsManager.Register(new MyGamePanel());
    }
}
```

写完后打开诊断视图（包内自带编辑器窗口：菜单 `Tools/XFramework/Diagnostics`）就能看到这个页签。想在**未进入播放模式**时也看到它，把注册放在带 `#if UNITY_EDITOR [UnityEditor.InitializeOnLoadMethod] #endif` 的编辑器分支里——但页签本身仍要能安静地处理「模块未初始化」的情况。

## 报告词汇表

| 写入方法 | 语义 | 文本渲染形如 |
|---|---|---|
| `Section(title)` | 小节标题，后续内容归入其下 | `== 标题 ==` |
| `Text(text)` | 一段说明，可含换行 | 原样 |
| `KeyValue(label, value)` | 一行属性（键值） | `名: 值` |
| `Notice(level, text)` | 带级别的提示；`DiagnosticLevel` 为 `Info` / `Warning` / `Error` | `[WARN] 正文` |
| `BeginTable(title, columns...)` → `AddRow(cells...)` | 表格：表头固定、行只增不改 | 列右补空格对齐，`-+-` 分隔线 |

`DiagnosticReport` 是采集器的默认实现，也是渲染方复用的那一个：`Clear()` + `Collect()` 重填，不必每次刷新重新分配。

## 编辑器窗口与渲染

包内自带窗口与渲染层：菜单 `Tools/XFramework/Diagnostics`（左栏页签列表 + 顶栏刷新/自动刷新/复制）。

- **默认路径**：页签只实现 `IDiagnosticPanel`，窗口用 `XFramework.Editor.Diagnostics.DiagnosticReportView` 按上面的词汇表渲染。
- **在自己的窗口里渲染任意报告**：`new DiagnosticReportView().Draw(report)`——报告可以自己填，不必与诊断窗口有关；观感与诊断窗口一致。
- **混搭自绘**：实现 `XFramework.Editor.Diagnostics.IDiagnosticPanelView`，窗口会把内容区整块委托给它（`Collect` 仍会被调用，因此「复制为文本」照常可用）。标准件用 `DiagnosticItemDrawer` 的单项原语（`DrawSection` / `DrawText` / `DrawKeyValue` / `DrawTable` / `DrawNotice`）。
- 渲染层在 Editor 程序集（`XFramework.Editor.Diagnostics`）——只随编辑器加载，不进 Player 构建。

## 契约

### `Collect` 的调用契约（实现者必读）

1. **只在主线程调用**，与渲染方同线程。
2. **可重复调用**：每次刷新都会调。实现必须无副作用——不得改框架状态、不得启停定时器或发消息。
3. **不得抛异常**：渲染方会兜底捕获，但抛出会让本次报告整份作废，只显示一条错误提示。
4. **允许分配、允许遍历全表**：这是诊断路径，不适用「每帧路径零分配」那套约定；但**不得被每帧调用**——调用频率由渲染方决定（编辑器窗口默认 0.5 秒一次）。
5. **模块未初始化时不得抛**：写一行「未初始化」说明即可。

### 线程契约

| 操作 | 线程 |
|---|---|
| `DiagnosticsManager` 全部成员（登记 / 注销 / 拷贝 / 计数 / 清空） | 主线程 |
| `IDiagnosticPanel.Collect` 与 `IDiagnosticReport.*` | 主线程（与渲染方同线程） |
| `DiagnosticReportFormatter.ToText` | 任意线程（纯函数，只读入参） |

### 注册、排序与去重

- **按实例去重**：同一实例重复登记被静默忽略（记一条 Warning）。关闭域重载时两条注册路径都会重跑，幂等是硬要求。
- **标题可以重复**：不同实例用同一个 `Title` 时两个都保留，记一条 Warning——标题由各实现自己决定，框架不替调用方裁定。
- **排序**：`Order` 升序，同值按注册先后（稳定）。`DiagnosticOrders.Framework = 0`、`DiagnosticOrders.User = 1000` 是建议值，不是约束。
- `Title` 为 null 或空白会在登记时抛 `ArgumentException`；`panel` 为 null 抛 `ArgumentNullException`。

### 生命周期：域重载与关闭域重载

登记表是静态的，随程序集一起重建。

- 域重载**开**：每次进播放都会重载，登记与注册照常重跑。
- 域重载**关**：上一会话登记过的页签**跨播放会话存活**。`[RuntimeInitializeOnLoadMethod]` 每次进播放都重跑（重复登记被幂等吸收），`[InitializeOnLoadMethod]` 不重跑（编辑器侧注册的那批本来就应该长期在）。因此**运行时注册的页签请在自己的退出路径里 `Unregister`**。
- **刻意不做**「进播放时清空注册表」：那会把编辑器侧注册的框架页签一并抹掉。

## 已知限制

- **没有泛型/自定义条目类型**：词汇表固定为小节 / 文本 / 键值 / 表格 / 提示五种。想要进度条、图表这类自定义视觉，走渲染方的自绘入口（编辑器侧可选接口），别指望数据层开洞——数据契约一旦携带渲染回调，文本渲染与将来的运行时覆盖层就只能降级。
- **没有反射发现**：页签必须显式登记。框架不扫描程序集。
- **表格不对齐 CJK**：文本渲染按**字符数**补空格，中文字符按 1 计；等宽对齐需要东亚宽度表，本仓不做（表格以类型名与数字为主，误差只出现在中文表头）。
- **`CopyPanels` 返回的是实时视图的拷贝，不是排序快照的引用**——顺序在登记时维护，读取只是拷贝，因此读取零排序、零分配。
- **未捕获的 `Collect` 异常不会自动重试**：渲染方每次刷新都会重新调用该页签（下一次刷新即重试）。

## 设计取舍

- **拉模式，不是推模式**：页签不缓存、不上报，渲染方每次刷新现调 `Collect`。好处是「窗口里看到的」与「代码里读到的」永远是同一份真相，不存在第二本账；代价是昂贵的数据要页签自己缓存（而契约允许它这么做）。
- **纯数据契约**：运行时不引用 `UnityEngine.IMGUIModule` / `UnityEditor`，因此同一份报告可以被文本渲染、编辑器渲染、运行时覆盖层或第三方工具消费。
- **没有接口的静态门面**：本仓「有接口」的分界线是「是否存在可替换的后端」，注册表没有后端——整体替换注册表没有意义，页签本来就是多方各自登记的。
