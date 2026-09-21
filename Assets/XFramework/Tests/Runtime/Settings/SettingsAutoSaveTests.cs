using System;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XSettings;
using XFramework.XUpdate;

namespace XFramework.XSettings.Tests
{
    /// <summary>
    /// 自动保存（去抖）测试。直接驱动 <see cref="SettingsAutoSaveTicker{T}"/>，
    /// 不经 <see cref="UpdateManager"/> 的档位切片——那样每个用例都要推几十帧才能触发一次回调。
    /// </summary>
    /// <remarks>
    /// <b>关于步长：</b>用例刻意用「明确大于去抖窗口」的 delta 来推进（窗口 0.5s 时用 1.0s），
    /// 而不是拿 0.1s 累加凑到窗口边缘。两个原因：一是首次 tick 只重置窗口、不做减法，
    /// 用累加极易把「经过的时间」算多一次；二是 0.1f 不是精确可表示值，逐次相减会累积误差，
    /// 断言落在边界上就会取决于浮点舍入方向。大步长让「是否已越过窗口」毫无歧义。
    /// </remarks>
    [TestFixture]
    public class SettingsAutoSaveTests
    {
        #region Test Doubles

        private sealed class SampleSettings
        {
            public int Volume;
        }

        private sealed class FakeStore : ISettingsStore
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

        private static SettingsManagerImpl<SampleSettings> CreateManager(ISettingsStore store, SettingsOptions options = null)
            => new SettingsManagerImpl<SampleSettings>(store, () => new SampleSettings { Volume = 5 }, options);

        /// <summary>
        /// 可切换抛异常的存储后端。云存档、平台 SDK 这类后端抛异常是正常失败模式而非 bug，
        /// 用于验证自动保存不会因一次失败而永久停摆。
        /// </summary>
        private sealed class ThrowingStore : ISettingsStore
        {
            public int SaveCalls;
            public bool ThrowOnSave;
            public object Data;

            public bool Exists() => Data != null;

            public T Load<T>() where T : class, new() => Data as T ?? new T();

            public void Save<T>(T settings) where T : class, new()
            {
                SaveCalls++;

                if (ThrowOnSave)
                    throw new InvalidOperationException("模拟存储后端失败（配额/签名/平台 SDK）");

                Data = settings;
            }

            public void Delete() => Data = null;
        }

        #endregion

        #region Fixture

        [SetUp]
        public void SetUp()
        {
            // 本 fixture 会读写<b>全局</b> UpdateManager（构造 SettingsManagerImpl 时注册真实 ticker、
            // 断言 TotalCount 增量），因此必须复位它触碰的静态门面——PlayMode 下所有用例共享一个
            // player 实例，不复位的后果是「某用例中途抛异常留下悬挂注册」一路传给后面的 fixture，
            // 而全量 0 失败正是靠各 fixture 各自复位撑起来的
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();
        }

        #endregion

        #region 去抖

        [Test]
        public void AutoSave_AfterDelay_SavesOnce()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            manager.MarkDirty();

            ticker.OnUpdate(0.1f, 0f); // 首次 tick 检测到改动：只重置窗口，不计时
            ticker.OnUpdate(0.1f, 0f); // 窗口内，仅走了 0.1s

            Assert.AreEqual(0, store.SaveCalls, "窗口未满不写盘");

            ticker.OnUpdate(1.0f, 0f); // 明确越过 0.5s 窗口

            Assert.AreEqual(1, store.SaveCalls);
            Assert.IsFalse(manager.IsDirty, "写盘后应转为干净");
        }

        [Test]
        public void AutoSave_ContinuousChanges_DoNotSaveUntilSilence()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            // 每次 tick 的步长本身就超过窗口（1.0s > 0.5s）。若实现是「节流」，
            // 这里第一次 tick 就会写盘；只有「去抖」才会因为每次都有新改动而一直推迟
            for (int i = 0; i < 10; i++)
            {
                manager.MarkDirty();
                ticker.OnUpdate(1.0f, 0f);
            }

            Assert.AreEqual(0, store.SaveCalls, "拖动过程中一次都不写——这是去抖而非节流");

            ticker.OnUpdate(1.0f, 0f); // 不再有改动，静默超过窗口

            Assert.AreEqual(1, store.SaveCalls, "松手静默一个窗口后写一次");
        }

        [Test]
        public void AutoSave_NoChanges_DoesNotSave()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            for (int i = 0; i < 20; i++)
                ticker.OnUpdate(1.0f, 0f);

            Assert.AreEqual(0, store.SaveCalls, "无改动时永不写盘");
        }

        [Test]
        public void AutoSave_AfterSaving_CountsFreshChangesAgain()
        {
            var store = new FakeStore();
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            manager.MarkDirty();
            ticker.OnUpdate(1.0f, 0f); // 首次 tick 重置窗口
            ticker.OnUpdate(1.0f, 0f);

            Assert.AreEqual(1, store.SaveCalls, "第一轮改动已写盘");

            manager.MarkDirty();
            ticker.OnUpdate(1.0f, 0f);
            ticker.OnUpdate(1.0f, 0f);

            Assert.AreEqual(2, store.SaveCalls, "保存之后的新改动同样会触发下一轮");
        }

        #endregion

        #region 写失败

        [Test]
        public void AutoSave_StoreThrows_DoesNotThrowAndStaysDirty()
        {
            var store = new ThrowingStore { ThrowOnSave = true };
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            manager.MarkDirty();
            ticker.OnUpdate(1.0f, 0f); // 首次 tick 检测到改动：只重置窗口，不计时

            LogAssert.Expect(LogType.Warning, new Regex("自动保存失败"));

            // 异常若冒到 UpdateScheduler，该节点会被当作坏节点永久注销（catch → Unregister），
            // 于是「store 抛了异常」这个可由环境恢复的失败会变成「本次会话再也不自动保存」
            UpdateTier tier = UpdateTier.Tier5;
            Assert.DoesNotThrow(() => tier = ticker.OnUpdate(1.0f, 0f), "驱动器必须吞住 store 的异常");

            Assert.AreEqual(UpdateTier.Tier0, tier, "失败后重新起算窗口，故维持细粒度");
            Assert.AreEqual(1, store.SaveCalls);
            Assert.IsTrue(manager.IsDirty, "写失败后改动不能被当作已提交");
        }

        [Test]
        public void AutoSave_StoreThrows_RetriesOnNextWindow()
        {
            var store = new ThrowingStore { ThrowOnSave = true };
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            manager.MarkDirty();
            ticker.OnUpdate(1.0f, 0f);

            LogAssert.Expect(LogType.Warning, new Regex("自动保存失败"));
            ticker.OnUpdate(1.0f, 0f);
            Assert.AreEqual(1, store.SaveCalls);

            // 失败后窗口被重新起算：紧接着的一帧不该重试，否则持续失败的 store
            // （比如云端无网）会以约 7.5 次/秒的频率空转
            ticker.OnUpdate(0.1f, 0f);
            Assert.AreEqual(1, store.SaveCalls, "失败后重新起算窗口，不逐帧空转");

            store.ThrowOnSave = false;
            ticker.OnUpdate(1.0f, 0f);

            Assert.AreEqual(2, store.SaveCalls, "下一个去抖窗口重试");
            Assert.IsFalse(manager.IsDirty, "重试成功后转为干净");
        }

        [Test]
        public void AutoSave_StoreThrows_LogsOncePerFailureEpisode()
        {
            var store = new ThrowingStore { ThrowOnSave = true };
            var manager = CreateManager(store);
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            // LogAssert 数不出「只打了一次」——它只保证期待的消息出现过，多余的 Warning 不会让
            // 用例失败。故直接挂 logMessageReceived 计数。持续失败的 store 若每次重试都打印，
            // 就是每秒两次的日志洪水，这条节流是刻意的取舍而非疏漏
            var warnings = 0;
            Application.LogCallback counter = (condition, _, type) =>
            {
                if (type == LogType.Warning && condition.Contains("自动保存失败"))
                    warnings++;
            };
            Application.logMessageReceived += counter;
            try
            {
                manager.MarkDirty();
                ticker.OnUpdate(1.0f, 0f); // 首次 tick 只重置窗口

                ticker.OnUpdate(1.0f, 0f);
                Assert.AreEqual(1, warnings, "首次失败要告警");

                ticker.OnUpdate(1.0f, 0f);
                Assert.AreEqual(1, warnings, "同一失败周期内不重复打印");
                Assert.AreEqual(2, store.SaveCalls, "静默不等于放弃重试");

                // 成功一次即复位，下一个失败周期重新告警
                store.ThrowOnSave = false;
                ticker.OnUpdate(1.0f, 0f);
                Assert.IsFalse(manager.IsDirty);

                store.ThrowOnSave = true;
                manager.MarkDirty();
                ticker.OnUpdate(1.0f, 0f); // 新改动，重置窗口
                ticker.OnUpdate(1.0f, 0f);
                Assert.AreEqual(2, warnings, "成功之后的新失败周期要重新告警");
            }
            finally
            {
                Application.logMessageReceived -= counter;
            }
        }

        [Test]
        public void QuitFallback_StoreThrows_DoesNotPropagate()
        {
            var store = new ThrowingStore { ThrowOnSave = true };
            var manager = CreateManager(store, new SettingsOptions { SaveOnQuit = true });
            manager.MarkDirty();

            LogAssert.Expect(LogType.Warning, new Regex("退出兜底保存失败"));

            try
            {
                // Application.quitting 在编辑器中不触发，但处理函数本身与事件无关，可直接调用。
                // 异常若从这里冒出去就落进 Unity 的退出流程——那是游戏正在关闭、无人能补救的位置
                Assert.DoesNotThrow(() => manager.OnApplicationQuitting(), "异常不该窜进退出流程");
                Assert.AreEqual(1, store.SaveCalls);
            }
            finally
            {
                // SaveOnQuit 使构造函数订阅了 Application.quitting，而 PlayMode 下所有用例
                // 共享一个 player 实例：不释放就会把订阅留给后面的 fixture
                manager.Dispose();
            }
        }

        #endregion

        #region 档位

        [Test]
        public void AutoSave_TierReflectsWhetherWorkIsPending()
        {
            var manager = CreateManager(new FakeStore());
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);

            Assert.AreEqual(UpdateTier.Tier3, ticker.OnUpdate(0.1f, 0f), "无待提交改动时返回粗粒度");

            manager.MarkDirty();

            Assert.AreEqual(UpdateTier.Tier0, ticker.OnUpdate(0.1f, 0f), "窗口内需细粒度才能守住 delay");
        }

        #endregion

        #region 注册与释放

        [Test]
        public void AutoSave_Disabled_RegistersNothing()
        {
            var before = UpdateManager.TotalCount;
            var manager = CreateManager(new FakeStore()); // AutoSave 默认关闭

            Assert.AreEqual(before, UpdateManager.TotalCount, "关闭自动保存时不注册任何帧回调");

            manager.Dispose();
        }

        [Test]
        public void AutoSave_Enabled_RegistersAndUnregisters()
        {
            Assume.That(UpdateManager.IsInitialized, Is.True, "UpdateManager 未初始化时本用例无意义");

            var before = UpdateManager.TotalCount;
            var manager = CreateManager(new FakeStore(), new SettingsOptions { AutoSave = true });

            Assert.AreEqual(before + 1, UpdateManager.TotalCount, "开启自动保存时注册帧驱动器");

            manager.Dispose();

            Assert.AreEqual(before, UpdateManager.TotalCount, "释放时注销，不留悬挂回调");
        }

        [Test]
        public void AutoSave_TickAfterDispose_DoesNotThrow()
        {
            var manager = CreateManager(new FakeStore());
            var ticker = new SettingsAutoSaveTicker<SampleSettings>(manager, 0.5f);
            manager.Dispose();

            // 注销与「当帧已调度」之间存在竞态窗口，驱动器必须能安全退出
            UpdateTier tier = UpdateTier.Tier0;
            Assert.DoesNotThrow(() => tier = ticker.OnUpdate(0.1f, 0f));
            Assert.AreEqual(UpdateTier.Tier5, tier, "已释放时直接退到最粗粒度，不再触碰管理器");
        }

        #endregion
    }
}
