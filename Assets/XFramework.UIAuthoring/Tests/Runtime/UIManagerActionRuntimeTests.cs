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
