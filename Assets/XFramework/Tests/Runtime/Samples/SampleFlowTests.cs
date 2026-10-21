using System;
using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using XFramework.XBootstrap;
using XFramework.XData;
using XFramework.XFileManager;
using XFramework.XMessage;
using XFramework.XReactive;
using XFramework.XSave;
using XFramework.XSave.Tests;
using XFramework.XSerialize;
using XFramework.XTimer;

namespace XFramework.Samples.Tests
{
    /// <summary>
    /// 可玩示例的启动链路守卫：Bootstrap(Data → Save) → 存档往返，以及 Message / Reactive / Timer 的接缝。
    /// <para>示例本身在 <c>Samples~/Playable</c>（不参与 Unity 编译），本 fixture 用公开 API 复刻它的
    /// 启动顺序，锁住「单测各自通过、拼起来失败」的那类问题。</para>
    /// </summary>
    [TestFixture]
    public class SampleFlowTests
    {
        #region Types

        [Serializable]
        private sealed class SampleFlowData : IDataBlock
        {
            public string BlockName => "SampleFlow";
            public int DataVersion => 1;
            public int Score;

            [Serializable]
            private struct Snapshot
            {
                public int score;
            }

            public object OnSave() => new Snapshot { score = Score };

            public object OnMigrate(object saveData, int fromVersion) => saveData;

            public void OnLoad(object data)
            {
                if (data is Snapshot snapshot)
                    Score = snapshot.score;
            }

            public void OnClear() => Score = 0;
        }

        private readonly struct SampleFlowMessage
        {
            public readonly int Value;

            public SampleFlowMessage(int value)
            {
                Value = value;
            }
        }

        #endregion

        #region Lifecycle

        private TempFileProvider _fileProvider;

        [SetUp]
        public void SetUp()
        {
            Bootstrap.Clear();
            MessageManager.Clear();
            TimerManager.CancelAll();

            _fileProvider = new TempFileProvider();
            FileManager.Destroy();
            FileManager.Initialize(_fileProvider);
            Serializer.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            Bootstrap.Shutdown();
            Bootstrap.Clear();
            MessageManager.Clear();
            TimerManager.CancelAll();

            SaveManager.Shutdown();
            DataManager.Shutdown();

            FileManager.Destroy();
            FileManager.Initialize();
            _fileProvider.Cleanup();
        }

        #endregion

        #region Tests

        [Test]
        public async Task Bootstrap_DataThenSave_RoundTripsThroughFile()
        {
            // 与示例相同的登记顺序：Data(3) → Save(4)。Save 依赖 Data 的快照能力，
            // 能完成一次存档往返即证明相位顺序生效（不是靠测试自己手动初始化）。
            Bootstrap.Register(new DataBootstrapStage());
            Bootstrap.Register(new SaveBootstrapStage());
            await Bootstrap.RunAsync();

            var data = DataManager.GetOrCreateBlock<SampleFlowData>();
            data.Score = 7;

            await SaveManager.SaveAsync(0);
            data.Score = 0;

            SaveLoadResult result = await SaveManager.TryLoadAsync(0);

            Assert.IsTrue(result.IsSuccess, $"读档应成功，实际：{result.Status}");
            Assert.AreEqual(7, DataManager.GetOrCreateBlock<SampleFlowData>().Score,
                "存档应恢复 Data 块的快照");
        }

        [Test]
        public void MessageAndReactive_BindAndNotify()
        {
            int received = -1;
            using (MessageManager.Subscribe<SampleFlowMessage>(message => received = message.Value))
            {
                MessageManager.Publish(new SampleFlowMessage(42));
            }

            Assert.AreEqual(42, received, "订阅应收到同步发布的消息");

            using var property = new ReactiveProperty<int>(0);
            int observed = -1;
            using (property.Subscribe(value => observed = value))
            {
                property.Value = 5;
            }

            Assert.AreEqual(5, observed, "ReactiveProperty 应把新值推给订阅者");
        }

        [Test]
        public async Task Timer_FiresWithinBudget()
        {
            bool fired = false;
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));

            TimerManager.After(0.05f, () => fired = true, cancellationToken: timeout.Token);
            await UniTask.WaitUntil(() => fired, cancellationToken: timeout.Token);

            Assert.IsTrue(fired, "定时器应在超时前触发");
        }

        #endregion
    }
}
