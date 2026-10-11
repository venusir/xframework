using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUpdate;

namespace XFramework.XUI.Tests
{
    /// <summary>
    /// 对话框的契约测试：结果通道（<see cref="UIDialogPanel{TResult}"/>）与门面入口
    /// （<c>UIManager.ShowDialogAsync</c>）。
    /// <para>本 fixture 守的是一整类<b>静默故障</b>：等待方永不恢复、遮罩盖住对话框、遮罩不释放。
    /// 它们都不会抛异常、不会报错，只是「流程停在那里」或「看起来不对」——所以每一条用例都在钉一个
    /// 具体的关闭途径或一处具体的配置。</para>
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

        #region ShowDialogAsync（门面入口）

        /// <summary>取遮罩对象；未显示时返回 null。遮罩名与容器位置见 <c>UIManagerImpl.EnsureMaskInstance</c>。</summary>
        private Transform FindMask() => _root.transform.Find("UIManager_Mask");

        [Test]
        public async Task Modal_ShowsMaskOneLayerBelowDialog()
        {
            // ★ 本用例是「遮罩层自动推导」的核心断言。此前文档教的 ShowMask() 默认组合
            //   （UILayers.Mask = 500 层）会算出 16031，远高于 Popup 层对话框的 6401，
            //   于是对话框被自己的遮罩盖住——变暗且点不动
            var dialog = UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>("PF_FakeDialog");
            await UniTask.Yield();

            var mask = FindMask();
            Assert.IsNotNull(mask, "模态对话框必须挂上遮罩");

            int dialogOrder = UIManager.GetPanel<FakeDialog>().Canvas.sortingOrder;
            int maskOrder = mask.GetComponent<Canvas>().sortingOrder;

            Assert.AreEqual(UISorting.MaskOrder(UILayers.Popup - 1), maskOrder,
                "遮罩层应取对话框层 − 1");
            Assert.Less(maskOrder, dialogOrder, "遮罩必须在对话框之下，否则会盖住对话框");

            await UIManager.CloseAllAsync();
            await dialog;
        }

        [Test]
        public async Task CustomLayer_MaskFollowsIt()
        {
            var dialog = UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>(
                "PF_FakeDialog", layer: UILayers.Top);
            await UniTask.Yield();

            Assert.AreEqual(UISorting.MaskOrder(UILayers.Top - 1),
                FindMask().GetComponent<Canvas>().sortingOrder,
                "遮罩层应随对话框层一起移动");

            await UIManager.CloseAllAsync();
            await dialog;
        }

        [Test]
        public async Task NonModal_ShowsNoMask()
        {
            var dialog = UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>(
                "PF_FakeDialog", modal: false);
            await UniTask.Yield();

            Assert.IsFalse(UIManager.IsMaskShowing, "modal: false 时不应建遮罩");

            await UIManager.CloseAllAsync();
            await dialog;
        }

        [Test]
        public async Task Mask_IsReleasedWhenPanelCloses()
        {
            // 遮罩持有经 owner 绑定到面板：不自己 Dispose 也不会泄漏
            var dialog = UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>("PF_FakeDialog");
            await UniTask.Yield();
            Assert.IsTrue(UIManager.IsMaskShowing, "前提：此刻遮罩应在显示中");

            await UIManager.CloseAllAsync();

            // 断言用公开契约而非「对象不存在」——遮罩是单实例复用的，隐藏是失活、对象仍常驻
            Assert.IsFalse(UIManager.IsMaskShowing,
                "面板关闭后遮罩必须一并释放，否则界面此后全黑且点不动");
            await dialog;
        }

        [Test]
        public async Task Result_FlowsBackToCaller()
        {
            var dialog = UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>("PF_FakeDialog");
            await UniTask.Yield();

            var panel = UIManager.GetPanel<FakeDialog>();
            panel.ResultOnClose = FakeDialogResult.Confirm;
            await UIManager.CloseAsync(panel);

            Assert.AreEqual(FakeDialogResult.Confirm, await dialog);
        }

        [Test]
        public async Task OpenFailure_ReturnsDefaultWithoutThrowing()
        {
            // 打不开就没有可等的结果——与 OpenAsync 的「失败返回 null」一致，不抛异常
            _factory.RegisterPanel<FakeDialog>(parent => null);

            // 伪工厂返回 null 之后，管理器会记一条「实例化失败」的错误日志；
            // 框架把意外 Error 当失败，故这里显式声明它是预期的
            LogAssert.Expect(LogType.Error, new Regex("Failed to instantiate panel"));

            var result = await UIManager.ShowDialogAsync<FakeDialog, FakeDialogResult>("PF_FakeDialog");

            Assert.AreEqual(FakeDialogResult.None, result);
        }

        #endregion
    }
}
