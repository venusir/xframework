using System;
using NUnit.Framework;
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
