using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 持久化格式版本与迁移测试（<see cref="SettingsOptions.CurrentVersion"/> +
    /// <see cref="ISettingsMigrator{T}"/>）。
    /// </summary>
    /// <remarks>
    /// 多数用例在构造后才播种数据，而不是让构造函数触发加载——否则构造期就会执行迁移，
    /// 而 <c>Migrator</c> 此时还没来得及注册，用例会被构造期的告警污染。
    /// </remarks>
    [TestFixture]
    public class SettingsVersioningTests
    {
        #region Test Doubles

        private sealed class SampleSettings
        {
            public int Volume;
        }

        private sealed class FakeStore : ISettingsStore
        {
            public object Data;
            public bool HasData;

            /// <summary>播种数据并使 <see cref="Exists"/> 变为 true（构造之后再调用）。</summary>
            public void Seed(object payload)
            {
                Data = payload;
                HasData = true;
            }

            public bool Exists() => HasData;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Seed(settings);

            public void Delete()
            {
                Data = null;
                HasData = false;
            }
        }

        private sealed class RecordingMigrator : ISettingsMigrator<SampleSettings>
        {
            public int Calls;
            public int LastFromVersion = -1;
            public int LastToVersion = -1;
            public Action<SampleSettings> Mutate;

            public void Migrate(int fromVersion, int toVersion, SampleSettings settings)
            {
                Calls++;
                LastFromVersion = fromVersion;
                LastToVersion = toVersion;
                Mutate?.Invoke(settings);
            }
        }

        /// <summary>构造时不加载（store 无数据），默认值刻意取 Volume=5 以便与数据/初始值区分。</summary>
        private static SettingsManagerImpl<SampleSettings> CreateManager(ISettingsStore store, SettingsOptions options = null)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 }, options);

        private static SettingsEnvelope<SampleSettings> Envelope(int version, int volume)
            => new SettingsEnvelope<SampleSettings> { Version = version, Data = new SampleSettings { Volume = volume } };

        #endregion

        #region 落盘格式

        [Test]
        public void Save_Unversioned_StoresBarePayload()
        {
            var store = new FakeStore();
            var manager = CreateManager(store); // CurrentVersion 默认 0

            manager.Save();

            Assert.IsInstanceOf<SampleSettings>(store.Data,
                "未启用版本化时落盘的就是设置对象本身，JSON 最干净");
        }

        [Test]
        public void Save_Versioned_StoresEnvelope()
        {
            var store = new FakeStore();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 2 });

            manager.Save();

            var envelope = store.Data as SettingsEnvelope<SampleSettings>;
            Assert.IsNotNull(envelope, "启用版本化后落盘的是信封");
            Assert.AreEqual(2, envelope.Version);
            Assert.AreEqual(5, envelope.Data.Volume);
        }

        [Test]
        public async Task SaveAsync_Versioned_StoresEnvelope()
        {
            var store = new FakeStore();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 2 });

            await manager.SaveAsync();

            var envelope = store.Data as SettingsEnvelope<SampleSettings>;
            Assert.IsNotNull(envelope, "异步路径同样写入信封");
            Assert.AreEqual(2, envelope.Version);
        }

        #endregion

        #region 加载与迁移

        [Test]
        public void Load_SameVersion_KeepsValuesWithoutMigration()
        {
            var store = new FakeStore();
            var migrator = new RecordingMigrator();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 1 });
            manager.Migrator = migrator;
            store.Seed(Envelope(version: 1, volume: 9));

            manager.Load();

            Assert.AreEqual(9, manager.Settings.Volume);
            Assert.AreEqual(0, migrator.Calls, "版本一致不应触发迁移");
        }

        [Test]
        public void Load_OlderData_InvokesMigrator()
        {
            var store = new FakeStore();
            var migrator = new RecordingMigrator { Mutate = s => s.Volume = 42 };
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 3 });
            manager.Migrator = migrator;
            store.Seed(Envelope(version: 1, volume: 1));

            manager.Load();

            Assert.AreEqual(1, migrator.Calls);
            Assert.AreEqual(1, migrator.LastFromVersion);
            Assert.AreEqual(3, migrator.LastToVersion);
            Assert.AreEqual(42, manager.Settings.Volume, "迁移结果被采用");
        }

        [Test]
        public async Task LoadAsync_OlderData_InvokesMigrator()
        {
            var store = new FakeStore();
            var migrator = new RecordingMigrator { Mutate = s => s.Volume = 42 };
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 3 });
            manager.Migrator = migrator;
            store.Seed(Envelope(version: 1, volume: 1));

            await manager.LoadAsync();

            Assert.AreEqual(1, migrator.Calls, "异步路径同样执行迁移");
            Assert.AreEqual(42, manager.Settings.Volume);
        }

        [Test]
        public void Load_OlderData_NoMigrator_FallsBackToDefault()
        {
            var store = new FakeStore();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 3 });
            store.Seed(Envelope(version: 1, volume: 9));

            LogAssert.Expect(LogType.Warning,
                new Regex(@"需要迁移到 3，但未注册 ISettingsMigrator<SampleSettings>"));
            manager.Load();

            Assert.AreEqual(5, manager.Settings.Volume,
                "无迁移器时宁可回默认值，也不按旧结构解析出静默错位的设置");
        }

        [Test]
        public void Load_NewerData_RejectsEntirely()
        {
            var store = new FakeStore();
            var migrator = new RecordingMigrator();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 2 });
            manager.Migrator = migrator;
            store.Seed(Envelope(version: 5, volume: 9));

            LogAssert.Expect(LogType.Warning, new Regex(@"格式版本 5 高于本版本支持的 2"));
            manager.Load();

            Assert.AreEqual(5, manager.Settings.Volume, "版本过高整份拒绝，回退默认值");
            Assert.AreEqual(0, migrator.Calls, "拒绝路径不应调用迁移器");
        }

        [Test]
        public void Load_LegacyUnversionedFile_FallsBackToDefault()
        {
            // 启用版本化之前按无版本格式写下的文件：当成信封解析会得到 Data == null
            var store = new FakeStore();
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 1 });
            store.Seed(new SampleSettings { Volume = 9 });

            LogAssert.Expect(LogType.Warning, new Regex(@"缺少版本信封或载荷为空"));
            manager.Load();

            Assert.AreEqual(5, manager.Settings.Volume,
                "启用版本化会改变落盘格式，旧文件不可识别——这是 SettingsOptions 文档写明的陷阱");
        }

        [Test]
        public void Load_OlderData_NotifiesSubscribersWithMigratedValues()
        {
            var store = new FakeStore();
            var migrator = new RecordingMigrator { Mutate = s => s.Volume = 42 };
            var manager = CreateManager(store, new SettingsOptions { CurrentVersion = 3 });
            manager.Migrator = migrator;
            store.Seed(Envelope(version: 1, volume: 1));

            SampleSettings observed = null;
            using var handle = manager.Observe(s => observed = s);

            manager.Load();

            Assert.IsNotNull(observed);
            Assert.AreEqual(42, observed.Volume, "订阅者看到的是迁移后的数据，不是迁移前的");
        }

        #endregion
    }
}
