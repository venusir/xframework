using System;
using System.Threading.Tasks;
using NUnit.Framework;
using XFramework.XMessage;
using XFramework.XUI;
using XFramework.XUpdate;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 生产实现 <see cref="UIManagerActionRuntime"/> 的直接用例：经公开的 <c>UIManager.SetInstance</c>
    /// 注入 <see cref="FakeUIManagerForAuthoring"/>，不再只靠 <see cref="FakeActionRuntime"/> 间接覆盖。
    /// </summary>
    [TestFixture]
    public sealed class UIManagerActionRuntimeTests
    {
        private FakeUIManagerForAuthoring _fake;

        [SetUp]
        public void SetUp()
        {
            // 共享 PlayMode player：先归零，不依赖上一个 fixture 的 TearDown
            UIManager.Destroy();
            UpdateManager.Clear();
            MessageManager.Clear();
            _fake = new FakeUIManagerForAuthoring();
            UIManager.SetInstance(_fake);
        }

        [TearDown]
        public void TearDown()
        {
            UIManager.Destroy();
            UpdateManager.Clear();
            MessageManager.Clear();
        }

        [TestCase(false, null, UIAuthoringDialogStatus.OpenFailed)]
        [TestCase(false, "confirm", UIAuthoringDialogStatus.OpenFailed)]
        [TestCase(true, null, UIAuthoringDialogStatus.Canceled)]
        [TestCase(true, "confirm", UIAuthoringDialogStatus.Selected)]
        public void ResolveDialogStatus_CoversFourStates(bool opened, string resultId,
            UIAuthoringDialogStatus expected)
        {
            Assert.That(UIManagerActionRuntime.ResolveDialogStatus(opened, resultId),
                Is.EqualTo(expected));
        }

        [Test]
        public async Task ShowDialog_SingleFlightRejection_PublishesOpenFailed()
        {
            _fake.OpenTypes.Add(typeof(UIDeclarativeDialogPanel));
            var runtime = new UIManagerActionRuntime();
            UIAuthoringDialogStatus publishedStatus = default;
            bool published = false;

            using (MessageManager.Subscribe<string, UIAuthoringDialogResultMessage>(
                       "confirm", message =>
                       {
                           published = true;
                           publishedStatus = message.Status;
                       }))
            {
                UIAuthoringDialogResult result = await runtime.ShowDeclarativeDialogAsync(
                    "ui/dialog", "confirm", default);

                Assert.That(result.Status, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            }

            Assert.That(published, Is.True, "单飞拒绝同样要发布失败结果");
            Assert.That(publishedStatus, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            Assert.That(_fake.OpenCalls, Is.EqualTo(0), "拒绝时不应触达打开路径");
        }

        [Test]
        public async Task ShowDialog_OpenReturnsNull_ReturnsOpenFailed()
        {
            var runtime = new UIManagerActionRuntime();
            bool published = false;

            using (MessageManager.Subscribe<string, UIAuthoringDialogResultMessage>(
                       "confirm", _ => published = true))
            {
                UIAuthoringDialogResult result = await runtime.ShowDeclarativeDialogAsync(
                    "ui/dialog", "confirm", default);

                Assert.That(result.Status, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            }

            Assert.That(published, Is.True);
            Assert.That(_fake.OpenCalls, Is.EqualTo(1));
        }

        [Test]
        public async Task Replace_CloseBlocked_ReturnsFalseAndDoesNotOpen()
        {
            _fake.OpenTypes.Add(typeof(UIDeclarativePanel));
            UIDeclarativePanel current = UIAuthoringTestFactory.CreatePanel();
            try
            {
                var runtime = new UIManagerActionRuntime();

                bool switched = await runtime.ReplaceDeclarativeAsync(current, "ui/next", 100, default);

                Assert.That(switched, Is.False, "关闭被拦下时置换中止");
                Assert.That(_fake.CloseCalls, Is.EqualTo(1));
                Assert.That(_fake.OpenCalls, Is.EqualTo(0), "中止时不得打开目标面板");
            }
            finally
            {
                UIAuthoringTestFactory.Destroy(current.gameObject);
            }
        }

        [Test]
        public async Task Replace_Success_ReturnsTrue()
        {
            UIDeclarativePanel current = UIAuthoringTestFactory.CreatePanel();
            UIDeclarativePanel target = UIAuthoringTestFactory.CreatePanel();
            _fake.PanelToReturn = target;
            try
            {
                var runtime = new UIManagerActionRuntime();

                bool switched = await runtime.ReplaceDeclarativeAsync(current, "ui/next", 100, default);

                Assert.That(switched, Is.True);
                Assert.That(_fake.CloseCalls, Is.EqualTo(1));
                Assert.That(_fake.OpenCalls, Is.EqualTo(1));
            }
            finally
            {
                UIAuthoringTestFactory.Destroy(current.gameObject);
                UIAuthoringTestFactory.Destroy(target.gameObject);
            }
        }

        [Test]
        public async Task CloseAsync_AlreadyClosedPanel_ReturnsTrue()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            try
            {
                var runtime = new UIManagerActionRuntime();

                bool closed = await runtime.CloseAsync(panel, false, default);

                Assert.That(closed, Is.True, "面板已经不在打开状态时视为关闭成功");
                Assert.That(_fake.CloseCalls, Is.EqualTo(1));
            }
            finally
            {
                UIAuthoringTestFactory.Destroy(panel.gameObject);
            }
        }

        [Test]
        public async Task ShowDialog_EmptyAssetPath_PublishesOpenFailed_WhenActionIdPresent()
        {
            var runtime = new UIManagerActionRuntime();
            UIAuthoringDialogResultMessage received = default;
            bool published = false;

            using (MessageManager.Subscribe<string, UIAuthoringDialogResultMessage>(
                       "confirm", message =>
                       {
                           received = message;
                           published = true;
                       }))
            {
                UIAuthoringDialogResult result = await runtime.ShowDeclarativeDialogAsync(
                    string.Empty, "confirm", default);

                Assert.That(result.Status, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            }

            Assert.That(published, Is.True, "空 assetPath 的失败也要发布结果");
            Assert.That(received.Status, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            Assert.That(_fake.OpenCalls, Is.EqualTo(0), "空参数不应触达核心打开路径");
        }

        [Test]
        public void ShowDialog_Cancellation_PropagatesAndPublishesNothing()
        {
            _fake.OpenException = new OperationCanceledException();
            var runtime = new UIManagerActionRuntime();
            bool published = false;

            using (MessageManager.Subscribe<string, UIAuthoringDialogResultMessage>(
                       "confirm", _ => published = true))
            {
                // TaskCanceledException 是 OperationCanceledException 的派生类，用 CatchAsync 接受
                Assert.CatchAsync<OperationCanceledException>(async () =>
                    await runtime.ShowDeclarativeDialogAsync("ui/dialog", "confirm", default));
            }

            Assert.That(published, Is.False,
                "取消不是一次业务结果：不得吞成 OpenFailed，也不得发布结果消息");
        }
    }
}
