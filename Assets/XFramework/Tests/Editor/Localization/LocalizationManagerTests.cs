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
    }
}
