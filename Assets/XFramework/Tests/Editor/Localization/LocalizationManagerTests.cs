using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLocalization;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// <see cref="LocalizationManager"/> 的行为测试：`GetFormat` 与两个花括号语法（命名占位符 vs
    /// <c>string.Format</c>）的共存边界，以及取值的回退链。
    /// <para>本 fixture 的存在理由是一条真实的组合缺陷：<see cref="LocalizationManager"/> 承诺
    /// 「未注册的占位符保持原样输出」且「占位符替换在 string.Format 之前执行」——两条合起来，
    /// 一段含未注册 <c>{Name}</c> 的表文本交给 <c>GetFormat</c> 就是一个 <c>FormatException</c>，
    /// 而 README 的示例正是这么写的。</para>
    /// </summary>
    class LocalizationManagerTests
    {
        [SetUp]
        public void SetUp()
        {
            // 防御：上一个 fixture 若在 TearDown 之前失败，静态门面会带着它的状态进来
            LocalizationManager.Destroy();
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "seed", "seed" } });
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        private static void UseTable(params (string Key, string Value)[] entries)
        {
            var table = new Dictionary<string, string>();
            foreach (var (key, value) in entries)
                table[key] = value;

            LocalizationManager.SetLanguageData("ja", table);
            LocalizationManager.SetLanguage("ja");
        }

        #region GetFormat × 命名占位符

        [Test]
        public void GetFormat_UnregisteredNamedPlaceholder_RendersLiterally()
        {
            // README「全局占位符」一节的示例原文，一字未改
            UseTable(("ui_guild_info", "{GuildName} - 等级 {GuildLevel}"));
            LocalizationManager.SetPlaceholder("GuildName", "传奇公会");

            var info = LocalizationManager.GetFormat("ui_guild_info", "5");

            Assert.AreEqual("传奇公会 - 等级 {GuildLevel}", info,
                "未注册的命名占位符应原样输出；旧实现把它交给 string.Format，抛 FormatException");
        }

        [Test]
        public void GetFormat_UnregisteredNamedPlaceholder_WithoutAnyPlaceholderRegistered()
        {
            // 一个占位符都没注册时，ReplacePlaceholders 直接早退——这条路径同样会炸
            UseTable(("ui_guild_info", "{GuildName} - 等级 {0}"));

            var info = LocalizationManager.GetFormat("ui_guild_info", "5");

            Assert.AreEqual("{GuildName} - 等级 5", info);
        }

        [Test]
        public void GetFormat_MixedNamedAndPositional_SubstitutesBoth()
        {
            // README 示例想表达的写法：可变值走 {0}，全局值走 {Name}
            UseTable(("ui_guild_info", "{GuildName} - 等级 {0}"));
            LocalizationManager.SetPlaceholder("GuildName", "传奇公会");

            Assert.AreEqual("传奇公会 - 等级 5", LocalizationManager.GetFormat("ui_guild_info", "5"));
        }

        #endregion

        #region GetFormat × string.Format 既有能力不得回归

        [Test]
        public void GetFormat_PositionalItem_StillFormats()
        {
            UseTable(("ui_gold", "ゴールド: {0}"));

            Assert.AreEqual("ゴールド: 42", LocalizationManager.GetFormat("ui_gold", 42));
        }

        [Test]
        public void GetFormat_FormatSpecifierAndAlignment_StillWork()
        {
            UseTable(("n", "{0:N2}"), ("a", "[{0,-5}]"));

            Assert.AreEqual("1.50", LocalizationManager.GetFormat("n", 1.5));
            Assert.AreEqual("[x    ]", LocalizationManager.GetFormat("a", "x"));
        }

        [Test]
        public void GetFormat_EscapedBraces_RenderLiteral()
        {
            UseTable(("code", "{{literal}}"));

            Assert.AreEqual("{literal}", LocalizationManager.GetFormat("code"));
        }

        [Test]
        public void GetFormat_StrayClosingBrace_DoesNotThrow()
        {
            UseTable(("odd", "闭合花括号 } 单独出现"));

            Assert.AreEqual("闭合花括号 } 单独出现", LocalizationManager.GetFormat("odd"),
                "游离的 '}' 在 string.Format 里是硬错误，表文本里却可能真实存在");
        }

        [Test]
        public void GetFormat_MissingKey_FallsBackToKeyThenFormats()
        {
            Assert.AreEqual("missing x", LocalizationManager.GetFormat("missing {0}", "x"),
                "缺键时 GetRaw 返回键本身，随后照常走 string.Format");
        }

        #endregion

        #region Get × 占位符

        [Test]
        public void Get_RegisteredPlaceholder_Substituted()
        {
            UseTable(("ui_welcome", "欢迎回来，{PlayerName}！"));
            LocalizationManager.SetPlaceholder("PlayerName", "张三");

            Assert.AreEqual("欢迎回来，张三！", LocalizationManager.Get("ui_welcome"));
        }

        [Test]
        public void Get_UnregisteredPlaceholder_StaysLiteral()
        {
            UseTable(("ui_welcome", "欢迎回来，{PlayerName}！"));

            Assert.AreEqual("欢迎回来，{PlayerName}！", LocalizationManager.Get("ui_welcome"));
        }

        [Test]
        public void Get_NoPlaceholderRegistered_ZeroTouchFastPath()
        {
            // 未注册任何占位符时应逐字返回缓存里的那个字符串实例（README 承诺的「无 GC 开销」）
            UseTable(("ui_welcome", "欢迎回来，{PlayerName}！"));

            Assert.AreSame(LocalizationManager.Get("ui_welcome"), LocalizationManager.Get("ui_welcome"));
        }

        #endregion

        #region 取值回退链

        [Test]
        public void Get_MissingInCurrentLanguage_FallsBackToFallbackLanguage()
        {
            LocalizationManager.SetLanguageData("ja", new Dictionary<string, string> { { "only_en", "fallback value" } });
            LocalizationManager.SetLanguage("ja");

            Assert.AreEqual("fallback value", LocalizationManager.Get("only_en"), "当前语言缺键应回退，回退语言默认是初始语言 en");
        }

        [Test]
        public void Get_MissingEverywhere_ReturnsKey()
        {
            Assert.AreEqual("no_such_key", LocalizationManager.Get("no_such_key"));
            Assert.IsFalse(LocalizationManager.ContainsKey("no_such_key"));
        }

        [Test]
        public void ContainsKey_SeesFallbackLanguage()
        {
            Assert.IsTrue(LocalizationManager.ContainsKey("seed"), "回退语言里的键也算存在");
        }

        #endregion

        #region LRU 缓存

        private static Dictionary<string, string> Table() =>
            new Dictionary<string, string> { { "seed", "seed" } };

        [Test]
        public void Cache_EvictsOldestNonPinnedLanguageBeyondLimit()
        {
            // SetUp 已 Initialize("en")：en 同时是当前语言与回退语言（两个钉住位是同一个值）
            LocalizationManager.SetLanguageData("ja", Table());
            LocalizationManager.SetLanguageData("ko", Table());
            LocalizationManager.SetLanguageData("fr", Table());
            // 此刻缓存 = en, ja, ko, fr，正好在上限

            LocalizationManager.SetLanguageData("de", Table()); // 第 5 个 → 淘汰最旧的 ja

            Assert.IsFalse(LocalizationManager.HasLanguage("ja"), "最旧的非钉住语言应被淘汰");
            Assert.IsTrue(LocalizationManager.HasLanguage("en"), "当前 / 回退语言永不被淘汰");
            Assert.IsTrue(LocalizationManager.HasLanguage("ko"));
            Assert.IsTrue(LocalizationManager.HasLanguage("fr"));
            Assert.IsTrue(LocalizationManager.HasLanguage("de"));
        }

        [Test]
        public void Cache_CurrentAndFallbackAreBothPinnedWhenDistinct()
        {
            LocalizationManager.SetLanguageData("ja", Table());
            LocalizationManager.SetLanguage("ja");       // 当前 = ja
            LocalizationManager.FallbackLanguage = "en"; // 回退 = en（与当前不同，占掉两个钉住位）

            LocalizationManager.SetLanguageData("ko", Table());
            LocalizationManager.SetLanguageData("fr", Table());
            LocalizationManager.SetLanguageData("de", Table());

            Assert.IsTrue(LocalizationManager.HasLanguage("ja"), "当前语言永不被淘汰");
            Assert.IsTrue(LocalizationManager.HasLanguage("en"), "回退语言永不被淘汰");
            Assert.IsFalse(LocalizationManager.HasLanguage("ko"), "两个钉住位之外只有 2 个 LRU 位，最旧的先走");
        }

        [Test]
        public void Cache_ReInjectingSameLanguageRefreshesRecency()
        {
            // README：「每次 SetLanguageData 或 SetLanguage 都会将目标语言标记为最近使用」
            LocalizationManager.SetLanguageData("ja", Table());
            LocalizationManager.SetLanguageData("ko", Table());
            LocalizationManager.SetLanguageData("ja", Table()); // 重新注入 → ja 变成最近使用

            LocalizationManager.SetLanguageData("fr", Table());
            LocalizationManager.SetLanguageData("de", Table()); // 第 5 个 → 淘汰最旧的非钉住项

            Assert.IsTrue(LocalizationManager.HasLanguage("ja"), "重新注入应刷新它的最近使用位置");
            Assert.IsFalse(LocalizationManager.HasLanguage("ko"), "被挤到最旧的是 ko，不是刚注入过的 ja");
        }

        #endregion

        #region 生命周期与参数防御

        [Test]
        public void Dispose_ResetsLanguageState()
        {
            var impl = new LocalizationManagerImpl();
            impl.InitWithDefault("en", Table());
            impl.SetLanguageData("ja", Table());

            impl.Dispose();

            Assert.IsNull(impl.CurrentLanguage, "Dispose 应复位当前语言（与它已经清掉的 LanguageAssetPath 对称）");
            Assert.IsNull(impl.FallbackLanguage);
            Assert.IsNull(impl.LanguageAssetPath);
            Assert.IsFalse(impl.HasLanguage("en"), "缓存应被清空");
        }

        [Test]
        public void UninitializedImplementation_GetDegradesToKey()
        {
            // 门面的 IsInitialized 只看「有没有实例」，注入一个未初始化的实现后它仍为 true
            LocalizationManager.SetInstance(new LocalizationManagerImpl());

            Assert.AreEqual("any_key", LocalizationManager.Get("any_key"),
                "不该抛裸 ArgumentNullException(key)——那是 Dictionary 在查 null 键，模块既没前缀也没修复提示");
            Assert.IsFalse(LocalizationManager.ContainsKey("any_key"));
        }

        [Test]
        public void Queries_AnswerFalseForEmptyKeyOrLanguage()
        {
            Assert.IsFalse(LocalizationManager.HasLanguage(null));
            Assert.IsFalse(LocalizationManager.HasLanguage(string.Empty));
            Assert.IsFalse(LocalizationManager.ContainsKey(null));
            Assert.IsFalse(LocalizationManager.ContainsKey(string.Empty));
        }

        [Test]
        public void Actions_ThrowArgumentNullExceptionForEmptyKey()
        {
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.Get(null));
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.Get(string.Empty));
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.GetFormat(null));
        }

        #endregion

        #region 键值占位符（按语言解析）

        [Test]
        public void PlaceholderFromKey_FollowsLanguageSwitch()
        {
            // 本用例是这条特性的存在理由：注册一次，切语言后自动跟着变。
            // 「注册时求值」那种错实现只会挂在这一条上。
            LocalizationManager.SetLanguageData("en", new Dictionary<string, string>
            {
                { "ui_guild_info", "Guild: {Guild}" },
                { "guild_legendary", "Legendary Guild" },
            });
            LocalizationManager.SetLanguageData("ja", new Dictionary<string, string>
            {
                { "ui_guild_info", "公会：{Guild}" },
                { "guild_legendary", "传奇公会" },
            });
            LocalizationManager.SetLanguage("ja");
            LocalizationManager.SetPlaceholderFromKey("Guild", "guild_legendary");

            Assert.AreEqual("公会：传奇公会", LocalizationManager.Get("ui_guild_info"), "当前语言是 ja");

            LocalizationManager.SetLanguage("en");

            Assert.AreEqual("Guild: Legendary Guild", LocalizationManager.Get("ui_guild_info"),
                "注册一次、跟随语言——这正是它相对 SetPlaceholder 的意义");
        }

        [Test]
        public void PlaceholderFromKey_UsesFallbackLanguage()
        {
            // 表项只在回退语言（en）里有
            LocalizationManager.SetLanguageData("en", new Dictionary<string, string>
            {
                { "ui_x", "[{OnlyEn}]" },
                { "only_en", "fallback text" },
            });
            LocalizationManager.SetLanguageData("ja", new Dictionary<string, string> { { "ui_x", "[{OnlyEn}]" } });
            LocalizationManager.SetLanguage("ja");
            LocalizationManager.SetPlaceholderFromKey("OnlyEn", "only_en");

            Assert.AreEqual("[fallback text]", LocalizationManager.Get("ui_x"), "走的是与 Get 同一条回退链");
        }

        [Test]
        public void PlaceholderFromKey_MissingTableKey_RendersTheKey()
        {
            UseTable(("ui_x", "值：{Gone}"));
            LocalizationManager.SetPlaceholderFromKey("Gone", "no_such_entry");

            Assert.AreEqual("值：no_such_entry", LocalizationManager.Get("ui_x"),
                "解析规则与 Get 同一条：表里没有就返回键本身，便于发现漏配");
        }

        [Test]
        public void PlaceholderFromKey_ValueIsNotRescanned()
        {
            UseTable(("ui_x", "{Outer}"), ("inner_ref", "内层：{Inner}"));
            LocalizationManager.SetPlaceholder("Inner", "会被替换");
            LocalizationManager.SetPlaceholderFromKey("Outer", "inner_ref");

            Assert.AreEqual("内层：{Inner}", LocalizationManager.Get("ui_x"),
                "单趟扫描、不递归：替换进去的值不再扫，值里再含 {Inner} 只按字面输出");
        }

        [Test]
        public void PlaceholderFromKey_CombinesWithPositionalArgs()
        {
            UseTable(("ui_info", "{Guild} - 等级 {0}"), ("guild_legendary", "传奇公会"));
            LocalizationManager.SetPlaceholderFromKey("Guild", "guild_legendary");

            Assert.AreEqual("传奇公会 - 等级 5", LocalizationManager.GetFormat("ui_info", "5"),
                "命名占位符与位置参数照常共存");
        }

        [Test]
        public void SetPlaceholderFromKey_OverridesLiteralOfSameName()
        {
            UseTable(("ui_x", "{Name}"), ("en_name", "来自表的英文名"));
            LocalizationManager.SetPlaceholder("Name", "字面量");
            LocalizationManager.SetPlaceholderFromKey("Name", "en_name");

            Assert.AreEqual("来自表的英文名", LocalizationManager.Get("ui_x"), "后注册的覆盖前者");
        }

        [Test]
        public void SetPlaceholder_OverridesKeyBindingOfSameName()
        {
            UseTable(("ui_x", "{Name}"), ("en_name", "来自表的英文名"));
            LocalizationManager.SetPlaceholderFromKey("Name", "en_name");
            LocalizationManager.SetPlaceholder("Name", "字面量");

            Assert.AreEqual("字面量", LocalizationManager.Get("ui_x"), "反方向同样：一个名字只有一种含义");
        }

        [Test]
        public void RemovePlaceholder_RemovesKeyBindingToo()
        {
            UseTable(("ui_x", "{Name}"));
            LocalizationManager.SetPlaceholderFromKey("Name", "en_name");

            LocalizationManager.RemovePlaceholder("Name");

            Assert.AreEqual("{Name}", LocalizationManager.Get("ui_x"));
        }

        [Test]
        public void ClearPlaceholders_ClearsKeyBindingsToo()
        {
            UseTable(("ui_x", "{A}{B}"));
            LocalizationManager.SetPlaceholder("A", "字面");
            LocalizationManager.SetPlaceholderFromKey("B", "en_name");

            LocalizationManager.ClearPlaceholders();

            Assert.AreEqual("{A}{B}", LocalizationManager.Get("ui_x"));
        }

        [Test]
        public void HasPlaceholder_SeesKeyBindings()
        {
            LocalizationManager.SetPlaceholderFromKey("Name", "en_name");

            Assert.IsTrue(LocalizationManager.HasPlaceholder("Name"));
        }

        [Test]
        public void SetPlaceholderFromKey_EmptyArguments_Throw()
        {
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.SetPlaceholderFromKey(null, "k"));
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.SetPlaceholderFromKey(string.Empty, "k"));
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.SetPlaceholderFromKey("n", null));
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.SetPlaceholderFromKey("n", string.Empty));
        }

        [Test]
        public void Get_AfterRemovingKeyBinding_ZeroTouchFastPath()
        {
            // 文本**必须含花括号**：否则这条会靠「无 '{' 早退」蒙混过去，验不到「两张表都空才走快路径」
            UseTable(("ui_x", "含 {Name} 的文本"));
            LocalizationManager.SetPlaceholderFromKey("Name", "en_name");
            LocalizationManager.RemovePlaceholder("Name");

            Assert.AreSame(LocalizationManager.Get("ui_x"), LocalizationManager.Get("ui_x"),
                "两类都摘干净后应回到零分配快路径（原样返回缓存里那个实例）");
        }

        #endregion
    }
}
