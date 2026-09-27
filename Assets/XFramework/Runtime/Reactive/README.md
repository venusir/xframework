# XFramework / Reactive 模块

## 概述

XFramework 响应式模块提供**响应式属性**。基于 `XFramework.XEvent` 模块的事件流引擎实现,可在任意 C# 类中使用。

- `ReactiveProperty<T>`:可写响应式值,订阅时立即回调当前值,设置相同值不通知(去重语义)
- `ReadOnlyReactiveProperty<T>`:由 `Select` 映射派生的只读属性,值随源自动变化(去重)。同样实现 `IReactiveProperty<T>`(只读接口本无 setter,故不因此获得写入能力),可直接交给收该接口的绑定 API,也可继续 `Select` 做链式映射
- `IReactivePropertyWriter<T>`:「确实需要写入」时按需索取的能力接口(继承 `IReactiveProperty<T>`,只加 `TryWriteValue`)。双向绑定 API 收它而非具体类,故任何第三方实现都能接入——只读接口本身不因此多出 setter
- 全局消息总线在 Message 模块(`XFramework.XMessage.MessageManager`),不在此模块

**命名空间**: `XFramework.XReactive`

## 架构设计

```
Runtime/Reactive/
├── IReactiveProperty.cs          # 响应式属性接口(Value 只读 + Subscribe,面向接口编程)
├── IReactivePropertyWriter.cs    # 可写能力接口(继承前者 + TryWriteValue),双向绑定按需索取
├── ReactiveProperty.cs           # 响应式属性(可写值 + 自动通知 + 去重)
└── ReadOnlyReactiveProperty.cs   # 只读派生属性 + Select 映射扩展
```

事件流引擎位于 Event 模块(`Runtime/Event/`,`XFramework.XEvent`)——本模块经它的**公开面**使用(`IEventStream<T>` / `EventStream.Create`),不触碰其内部实现。

## 快速使用

```csharp
using UnityEngine;
using XFramework.XReactive;

// 创建响应式属性(实现 IReactiveProperty<int>,可面向接口编程)
var healthProp = new ReactiveProperty<int>(100);

// 订阅值变化。注意:订阅本身就会立即同步回调一次当前值
var subscription = healthProp.Subscribe(newValue =>
{
    Debug.Log($"血量变化: {newValue}");
    // 更新血量条 UI
});
// 上面这行订阅先打印了一次「血量变化: 100」——立即回调发生在订阅那一刻,不是值变了

// 修改值(自动推送;相同值不通知)
healthProp.Value = 80;   // 输出: 血量变化: 80
healthProp.Value = 50;   // 输出: 血量变化: 50

// 只读视图:只暴露读接口,写值仍只经源属性。这不是派生,就是同一个属性换个角度看
IReactiveProperty<int> readOnlyView = healthProp;

// 映射派生:由血量算出血条比例。返回值同样是可订阅的只读值,还能继续 Select 链式映射
var healthRatio = healthProp.Select(hp => hp / 100f);
var ratioSub = healthRatio.Subscribe(r => Debug.Log($"血条: {r:P0}"));
// 输出: 血条: 50%(订阅时立即回调当前映射值)

// 收尾:订阅句柄与派生值都要释放
ratioSub.Dispose();
healthRatio.Dispose();   // ← Select 创建的派生值,谁创建谁释放
subscription.Dispose();
healthProp.Dispose();
```

> **派生值必须有人释放,否则会永久泄漏。** `Select` 在**构造时**就订阅了源,所以源的事件流一直引用着这个派生值:把返回值就地丢弃,它就会活到源消失为止,并且此后每次源变化都还会再跑一遍 selector。
>
> 尤其注意 `BindToXxx` 返回的是**绑定方**的句柄——释放它**不能**释放派生值。所以 `src.Select(f).BindToText(label)` 这种写法即使把绑定句柄收好了,派生值照样泄漏。
>
> 别在业务代码里手工记账,交给已就位的归口：UI 面板里用 `ViewModelBase.CreateReadOnlyProperty`（创建 + 登记,随 ViewModel 一起释放），或把句柄交给 `UIViewBase.Track`。Reactive 模块本身不管生命周期,这是使用方的责任。

## 双向绑定

`IReactivePropertyWriter<T>` 是「确实需要写入」时按需索取的能力接口——`ReactiveProperty<T>` 与 Settings 的 `SettingRef<T,TField>` 各自实现，第三方实现同样可接入 `UIBinder.BindTwoWay`。只读接口 `IReactiveProperty<T>` 本身不因此多出 setter。

```csharp
if (!writer.TryWriteValue(value, out var actual))
    return;   // 目标已失效:返回 false 而不抛异常,绑定层不该把异常抛进 UI 事件回调

// actual 是写入后目标实际持有的值。用它,不要再读 writer.Value:写入会同步派发通知,
// 某个订阅者在派发中释放目标也照常发生,而 actual 由实现直接给出——读它不依赖 Value
// 对「已失效」采取何种策略(那是各实现自己的事,见下节)。
```

`false` 表示**未写入**（目标已失效：属性已释放，或句柄背后的设置类型已注销），此时 `actual` 为 `default`。

> **`true` 不等于「值变了」。** 返回 `true` 只说明目标现在持有的值就是 `actual`——写入与当前值相同时会被去重、订阅者不会收到通知，但 `TryWriteValue` 照旧返回 `true`。把它当「值变了」用（落盘、重算、重放副作用）会在每次空写时多做一遍。

> **`actual` 与「规范化」无关。** 不要指望目标会取整或钳制：随框架发布的两个实现都不做规范化，`actual` 恒等于你写进去的 `value`。`actual` 的价值在于省掉一次二次读取，不在于取代调用方自己的回填判断（`UIBinder` 的双向绑定仍会比对 `actual` 与输入值再决定是否回填控件）。

## 接口承诺到哪为止

`IReactiveProperty<T>` 只承诺一件事：**订阅时立即同步回调当前值**。其余几条都是**具体实现的特性，不是接口保证**——按接口编程时不要当前提：

| 行为 | `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` | Settings 的 `SettingRef<T,TField>` |
| ---- | ---- | ---- |
| 订阅时立即回调当前值 | ✅ | ✅ |
| 相同值不通知（去重） | ✅ | ❌ 实例替换（`Load`/`Reset`/`Apply`）会**无条件重放**，值没变也回调 |
| 读取 `Value` 不抛 | ✅ 已释放后仍返回最后持有的值 | ❌ 设置类型已注销时抛 `InvalidOperationException` |
| `TryWriteValue` 失效时返回 `false` | ✅ 已释放后返回 `false` | ✅ 类型已注销后返回 `false`（见 `SettingsManager.IsRegistered`） |

`SettingRef` 那两条不是缺陷，而是它自己的取舍（重放是为了「换实例后 UI 自动跟随」；读抛是为了附上修复提示）。关键是**接口本身没有承诺它们**，所以消费者不能靠接口吃掉这些差异。

### 本模块没有的（按 Rx 直觉找来的人请注意）

- **集合型响应式**（R3 的 `ReactiveCollection` 等价物）：**没有**。列表/背包的增量通知由使用方自行组织（UI 侧的「列表虚拟化」也仍是未做项，见 UI README 的 `[ ]` 列表）。
- **命令**（R3 的 `ReactiveCommand` / `CanExecute`）：**没有**。UI 侧走 `UIBinder.BindClick` 直接绑点击，按钮可用性由使用方自己置 `interactable`。
- **Inspector / 序列化集成**（`BindableReactiveProperty` 那类）：**没有**——本仓在 UI 模块做约定式绑定（`txt_` / `sld_` / `tgl_` 前缀），不走 Inspector 配置。

## 派发顺序与重入

两条语义由底层事件流决定,都容易踩:

- **派发顺序是 LIFO**——后订阅的先收到(订阅节点插在链表头部)。框架**没有**控制订阅者相对顺序的手段,别围绕先后次序设计(比较各自收到的值也建立不了顺序)。
- **回调内写入本属性会嵌套派发,且嵌套那一轮先跑完**。订阅者 A、B 中 B 后订阅(故先收到):B 在收到 `1` 时写入 `2`,嵌套派发让 A、B 都先收到 `2`;随后外层循环继续把 **`1`** 投给 A——A 最终停在一个 `Value` 已不再是的值上,且不会再有通知来纠正它。**回调里不要写入本属性**;确实需要「收到变化后再修正」时,把写入推迟到下一帧再写,而不是就地写(框架**没有**一次性下一帧入口,可自行排期,或实现 `IUpdateable` 经 `UpdateManager` 注册)。

## 排查订阅泄漏

订阅泄漏在运行时是**无声的**——没人报错、值也照常更新,只是内存与回调悄悄堆积。模块为此提供两样东西:

- **`SubscriptionCount`**(两个类型都有)——当前存活订阅数。源长期存活时(单例上的属性、Settings 句柄)这个数**只增不减**,即说明有订阅没人释放。
- **`ToString()`**——形如 `ReactiveProperty<Int32>(50)`,与 Settings 的 `SettingRef.ToString()` 同形,排查时一眼看出是谁、现在是多少。

最常见的泄漏源就是上一节说的 `Select` 返回值被就地丢弃:

```csharp
// 症状:每次打开面板这个数就 +1
Debug.Log($"{vm.Hp} 订阅数={vm.Hp.SubscriptionCount}");
// 输出: ReactiveProperty<Int32>(100) 订阅数=3   ← 本该是 0 或 1
```

两个成员都**只用于排查**,不要拿它们做逻辑分支:它们是实现的当前状态,不是契约。

同类工具:Message 模块的 `MessageManager.GetStats()`、UI 模块的 `UIManager.DumpState()`。

> **没有自动跟踪器**:本仓只有上面这种**拉取式**诊断,没有 R3 `ObservableTracker` 那样的「列出未释放订阅**及其创建调用栈**」的编辑器工具——排查靠这两条信号 + 人工核对。订阅泄漏是本仓历史上反复出现的一类(UI / Reactive / Settings 的 README 都专设排查节);若这类事故再出现,值得单独立项补一个(与 `UIStateWindow` 同级的编辑器窗口)。

## 设计原则

- **事件流驱动** — 基于 `XFramework.XEvent` 自研事件流引擎(锁 + 快照线程模型、订阅节点池)
- **订阅立即回调** — 订阅时立即同步回调当前值(UI 初始绑定依赖此语义)
- **相同值去重** — 设置相同值不通知。**这是本模块两个类型的行为,不是接口保证**(见「接口承诺到哪为止」)
- **读宽容、写严格** — 已释放后读取 `Value` 仍返回最后持有的值（与 `ReadOnlyReactiveProperty<T>` 一致）；写入 `Value` 与订阅则抛 `ObjectDisposedException`。读是只读操作、不改变任何状态,让它抛只会把「先 Dispose 再读一次收尾值」变成必须 try/catch 的地雷;写是编程错误,应当被立刻发现。**同样只是本模块类型的行为**——`SettingRef.Value` 在设置类型注销后照抛
- **主线程专用** — 引擎的锁与快照只保证订阅链表与终止标志在并发退订下不被写坏,**不构成「可以多线程读写」的许可**(详见 Event 模块 README 的「线程」节)。本模块自身未做任何同步:跨线程写入会让去重判断与派发载荷分叉(最后一个订阅者见到的值不再是 `Value`,且无人纠正)
- **接口即只读视图** — `IReactiveProperty<T>.Value` 无 setter,写值经具体实现类型,避免外部误写状态。可写属性、`Select` 派生值、Settings 的 `SettingRef` 句柄一律实现该接口,于是绑定 API 只认接口、任何第三方实现都能接入
- **异常隔离** — **投递**路径的订阅回调抛异常记 Error 日志后继续;订阅时的立即回调属于注册期、同步执行,它抛出的异常原样上抛(订阅已自动清理,不会泄漏)。两条路径语义不同是有意的:绑定初始化失败应当被看见,而运行期的单个订阅者出错不该拖垮其余订阅者

## 依赖

- `XFramework.XEvent` 的**事件流引擎**——单向依赖:Reactive → Event。用的是它的**公开面**(`IEventStream<T>` / `EventStream.Create` / `SubscriptionCount`),不再触碰任何模块的 `Internal` 命名空间
- 全局消息总线在 XMessage 模块,需要发布/订阅消息时 `using XFramework.XMessage`;本模块**不**依赖它

> **这条依赖曾经的形态(留档)**：引擎原先物理上住在 `XMessage.Internal` 里,本模块直接 `using` 它取 `EventStream<T>`——合计 5 个跨模块文件引用同一处内部命名空间,而全框架共用一个 asmdef、`internal` 不构成编译边界,既没有编译器约束、也没有成文约定可依。2026-09-27 把引擎下沉为**独立模块** `XFramework.XEvent`(公开接口 + 静态工厂 + internal 实现,照 Pipeline 先例),这条依赖随之变成**公开、单向、可自查**——`Tests/Editor/Architecture/ModuleBoundaryTests` 会拦住任何模块对别的模块 `Internal` 的新引用。引擎的语义契约(派发顺序 LIFO、重入、异常隔离、completed 与 Dispose 的差别等)现由 [Event README](../Event/README.md) 承载,本节不再复述。

> **引擎的语义契约**(派发顺序 LIFO、重入的后果、异常隔离、`OnCompleted` 与 `Dispose` 的差别、缓冲重放)现由 [Event README](../Event/README.md) 承载——本模块「派发顺序与重入」一节描述的正是这些语义在属性上的表现,若哪天引擎改了语义,请同步改那两处。
