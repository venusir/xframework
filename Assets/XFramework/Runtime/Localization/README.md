# XFramework / Localization 模块

## 概述

XFramework 本地化模块提供多语言文本管理功能。通过 `ILocalizationManager` 接口抽象，支持初始化时注入默认语言数据，运行时按需异步加载目标语言、LRU 缓存管理，以及格式化文本和全局占位符替换。

**命名空间**: `XFramework.XLocalization`

**语言标识**: 使用 `string` 类型（如 `"zh_Hans"`, `"en"`, `"ja"`），可自定义任意标识。

## 核心机制：LRU 缓存

`LocalizationManagerImpl` 内存中最多缓存 **4 种语言**的数据。当前语言和回退语言始终保留，不被淘汰；其余按 LRU（最近最少使用）淘汰。

- 每次 `SetLanguageData` 或 `SetLanguage` 都会将目标语言标记为"最近使用"
- 调用 `SwitchLanguageAsync` 加载新语言时，如果缓存已满 4 种，淘汰最早加载的非当前/非回退语言
- 缓存命中时语言切换为同步（零 GC），未命中时通过 YooAsset 异步加载 JSON

**「4 种」里真正可淘汰的只有 2 个**：当前语言与回退语言各占一个钉住位（默认初始化时两者是同一个值，那就只占 1 个）。所以「4」不是「能同时留住 4 种语言」，而是「2 个钉住位 + 2 个 LRU 位」。用 8 种语言来回切的场景会在 LRU 位上颠簸——每次切回被淘汰的语言都要重新走一遍资源加载与解析。

## 快速使用

### 1. 初始化

初始化时需注入默认语言和对应数据：

```csharp
using XFramework.XLocalization;
using System.Collections.Generic;

// 方式一：通过 Bootstrap 引导阶段初始化（推荐）
// 登记后由启动管线在 Phase 90 执行：
// Bootstrap.Register(new LocalizationBootstrapStage("zh_Hans", myLanguageData));
// 它不在 Bootstrap.RegisterDefaults() 的默认组合内——本地化需要语言数据，须显式登记

// 方式二：手动初始化
var defaultData = new Dictionary<string, string>
{
    { "ui_main_title", "主菜单" },
    { "ui_play_button", "开始游戏" },
};
LocalizationManager.Initialize("zh_Hans", defaultData);

// 方式三：注入自定义实现
LocalizationManager.SetInstance(myLocalizationManager);
```

### 2. 获取本地化文本

```csharp
// 通过 key 获取当前语言的文本
string text = LocalizationManager.Get("ui_main_title");

// 格式化文本（内部使用 string.Format）
string goldText = LocalizationManager.GetFormat("ui_player_gold", currentGold, maxGold);

// 判断 key 是否存在
bool exists = LocalizationManager.ContainsKey("ui_settings_title");
```

找不到 key 时先回退回退语言，仍找不到则返回 key 本身，方便调试。

### 3. 切换语言（异步加载）

已缓存的切换是同步的（零等待），未缓存的自动异步加载：

```csharp
using Cysharp.Threading.Tasks;

// 异步切换——已缓存则同步完成，否则加载 JSON 文件
await LocalizationManager.SwitchLanguageAsync("ja");

// 带取消令牌
var cts = new CancellationTokenSource();
await LocalizationManager.SwitchLanguageAsync("en", cts.Token);
```

切换前可通过 `HasLanguage` 检查是否已缓存：

```csharp
if (LocalizationManager.HasLanguage("ja"))
{
    // 已缓存，可直接同步切换
    LocalizationManager.SetLanguage("ja");
}
```

### 4. 同步切换（仅已缓存语言）

当语言数据已在缓存中时，可直接同步切换：

```csharp
// 前提：HasLanguage("en") == true
LocalizationManager.SetLanguage("en");
```

若语言未缓存，`SetLanguage` 会抛出 `InvalidOperationException`。这种情况下应使用 `SwitchLanguageAsync`。

### 5. 语言资产路径配置

语言数据 JSON 文件的 YooAsset 地址模板，默认为 `"localization/lang_{0}"`：

```csharp
// 默认值
// LocalizationManager.LanguageAssetPath == "localization/lang_{0}"

// 自定义路径
LocalizationManager.LanguageAssetPath = "i18n/{0}";

// 切换时自动拼接："i18n/ja" → AssetManager.LoadAsync<TextAsset>("i18n/ja")
await LocalizationManager.SwitchLanguageAsync("ja");
```

### 6. 手动注入语言数据

```csharp
var enData = new Dictionary<string, string>
{
    { "ui_main_title", "Main Menu" },
    { "ui_play_button", "Play" },
};
LocalizationManager.SetLanguageData("en", enData);
```

注入后该语言即进入缓存，`HasLanguage("en")` 返回 `true`。

### 7. 全局占位符

通过全局占位符，可以在所有本地化文本中自动替换命名变量（如玩家名、数量等），无需每次调用 `GetFormat` 传递参数。

**占位符有两类，按「值会不会随语言变」选**：

| 入口 | 值的来源 | 跟随语言切换？ | 用在哪 |
|---|---|---|---|
| `SetPlaceholder(name, value)` | 你给的字面量 | **否** | 玩家名、玩家自建的公会名、数量、金币 |
| `SetPlaceholderFromKey(name, localizationKey)` | 语言表的某条表项 | **是**（替换的当下解析） | 称号 / 段位 / 职业 / 日期等需要翻译的值 |

**一个名字只有一种含义**：同一个名字先后用两个入口注册，后注册的覆盖前者（内部两张表互斥）。

> **两类各自最容易踩的坑**
>
> - **用 `SetPlaceholder` 存需要翻译的值**（`SetPlaceholder("Guild", Get("guild_legendary"))`）——
>   取的是**注册当时**那个语言的值，此后再没人更新它。症状是：
>   > 切换语言后，句子模板换成了英文，**嵌进去的称号还是中文**。没有异常、没有日志。
>
>   `LanguageChangedMessage` 的语义只是「语言变了」，不覆盖「你注册的派生值过期了」。
>   这种值改用 `SetPlaceholderFromKey`（多处复用同一个绑定时），或一次性嵌句时用 `Get` + `GetFormat`。
> - **用 `SetPlaceholderFromKey` 存语言无关的值**（玩家名）——不是错，只是白绕一次表；而且玩家名若
>   恰好等于某个表键，还会被**翻译掉**。语言无关的值就该用 `SetPlaceholder`。

#### 按语言取值的占位符

```csharp
LocalizationManager.SetPlaceholderFromKey("Guild", "guild_legendary");   // 注册一次

// 本地化文本："ui_guild_info": "{Guild} - 等级 {0}"
LocalizationManager.GetFormat("ui_guild_info", "5");
// 当前语言 zh_Hans → "传奇公会 - 等级 5"
// 切到 en 之后     → "Legendary Guild - 等级 5"
```

四条规则：

- **注册一次、跟随语言**：替换发生的当下按当前语言（含回退链）解析，不需要在语言切换时重新注册。
- **解析规则与 `Get` 同一条**：先当前语言、再回退语言；两边都没有时**返回那条表项的键本身**，便于发现漏配。
- **单趟、不递归**：解析出来的值里若再含 `{Other}`，**不会被二次扫描**，按字面输出。
  （将来若真要递归，需要先定环检测与深度上限。）
- **它不解决「句子碎片化」**：把一句话拆成几个片段再拼（如 `"按下 {ClickOk} 继续"`）在语序、格变化、
  （性、数）不一致的语言里会出错——ICU 的说法是「a message has to be written and translated as
  **a single unit**... translators would not be able to rearrange the pieces」。**整句仍是首选**；
  键值占位符适合的是**能独立成词的值**：称号、职业名、段位。

#### 语言无关的值

```csharp
// 注册全局占位符
LocalizationManager.SetPlaceholder("PlayerName", "张三");
LocalizationManager.SetPlaceholder("GuildName", "传奇公会");

// 本地化文本（JSON 文件中）
// "ui_welcome": "欢迎回来，{PlayerName}！"
// "ui_guild_info": "{GuildName} - 等级 {0}"

// Get 时自动替换
string welcome = LocalizationManager.Get("ui_welcome");
// → "欢迎回来，张三！"

// 与 GetFormat 混用：先替换占位符，再执行 string.Format
string info = LocalizationManager.GetFormat("ui_guild_info", "5");
// → "传奇公会 - 等级 5"
```

还有一条不复用绑定时最省事的路——一次性嵌句，调用点在调用时就知道当前语言，不存在陈旧问题：

```csharp
var guild = LocalizationManager.Get("guild_legendary");            // 按当前语言取
var info  = LocalizationManager.GetFormat("ui_guild_info", guild); // 表里写 "{GuildName} - 等级 {0}"
```

（注意 `UIBinder.BindToLocalizedText` 只接一个 key、不接参数，这条路径需要手动 `GetFormat` + 订阅刷新。）

**占位符替换规则：**
- 语法：`{Key}`（`{` + 占位符名称 + `}`）
- 替换在 `string.Format` 之前执行，两者可安全混用
- 未注册的占位符保持原样输出
- 未设置任何占位符（**两类都算**）时无 GC 开销（快速路径跳过）

**两类花括号语法（重要）**：`GetFormat` 里「命名占位符」与「位置参数」共用花括号，必须分清：

| 写法 | 含义 | `GetFormat` 里的行为 |
|---|---|---|
| `{PlayerName}` | 全局占位符（两类） | 已注册 → 替换；未注册 → **原样显示**，不会被参数填 |
| `{0}` / `{0,-5}` / `{0:N2}` | `string.Format` 位置参数 | 照常格式化 |
| `{{` / `}}` | 字面花括号 | 显示为 `{` / `}` |

即：**要由 `GetFormat` 的实参填的值，必须写成 `{0}` 这种位置形式**。写成 `{GuildLevel}` 只会原样显示
（实现会把非格式项的花括号转义为字面量——否则 `string.Format` 会直接抛 `FormatException`）。

**管理占位符：**

```csharp
// 更新占位符值（如玩家改名）
LocalizationManager.SetPlaceholder("PlayerName", "李四");

// 移除单个占位符
LocalizationManager.RemovePlaceholder("PlayerName");

// 判断占位符是否存在
bool exists = LocalizationManager.HasPlaceholder("PlayerName");

// 清空所有占位符（如退出登录）
LocalizationManager.ClearPlaceholders();
```

四个管理入口对**两类占位符一视同仁**：`RemovePlaceholder` / `ClearPlaceholders` 两种都摘，
`HasPlaceholder` 两种都算。

### 8. 语言切换通知

语言切换通过 `MessageManager` 广播（`LocalizationManager` 本身不暴露 C# event），订阅方可经模块归口入口或消息总线直接订阅：

```csharp
using XFramework.XMessage;

// 方式一：模块归口入口(底层即 MessageManager.Subscribe)
var subscription = LocalizationManager.Subscribe(msg =>
{
    Debug.Log($"语言已切换为: {msg.Language}");
});

// 方式二：直接订阅消息(与方式一等价,适合已有 using XFramework.XMessage 的代码)
var direct = MessageManager.Subscribe<LanguageChangedMessage>(msg =>
{
    Debug.Log($"语言已切换为: {msg.Language}");
});

// 取消订阅：订阅句柄本身即 IDisposable
subscription.Dispose();
direct.Dispose();
```

### 9. 当前状态

```csharp
string current = LocalizationManager.CurrentLanguage;   // 如 "ja"
string fallback = LocalizationManager.FallbackLanguage;  // 如 "zh_Hans"
bool initialized = LocalizationManager.IsInitialized;    // true
```

## 本地化数据格式

### JSON 文件结构

语言数据文件为 `TextAsset`，内容为扁平的 JSON 键值对对象：

```json
{
    "ui_main_title": "メインメニュー",
    "ui_play_button": "ゲーム開始",
    "ui_gold_format": "ゴールド: {0}",
    "error_connection": "接続エラー"
}
```

文件放置路径需匹配 `LanguageAssetPath`——那是 **YooAsset 地址模板**（默认 `"localization/lang_{0}"`，拼出
`"localization/lang_ja"`），不是 `Resources` 路径：加载走的是 `AssetManager.LoadAsync<TextAsset>`，
所以文件要按 YooAsset 的可寻址规则（收集器 / 分组）配进去，光放进 `Resources` 目录不会生效。

### JSON 解析策略

模块内使用自定义的轻量 JSON 解析器（`LanguageAssetLoader.ParseJson`），仅支持 `"string": "string"` 的简单格式，无需引入 Newtonsoft.Json 或其他第三方库。如果 JSON 含嵌套结构或数组，需替换为完整 JSON 库。

解析器的两条边界要知道，它们都**从静默改为硬失败**（2026-09-29 审计修）：

- **转义序列会解码**：`\" \\ \/ \b \f \n \r \t \uXXXX`（含代理对）。这条不是可选项——`System.Text.Json` 默认就把非 ASCII 转义成 `\uXXXX`，Python 的 `json.dumps` 默认 `ensure_ascii=True`，不解码就是整张表把 `中文` 原样显示给玩家。
- **格式错误抛 `InvalidOperationException`（带字符位置）**，不再返回「已经读到的半张表」。
  宽容的例外只有两个、且都不影响数据完整性：**尾随逗号**与 **UTF-8 BOM**。

## 引导集成

### LocalizationBootstrapStage

引导阶段（`public sealed`），实现 `IBootstrapStage`（= Pipeline 的 `IPhaseStage` + `Shutdown`），在启动管线的相位分组 **Phase 90** 执行，晚于框架内置相位（0/3/4）：

```csharp
Bootstrap.Register(new LocalizationBootstrapStage("zh_Hans", myLanguageData));
```

构造参数即 `LocalizationManager.Initialize(lang, data)` 的两个实参；**未提供语言数据时**它打一条警告并直接置「已完成」——本地化是可选模块，缺数据不该让整个启动失败。

反向清理经其 `Shutdown`：**只销毁本阶段自己初始化的那份**。空转路径（无数据）什么都没建，`Shutdown` 便什么都不做；
使用方先手动 `Initialize` / `SetInstance`、再登记本阶段时，`Initialize` 会告警并忽略，本阶段同样不算「初始化过」。
重复调用是空操作（`IBootstrapStage` 要求幂等）。

### LanguageAssetLoader

内部异步加载器（非节点），由 `LocalizationManager.SwitchLanguageAsync` 在缓存未命中时创建并执行：

1. 检查缓存——已命中则直接同步切换（`SwitchLanguageAsync` 调用前亦先查缓存）
2. 缓存未命中——通过 `AssetManager.LoadAsync<TextAsset>` 加载 JSON 文件
3. 解析 JSON → 注入缓存 → 同步切换
4. 支持 `CancellationToken` 取消（`OperationCanceledException` 传播，不静默）

## 设计原则

- **接口可替换** — 通过 `ILocalizationManager` 接口，可替换底层实现
- **按需加载** — 初始化仅注入默认语言，其他语言首次切换时异步加载
- **LRU 缓存** — 最多 4 种语言驻留内存，当前+回退始终保留
- **零外部依赖** — JSON 解析自实现，不依赖 Newtonsoft.Json
- **静态外观** — `LocalizationManager` 提供全局入口，任意位置可调用
- **格式化支持** — 支持 `string.Format` 语法的参数化文本
- **全局占位符** — 支持 `{Key}` 语法全局替换，减少重复传参；分「字面量」与「指向语言表项」两类（后者跟随语言切换）

## 依赖

- `XFramework.XAsset` — 通过 `AssetManager` 加载语言 JSON 文件
- `XFramework.XMessage` — `LanguageChangedMessage` 经 `MessageManager` 广播（发布的替代品是它，模块不暴露 C# event）
- `XFramework.XBootstrap` — `IBootstrapStage` 引导阶段契约（LocalizationBootstrapStage，Phase 90）
- `XFramework.XPipeline` — `IPhaseStage` 相位阶段契约与相位分组
- `UniTask`（框架层已提供）

## 已知限制

知道边界比以为没有边界安全。以下都是刻意的取舍、跨模块的能力缺口或行为变更，**不是待修的缺陷**：

- **语言切换必须在主线程调用。** `SetLanguage` 会经 `MessageManager.Publish` 广播 `LanguageChangedMessage`，而消息总线的发布入口有主线程契约（Editor 下越线程调用会记一条 `[Message]` 前缀的 Error）。本模块自身不做任何线程同步：`Get` 从任意线程读是安全的，**但改状态（`SetLanguage` / `SetLanguageData` / `SetPlaceholder` / `SetPlaceholderFromKey`）不是**。这条此前在文档里一字未提。
- **并发切换语言是「后完成者胜」，不是「后请求者胜」。** `SwitchLanguageAsync` 没有在途去重、没有代次作废：快速连点 EN → JA → KO 时，若 JA 的资源加载最慢，最终生效的是 JA。要严格按最后一次选择收口，调用方得自己串行化（前一次 `await` 完再发下一次，或取消上一次的令牌）。
- **`SetLanguageData` 覆盖「当前语言」的数据时不会发通知。** 查询面立刻变了（`Get` 返回新文本），事件面没有变化（`LanguageChangedMessage` 只在 `SetLanguage` 真的换了语言时发）——已订阅刷新的 UI 会继续显示旧文本。运行时热更语言表要自己再触发一次刷新。
- **注入的 `Dictionary` 按引用持有，不拷贝。** `Initialize` / `SetLanguageData` / `LocalizationBootstrapStage` 的构造参数都是如此——注入即视为移交所有权，事后改这个字典会直接改到已加载的语言表。
- **键值占位符的值若含 `{0}`，`GetFormat` 会把它当格式项消费掉。** 替换发生在 `string.Format` 之前，所以一条表项写作 `"ui_tip": "确认({0})"`、又被 `SetPlaceholderFromKey` 引进来时，`{0}` 会被调用方传的实参填上。这条今天就存在（字面量占位符同理），键值占位符只是把暴露面略微放大：值来自语言表，而表是译者/工具产出的。要显示字面量花括号就写 `{{0}}`。
- **`GetFormat` 会吞掉非法花括号。** 非复合格式项的花括号（未注册的 `{Name}`、游离的 `}`）按字面显示，不再抛 `FormatException`；依赖那个异常做表校验的用法会静默。合法的格式项照旧，索引越界（如表里写 `{1}` 而只传了 1 个实参）仍由 `string.Format` 报错。
- **`Get` 在占位符路径上有分配。** 未注册任何占位符时走零分配快路径（原样返回缓存里的字符串实例）；一旦注册了占位符且文本含 `{`，每次 `Get` 会分配一个 `StringBuilder` 与结果串。
- **全局占位符的值不随语言切换刷新。** 它是语言无关的一张表：值**来自语言表**时（`SetPlaceholder("Guild", Get("guild_legendary"))`），切换语言后模板更新了、嵌进去的值还是旧的——**没有异常、没有日志**，症状是「英文句子里嵌着中文称号」。与「`SetLanguageData` 覆盖当前语言数据不发通知」同族：查询面变了、事件面没有。需要跟随时走 `Get` + `GetFormat`，见 §7 的判定线。
- **`GetFormat` 的数字 / 日期格式跟随设备文化，不跟随 `CurrentLanguage`。** 它用的是默认 `string.Format` 重载 → `CultureInfo.CurrentCulture`：德语设备上跑英文界面会得到 `1,5` 而不是 `1.5`。**如实标为「观察到的边界，不是缺陷」**——数字格式跟随设备文化本身是有争议但常见的选择，而本模块没有文化概念，也没有带 `IFormatProvider` 的重载。要有确定行为请自行格式化后当参数传入。
- **`MaxCachedLanguages = 4` 是自定值**，无对标物（Unity Localization 不做 LRU，它靠 Addressables 的引用计数与 `Release`）。且「4」里真正可淘汰的只有 2 个，见「核心机制」一节。
- **`LanguageAssetLoader` 是内部类型**，没有公开的加载扩展点：要换数据源（比如从远端拉表）只能替换整个 `ILocalizationManager` 实现，或改 `LanguageAssetPath` 让它指向别的资源。

## 设计取舍

- **自研 JSON 解析器，不引 Newtonsoft.Json / System.Text.Json。** 「零外部依赖」是模块自述的取舍，代价是只支持 `"string": "string"` 的扁平对象。**但代价不包括「不解码转义」**——转义不解码与格式错误的静默降级是缺陷，已修（见「JSON 解析策略」）。
- **缺键返回键本身，不抛也不记日志。** 与 .NET `ResourceManager` 一致（缺资源返回资源名），也是行业默认。好处是缺表时 UI 显示的是可搜索的 key 而不是空白；代价是「表漏了一条」在运行期没有信号——要检测就用 `ContainsKey`，或在导出侧校验。
- **不给 `ILocalizationManager` 加 `IsInitialized`。** 接口加成员对第三方实现是**源码破坏**（先例：`IPoolable` 不为新能力加成员），而门面的 `IsInitialized` 已经够用。代价是门面那个属性的语义是「**已经装了实例**」，不是「实例已装好数据」——`SetInstance` 传一个未初始化的实现进去，它照样返回 `true`（此时取值按「找不到」处理，返回键本身）。
- **`SetInstance` 不 Dispose 被替换掉的旧实例。** 本 impl 的 `Dispose` 不做任何非托管释放（只清集合、置空引用），旧实例被替换后交给 GC 回收等价；硬做反而会给「临时换实现」的场景埋雷。
- **不做并发切换的代次作废。** 要引入状态与一个作废窗口，而「后完成者胜」在多数使用方那里已被 UI 交互天然串行化。先记为已知限制（见「未决」）。
- **占位符替换没走 `StringBuilderPool`。** 分配只发生在「注册了占位符且文本含 `{`」时，而典型热路径 `GetFormat` 本来就要走 `string.Format`（必然分配）；频度未实测，而全仓目前**零使用先例**，引它会新增 Localization → XPool 的跨模块依赖。先记为已知限制。
- **不做 `SetPlaceholder(string lang, string key, string value)`（按语言各存一份字面值）。** 四条理由：① 与语言表机制重复——需要翻译的值本来就该在表里；② 调用方要按每种语言各注册一次，漏一种就是一个缺口；③ 新加载一种语言时不会自动有值；④ 它会把更好的那条路（**值指向一条表项**）堵死。
  对标物给的就是后者：Unity Localization 1.0.0 让 `LocalizedString` 本身可作变量（"When accessed in a Smart String this gives access to the **translated value**"），由替换发生的当下解析——变量仍是语言无关的一个引用，翻译发生在替换时。
- **不在同一个 `SetPlaceholder` 槽位里靠约定区分「字面量」与「key」。** 歧义只是从命名空间挪到了**值槽位**：判据仍是「这个字符串在表里存不存在」——玩家恰好叫 `Settings`、或表里有个键等于某人昵称时，**字面量会被静默翻译掉**；反过来「就想显示字面量 `guild_legendary`」再无表达方式（无法转义）。本仓所有回退**都留痕**（`Data` 的 `saveType` 回退、`Settings` 回退默认值、`Save` 侧车回退、`Input` 找不到 action 都有 `LogWarning`），静默回退与这条惯例相反。
- **不让占位符在 `LanguageChangedMessage` 上自动重解析。** 要正确就得保证「占位符刷新早于 UI 刷新」，而订阅顺序是使用方定的，模块给不出这条保证；自动做一半比不做更糟（会间歇性地渲染出旧值）。这正是「键值占位符」存在的理由——把翻译推迟到替换的当下，就不存在顺序问题。
- **键值占位符做成独立的入口，不在 `ReplacePlaceholders` 里隐式回退到语言表。** 三条理由：① 隐式回退合并的是**占位符命名空间与语言表键空间**——表键有几百上千个、由译者与工具产出，替换与否于是取决于「表里恰好有没有同名键」（本仓审计手册 E 类的形状：编译期无提示、运行期无日志）；② 它会作废「未设置任何占位符时无 GC 开销」这条已有承诺——任何含 `{` 的表文本都得走扫描与分配，**且是在一个占位符都没注册的默认状态下**；③ 它会给「句子碎片化」这个 i18n 经典反模式配上最省事的写法。独立入口把「显式声明」这道门槛留着，好处一个不少。
- **判定线：宽容于「数据完整」的畸形，严格于「静默丢数据」的畸形。** 具体到 JSON 解析器：尾随逗号与 BOM 容忍；缺逗号不宽容（`{"a":"1" "b":"2"}` 在旧实现里被静默跳过、数据无损，但它是非法 JSON，会在 `jq` / 编辑器 / 转换工具那边炸——那正是最该暴露它的地方）。入参防御同理：**问句对空值答 `false`**（`HasLanguage` / `ContainsKey`），**动作对空值抛 `ArgumentNullException`**（`Get` / `GetFormat` / `SetLanguage`）。
