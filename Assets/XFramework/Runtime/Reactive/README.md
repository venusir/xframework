# Reactive —— 响应式属性与集合：订阅即知变化，基于 XEvent 事件流

## 概述

XFramework 响应式模块提供**响应式属性**。基于 `XFramework.XEvent` 模块的事件流引擎实现,可在任意 C# 类中使用。

- `ReactiveProperty<T>`:可写响应式值,订阅时立即回调当前值,设置相同值不通知(去重语义)。「相同」默认按 `EqualityComparer<T>.Default` 判定,**可经构造重载注入自定义比较器**（浮点容差、大小写不敏感等）；`Select` 派生值的去重同样可经其重载注入
- `ReadOnlyReactiveProperty<T>`:由 `Select` 映射派生的只读属性,值随源自动变化(去重)。同样实现 `IReactiveProperty<T>`(只读接口本无 setter,故不因此获得写入能力),可直接交给收该接口的绑定 API,也可继续 `Select` 做链式映射
- `ReactiveList<T>`:可订阅增删改的列表(集合型响应式)。变更以 `readonly struct` 载荷**单流**投递(零 GC),`Clear` / `Sort` 发一条 `Reset` 兜底;**订阅不做立即回调**(与 `ReactiveProperty<T>` 相反,见「集合」节与「接口承诺到哪为止」)
- `ReactiveDictionary<TKey,TValue>`:可订阅增删改的字典(按键寻址的集合型响应式)。与列表同构(单流 struct 载荷);差别在**键比索引稳定**、**`Reset` 没有哨兵**与**键比较器可注入**——引用类型作键时的逃生口
- `IReactivePropertyWriter<T>`:「确实需要写入」时按需索取的能力接口(继承 `IReactiveProperty<T>`,只加 `TryWriteValue`)。双向绑定 API 收它而非具体类,故任何第三方实现都能接入——只读接口本身不因此多出 setter
- 全局消息总线在 Message 模块(`XFramework.XMessage.MessageManager`),不在此模块

**命名空间**: `XFramework.XReactive`

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
>
> **链式 `Select` 的中间派生值不可达（RCT-001）**：`d2 = d1.Select(g)` 之后 `d1` 不在任何可达引用上；`d2.Dispose()` 只退订 `d2 → d1`，`d1 → root` 的订阅与中间 selector 会活到 root 被 Dispose，且每次源变化继续跑中间 selector。长寿命属性上请显式持有每一级派生值并逐级释放——只保留 `d2` 而丢掉 `d1` 的写法会在长寿命源上泄漏订阅与 CPU。是否提供「派生值拥有上游」的显式构造入口仍待裁（不建议 Dispose 级联，会误杀共享下游）。

## 模块结构

```text
Runtime/Reactive/
├── IReactiveProperty.cs             # 响应式属性接口（Value 只读 + Subscribe，面向接口编程）
├── IReactivePropertyWriter.cs       # 可写能力接口（继承前者 + TryWriteValue），双向绑定按需索取
├── ReactiveProperty.cs              # 响应式属性（可写值 + 自动通知 + 去重）
├── ReadOnlyReactiveProperty.cs      # 只读派生属性 + Select 映射扩展
├── IReactiveList.cs                 # 列表接口（只读视图 IReadOnlyList<T> + 变更流订阅，不立即回调）
├── ReactiveList.cs                  # 响应式列表（增删改 + 单流 struct 载荷 + Reset 兜底）
├── ReactiveListChange.cs            # 列表变更载荷 readonly struct（Kind/Index/Item/OldItem）
├── IReactiveDictionary.cs           # 字典接口（只读视图 IReadOnlyDictionary<TKey,TValue> + 变更流订阅）
├── ReactiveDictionary.cs            # 响应式字典（upsert + 键比较器注入 + 单流 struct 载荷 + Reset 兜底）
├── ReactiveDictionaryChange.cs      # 字典变更载荷 readonly struct（Kind/Key/Value/OldValue）
└── ReactiveCollectionChangeKind.cs  # 集合共享词表（Add / Remove / Replace / Reset）
```

十一个文件都是公开面，本模块没有 `internal` 类型。

## 集合（`ReactiveList<T>`）

怪物存活表、弹幕、队伍编成这类「一组东西变了要告诉别人」的场景用 `ReactiveList<T>`：

```csharp
using XFramework.XReactive;

var alive = new ReactiveList<Monster>(capacity: 32);

// 订阅变更。注意:不立即回调——初始化靠「订阅后立即读一次列表」
using var sub = alive.Subscribe(change =>
{
    switch (change.Kind)
    {
        case ReactiveCollectionChangeKind.Add:     // change.Index / change.Item
        case ReactiveCollectionChangeKind.Remove:  // change.Index / change.Item
        case ReactiveCollectionChangeKind.Replace: // change.Index / change.Item(新) / change.OldItem(旧)
        case ReactiveCollectionChangeKind.Reset:   // Clear / Sort——重读列表
            break;
    }
    // 任何变更后都以列表本身为准(它是唯一真相),不要拿增量拼镜像
});

alive.Add(monster);      // → Add(Index = 0)
alive.Remove(monster);   // → Remove(Index = 0)
alive.Clear();           // → 一条 Reset
alive.Dispose();         // 收尾:订阅随之全部取消
```

只读视图就是接口本身:`IReactiveList<T> : IReadOnlyList<T>`(写面只在 `ReactiveList<T>` 上),
可直接交给收 `IReadOnlyList<T>` 的 API。批量装载走构造(`new ReactiveList<T>(items)`,静默、不发通知)。

**与 `ReactiveProperty<T>` 的两处关键差别**:① 订阅**不做**立即回调——它是变更流的订阅(不重放),初始化=订阅后立即读一次列表;
② 没有「相同值去重」这条,只有**无变化不发通知**:空表 `Clear`、与旧值相等的索引器赋值、元素少于 2 的 `Sort` 都静默。

### `ReactiveList<T>` 的已知限制

- **批量 = 逐项通知**:没有 batch 作用域、没有 `AddRange` / `RemoveRange` / `RemoveAll`。批量装载走构造(静默);对已订阅的列表连续增删就是连续 N 条通知。
- **索引只在当次通知内有效**——载荷是事件的即时描述,**不是可重放的操作日志**,别拿它把变更拼进镜像;要全量状态请重读列表。
- **排序即使次序未变也发 `Reset`**(检测次序变化要多跑一趟全量比较,不做)。
- **`foreach` 零分配**:直接用本类型遍历不装箱(公开的是 `List<T>` 的结构体枚举器);**经接口**(`IReadOnlyList<T>`)遍历仍装箱一次——那是接口本身的要求,无法避免。
- **回调里不要改动本列表**:改动会嵌套派发(嵌套那一轮先跑完),外层随后把按旧索引算出的载荷投给剩余订阅者——索引可能已指向别的元素;回调里遍历本列表也不安全(他人改动使枚举器失效)。回调里订阅 / 退订是安全的。
- **相等语义是 `EqualityComparer<T>.Default`，不是引用相等**（RCT-002）：重写 `Equals` / `GetHashCode` 的类型按 `Equals` 判等（索引器赋值去重、`Remove` / 查找都如此）。要按实例区分请注入引用比较器。
- **`Sort` 抛异常时列表可能已部分重排且不补发 `Reset`**（RCT-006）：比较器抛出或检测到非法比较时，底层序列可能已改动；异常后请重读列表，不要沿用旧视图。
- **未实现 `IEquatable<T>` 的结构体每次去重 / 查找装箱**（RCT-010）：`EqualityComparer<T>.Default` 对这类 `T` 走 `object.Equals` 路径；热路径请让 `T` 实现 `IEquatable<T>`，或改用引用类型。
- **容量构造对负值抛 `ArgumentOutOfRangeException`**；`Subscribe` 的立即回调抛出时订阅不会被登记（由实现清理），`Select` 依赖这条隐含要求（RCT-011）。
- **没有** `ReactiveSet` / 有序键值 / 按值查找,也没有集合绑定 / 列表虚拟化(后者是 UI 侧既有裁定,见 `../UI/README.md` 的 `## 设计取舍`)。

## 集合（`ReactiveDictionary<TKey,TValue>`）

实体 id → 状态、槽位 → 内容、叠加状态 → 层数、资源 id → 计数——**按键寻址**且「变了要告诉别人」的场景用它：

```csharp
using XFramework.XReactive;

var buffs = new ReactiveDictionary<string, int>(StringComparer.OrdinalIgnoreCase);   // 键比较器可注入

// 订阅变更。与列表一样:不做立即回调——初始化靠「订阅后立即读一次」
using var sub = buffs.Subscribe(change =>
{
    switch (change.Kind)
    {
        case ReactiveCollectionChangeKind.Add:     // change.Key / change.Value(新)
        case ReactiveCollectionChangeKind.Remove:  // change.Key / change.Value(被移除的)
        case ReactiveCollectionChangeKind.Replace: // change.Key / change.Value(新) / change.OldValue(旧)
        case ReactiveCollectionChangeKind.Reset:   // Clear——重读字典(Key 是 default,不要读)
            break;
    }
});

buffs["poison"] = 3;        // 新键 → Add(Key="poison", Value=3)
buffs["poison"] = 2;        // 已有键 → Replace(Key="poison", Value=2, OldValue=3)
buffs.Remove("poison");     // → Remove(Value=2——被移除的值)
buffs.Clear();              // → 一条 Reset(空表时不发)
buffs.Dispose();            // 收尾:订阅随之全部取消
```

只读视图就是接口本身:`IReactiveDictionary<TKey,TValue> : IReadOnlyDictionary<TKey,TValue>`
（写面只在 `ReactiveDictionary<TKey,TValue>` 上）。批量装载走构造（`new ReactiveDictionary<K,V>(items)`，静默、不发通知）。

### 与列表的四处差别

1. **键比索引稳定,但条目会被移除**——载荷的 `Key` 不会像列表索引那样被位移;可回调内改动嵌套派发后,
   外层载荷投出时那个键**可能已经不在字典里**。要状态就重读字典。
2. **`Reset` 没有哨兵**:列表用 `Index = -1`,`TKey` 没有通用非法值,所以 `Reset` 的 `Key` 是 `default(TKey)`
   ——文档明写「不要读」。
3. **键比较器可注入**（构造重载 + `Comparer` 属性;后者是具体类的事实,接口不承诺）。**引用类型作键时尤其注意**:
   默认比较器走 `Equals`,两个「不同实例但相等」的对象会被视为同一个键——这正是本仓 Lock 审计里出过的一类
   事故;要按实例区分请注入引用比较器。
4. **遍历顺序不承诺**——不保证插入序,也不承诺底层 `Dictionary` 当前的实现序。需要顺序请用 `ReactiveList<T>`
   或自维护一张顺序表。

### `ReactiveDictionary<TKey,TValue>` 的已知限制

- 与列表同款的照旧适用:**批量 = 逐项通知**（批量装载走构造）、**回调里不要改动本字典**、
  **`foreach` 零分配**（直接用本类型不装箱;经接口遍历仍装箱一次）、**订阅不做立即回调**。
  `Keys` / `Values` 是投影视图：**首次访问会创建并缓存视图对象**，之后不再分配；遍历它们仍有装箱（RCT-004）。
- **值相等语义是 `EqualityComparer<TValue>.Default`，不是引用相等；且值比较器不可注入**（RCT-002 / RCT-007）：
  需要自定义值相等请包装类型，或改用 `ReactiveList<T>`；未实现 `IEquatable<TValue>` 的结构体每次比较装箱（RCT-010）。
- **注入自定义键比较器时，「键稳定」只对按比较器等值成立**（RCT-005）：`Replace` / `Remove` 载荷里的 `Key`
  是调用方实例，字典保留首次插入的键实例。
- **等值 upsert 是空操作,且不替换实例**:`dict[key] = v` 与当前值相等时不发通知,新实例**被丢弃**
  （与列表索引器同法）——请知道这一点的 `TValue` 才这么做。
- **键的生命周期由持有者收口**:字典不提供「键对象销毁即自动移除」——字典归谁（子系统 / 场景 / 管理器）
  就由谁在销毁处 `Remove`;框架不替使用方决定生命周期（与 Message 的淘汰纪律同姿势,见其 README 的
  「带 Key 的淘汰是语义要求」）。
- **没有** `ReactiveSet` / 有序键值 / 按值查找,也没有集合绑定 / 列表虚拟化。

## 行为契约

两条语义由底层事件流决定,都容易踩:

- **派发顺序是 LIFO**——后订阅的先收到(订阅节点插在链表头部)。框架**没有**控制订阅者相对顺序的手段,别围绕先后次序设计(比较各自收到的值也建立不了顺序)。
- **回调内写入本属性会嵌套派发,且嵌套那一轮先跑完**。订阅者 A、B 中 B 后订阅(故先收到):B 在收到 `1` 时写入 `2`,嵌套派发让 A、B 都先收到 `2`;随后外层循环继续把 **`1`** 投给 A——A 最终停在一个 `Value` 已不再是的值上,且不会再有通知来纠正它。**回调里不要写入本属性**;确实需要「收到变化后再修正」时,把写入推迟到下一帧再写,而不是就地写(框架**没有**一次性下一帧入口,可自行排期,或实现 `IUpdateable` 经 `UpdateManager` 注册)。
- **列表版(`ReactiveList<T>`)有同样的问题,且多两条**:回调内改动列表会嵌套派发(嵌套那一轮先跑完);此后外层把按**旧索引**算出的载荷投给剩余订阅者——索引可能已指向别的元素(**索引只在当次通知内有效**)。回调内遍历本列表也不安全:他人改动会让 `foreach` 抛 `InvalidOperationException`,而索引 `for` 不抛、静默读到位移后的元素。回调里**订阅与退订是安全的**(退订同轮立即生效)。
- **字典版(`ReactiveDictionary<TKey,TValue>`)同理,但形状不同**:**键是稳定的,条目不是**——嵌套那一轮可能已经把那个键移除了,外层载荷随后照发,此时按载荷的 `Key` 去查已经查不到。要状态就重读字典,别从载荷重建。

## 典型场景

### 双向绑定

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

### 排查订阅泄漏

订阅泄漏在运行时是**无声的**——没人报错、值也照常更新,只是内存与回调悄悄堆积。模块为此提供两样东西:

- **`SubscriptionCount`**(四个类型都有)——当前存活订阅数。源长期存活时(单例上的属性、Settings 句柄)这个数**只增不减**,即说明有订阅没人释放。
- **`ToString()`**——形如 `ReactiveProperty<Int32>(50)` / `ReactiveList<Int32>(Count=3)` / `ReactiveDictionary<String, Int32>(Count=3)`,与 Settings 的 `SettingRef.ToString()` 同形,排查时一眼看出是谁、现在是多少。

最常见的泄漏源就是「快速使用」里说的 `Select` 返回值被就地丢弃:

```csharp
// 症状:每次打开面板这个数就 +1
Debug.Log($"{vm.Hp} 订阅数={vm.Hp.SubscriptionCount}");
// 输出: ReactiveProperty<Int32>(100) 订阅数=3   ← 本该是 0 或 1
```

两个成员都**只用于排查**,不要拿它们做逻辑分支:它们是实现的当前状态,不是契约。

同类工具:Message 模块的 `MessageManager.GetStats()`、UI 模块的 `UIManager.DumpState()`。

> **没有自动跟踪器**:本仓只有上面这种**拉取式**诊断,没有 R3 `ObservableTracker` 那样的「列出未释放订阅**及其创建调用栈**」的编辑器工具——排查靠这两条信号 + 人工核对。

## 线程契约

**本模块按主线程专用设计——自身未做任何同步。** 底层引擎的锁与快照只保证订阅链表与终止标志**在并发退订下不被写坏**,**不构成「可以多线程读写」的许可**(详见 Event 模块 README 的「线程契约」节)。模块**不提供运行时主线程断言**：误用的后果是数据竞争（值 / 载荷分叉）而非引擎异常，由文档约定兜底；需要严格检测的项目请在自己的调用点加 Editor-only 断言（RCT-009）。

跨线程写入本模块会让**去重判断与派发载荷分叉**:最后一个订阅者见到的值不再是 `Value`,且不会再有通知来纠正它。需要从别的线程改值时,请先切回主线程。

## 接口承诺到哪为止

`IReactiveProperty<T>` 只承诺一件事：**订阅时立即同步回调当前值**。其余几条都是**具体实现的特性，不是接口保证**——按接口编程时不要当前提：

| 行为 | `ReactiveProperty<T>` / `ReadOnlyReactiveProperty<T>` | Settings 的 `SettingRef<T,TField>` |
| ---- | ---- | ---- |
| 订阅时立即回调当前值 | ✅ | ✅ |
| 相同值不通知（去重） | ✅ | ❌ 实例替换（`Load`/`Reset`/`Apply`）会**无条件重放**，值没变也回调 |
| 读取 `Value` 不抛 | ✅ 已释放后仍返回最后持有的值 | ❌ 设置类型已注销时抛 `InvalidOperationException` |
| `TryWriteValue` 失效时返回 `false` | ✅ 已释放后返回 `false` | ✅ 类型已注销后返回 `false`（见 `SettingsManager.IsRegistered`） |

`SettingRef` 那两条不是缺陷，而是它自己的取舍（重放是为了「换实例后 UI 自动跟随」；读抛是为了附上修复提示）。关键是**接口本身没有承诺它们**，所以消费者不能靠接口吃掉这些差异。

> **同模块的 `Subscribe` 有两种语义，别记混**：`IReactiveProperty<T>.Subscribe` 的契约是「订阅时立即同步回调**当前值**」；`IReactiveList<T>.Subscribe` 与 `IReactiveDictionary<TKey,TValue>.Subscribe` 是**变更流**订阅，**不做立即回调**（语义对齐 `XEvent.IEventStream<T>.Subscribe`——变更不是「当前值」，也无法重放）。集合的初始化姿势是「订阅后立即读一次集合」（主线程同步，两步之间不可能发生变更）。

### 本模块没有的（按 Rx 直觉找来的人请注意）

- **集合型响应式**（R3 `ReactiveCollection` 一类）：**部分有**——`ReactiveList<T>`（列表）与 `ReactiveDictionary<TKey,TValue>`（按键寻址的字典）提供变更通知。**没有的**：`ReactiveSet`（`IReadOnlySet<T>` 不在 .NET Standard 2.1 API 面内）、有序键值、按值查找、批量作用域与 Range API、集合的 `Select` 派生。集合绑定 / 列表虚拟化 / 条目复用仍然没有——`UIBinder` 是「一属性 → 一控件」，池化的是面板/HUD/Tip 实例，长列表由项目自建（取舍与替代路径见 `../UI/README.md` 的 `## 设计取舍`）；**两个集合类型都只提供通知源，不改变这条 UI 侧结论**。
- **「稠密遍历 + 按键寻址」的专用容器**：**没有，且实测后裁定不做**。需要它的是「每帧全量遍历是主路径、同时又要按键取」的注册表（Actor / 单位表）——但实测（2026-10-11，`List<KVP>` + 键→下标 + 交换删除的原型 vs `Dictionary<K,V>`）**三条门槛均不过**：遍历只快 1.22–1.28×、内存反而多约 1.54×、查找退化 1.45–1.61×。**正解是自建索引**：`List<T>` + `Dictionary<TKey,int>`（键→下标，删除时用末尾元素补位换取 O(1)），索引与列表同步由你自己收口。参照量级：本模块的 `ReactiveDictionary` 在 N=4096 时整表遍历约 **0.12 ms/帧**（占 16.7 ms 帧预算的 0.7%）——多数场景根本不需要为这一步做专门容器。
- **命令**（R3 的 `ReactiveCommand` / `CanExecute`）：**没有**。UI 侧走 `UIBinder.BindClick` 直接绑点击，按钮可用性由使用方自己置 `interactable`。
- **Inspector / 序列化集成**（`BindableReactiveProperty` 那类）：**没有**——本仓在 UI 模块做约定式绑定（`txt_` / `sld_` / `tgl_` 前缀），不走 Inspector 配置。

## 设计原则

- **事件流驱动** — 基于 `XFramework.XEvent` 自研事件流引擎(锁 + 快照线程模型、订阅节点池)
- **订阅立即回调** — 订阅时立即同步回调当前值(UI 初始绑定依赖此语义)
- **相同值去重** — 设置相同值不通知。「相同」默认按 `EqualityComparer<T>.Default`，可经 `ReactiveProperty<T>` 的构造重载 / `Select` 的 `comparer` 参数注入自定义比较器（浮点容差等）。集合侧的同源规则是**无变化不发通知**（空表 `Clear`、等值 upsert、元素少于 2 的 `Sort`）。**这些都是本模块类型的行为,不是接口保证**(见「接口承诺到哪为止」)
- **读宽容、写严格** — 已释放后读取 `Value` 仍返回最后持有的值（与 `ReadOnlyReactiveProperty<T>` 一致）；写入 `Value` 与订阅则抛 `ObjectDisposedException`。读是只读操作、不改变任何状态,让它抛只会把「先 Dispose 再读一次收尾值」变成必须 try/catch 的地雷;写是编程错误,应当被立刻发现。**同样只是本模块类型的行为**——`SettingRef.Value` 在设置类型注销后照抛
- **接口即只读视图** — `IReactiveProperty<T>.Value` 无 setter,写值经具体实现类型,避免外部误写状态。可写属性、`Select` 派生值、Settings 的 `SettingRef` 句柄一律实现该接口,于是绑定 API 只认接口、任何第三方实现都能接入
- **异常隔离** — **投递**路径的订阅回调抛异常记 Error 日志(分类 `Event`,由 `EventStreamImpl` 记录)后继续;订阅时的立即回调属于注册期、同步执行,它抛出的异常原样上抛(订阅已自动清理,不会泄漏)。两条路径语义不同是有意的:绑定初始化失败应当被看见,而运行期的单个订阅者出错不该拖垮其余订阅者
- **集合走单流 struct 载荷** — `ReactiveList<T>` 与 `ReactiveDictionary<TKey,TValue>` 的变更各是一条 `readonly struct`（列表：`Kind` / `Index` / `Item` / `OldItem`；字典：`Kind` / `Key` / `Value` / `OldValue`），零 GC；对照 UniRx / BCL `ObservableCollection<T>` 每次通知分配一个事件参数对象。整表操作用一条 `Reset` 兜底（「整表变了，重读」），不做逐项展开。共享词表 `ReactiveCollectionChangeKind`
- **集合不做批量与算子面** — 没有 batch 作用域 / Range API / `Select` 派生 / `ObserveAdd` 式按 Kind 分桶的算子：批量装载走构造（静默、不发通知），其余缺口等真实需求出现再加（同 Event 模块「为什么不做算子面」的取舍）
- **字典的键比较器可注入，且值相等即空操作** — `ReactiveDictionary<TKey,TValue>` 的键比较器经构造注入（引用类型作键的逃生口，先例是 Lock 审计里那类「按 `Equals` 合并」的事故）；索引器 upsert 到等值时静默且不替换实例，换来一条可测的不变量：**有通知 ⇔ 状态确实变了**（两家第三方实现——UniRx `ReactiveDictionary` 与 Cysharp `ObservableDictionary`——都不做这条，本仓刻意取家族规则）

## 依赖

- `XFramework.XEvent` 的**事件流引擎**——单向依赖:Reactive → Event。用的是它的**公开面**(`IEventStream<T>` / `EventStream.Create` / `SubscriptionCount`),不再触碰任何模块的 `Internal` 命名空间
- 全局消息总线在 XMessage 模块,需要发布/订阅消息时 `using XFramework.XMessage`;本模块**不**依赖它

> **引擎的语义契约**(派发顺序 LIFO、重入的后果、异常隔离、`Complete` 与 `Dispose` 的差别、缓冲重放)现由 [Event README](../Event/README.md) 承载——本模块「行为契约」一节描述的正是这些语义在属性上的表现,若哪天引擎改了语义,请同步改那两处。
