using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XLocalization;

namespace Venusy609.Xframework.Editor.Tests
{
    /// <summary>
    /// 语言表 JSON 的解析边界测试：转义序列解码、BOM 容忍，以及**会造成数据丢失或损坏的格式错误必须抛**
    /// （<see cref="LanguageAssetLoader"/> 自述「加载/解析失败抛异常(不静默)」）。
    /// <para>判定线：只宽容 JSON 规范之外的两种常见手写痕迹——尾随逗号与 UTF-8 BOM（都不影响数据完整性）；
    /// 其余格式错误一律抛。反例正是本 fixture 的存在理由——旧实现在格式错误处 <c>break</c> 掉循环并返回
    /// **已经读到的半张表**，调用方拿到非空字典、顺利装上一种「所有值都是空串」的语言，一条日志都没有。</para>
    /// <para>文本经 <see cref="LanguageAssetLoader.LoadTextFunc"/> 注入，绕开 YooAsset 运行环境。</para>
    /// </summary>
    class LanguageAssetLoaderJsonTests
    {
        private const string Template = "localization/lang_{0}";

        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Initialize("en", new Dictionary<string, string> { { "seed", "seed" } });
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        /// <summary>把 <paramref name="json"/> 当作 ja 语言表加载。</summary>
        private static void LoadJson(string json)
        {
            var loader = new LanguageAssetLoader("ja", Template);
            loader.LoadTextFunc = (location, ct) => UniTask.FromResult(json);
            loader.LoadAsync(default).GetAwaiter().GetResult();
        }

        #region 转义序列解码

        [Test]
        public void EscapedNewline_DecodedToRealNewline()
        {
            LoadJson("{\"a\":\"line1\\nline2\"}");

            Assert.AreEqual("line1\nline2", LocalizationManager.Get("a"), "\\n 应解码为真实换行");
        }

        [Test]
        public void UnicodeEscape_DecodedToCharacter()
        {
            LoadJson("{\"a\":\"\\u4e2d\\u6587\"}");

            Assert.AreEqual("中文", LocalizationManager.Get("a"), "\\uXXXX 应解码为对应字符（导出工具默认转义非 ASCII 时全靠它）");
        }

        [Test]
        public void EscapedQuoteAndBackslash_Decoded()
        {
            LoadJson("{\"a\":\"say \\\"hi\\\" \\\\ ok\"}");

            Assert.AreEqual("say \"hi\" \\ ok", LocalizationManager.Get("a"), "\\\" 与 \\\\ 应解码");
        }

        [Test]
        public void PlainText_FastPathUnchanged()
        {
            LoadJson("{\"a\":\"plain 文本\"}");

            Assert.AreEqual("plain 文本", LocalizationManager.Get("a"), "无转义的条目走零拷贝快路径，结果不变");
        }

        #endregion

        #region BOM

        [Test]
        public void BomPrefixedJson_Parses()
        {
            // Unity 的 TextAsset 是否已剥离 BOM 未经实测；解析器自行容忍，代价是一行
            LoadJson("\uFEFF{\"a\":\"1\"}");

            Assert.AreEqual("1", LocalizationManager.Get("a"), "带 UTF-8 BOM 的文本不应解析失败");
        }

        #endregion

        #region 格式错误必须抛（旧实现在这几条上静默降级）

        [Test]
        public void NonStringValue_ThrowsInsteadOfEmptyString()
        {
            // 旧实现产出非空字典 {"a": ""}，绕过 LoadAsync 的 Count == 0 兜底
            Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":1}"));
            Assert.IsFalse(LocalizationManager.HasLanguage("ja"), "解析失败不得注入缓存");
        }

        [Test]
        public void KeyWithoutColon_ThrowsInsteadOfDroppingTail()
        {
            // 旧实现静默丢弃 "b"，装上半张表
            var ex = Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"1\",\"b\"}"));

            StringAssert.Contains("[LanguageAssetLoader]", ex.Message);
            Assert.IsFalse(LocalizationManager.HasLanguage("ja"), "解析失败不得注入缓存");
        }

        [Test]
        public void UnterminatedString_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"1"));
        }

        [Test]
        public void UnknownEscape_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"\\q\"}"));
        }

        [Test]
        public void MalformedHexEscape_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"\\uZZZZ\"}"));
        }

        [Test]
        public void TrailingGarbage_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"1\"} oops"));
        }

        [Test]
        public void MissingComma_ThrowsInsteadOfSilentlyAccepting()
        {
            // 旧实现静默跳过缺掉的逗号照常装上整张表；非法 JSON 应在生成侧暴露，而不是被宽容掩盖
            var ex = Assert.Throws<InvalidOperationException>(() => LoadJson("{\"a\":\"1\" \"b\":\"2\"}"));

            StringAssert.Contains("',", ex.Message);
        }

        #endregion

        #region 数据无损的畸形保持宽容

        [Test]
        public void TrailingComma_Tolerated()
        {
            LoadJson("{\"a\":\"1\",}");

            Assert.AreEqual("1", LocalizationManager.Get("a"), "尾随逗号不丢数据，容忍");
        }

        #endregion
    }
}
