using System;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// <see cref="SettingsManager"/> 静态门面测试。
    /// <para>覆盖：未初始化保护、重复初始化告警并复用实例、Destroy 清空注册、Destroy 后可重新初始化、
    /// 多类型独立共存。</para>
    /// </summary>
    public class SettingsManagerFacadeTests
    {
        #region Test Doubles

        private sealed class SettingsA
        {
            public int Volume;
        }

        private sealed class SettingsB
        {
            public int Quality;
        }

        /// <summary>内存假存储：始终「无持久化数据」，构造走 defaultFactory 路径。</summary>
        private sealed class MemoryStore : ISettingsStore
        {
            public bool Exists() => false;

            public T Load<T>() where T : class, new() => new T();

            public void Save<T>(T settings) where T : class, new() { }

            public void Delete() { }
        }

        /// <summary>可在构造之后播种数据的假存储（版本化用例需要「构造时无数据」）。</summary>
        private sealed class SeededStore : ISettingsStore
        {
            public object Payload;

            public bool Exists() => Payload != null;

            public T Load<T>() where T : class, new() => Payload as T ?? new T();

            public void Save<T>(T settings) where T : class, new() => Payload = settings;

            public void Delete() => Payload = null;
        }

        private sealed class VolumeMigrator : ISettingsMigrator<SettingsA>
        {
            public int Calls;

            public void Migrate(int fromVersion, int toVersion, SettingsA settings)
            {
                Calls++;
                settings.Volume += 1000;
            }
        }

        #endregion

        #region Lifecycle

        [TearDown]
        public void TearDown()
        {
            // 门面是静态的，测试间必须复位，否则用例执行顺序会互相影响
            SettingsManager.Destroy();
        }

        #endregion

        #region 未初始化保护

        [Test]
        public void Uninitialized_Access_ThrowsWithRepairHint()
        {
            SettingsManager.Destroy();

            // 注意用 Throws 而非 Catch：ObjectDisposedException 派生自 InvalidOperationException，
            // 只有精确类型匹配才能证明抛的不是「已销毁」异常
            var ex = Assert.Throws<InvalidOperationException>(() => SettingsManager.Settings<SettingsA>());

            StringAssert.Contains("[SettingsManager]", ex.Message, "异常消息带 [SettingsManager] 前缀");
            StringAssert.Contains("Initialize", ex.Message, "异常消息带修复提示");
        }

        #endregion

        #region 重复初始化

        [Test]
        public void Initialize_Twice_LogsWarningAndReturnsSameInstance()
        {
            var first = SettingsManager.Initialize<SettingsA>(new MemoryStore());

            LogAssert.Expect(LogType.Warning,
                "[SettingsManager] Initialize<SettingsA> was called more than once. Ignoring duplicate.");
            var second = SettingsManager.Initialize<SettingsA>(new MemoryStore());

            Assert.AreSame(first, second, "重复初始化返回已存在的实例而非新建");
        }

        #endregion

        #region Destroy 与重新初始化

        [Test]
        public void Destroy_ClearsRegistrations()
        {
            SettingsManager.Initialize<SettingsA>(new MemoryStore());
            Assert.IsTrue(SettingsManager.IsInitialized);

            SettingsManager.Destroy();

            Assert.IsFalse(SettingsManager.IsInitialized, "销毁后清空全部注册");
            Assert.Throws<InvalidOperationException>(() => SettingsManager.Settings<SettingsA>());
        }

        [Test]
        public void Destroy_ThenReinitialize_Recovers()
        {
            SettingsManager.Initialize<SettingsA>(new MemoryStore(), () => new SettingsA { Volume = 7 });
            SettingsManager.Destroy();

            // 修复前这里会永久失败：Destroy 置了单向闩锁，而重新 Initialize 开头就会抛
            var manager = SettingsManager.Initialize<SettingsA>(new MemoryStore(), () => new SettingsA { Volume = 7 });

            Assert.IsNotNull(manager);
            Assert.IsTrue(SettingsManager.IsInitialized);
            Assert.AreEqual(7, SettingsManager.Settings<SettingsA>().Volume, "重新初始化后完全可用");
        }

        #endregion

        #region 迁移器转发

        [Test]
        public void SetMigrator_WiresThroughToManager()
        {
            // 构造时 store 无数据：否则构造期就会执行迁移，而迁移器此时还没经门面挂上，
            // 用例会被构造期那条「未注册 ISettingsMigrator」告警污染（同 SettingsVersioningTests 的纪律）
            var store = new SeededStore();
            SettingsManager.Initialize<SettingsA>(store, null, new SettingsOptions { CurrentVersion = 2 });

            var migrator = new VolumeMigrator();
            SettingsManager.SetMigrator<SettingsA>(migrator);

            Assert.AreSame(migrator, SettingsManager.GetMigrator<SettingsA>(), "取回的是经门面挂上的同一个实例");

            store.Payload = new SettingsEnvelope<SettingsA> { Version = 1, Data = new SettingsA { Volume = 7 } };
            SettingsManager.Load<SettingsA>();

            // 这一条补的正是完备性测试声明自己拦不住的缺口：转发接错线或写成空实现，
            // 同名断言照样通过，只有真的走一遍才看得出来
            Assert.AreEqual(1, migrator.Calls, "经门面挂上的迁移器确实被调用");
            Assert.AreEqual(1007, SettingsManager.Settings<SettingsA>().Volume, "迁移结果被采纳");
        }

        #endregion

        #region 多类型共存

        [Test]
        public void TwoTypes_CoexistIndependently()
        {
            SettingsManager.Initialize<SettingsA>(new MemoryStore(), () => new SettingsA { Volume = 7 });
            SettingsManager.Initialize<SettingsB>(new MemoryStore(), () => new SettingsB { Quality = 3 });

            Assert.AreEqual(7, SettingsManager.Settings<SettingsA>().Volume, "各类型持有各自的默认值工厂");
            Assert.AreEqual(3, SettingsManager.Settings<SettingsB>().Quality);

            SettingsManager.Apply(new SettingsA { Volume = 11 });

            Assert.AreEqual(11, SettingsManager.Settings<SettingsA>().Volume);
            Assert.AreEqual(3, SettingsManager.Settings<SettingsB>().Quality, "改动 A 不影响 B");
        }

        #endregion
    }
}
