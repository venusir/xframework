# Localization —— 维护方技术文档

> **收件人**：框架作者 + 下一轮审计者。**不是**使用文档。
> **使用方文档**在 `Assets/XFramework/Runtime/Localization/README.md`——已知限制、设计取舍、接口承诺、线程契约、性能代价一律留在那里，本文件不复述、不复制。
> **本文件不随 UPM 包发布**：它在仓库根 `Documentation/` 下，第三方在包内看不到它。
> 变更流水记在 `CHANGELOG.md` 与 git 提交里；本文件只留**下一轮需要知道的**。

## 文件结构

```
Runtime/Localization/
├── ILocalizationManager.cs        # 本地化管理器公共接口
├── LocalizationManager.cs         # 静态外观（全局入口）
├── LocalizationManagerImpl.cs     # 默认实现（LRU 缓存）
├── LocalizationBootstrapStage.cs  # 引导阶段（IBootstrapStage，Phase 90）
├── LanguageChangedMessage.cs      # 语言切换消息（readonly struct，无装箱）
└── LanguageAssetLoader.cs          # 语言数据异步加载器（内部）
```

## 已评估未采纳与未决

**已评估未采纳**（逐条理由已在使用方 README 的 `## 设计取舍`，此处只留「本轮评估过并否决」这一层；**下一轮从这里读起**）：

2026-09-29 一轮：
- 占位符路径改走 `StringBuilderPool`
- 给 `ILocalizationManager` 加 `IsInitialized`
- `SetInstance` 时 Dispose 旧实例
- 换 JSON 库

2026-09-29 追加轮（占位符的多语言边界）：
- 在 `ReplacePlaceholders` 内部隐式回退到语言表
- 同一个 `SetPlaceholder` 槽位里靠约定区分「字面量」与「key」
- 按语言各存一份字面值（`SetPlaceholder(lang, key, value)`）
- 在 `LanguageChangedMessage` 上自动重解析占位符

不属以上索引的两条：
- `InputManager` 的 11 处同形手写订阅绑定 —— 与本次修的 `LocalizationManager.Subscribe` 同一缺陷类（闭包分配 / 只认 `MonoBehaviour` / 已取消令牌仍注册），但**不属本轮范围**，留给下一轮 Input 审计。
- 本轮**未发现新的缺陷类**，`ModuleAudit.md` 第三节无需新增。

**未决**：
- 并发切换的「后完成者胜」是否要引入代次作废。
- `SetLanguageData` 覆盖当前语言数据时是否该补一条事件面。
- **键值占位符要不要递归**：本轮定为**不递归**（与既有单趟扫描一致，值里再含 `{Key}` 不二次扫描）。
  真要做递归得先定环检测（A→B→A）与深度上限。
- **MonoBehaviour 分支的订阅绑定没有直接用例**：实测 EditMode 下 `Object.DestroyImmediate` **不触发** `MonoBehaviour.destroyCancellationToken`，该分支只能在 PlayMode 覆盖，而本模块的测试全在 `Tests/Editor/Localization/`，本轮未新增 PlayMode fixture。该分支由 `MessageManager.TryBindToDestroy` 承担，全仓目前都没有直接用例。
- **`IDestroyCancellationToken` 的实现范例有坑**（属 Message 模块，本轮只记录）：`Runtime/Message/IDestroyCancellationToken.cs` 的 XML 示例写的是 `public CancellationToken DestroyCancellationToken => _cts.Token;` 配 `Dispose()` 里 `_cts.Dispose()`——照抄会在对象销毁后抛 `ObjectDisposedException`，因为 `CancellationTokenSource.Token` 在源释放后不可读（实测撞上）。正解是构造时把令牌取出来缓存。

## 审计轮次

2026-09-29 按 `../ModuleAudit.md` 审计一轮（判据 A–F 全类扫过；本轮修了什么见 `CHANGELOG` 的 `[Unreleased]`）。本节只留**下一轮需要知道的**。

**2026-09-29 追加（占位符的多语言边界）**：由「`SetPlaceholder` 会不会有多语言需求」一问起头。
背景事实：占位符的引入提交 `04b3158` 自述动机是「减少重复传参」，与语言语义无关；全仓从未讨论过
「按语言」（grep `按语言|语言无关|per-language` 零命中）；截至当时**零生产调用点**。
第一轮只写文档（判定线 + 两条已知限制 + 两条设计取舍），第二轮落成 API：**`SetPlaceholderFromKey`**
（注册一次、跟随语言，替换的当下按当前语言 + 回退链解析，语义对齐 Unity 的 `LocalizedString`-as-variable）。
文档侧另补了「数字/日期格式跟随设备文化」这条边界。

## 沿革与已否决形状

- **JSON 格式错误曾静默降级**：旧实现在格式错误处直接跳出循环——`{"a":1}` 会产出非空字典 `{"a":""}`（一种「所有值都是空串」的语言，还绕过了「解析为空」的兜底），`{"a":"1","b"}` 会静默丢掉 `b`，`{"a":"1"} oops` 照常成功。现改为抛 `InvalidOperationException`（带字符位置）。
