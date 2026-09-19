using System.Threading;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// Tip 的帧通路归属与生命周期归口测试。
    /// <para>回归：<c>UITipItem.PlayAsync</c> 原先跑一个 <c>UniTask.Yield</c> 自循环并读
    /// <c>Time.deltaTime</c>——Tip 因此既不受 <c>UpdateManager.Pause</c> 约束、也不进档位调度，
    /// 是 UI 模块内最后一条绕过统一调度的帧通路。现由 <c>UIManager.Update</c> 与面板、HUD 共用一条通路。</para>
    /// <para><b>未覆盖</b>：<c>UITipItem.Tick</c> 的动画数值——它要求预制体上存在 TMP_Text，
    /// 而本工程未导入 TMP Essentials。</para>
    /// </summary>
    [TestFixture]
    public class UITipSchedulingTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;
        private RecordingTipProvider _tip;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_TipTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);

            _tip = new RecordingTipProvider();
            UIManager.Tip.SetProvider(_tip);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            _factory.DestroyAll();

            if (_root != null)
                UnityEngine.Object.DestroyImmediate(_root);

            UpdateManager.Clear();
            UpdateManager.Resume();
        }

        [Test]
        public void Tip_IsDrivenByUpdateManager()
        {
            UpdateManager.Tick(time: 1.0f);

            Assert.AreEqual(1, _tip.UpdateCount,
                "Tip 应经 UpdateManager 派发，不再自建 UniTask 循环");
        }

        [Test]
        public void Tip_StoppedByPause()
        {
            UpdateManager.Tick(time: 1.0f);
            Assert.AreEqual(1, _tip.UpdateCount);

            UpdateManager.Pause();
            UpdateManager.Tick(time: 2.0f);
            Assert.AreEqual(1, _tip.UpdateCount, "暂停后 Tip 与其它模块一并停掉");

            UpdateManager.Resume();
            UpdateManager.Tick(time: 3.0f);
            Assert.AreEqual(2, _tip.UpdateCount, "恢复后继续派发");
        }

        [Test]
        public void Tip_ReceivesDriverDeltaTime()
        {
            UpdateManager.Tick(time: 1.0f);
            UpdateManager.Tick(time: 2.5f);

            Assert.AreEqual(1.5f, _tip.LastDeltaTime, 0.0001f,
                "Tip 拿到的应是距上次派发的间隔，与面板一致");
            Assert.AreEqual(2.5f, _tip.LastTime, 0.0001f);
        }

        [Test]
        public void Destroy_DetachesAllTips()
        {
            UIManager.Destroy();

            Assert.AreEqual(1, _tip.DetachAllCount,
                "销毁前应回收在播 Tip——它们回池而非销毁，否则会留在场景里无人认领");
        }

        [Test]
        public void ReplaceProvider_DetachesPreviousTips()
        {
            UpdateManager.Tick(time: 1.0f);
            Assert.AreEqual(1, _tip.UpdateCount, "前置条件：旧 provider 正在被派发");

            var replacement = new RecordingTipProvider();

            UIManager.Tip.SetProvider(replacement);

            Assert.AreEqual(1, _tip.DetachAllCount, "换 provider 前应回收旧 provider 手上的 Tip");

            UpdateManager.Tick(time: 2.0f);

            Assert.AreEqual(1, replacement.UpdateCount, "此后由新 provider 接收派发");
            Assert.AreEqual(1, _tip.UpdateCount, "旧 provider 不再被派发");
        }

        #region Test Doubles

        /// <summary>记录派发与回收调用的 Tip provider，不触碰资源系统。</summary>
        private sealed class RecordingTipProvider : IUITipProvider
        {
            public int UpdateCount { get; private set; }
            public int DetachAllCount { get; private set; }
            public float LastDeltaTime { get; private set; }
            public float LastTime { get; private set; }

            public void SetUIRoot(Transform uiRoot) { }

            public UniTask ShowTipAsync(string text, TipConfig config = default,
                CancellationToken cancellationToken = default)
            {
                return UniTask.CompletedTask;
            }

            public void Update(float deltaTime, float time)
            {
                UpdateCount++;
                LastDeltaTime = deltaTime;
                LastTime = time;
            }

            public void DetachAll() => DetachAllCount++;
        }

        #endregion
    }
}
