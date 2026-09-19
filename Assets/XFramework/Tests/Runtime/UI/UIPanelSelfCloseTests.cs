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
            _factory.RegisterPanel<CloseOtherOnClosePanel>();
            _factory.RegisterPanel<CloseOtherOnClosePanelB>();

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

        /// <summary>
        /// 批量关闭时，被同批面板顺手关掉的那一个不该被再关一次。
        /// <para><c>CloseAllAsync</c> / <c>CloseLayerAsync</c> 都是「先对活动集合快照、再逐个 await」。
        /// 若快照里的 A 在自己的 <c>OnClose</c> 里关掉了同批的 B，B 的那一次已经发生完了；外层循环
        /// 随后走到 B 时它已不在活动集合里。此前这条直连 <c>ClosePanelInternalAsync</c> 的路径没有守卫
        /// （公开的 <c>CloseAsync(panel)</c> 有，但它按类型判「在不在册」，且批量循环根本不走它），
        /// 于是 B 被关第二遍：<c>OnClose</c> 跑两次、<c>PanelClosedMessage</c> 发两次，而回池没有去重
        /// （AssetManager 回池是直接 Push），同一个 GameObject 会被压进池里两次，之后可能被两个调用方
        /// 各取一次。</para>
        /// <para>递归不会无限展开：<c>CloseSelfAsync</c> 自带「未打开则忽略」的守卫，而面板在
        /// <c>DoCloseAsync</c> 一开始就进入 Closing 态，故 A、B 互相关不会来回弹。</para>
        /// </summary>
        [Test]
        public async Task CloseAllAsync_PanelsClosingEachOther_ReleasesEachExactlyOnce()
        {
            var a = await UIManager.OpenAsync<CloseOtherOnClosePanel>("ui/a");
            var b = await UIManager.OpenAsync<CloseOtherOnClosePanelB>("ui/b");

            // 互为牺牲者：无论批量关闭先轮到谁，另一个都会在它的 OnClose 里被顺手关掉，
            // 于是外层快照里的那一项必然变成「已经关过的面板」——与字典枚举顺序无关
            a.Victim = b;
            b.Victim = a;

            await UIManager.CloseAllAsync();

            Assert.IsFalse(UIManager.IsAnyOpen);
            Assert.AreEqual(1, CountOf(a, "OnClose"), "每个面板只应关一次");
            Assert.AreEqual(1, CountOf(b, "OnClose"), "每个面板只应关一次");
            Assert.AreEqual(2, _factory.ReleaseCount, "每个面板只应回池一次");
            Assert.AreEqual(2, _factory.PooledCount, "池里应是两个不同实例，而不是同一个被压了两次");
        }

        /// <summary>数某个生命周期回调在日志里出现了几次（列表小，手写循环即可）。</summary>
        private static int CountOf(FakePanel panel, string entry)
        {
            int count = 0;
            for (int i = 0; i < panel.Log.Count; i++)
            {
                if (panel.Log[i] == entry)
                    count++;
            }
            return count;
        }

        [Test]
        public async Task DuringOnOpen_IsOpenIsAlreadyTrue()
        {
            var panel = await UIManager.OpenAsync<StateProbePanel>("ui/probe");

            Assert.IsTrue(panel.IsOpenDuringOnOpen,
                "打开期间 IsOpen 就应为 true，否则 CloseSelfAsync 的守卫会把请求丢掉");
        }

        [Test]
        public async Task DuringOnOpen_IsOpeningIsTrue()
        {
            var panel = await UIManager.OpenAsync<StateProbePanel>("ui/probe");

            Assert.IsTrue(panel.IsOpeningDuringOnOpen, "打开期间 IsOpening 应为 true");
            Assert.IsFalse(panel.IsOpening, "打开完成后 IsOpening 应复位");
            Assert.IsTrue(panel.IsOpen, "打开完成后仍是已打开状态");
        }

        [Test]
        public async Task CloseSelfAsyncInsideOnOpen_ActuallyCloses()
        {
            var panel = await UIManager.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            Assert.IsNotNull(panel, "打开仍应返回实例（调用方需自行检查 IsOpen）");
            Assert.IsFalse(UIManager.IsOpen<SelfClosingOnOpenPanel>(),
                "面板在自己的 OnOpen 里请求关闭，应当在 OnOpen 返回后真的关掉");
            Assert.IsFalse(UIManager.CanGoBack, "不应留在显示栈里");
        }

        [Test]
        public async Task CloseSelfAsyncInsideOnOpen_OnCloseRunsAfterOnOpenReturns()
        {
            var panel = await UIManager.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
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
            var subscription = UIManager.Subscribe((PanelOpenedMessage _) => openedCount++);

            try
            {
                await UIManager.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
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
            var subscription = UIManager.Subscribe((PanelOpenedMessage _) => openedCount++);

            try
            {
                await UIManager.OpenAsync<FakePanel>("ui/normal");

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
            await UIManager.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            // 实例已回池；再次打开应能正常走完（复用池中实例）
            var again = await UIManager.OpenAsync<SelfClosingOnOpenPanel>("ui/self");
            await UniTask.Yield();

            Assert.IsNotNull(again, "自关后面板应已回池，再次打开不应失败");
            Assert.IsFalse(UIManager.IsOpen<SelfClosingOnOpenPanel>(), "它仍会再次自关");
        }
    }
}
