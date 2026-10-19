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
├── LocalizationLanguageInfo.cs    # 诊断回读载荷：CopyLoadedLanguages 的列表元素（2026-10-16 补录——它一直是公开面，此前漏在本树之外）
├── LanguageChangedMessage.cs      # 语言切换消息（readonly struct，无装箱）
└── LanguageAssetLoader.cs          # 语言数据异步加载器（内部）
```

## 决策台账

（逐条理由已在使用方 README 的 `## 设计取舍`，此处只留「本轮评估过并否决」这一层；**下一轮从这里读起**。）

- 不做：**占位符路径改走 `StringBuilderPool`**（2026-09-29 · 审计轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**给 `ILocalizationManager` 加 `IsInitialized`**（2026-09-29 · 审计轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**`SetInstance` 时 Dispose 旧实例**（2026-09-29 · 审计轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**换 JSON 库**（2026-09-29 · 审计轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**在 `ReplacePlaceholders` 内部隐式回退到语言表**（2026-09-29 · 追加轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**同一个 `SetPlaceholder` 槽位里靠约定区分「字面量」与「key」**（2026-09-29 · 追加轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**按语言各存一份字面值（`SetPlaceholder(lang, key, value)`）**（2026-09-29 · 追加轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 不做：**在 `LanguageChangedMessage` 上自动重解析占位符**（2026-09-29 · 追加轮）——理由见使用方 README 的 `## 设计取舍`。无重开判据。
- 已做：**`InputManager` 的 11 处订阅绑定改走 `TryBindToDestroy`**（2026-09-30 · `fc07a5b`）——闭包分配 / 只认 `MonoBehaviour` / 已取消令牌仍注册三处缺陷一并修复；`Input.md` 仍未认领该事实，建议在 Input 台账补交叉引用。
- 已做：**审计轮未发现新的缺陷类**（2026-09-29 · 审计轮）——`ModuleAudit.md` 第三节无需新增。
- 已做：**五处零断言与陈旧注释补齐**（2026-10-19 · R3-LOC-007/008，本轮补测试）——新增 `ToString` 两态、MRU 顺序与 LRU 淘汰、同实例 `SetInstance` 三条用例；订正 `LocalizationSubscribeTests` 的陈旧注释。测试抓到 `ToString` 当前语言分支多一个 `)` 的缺陷并修正。
- 已做：**坏路径模板与空键告警**（2026-10-19 · R3-LOC-004/006，本轮修复）——`LanguageAssetPath` 缺 `{0}` 时语言被静默忽略、坏模板抛裸 `FormatException`；空键入库后不可达且零告警。修法：缺占位符 Warning、坏模板包成带模块前缀的 `InvalidOperationException`、空键 Warning 并跳过；回归 `LanguageAssetLoaderTests` 三例。
- 已做：**格式说明里的 `{` 按字面处理**（2026-10-19 · R3-LOC-003，本轮修复）——`{0:0{0}` 这类非格式项被交给 `string.Format` 抛 `FormatException`；旧字面化只转义开头，内层 `{0}` 又被当成新格式项。修法：非格式项从 `{` 到下一个 `}` 整段按字面；回归 `GetFormat_FormatSpecWithBrace_RendersLiterally`（改前红）。IL2CPP 行为仍需真机探针（登记 `未做`）。
- 已做：**Bootstrap 阶段二次执行保留所有权**（2026-10-19 · R3-LOC-002，本轮修复）——第二次 `ExecuteAsync` 时 `alreadyInitialized` 为真（正是本阶段建的），却把 `_initializedByThisStage` 覆写为 false；空数据分支也直接置 false，`Shutdown` 漏销毁。修法：所有权只在「本次确实创建」时置位、二次执行保留；回归 `LocalizationBootstrapStageTests.ExecuteAsync_Twice_ShutdownStillDestroysOwnManager`（改前红）。
- 不做：**并发切换的「后完成者胜」要不要引入代次作废**（2026-09-29 · 审计轮；2026-10-19 R3-ADJ-03 裁定为不做）——当前语义可接受；代次作废会增加状态机复杂度，且无使用方需求。无重开判据。
- 暂不做：**`SetLanguageData` 覆盖当前语言数据时要不要补一条事件面**（2026-09-29 · 审计轮；2026-10-19 R3-ADJ-04 裁定为暂不做）——覆盖当前语言是低频运维动作，新增事件会扩大公开面。重开判据：使用方运行期覆盖当前语言数据且绑定文本需自动刷新。
- 暂不做：**键值占位符不做递归**（2026-09-29 · 追加轮）——本轮定为**不递归**（与既有单趟扫描一致，值里再含 `{Key}` 不二次扫描）。重开判据：真要做递归时，先定环检测（A→B→A）与深度上限。
- 已做：**MonoBehaviour 分支的订阅绑定补上直接用例**（2026-10-12 · `3b16e06`，`Tests/Runtime/Localization/LocalizationDestroyBindingTests` 2 例）——EditMode 下 `Object.DestroyImmediate` **不触发** `MonoBehaviour.destroyCancellationToken`（Editor 侧那份 fixture 自己记着这句话），该分支只能在 PlayMode 覆盖；这是本模块第一次有 PlayMode 侧用例，验的是**本模块自己的归口入口** `LocalizationManager.Subscribe` → `MessageManager.TryBindToDestroy`：① 对象销毁后跨帧自动退订；② **同帧边界**——`Destroy` 已调用但帧还没走完时令牌尚未取消、回调照常送达（实测确认：`destroyCancellationToken` 在帧末才取消，不是 `Destroy()` 当场）。注：UI 与 Lock 侧此前已各有同类用例，本条此前说的是本模块的入口没有。

- 暂不做：**`TextAsset` 重载仍保留双重重载**（2026-10-18 · 文档订正）——新增 `(string, TextAsset)` 重载使既有 `("en", null)` 变 CS0121（LOC-001）；本轮只文档声明「显式 `null` 需强转」，静态工厂改造留待公开 API 变更授权。重开判据：出现按旧写法调用并升级的使用方。
- 已做：**在途 `SwitchLanguageAsync` 的代次守卫**（2026-10-18 · `ea8dbd1`）——门面加实例代次，loader 捕获发起时实例 + 代次，`await` 后先查代次再解析，过期则 Warning + 丢弃；`SetInstance` 传同一实例不自增（LOC-004）。
- 已做：**空语言表统一为「空表 = 缺表」**（2026-10-18 · `999f6ee`）——引导阶段 Warning + 跳过（不置已初始化），异步加载器维持硬失败；显式 `Initialize` 仍按调用方数据安装（LOC-006）。

## 审计轮次

2026-09-29 按 `../ModuleAudit.md` 审计一轮（判据 A–F 全类扫过；本轮修了什么见 `CHANGELOG` 的 `[0.3.0]`）。本节只留**下一轮需要知道的**。

**2026-09-29 追加（占位符的多语言边界）**：由「`SetPlaceholder` 会不会有多语言需求」一问起头。
背景事实：占位符的引入提交 `04b3158` 自述动机是「减少重复传参」，与语言语义无关；全仓从未讨论过
「按语言」（grep `按语言|语言无关|per-language` 零命中）；截至当时**零生产调用点**。
第一轮只写文档（判定线 + 两条已知限制 + 两条设计取舍），第二轮落成 API：**`SetPlaceholderFromKey`**
（注册一次、跟随语言，替换的当下按当前语言 + 回退链解析，语义对齐 Unity 的 `LocalizedString`-as-variable）。
文档侧另补了「数字/日期格式跟随设备文化」这条边界。

## 沿革与已否决形状

- **JSON 格式错误曾静默降级**：旧实现在格式错误处直接跳出循环——`{"a":1}` 会产出非空字典 `{"a":""}`（一种「所有值都是空串」的语言，还绕过了「解析为空」的兜底），`{"a":"1","b"}` 会静默丢掉 `b`，`{"a":"1"} oops` 照常成功。现改为抛 `InvalidOperationException`（带字符位置）。
