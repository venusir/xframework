# XFramework / Reactive 模块

## 概述

XFramework 响应式模块提供**响应式属性**。基于 XMessage 模块的事件流引擎(`XFramework.XMessage.Internal`)实现,不依赖节点树,可在任意 C# 类中使用。

- `ReactiveProperty<T>`:可写响应式值,订阅时立即回调当前值,设置相同值不通知(去重语义)
- `ReadOnlyReactiveProperty<T>`:由 `Select` 映射派生的只读属性,值随源自动变化(去重)
- 全局消息总线在 Message 模块(`XFramework.XMessage.MessageManager`),不在此模块

**命名空间**: `XFramework.XReactive`

## 架构设计

```
Runtime/Reactive/
├── IReactiveProperty.cs          # 响应式属性接口(Value 只读 + Subscribe,面向接口编程)
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

## 设计原则

- **事件流驱动** — 基于 Message 模块自研事件流引擎(锁 + 快照线程模型、订阅节点池)
- **订阅立即回调** — 订阅时立即同步回调当前值(UI 初始绑定依赖此语义)
- **相同值去重** — 设置相同值不通知
- **接口即只读视图** — `IReactiveProperty<T>.Value` 无 setter,写值经具体实现类型,避免外部误写状态
- **异常隔离** — 订阅回调抛异常记 Error 日志后继续

## 依赖

- `XFramework.XMessage` — 事件流引擎(单向依赖:Reactive → Message)
- 全局消息总线亦在 XMessage 模块,需要发布/订阅消息时 `using XFramework.XMessage`
