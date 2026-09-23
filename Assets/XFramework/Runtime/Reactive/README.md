# XFramework / Reactive 模块

## 概述

XFramework 响应式模块提供**响应式属性**。基于 XMessage 模块的事件流引擎(`XFramework.XMessage.Internal`)实现,可在任意 C# 类中使用。

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

事件流引擎位于 Message 模块(`Runtime/Message/Internal/`,`XFramework.XMessage.Internal`)。

## 快速使用

```csharp
using XFramework.XReactive;

// 创建响应式属性(实现 IReactiveProperty<int>,可面向接口编程)
var healthProp = new ReactiveProperty<int>(100);

// 订阅值变化(订阅时立即回调当前值)
var subscription = healthProp.Subscribe(newValue =>
{
    Debug.Log($"血量变化: {newValue}");
    // 更新血量条 UI
});

// 修改值(自动推送;相同值不通知)
healthProp.Value = 80;   // 输出: 血量变化: 80
healthProp.Value = 50;   // 输出: 血量变化: 50

// 只读派生:UI 展示层可持有只读视图,写值仅经源属性
IReactiveProperty<int> view = healthProp;
var levelLabel = healthProp.Select(lv => $"Lv.{lv}");

// 取消订阅
subscription.Dispose();
```

## 双向绑定

`IReactivePropertyWriter<T>` 是「确实需要写入」时按需索取的能力接口——`ReactiveProperty<T>` 与 Settings 的 `SettingRef<T,TField>` 各自实现，第三方实现同样可接入 `UIBinder.BindTwoWay`。只读接口 `IReactiveProperty<T>` 本身不因此多出 setter。

```csharp
if (!writer.TryWriteValue(value, out var actual))
    return;   // 目标已失效:返回 false 而不抛异常,绑定层不该把异常抛进 UI 事件回调

// actual 是写入后目标实际持有的值(目标可能规范化:取整、钳制到上下限)。
// 用它,不要再读 writer.Value——写入会同步派发通知,某个订阅者可能在派发中释放
// 目标,那之后再读 Value 就会抛,正是本接口要挡掉的那类异常。
```

`false` 严格表示**未写入**（目标已失效），此时 `actual` 为 `default`；成功则返回 `true` 并给出写入后的值。

## 派发顺序与重入

两条语义由底层事件流决定,都容易踩:

- **派发顺序是 LIFO**——后订阅的先收到(订阅节点插在链表头部)。需要确定的先后顺序时不能依赖订阅次序,应在回调里自行判断收到的值。
- **回调内写入本属性会嵌套派发,且嵌套那一轮先跑完**。订阅者 A、B 中 B 后订阅(故先收到):B 在收到 `1` 时写入 `2`,嵌套派发让 A、B 都先收到 `2`;随后外层循环继续把 **`1`** 投给 A——A 最终停在一个 `Value` 已不再是的值上,且不会再有通知来纠正它。**回调里不要写入本属性**;确实需要「收到变化后再修正」时,把写入排到下一帧(如经 `UpdateManager` 注册)而不是就地写。

## 设计原则

- **事件流驱动** — 基于 Message 模块自研事件流引擎(锁 + 快照线程模型、订阅节点池)
- **订阅立即回调** — 订阅时立即同步回调当前值(UI 初始绑定依赖此语义)
- **相同值去重** — 设置相同值不通知
- **读宽容、写严格** — 已释放后读取 `Value` 仍返回最后持有的值（与 `ReadOnlyReactiveProperty<T>` 一致）；写入 `Value` 与订阅则抛 `ObjectDisposedException`。读是只读操作、不改变任何状态,让它抛只会把「先 Dispose 再读一次收尾值」变成必须 try/catch 的地雷;写是编程错误,应当被立刻发现
- **接口即只读视图** — `IReactiveProperty<T>.Value` 无 setter,写值经具体实现类型,避免外部误写状态。可写属性、`Select` 派生值、Settings 的 `SettingRef` 句柄一律实现该接口,于是绑定 API 只认接口、任何第三方实现都能接入
- **异常隔离** — **投递**路径的订阅回调抛异常记 Error 日志后继续;订阅时的立即回调属于注册期、同步执行,它抛出的异常原样上抛(订阅已自动清理,不会泄漏)。两条路径语义不同是有意的:绑定初始化失败应当被看见,而运行期的单个订阅者出错不该拖垮其余订阅者

## 依赖

- `XFramework.XMessage` — 事件流引擎(单向依赖:Reactive → Message)
- 全局消息总线亦在 XMessage 模块,需要发布/订阅消息时 `using XFramework.XMessage`
