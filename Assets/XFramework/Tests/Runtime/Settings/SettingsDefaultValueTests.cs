using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 默认值语义测试：构造、Load、Reset 三条路径必须走同一个 defaultFactory。
    /// <para>锁定 D2/D3 的修复——<c>Reset</c> 曾硬编码 <c>new T()</c>、<c>Load</c> 曾直接取 store 的返回值，
    /// 两者都会绕过注入的默认值工厂，使「恢复默认」与首次启动得到不同结果。</para>
    /// </summary>
    [TestFixture]
    public class SettingsDefaultValueTests
    {
        #region Test Doubles

        /// <summary>设置对象。字段初始值刻意全为 0，以便与工厂产出的默认值区分。</summary>
        private sealed class SampleSettings
        {
            public int Volume;
            public int Quality;
        }

        /// <summary>可编程假存储：<see cref="HasData"/> 决定 <see cref="Exists"/>，<see cref="Data"/> 决定 <see cref="Load{T}"/> 的返回值。</summary>
        private sealed class FakeStore : ISettingsStore
        {
            public bool HasData;
            public object Data;

            /// <summary>模拟违约的第三方 store：<see cref="Load{T}"/> 返回 <c>null</c> 而非契约要求的 <c>new T()</c>。</summary>
            public bool ReturnNull;

            public bool Exists() => HasData;

            public T Load<T>() where T : class, new() => ReturnNull ? null : Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Data = settings;

            public void Delete()
            {
                HasData = false;
                Data = null;
            }
        }

        /// <summary>工厂产出的默认值：Volume=5、Quality=3。</summary>
        private static SampleSettings FactoryDefault() => new SampleSettings { Volume = 5, Quality = 3 };

        #endregion

        #region Constructor

        [Test]
        public void Constructor_NoPersistedData_UsesDefaultFactory()
        {
            var manager = new SettingsManagerImpl<SampleSettings>(new FakeStore { HasData = false }, FactoryDefault);

            Assert.AreEqual(5, manager.Settings.Volume, "无持久化数据时用 defaultFactory 的产出");
        }

        [Test]
        public void Constructor_NoFactory_FallsBackToNewT()
        {
            var manager = new SettingsManagerImpl<SampleSettings>(new FakeStore { HasData = false });

            Assert.AreEqual(0, manager.Settings.Volume, "未注入工厂时退回 new T()");
        }

        [Test]
        public void Constructor_HasPersistedData_UsesPersistedValue()
        {
            var store = new FakeStore { HasData = true, Data = new SampleSettings { Volume = 99 } };

            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            Assert.AreEqual(99, manager.Settings.Volume, "有持久化数据时以数据为准，工厂不介入");
        }

        #endregion

        #region Load

        [Test]
        public void Load_NoPersistedData_UsesDefaultFactory()
        {
            var manager = new SettingsManagerImpl<SampleSettings>(new FakeStore { HasData = false }, FactoryDefault);

            // 先改掉当前值：否则断言无法区分「Load 重新取默认值」与「Load 什么也没做」
            manager.Settings.Volume = 42;
            manager.Load();

            Assert.AreEqual(5, manager.Settings.Volume, "Load 遇到无数据时也走 defaultFactory");
        }

        [Test]
        public void Load_HasPersistedData_UsesPersistedValue()
        {
            var store = new FakeStore { HasData = true, Data = new SampleSettings { Volume = 99 } };
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            manager.Load();

            Assert.AreEqual(99, manager.Settings.Volume, "有数据时不得被 defaultFactory 覆盖");
        }

        [Test]
        public void Load_NoPersistedData_NotifiesSubscribers()
        {
            var manager = new SettingsManagerImpl<SampleSettings>(new FakeStore { HasData = false }, FactoryDefault);
            var notified = 0;
            var handle = manager.Observe(_ => notified++);
            var before = notified; // Observe 订阅即回调一次，取基线而非写死次数

            manager.Load();

            Assert.AreEqual(before + 1, notified, "Load 后通知订阅者");
            handle.Dispose();
        }

        #endregion

        #region Store 违约返回 null

        [Test]
        public void Constructor_StoreReturnsNull_FallsBackToDefault()
        {
            var store = new FakeStore { HasData = true, ReturnNull = true };

            // Expect 必须在构造之前登记——告警正是在构造函数里发出的
            LogAssert.Expect(LogType.Warning, new Regex(@"ISettingsStore\.Load<SampleSettings> 返回了 null"));
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            Assert.AreEqual(5, manager.Settings.Volume, "违约返回 null 时回退到 defaultFactory");
        }

        [Test]
        public void Load_StoreReturnsNull_FallsBackToDefault()
        {
            // 构造时 HasData=false 走工厂，不产生告警；随后才让 store 违约
            var store = new FakeStore { HasData = false };
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            store.HasData = true;
            store.ReturnNull = true;

            LogAssert.Expect(LogType.Warning, new Regex(@"ISettingsStore\.Load<SampleSettings> 返回了 null"));
            manager.Load();

            Assert.IsNotNull(manager.Settings, "不得把 null 交给调用方——那会把 NRE 推迟到各处爆发");
            Assert.AreEqual(5, manager.Settings.Volume, "回退到 defaultFactory");
        }

        #endregion

        #region Reset

        [Test]
        public void Reset_UsesDefaultFactory()
        {
            var store = new FakeStore { HasData = true, Data = new SampleSettings { Volume = 99, Quality = 1 } };
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            manager.Reset();

            Assert.AreEqual(5, manager.Settings.Volume, "Reset 必须回到 defaultFactory 的产出");
            Assert.AreEqual(3, manager.Settings.Quality, "工厂产出的每个字段都要生效");
        }

        [Test]
        public void Reset_AfterLoad_ReturnsToFactoryDefaults()
        {
            // 复现真实场景：玩家有存档 → 载入 → 点「恢复默认」
            var store = new FakeStore { HasData = true, Data = new SampleSettings { Volume = 99, Quality = 1 } };
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);
            manager.Load();

            manager.Reset();

            Assert.AreEqual(5, manager.Settings.Volume);
            Assert.AreEqual(3, manager.Settings.Quality);
        }

        [Test]
        public void Reset_DeletesPersistedData()
        {
            var store = new FakeStore { HasData = true, Data = new SampleSettings { Volume = 99 } };
            var manager = new SettingsManagerImpl<SampleSettings>(store, FactoryDefault);

            manager.Reset();

            Assert.IsFalse(store.Exists(), "Reset 同时清除持久化数据");
        }

        #endregion
    }
}
