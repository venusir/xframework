using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    [TestFixture]
    public sealed class UIAuthoringNavigationTests
    {
        [Test]
        public async Task Open_NoExisting_Opens()
        {
            var runtime = new FakeActionRuntime();

            UIDeclarativePanel result = await UIAuthoringNavigation.OpenAsync(runtime, "ui/a", 100, default);

            Assert.That(runtime.OpenDeclarativeCalls, Is.EqualTo(1));
            Assert.That(runtime.LastDeclarativeAddress, Is.EqualTo("ui/a"));
            Assert.That(result, Is.Null);
        }

        [Test]
        public async Task Open_SameAddress_Refocuses()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            try
            {
                UIAuthoringTestFactory.SetAssetPath(panel, "ui/a");
                var runtime = new FakeActionRuntime { DeclarativePanel = panel };

                UIDeclarativePanel result = await UIAuthoringNavigation.OpenAsync(runtime, "ui/a", 100, default);

                Assert.That(runtime.OpenDeclarativeCalls, Is.EqualTo(1));
                Assert.That(result, Is.SameAs(panel));
            }
            finally
            {
                UIAuthoringTestFactory.Destroy(panel.gameObject);
            }
        }

        [Test]
        public async Task Open_DifferentAddress_Rejects()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            try
            {
                UIAuthoringTestFactory.SetAssetPath(panel, "ui/a");
                var runtime = new FakeActionRuntime { DeclarativePanel = panel };

                UIDeclarativePanel result = await UIAuthoringNavigation.OpenAsync(runtime, "ui/b", 100, default);

                Assert.That(runtime.OpenDeclarativeCalls, Is.EqualTo(0));
                Assert.That(result, Is.SameAs(panel));
            }
            finally
            {
                UIAuthoringTestFactory.Destroy(panel.gameObject);
            }
        }

        [Test]
        public async Task Open_EmptyAddress_DoesNotThrow()
        {
            var runtime = new FakeActionRuntime();

            UIDeclarativePanel result = await UIAuthoringNavigation.OpenAsync(runtime, string.Empty, 100, default);

            Assert.That(result, Is.Null);
            Assert.That(runtime.OpenDeclarativeCalls, Is.EqualTo(0));
        }

        [Test]
        public async Task ShowDialog_ForwardsStatus()
        {
            var runtime = new FakeActionRuntime
            {
                DialogResult = new UIAuthoringDialogResult(UIAuthoringDialogStatus.Selected, "confirm"),
            };

            UIAuthoringDialogStatus status = await UIAuthoringNavigation.ShowDialogAsync(
                runtime, "ui/dialog", "shop.confirm", default);

            Assert.That(status, Is.EqualTo(UIAuthoringDialogStatus.Selected));
            Assert.That(runtime.ShowDeclarativeDialogCalls, Is.EqualTo(1));
            Assert.That(runtime.LastDialogAddress, Is.EqualTo("ui/dialog"));
            Assert.That(runtime.LastDialogActionId, Is.EqualTo("shop.confirm"));
        }

        [Test]
        public async Task ShowDialog_EmptyActionId_ReturnsOpenFailed()
        {
            var runtime = new FakeActionRuntime();

            UIAuthoringDialogStatus status = await UIAuthoringNavigation.ShowDialogAsync(
                runtime, "ui/dialog", string.Empty, default);

            Assert.That(status, Is.EqualTo(UIAuthoringDialogStatus.OpenFailed));
            Assert.That(runtime.ShowDeclarativeDialogCalls, Is.EqualTo(0));
        }
    }
}
