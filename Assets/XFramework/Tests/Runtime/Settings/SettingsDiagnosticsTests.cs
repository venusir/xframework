using System;
using System.Collections.Generic;
using NUnit.Framework;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingsManager.CopyRegisteredTypes"/> / <see cref="SettingsManager.RegisteredTypeCount"/>：
    /// 已注册类型清单投影。
    /// <para>门面是静态的，TearDown 一律 <see cref="SettingsManager.Destroy"/>（与其他 Settings fixture 同一纪律）。</para>
    /// </summary>
    [TestFixture]
    public class SettingsDiagnosticsTests
    {
        #region Test Doubles

        private sealed class DiagnosticsSettings
        {
            public int Value;
        }

        private sealed class MemoryStore : ISettingsStore
        {
            public bool Exists() => false;

            public T Load<T>() where T : class, new() => new T();

            public void Save<T>(T settings) where T : class, new() { }

            public void Delete() { }
        }

        #endregion

        [TearDown]
        public void TearDown()
        {
            SettingsManager.Destroy();
        }

        [Test]
        public void CopyRegisteredTypes_ListsTypeAndDirtyFlag()
        {
            SettingsManager.Initialize<DiagnosticsSettings>(new MemoryStore());

            var buffer = new List<SettingsTypeInfo>();
            int count = SettingsManager.CopyRegisteredTypes(buffer);

            Assert.AreEqual(1, count);
            Assert.AreEqual(typeof(DiagnosticsSettings), buffer[0].SettingsType);
            Assert.IsFalse(buffer[0].IsDirty, "刚构造、没有未提交改动");

            // 注意：直接改 POCO 字段**不会**标脏——脏跟踪走 SettingRef 句柄或显式 MarkDirty。
            // 本用例锁的是「回读与 IsDirty<T>() 同源」，故用文档口径的显式标记。
            SettingsManager.MarkDirty<DiagnosticsSettings>();

            SettingsManager.CopyRegisteredTypes(buffer);
            Assert.IsTrue(buffer[0].IsDirty, "回读应反映 IsDirty<T>() 的当前值");
        }

        [Test]
        public void CopyRegisteredTypes_ClearsBufferFirst()
        {
            var buffer = new List<SettingsTypeInfo> { default };

            SettingsManager.CopyRegisteredTypes(buffer);

            Assert.AreEqual(0, buffer.Count);
        }

        [Test]
        public void CopyRegisteredTypes_NullBuffer_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => SettingsManager.CopyRegisteredTypes(null));
        }

        [Test]
        public void RegisteredTypeCount_TracksRegistrations()
        {
            Assert.AreEqual(0, SettingsManager.RegisteredTypeCount);

            SettingsManager.Initialize<DiagnosticsSettings>(new MemoryStore());

            Assert.AreEqual(1, SettingsManager.RegisteredTypeCount);
        }
    }
}
