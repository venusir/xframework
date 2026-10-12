using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XConfig;

namespace XFramework.XConfig.Tests
{
    /// <summary>
    /// Config 的 <b>PlayMode 侧</b>覆盖：跨帧的预加载语义。
    /// <para><b>为什么这三条只能在 PlayMode</b>：它们都要求「加载真的跨了几帧」——EditMode 里
    /// <c>UniTask</c> 没有 PlayerLoop 泵，<c>await UniTask.Yield()</c> 不会让出到下一帧，
    /// 「挂起期」根本不存在，测出来的只是同步语义。本模块此前的用例全在 <c>Tests/Editor/</c>，
    /// 这是它第一次有 PlayMode 侧（覆盖失衡盘点见 <c>FrameworkReview.md</c> §4.2）。</para>
    /// <para><b>三条各自的赌注</b>：① 挂起期可观测（<c>InFlightLoadCount</c>）；② 并发共享的是
    /// **跨帧**的那一个在途任务；③ README/`Config.md` 承诺的「取消只中断调用方等待，底层仍完成并注册」
    /// ——这条此前从未被真帧验证过。</para>
    /// </summary>
    [TestFixture]
    public class ConfigPreloadFrameTests
    {
        #region Test Doubles

        [Serializable]
        private struct TestItemRow : IConfigRow<int>
        {
            public int Id { get; set; }

            // 反射填充：赋值发生在 CSV 装载器的列赋值里，编译器看不到 → CS0649 是假阳性
#pragma warning disable CS0649
            public string Name;
#pragma warning restore CS0649
        }

        #endregion

        #region Fixture

        private FakeConfigLoader _loader;

        [SetUp]
        public void SetUp()
        {
            ConfigManager.Destroy();
            _loader = new FakeConfigLoader();
            ConfigManager.SetInstance(new ConfigManagerImpl());
        }

        [TearDown]
        public void TearDown()
        {
            ConfigManager.Destroy();
        }

        /// <summary>造一个「N 帧后才完成」的加载任务——这就是本 fixture 的门控。</summary>
        private static UniTask<ConfigTable<TestItemRow>> LoadAfterFramesAsync(int frames)
        {
            return UniTask.Create(async () =>
            {
                for (int i = 0; i < frames; i++)
                    await UniTask.Yield();

                return new ConfigTable<TestItemRow>(
                    new Dictionary<int, TestItemRow> { [1] = new TestItemRow { Id = 1 } });
            });
        }

        #endregion

        #region 跨帧预加载

        [Test]
        public async Task PreloadTable_CompletesAcrossFrames()
        {
            _loader.SetTableTask(LoadAfterFramesAsync(3));

            UniTask<ConfigTable<TestItemRow>> pending =
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _loader);

            await UniTask.Yield();

            Assert.AreEqual(1, ConfigManager.InFlightLoadCount, "挂起期应当能被看见——这正是它存在的意义");
            Assert.IsFalse(ConfigManager.IsLoaded<TestItemRow>(), "还没完成就不该说已加载");

            ConfigTable<TestItemRow> table = await pending;

            Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>(), "跨帧完成后应当注册");
            Assert.AreEqual(1, table.Get(1).Id);
            Assert.AreEqual(0, ConfigManager.InFlightLoadCount, "完成后在途数应归零");
        }

        [Test]
        public async Task ConcurrentPreloads_ShareOneInFlightLoad_AcrossFrames()
        {
            _loader.SetTableTask(LoadAfterFramesAsync(3));

            UniTask<ConfigTable<TestItemRow>> first =
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _loader);

            await UniTask.Yield(); // 第一个调用真的挂起在这一帧——第二个是在「在途」状态下进来的

            UniTask<ConfigTable<TestItemRow>> second =
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _loader);

            ConfigTable<TestItemRow> a = await first;
            ConfigTable<TestItemRow> b = await second;

            Assert.AreEqual(1, _loader.LoadTableCallCount, "同一类型的并发预加载应当共享同一次底层加载");
            Assert.AreSame(a, b, "两个调用方应当拿到同一个表实例");
        }

        [Test]
        public async Task CancelledCaller_UnderlyingLoadStillRegisters_LaterFrame()
        {
            _loader.SetTableTask(LoadAfterFramesAsync(3));
            using var cts = new CancellationTokenSource();

            UniTask<ConfigTable<TestItemRow>> pending =
                ConfigManager.PreloadTableAsync<TestItemRow>("config/items", _loader, cts.Token);

            await UniTask.Yield();

            bool cancelled = false;
            cts.Cancel();
            try
            {
                await pending;
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }

            Assert.IsTrue(cancelled, "取消应当中断调用方的等待");
            Assert.IsFalse(ConfigManager.IsLoaded<TestItemRow>(), "此刻底层还没完成");

            // 让那个被放弃等待的加载继续跑到完成
            for (int i = 0; i < 5 && !ConfigManager.IsLoaded<TestItemRow>(); i++)
                await UniTask.Yield();

            Assert.IsTrue(ConfigManager.IsLoaded<TestItemRow>(),
                "取消只中断调用方的等待——底层加载仍会完成并注册（模块 README 的承诺，此前从未被真帧验证）");
        }

        #endregion
    }
}
