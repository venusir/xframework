using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XConfig;

namespace XFramework.XConfig.Tests
{
    /// <summary>
    /// <see cref="ConfigManager.CopyLoaded"/> / <see cref="ConfigManager.InFlightLoadCount"/>：已加载清单投影。
    /// <para>门面是静态的，SetUp/TearDown 都 <see cref="ConfigManager.Destroy"/> 复位
    /// （与 <c>ConfigManagerFacadeTests</c> 同一纪律）。</para>
    /// </summary>
    [TestFixture]
    public class ConfigDiagnosticsTests
    {
        private sealed class DiagnosticsGlobal
        {
            public int Value;
        }

        [SetUp]
        public void SetUp()
        {
            ConfigManager.Destroy();
        }

        [TearDown]
        public void TearDown()
        {
            ConfigManager.Destroy();
        }

        private static ConfigLoadedInfo Find(List<ConfigLoadedInfo> buffer, Type type)
        {
            for (int i = 0; i < buffer.Count; i++)
            {
                if (buffer[i].ConfigType == type)
                    return buffer[i];
            }

            Assert.Fail($"缓冲区里没有类型 '{type.Name}' 的配置");
            return default;
        }

        [Test]
        public void CopyLoaded_ListsRegisteredGlobal()
        {
            ConfigManager.Initialize();
            ConfigManager.RegisterGlobal(new DiagnosticsGlobal { Value = 1 });

            var buffer = new List<ConfigLoadedInfo>();
            int count = ConfigManager.CopyLoaded(buffer);

            Assert.AreEqual(buffer.Count, count);
            var info = Find(buffer, typeof(DiagnosticsGlobal));
            Assert.AreEqual(ConfigLoadKind.Global, info.Kind);
            Assert.AreEqual(1, info.EntryCount);
            Assert.IsNull(info.AssetPath, "直接登记的配置没有资源路径");
        }

        [Test]
        public void CopyLoaded_NotInitialized_ReturnsZeroAndClearsBuffer()
        {
            var buffer = new List<ConfigLoadedInfo> { default };

            int count = ConfigManager.CopyLoaded(buffer);

            Assert.AreEqual(0, count);
            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyLoaded_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => ConfigManager.CopyLoaded(null));
        }

        [Test]
        public void InFlightLoadCount_NotInitialized_IsZero()
        {
            Assert.AreEqual(0, ConfigManager.InFlightLoadCount);
        }
    }
}
