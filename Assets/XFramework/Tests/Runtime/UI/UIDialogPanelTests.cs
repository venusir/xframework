using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 对话框结果通道的契约测试。
    /// <para>本 fixture 守的是一整类<b>静默故障</b>：等待方永远不恢复。它不会抛异常、不会报错，
    /// 只是那条异步流程从此停在那里——所以每一条用例都在钉一个具体的关闭途径。</para>
    /// <para><b>断言用「已完成」而不是「等一会儿看有没有结果」</b>：后者在设计回归时会<b>挂住整套用例</b>
    /// 而不是失败。兜底是同步兑现的，故关闭返回时任务必然已完成，这条断言既精确又不会挂。</para>
    /// </summary>
    [TestFixture]
    public class UIDialogPanelTests
    {
        private GameObject _root;
        private FakePanelFactory _factory;

        [SetUp]
        public void SetUp()
        {
            // 归零：不依赖上一个 fixture 的 TearDown（SetUp 中途失败时它不会执行）
            UIManager.Destroy();
            UpdateManager.AutoInit();
            UpdateManager.Clear();

            _root = new GameObject("UIRoot_DialogTest", typeof(RectTransform));

            _factory = new FakePanelFactory();
            _factory.RegisterPanel<FakeDialog>();

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

        #region 辅助

        private async UniTask<FakeDialog> OpenDialogAsync()
        {
            return await UIManager.OpenAsync<FakeDialog>("PF_FakeDialog");
        }

        /// <summary>断言任务已兑现且取到期望值。在 await 之前先断言已完成，避免回归时挂住整轮。</summary>
        private static async UniTask AssertResolvedAsync(UniTask<FakeDialogResult> wait,
            FakeDialogResult expected, string message)
        {
            Assert.IsTrue(wait.Status.IsCompleted(), message + "——任务未兑现，等待方会一直挂着");
            Assert.AreEqual(expected, await wait, message);
        }

        #endregion

        #region 兜底兑现

        [Test]
        public async Task CloseWithoutResult_ResolvesWithDefault()
        {
            var panel = await OpenDialogAsync();
            var wait = panel.WaitResultAsync();

            await UIManager.CloseAsync(panel);

            await AssertResolvedAsync(wait, FakeDialogResult.None, "关闭时无人设结果，应兜底为 default");
        }

        [Test]
        public async Task CloseAllAsync_ResolvesWithDefault()
        {
            // 切场景走的就是这条——它不会去点「取消」按钮
            var panel = await OpenDialogAsync();
            var wait = panel.WaitResultAsync();

            await UIManager.CloseAllAsync();

            await AssertResolvedAsync(wait, FakeDialogResult.None, "CloseAllAsync 也必须兑现");
        }

        [Test]
        public async Task ManagerDispose_ResolvesWithDefault()
        {
            // ★ 本用例是「兜底不能放 OnClose」的直接理由：Dispose 同步回池全部面板，
            //   刻意不走关闭路径（不 await 关闭动画），故 OnClose 在这条路上根本不会被调用
            var panel = await OpenDialogAsync();
            var wait = panel.WaitResultAsync();

            UIManager.Destroy();

            await AssertResolvedAsync(wait, FakeDialogResult.None, "Dispose 也必须兑现");
        }

        [Test]
        public async Task CloseThrowing_StillResolvesWithDefault()
        {
            var panel = await OpenDialogAsync();
            panel.ThrowOnClose = true;
            var wait = panel.WaitResultAsync();

            // 关闭失败会上抛，但面板已被摘账并回池——兑现不能因此被跳过
            Assert.ThrowsAsync<System.InvalidOperationException>(
                async () => await UIManager.CloseAsync(panel));

            await AssertResolvedAsync(wait, FakeDialogResult.None, "OnClose 抛异常时兜底仍应兑现");
        }

        #endregion

        #region 先到者胜

        [Test]
        public async Task ResultSetInOnClose_Wins()
        {
            var panel = await OpenDialogAsync();
            panel.ResultOnClose = FakeDialogResult.Cancel;
            var wait = panel.WaitResultAsync();

            await UIManager.CloseAsync(panel);

            await AssertResolvedAsync(wait, FakeDialogResult.Cancel,
                "OnClose 里设的结果必须胜过回池兜底——兜底只在没人设时才生效");
        }

        [Test]
        public async Task ResultSetBeforeWait_ReturnsImmediately()
        {
            // 「在 OnOpen 里就兑现」是合法顺序（如打开即判定无权限），等待方不该因此挂起。
            // 用池化复用把标记带进第二轮：属性活在实例上，复用同一实例即等效于「打开时就有结果」
            var first = await OpenDialogAsync();
            await UIManager.CloseAsync(first);
            first.ResultOnOpen = FakeDialogResult.Confirm;

            var second = await OpenDialogAsync();
            var wait = second.WaitResultAsync();

            await AssertResolvedAsync(wait, FakeDialogResult.Confirm, "先兑现后等待应立即拿到结果");
            await UIManager.CloseAsync(second);
        }

        [Test]
        public async Task SetResultAfterResolved_IsNoOp()
        {
            var panel = await OpenDialogAsync();
            var wait = panel.WaitResultAsync();

            await UIManager.CloseAsync(panel);
            panel.SetResult(FakeDialogResult.Confirm);   // 迟到的一次设置

            await AssertResolvedAsync(wait, FakeDialogResult.None, "结果已兑现后再次设置应为空操作");
        }

        #endregion

        #region 池化复用

        [Test]
        public async Task Reopen_ReturnsFreshResult_NotPrevious()
        {
            // 面板是池化复用的：新一轮打开必须是白纸，否则重开的对话框立刻返回上一轮的结果
            var first = await OpenDialogAsync();
            first.ResultOnClose = FakeDialogResult.Confirm;
            var firstWait = first.WaitResultAsync();
            await UIManager.CloseAsync(first);
            await AssertResolvedAsync(firstWait, FakeDialogResult.Confirm, "第一轮");

            // 标记活在实例上、随池化一起进第二轮——不清掉的话第二轮会自己再设一次，
            // 于是断言看到 Confirm 也分不清是「没白纸化」还是「本轮真的又设了」
            first.ResultOnClose = null;

            var second = await OpenDialogAsync();
            var secondWait = second.WaitResultAsync();
            await UIManager.CloseAsync(second);

            await AssertResolvedAsync(secondWait, FakeDialogResult.None,
                "第二轮没人设结果，必须拿到 default——拿到 Confirm 说明结果通道没在白纸化");
        }

        [Test]
        public async Task Reopen_IsSamePooledInstance()
        {
            // 前提守卫：上面那条只有在确实是同一个实例复用时才有意义
            var first = await OpenDialogAsync();
            await UIManager.CloseAsync(first);
            var second = await OpenDialogAsync();

            Assert.AreSame(first, second, "本 fixture 依赖伪工厂复用实例；不成立时上面那条用例是空的");
            await UIManager.CloseAsync(second);
        }

        #endregion
    }
}
