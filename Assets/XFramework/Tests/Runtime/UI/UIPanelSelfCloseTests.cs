using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUI.Data;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 「OnOpen 期间可关闭自身」测试。
    /// <para>回归：<c>IsOpen</c> 原先在 <c>await OnOpenImpl()</c> <strong>之后</strong>才置位，而
    /// <c>CloseSelfAsync</c> 带「未打开则忽略」的守卫——于是面板在自己的 <c>OnOpen</c> 里关自己会静默失败，
    /// 既不报错也不生效（典型场景：打开后校验发现数据非法，立刻要关掉自己）。</para>
    /// </summary>
    [TestFixture]
    public class UIPanelSelfCloseTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_SelfCloseTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakePanel>();
            _factory.RegisterPanel<SelfClosingOnOpenPanel>();
            _factory.RegisterPanel<StateProbePanel>();

            UIManager.PanelFactoryFactory = () => _factory;
            UIManager.Initialize(_root.transform);
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
        public async Task DuringOnOpen_IsOpenIsAlreadyTrue()
        {
            var panel = await UIManager.Panel.OpenAsync<StateProbePanel>("ui/probe");

            Assert.IsTrue(panel.IsOpenDuringOnOpen,
                "打开期间 IsOpen 就应为 true，否则 CloseSelfAsync 的守卫会把请求丢掉");
        }

        [Test]
        public async Task DuringOnOpen_IsOpeningIsTrue()
        {
            var panel = await UIManager.Panel.OpenAsync<StateProbePanel>("ui/probe");

            Assert.IsTrue(panel.IsOpeningDuringOnOpen, "打开期间 IsOpening 应为 true");
            Assert.IsFalse(panel.IsOpening, "打开完成后 IsOpening 应复位");
            Assert.IsTrue(panel.IsOpen, "打开完成后仍是已打开状态");
        }

        [Test]
        public async Task CloseSelfAsyncInsideOnOpen_ActuallyCloses()
        {
            var panel = await UIManager.Panel.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            Assert.IsNotNull(panel, "打开仍应返回实例（调用方需自行检查 IsOpen）");
            Assert.IsFalse(UIManager.Panel.IsOpen<SelfClosingOnOpenPanel>(),
                "面板在自己的 OnOpen 里请求关闭，应当在 OnOpen 返回后真的关掉");
            Assert.IsFalse(UIManager.Stack.CanGoBack, "不应留在显示栈里");
        }

        [Test]
        public async Task CloseSelfAsyncInsideOnOpen_OnCloseRunsAfterOnOpenReturns()
        {
            var panel = await UIManager.Panel.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            int returned = panel.Log.IndexOf("OnOpenReturned");
            int closed = panel.Log.IndexOf("OnClose");

            Assert.GreaterOrEqual(returned, 0, "OnOpen 应当跑完");
            Assert.GreaterOrEqual(closed, 0, "OnClose 应当被调用");
            Assert.Less(returned, closed,
                "OnClose 必须在 OnOpen 返回之后——在 OnOpen 的调用栈上重入关闭会写出「先初始化再被清理」的乱序");
        }

        [Test]
        public async Task CloseSelfAsyncInsideOnOpen_DoesNotPublishOpenedMessage()
        {
            int openedCount = 0;
            var subscription = UIManager.Events.Subscribe((PanelOpenedMessage _) => openedCount++);

            try
            {
                await UIManager.Panel.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
                await UniTask.Yield();

                Assert.AreEqual(0, openedCount,
                    "面板没打开成，不该发「已打开」消息——它的语义是「打开已完成」");
            }
            finally
            {
                subscription.Dispose();
            }
        }

        [Test]
        public async Task NormalOpen_StillPublishesOpenedMessage()
        {
            int openedCount = 0;
            var subscription = UIManager.Events.Subscribe((PanelOpenedMessage _) => openedCount++);

            try
            {
                await UIManager.Panel.OpenAsync<FakePanel>("ui/normal");

                Assert.AreEqual(1, openedCount, "正常打开仍应发一次「已打开」消息");
            }
            finally
            {
                subscription.Dispose();
            }
        }

        [Test]
        public async Task ReopenAfterSelfClose_Works()
        {
            await UIManager.Panel.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            // 实例已回池；再次打开应能正常走完（复用池中实例）
            var again = await UIManager.Panel.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            Assert.IsNotNull(again, "自关后面板应已回池，再次打开不应失败");
            Assert.IsFalse(UIManager.Panel.IsOpen<SelfClosingOnOpenPanel>(), "它仍会再次自关");
        }
    }
}
