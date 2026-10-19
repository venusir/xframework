using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XLocalization;

namespace XFramework.XLocalization.Tests
{
    /// <summary>
    /// <see cref="LocalizationManager.CopyLoadedLanguages"/> / <see cref="LocalizationManager.CachedLanguageCount"/>：
    /// 已缓存语言清单投影。
    /// <para>门面是静态的，SetUp/TearDown 都 <see cref="LocalizationManager.Destroy"/> 复位。</para>
    /// </summary>
    [TestFixture]
    public class LocalizationDiagnosticsTests
    {
        [SetUp]
        public void SetUp()
        {
            LocalizationManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            LocalizationManager.Destroy();
        }

        [Test]
        public void CopyLoadedLanguages_ListsDefaultLanguage()
        {
            LocalizationManager.Initialize("zh-Hans", new Dictionary<string, string>
            {
                { "hello", "你好" },
                { "bye", "再见" },
            });

            var buffer = new List<LocalizationLanguageInfo>();
            int count = LocalizationManager.CopyLoadedLanguages(buffer);

            Assert.AreEqual(buffer.Count, count);
            Assert.AreEqual(1, count);
            Assert.AreEqual("zh-Hans", buffer[0].Language);
            Assert.AreEqual(2, buffer[0].EntryCount);
            Assert.IsTrue(buffer[0].IsCurrent);
            Assert.AreEqual(1, LocalizationManager.CachedLanguageCount);
        }

        [Test]
        public void LocalizationLanguageInfo_ToString_CurrentAndOther()
        {
            LocalizationManager.Initialize("zh-Hans", new Dictionary<string, string> { { "a", "1" } });
            LocalizationManager.SetLanguageData("en", new Dictionary<string, string>
            {
                { "a", "1" }, { "b", "2" },
            });

            var buffer = new List<LocalizationLanguageInfo>();
            LocalizationManager.CopyLoadedLanguages(buffer);

            var current = buffer.Find(x => x.Language == "zh-Hans");
            var other = buffer.Find(x => x.Language == "en");

            Assert.AreEqual("zh-Hans(当前), 1 条", current.ToString(),
                "当前语言格式：名字(当前), N 条（R3-LOC-001）");
            Assert.AreEqual("en(2 条)", other.ToString(),
                "非当前/回退语言格式：名字(N 条)");
        }

        [Test]
        public void CopyLoadedLanguages_MostRecentlyUsedFirst_AndEvictsLru()
        {
            LocalizationManager.Initialize("a", new Dictionary<string, string> { { "k", "a" } });
            LocalizationManager.SetLanguageData("b", new Dictionary<string, string> { { "k", "b" } });
            LocalizationManager.SetLanguageData("c", new Dictionary<string, string> { { "k", "c" } });
            LocalizationManager.SetLanguageData("d", new Dictionary<string, string> { { "k", "d" } });
            LocalizationManager.SetLanguageData("b", new Dictionary<string, string> { { "k", "b2" } }); // touch b
            LocalizationManager.SetLanguageData("e", new Dictionary<string, string> { { "k", "e" } }); // 淘汰 c

            var buffer = new List<LocalizationLanguageInfo>();
            LocalizationManager.CopyLoadedLanguages(buffer);

            CollectionAssert.AreEqual(new[] { "e", "b", "d", "a" },
                buffer.ConvertAll(x => x.Language),
                "MRU 在前；最久未用的非当前语言（c）应被淘汰");
        }

        [Test]
        public void CopyLoadedLanguages_NotInitialized_ReturnsZeroAndClearsBuffer()
        {
            var buffer = new List<LocalizationLanguageInfo> { default };

            int count = LocalizationManager.CopyLoadedLanguages(buffer);

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, buffer.Count);
            Assert.AreEqual(0, LocalizationManager.CachedLanguageCount);
        }

        [Test]
        public void CopyLoadedLanguages_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => LocalizationManager.CopyLoadedLanguages(null));
        }
    }
}
