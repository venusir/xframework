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
