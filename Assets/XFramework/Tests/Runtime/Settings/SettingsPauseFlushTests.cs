using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 切后台兜底（<see cref="SettingsOptions.SaveOnPause"/>）与按需落盘测试。
    /// <para>覆盖：<see cref="SettingsManager.SaveAllDirty"/> 的显式语义（不看开关）、
    /// 切后台只写开了开关的类型、宿主的申请与归还、以及 <c>OnApplicationPause</c> 的实际接线
    /// （用 <c>SendMessage</c> 走 Unity 的消息派发，故连私有方法也能验证）。</para>
    /// <remarks>
    /// <b>每个用例都要释放自己创建的管理器</b>：开启 <c>SaveOnPause</c> 的类型持有共享宿主，
    /// 不释放就留下一个 <c>DontDestroyOnLoad</c> 的 GameObject 给后续 fixture。
    /// </remarks>
    /// </summary>
    [TestFixture]
    public class SettingsPauseFlushTests
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

        private sealed class CountingStore : ISettingsStore
        {
            public int SaveCalls;
            public object Data;

            public bool Exists() => Data != null;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new()
            {
                SaveCalls++;
                Data = settings;
            }

            public void Delete() => Data = null;
        }

        private sealed class ThrowingStore : ISettingsStore
        {
            public bool Exists() => false;

            public T Load<T>() where T : class, new() => new T();

            public void Save<T>(T settings) where T : class, new()
                => throw new InvalidOperationException("模拟存储后端失败");

            public void Delete() { }
        }

        private static SettingsManagerImpl<SettingsA> CreateManager(ISettingsStore store, SettingsOptions options = null)
            => new SettingsManagerImpl<SettingsA>(store, () => new SettingsA { Volume = 1 }, options);

        #endregion

        #region Fixture

        [TearDown]
        public void TearDown() => SettingsManager.Destroy();

        #endregion

        #region 按需落盘（显式）

        [Test]
        public void SaveAllDirty_WritesEveryDirtyTypeRegardlessOfOptIn()
        {
            var storeA = new CountingStore();
            var storeB = new CountingStore();
            SettingsManager.Initialize<SettingsA>(storeA, () => new SettingsA { Volume = 1 });
            SettingsManager.Initialize<SettingsB>(storeB, () => new SettingsB { Quality = 2 });

            SettingsManager.MarkDirty<SettingsA>(); // 两者都没开 SaveOnPause，只有 A 是脏的

            SettingsManager.SaveAllDirty();

            Assert.AreEqual(1, storeA.SaveCalls, "脏的类型被写盘");
            Assert.AreEqual(0, storeB.SaveCalls, "干净的类型不产生任何 IO");
        }

        [Test]
        public void SaveAllDirty_NoRegistrations_DoesNotThrow()
        {
            SettingsManager.Destroy();

            Assert.DoesNotThrow(() => SettingsManager.SaveAllDirty(),
                "它是兜底路径，未初始化时应当是零操作，不该在退出流程里制造失败");
        }

        #endregion

        #region 切后台兜底（自动）

        [Test]
        public void FlushDirtyForPause_OnlyWritesTypesThatOptedIn()
        {
            var optedIn = new CountingStore();
            var notOptedIn = new CountingStore();
            SettingsManager.Initialize<SettingsA>(optedIn, null, new SettingsOptions { SaveOnPause = true });
            SettingsManager.Initialize<SettingsB>(notOptedIn);

            SettingsManager.MarkDirty<SettingsA>();
            SettingsManager.MarkDirty<SettingsB>();

            SettingsManager.FlushDirtyForPause();

            Assert.AreEqual(1, optedIn.SaveCalls, "开启 SaveOnPause 的类型落盘");
            Assert.AreEqual(0, notOptedIn.SaveCalls, "未开启的类型不受切后台兜底影响——开关是逐类型的");
        }

        [Test]
        public void SaveOnPause_StoreThrows_DoesNotPropagate()
        {
            var manager = CreateManager(new ThrowingStore(), new SettingsOptions { SaveOnPause = true });
            manager.MarkDirty();

            try
            {
                LogAssert.Expect(LogType.Warning, new Regex("切后台兜底保存失败"));

                Assert.DoesNotThrow(() => ((ISettingsDirtyFlush)manager).SaveIfDirty(),
                    "异常若冒出就落进 Unity 的切后台流程，那里无人能接");
                Assert.IsTrue(manager.IsDirty, "写失败后改动不能被当作已提交");
            }
            finally
            {
                manager.Dispose();
            }
        }

        #endregion

        #region 宿主生命周期

        [Test]
        public void SaveOnPause_AcquiresHost_AndLastReleaseDestroysIt()
        {
            Assume.That(SettingsPauseNotifier.Instance, Is.Null, "前提：进入用例时没有残留宿主");

            var first = CreateManager(new CountingStore(), new SettingsOptions { SaveOnPause = true });
            var second = CreateManager(new CountingStore(), new SettingsOptions { SaveOnPause = true });

            Assert.IsNotNull(SettingsPauseNotifier.Instance, "首个开启者创建宿主");

            first.Dispose();
            Assert.IsNotNull(SettingsPauseNotifier.Instance, "还有开启者时宿主必须留着");

            second.Dispose();
            Assert.IsNull(SettingsPauseNotifier.Instance, "最后一个开启者释放后销毁宿主");
        }

        [Test]
        public void SaveOnPause_Disabled_DoesNotCreateHost()
        {
            var before = SettingsPauseNotifier.Instance;

            var manager = CreateManager(new CountingStore());

            Assert.AreSame(before, SettingsPauseNotifier.Instance, "关闭时不创建宿主——默认路径零开销");

            manager.Dispose();
        }

        #endregion

        #region OnApplicationPause 接线

        [Test]
        public void OnApplicationPause_OnlyEnteringBackgroundFlushes()
        {
            // 必须经门面注册：切后台兜底遍历的是门面的管理器表，
            // 直接构造的 impl 不在表里，宿主看不到它
            var store = new CountingStore();
            SettingsManager.Initialize<SettingsA>(store, () => new SettingsA { Volume = 1 },
                new SettingsOptions { SaveOnPause = true });
            SettingsManager.MarkDirty<SettingsA>();

            try
            {
                // 用 SendMessage 走 Unity 自己的消息派发：这样连私有方法也能验，不必为测试放宽可见性
                var host = SettingsPauseNotifier.Instance;
                Assert.IsNotNull(host);

                host.SendMessage("OnApplicationPause", false);
                Assert.AreEqual(0, store.SaveCalls, "恢复前台不写盘——那里没有新的丢失风险");

                host.SendMessage("OnApplicationPause", true);
                Assert.AreEqual(1, store.SaveCalls, "进入后台落盘");
                Assert.IsFalse(SettingsManager.IsDirty<SettingsA>());
            }
            finally
            {
                SettingsManager.Destroy(); // 释放管理器，进而归还宿主
            }
        }

        #endregion
    }
}
