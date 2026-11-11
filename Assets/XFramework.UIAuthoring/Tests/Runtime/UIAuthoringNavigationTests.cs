using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using XFramework.XUI;

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

        [Test]
        public void ShowDialog_Cancellation_Propagates()
        {
            var runtime = new FakeActionRuntime
            {
                DialogException = new System.OperationCanceledException(),
            };

            Assert.CatchAsync<System.OperationCanceledException>(async () =>
                await UIAuthoringNavigation.ShowDialogAsync(runtime, "ui/dialog", "shop.confirm", default));
        }

        [Test]
        public async Task ShowPopup_NoExisting_Opens()
        {
            var runtime = new FakeActionRuntime();
            var anchorGo = new GameObject("Anchor", typeof(RectTransform));
            try
            {
                var anchor = (RectTransform)anchorGo.transform;

                UIDeclarativePopupPanel result = await UIAuthoringNavigation.ShowPopupAsync(
                    runtime, anchor, "ui/popup", UIPopupDirection.Down, UIPopupAlign.Center, true,
                    default);

                Assert.That(runtime.ShowDeclarativePopupCalls, Is.EqualTo(1));
                Assert.That(runtime.LastPopupAddress, Is.EqualTo("ui/popup"));
                Assert.That(runtime.LastPopupAnchor, Is.SameAs(anchor));
                Assert.That(result, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(anchorGo);
            }
        }

        [Test]
        public async Task ShowPopup_SameAddress_RefocusesAndRepositions()
        {
            UIDeclarativePopupPanel popup = UIAuthoringTestFactory.CreatePopup();
            var anchorGo = new GameObject("Anchor", typeof(RectTransform));
            try
            {
                UIAuthoringTestFactory.SetAssetPath(popup, "ui/popup");
                var runtime = new FakeActionRuntime { DeclarativePopup = popup };

                UIDeclarativePopupPanel result = await UIAuthoringNavigation.ShowPopupAsync(
                    runtime, (RectTransform)anchorGo.transform, "ui/popup",
                    UIPopupDirection.Auto, UIPopupAlign.Start, true, default);

                Assert.That(runtime.ShowDeclarativePopupCalls, Is.EqualTo(1));
                Assert.That(result, Is.SameAs(popup));
            }
            finally
            {
                Object.DestroyImmediate(anchorGo);
                UIAuthoringTestFactory.Destroy(popup.gameObject);
            }
        }

        [Test]
        public async Task ShowPopup_DifferentAddress_Rejects()
        {
            UIDeclarativePopupPanel popup = UIAuthoringTestFactory.CreatePopup();
            var anchorGo = new GameObject("Anchor", typeof(RectTransform));
            try
            {
                UIAuthoringTestFactory.SetAssetPath(popup, "ui/other");
                var runtime = new FakeActionRuntime { DeclarativePopup = popup };
                LogAssert.Expect(LogType.Warning,
                    new Regex("ShowPopupAsync rejected: a different declarative popup is already open"));

                UIDeclarativePopupPanel result = await UIAuthoringNavigation.ShowPopupAsync(
                    runtime, (RectTransform)anchorGo.transform, "ui/popup",
                    UIPopupDirection.Auto, UIPopupAlign.Start, true, default);

                Assert.That(runtime.ShowDeclarativePopupCalls, Is.EqualTo(0));
                Assert.That(result, Is.SameAs(popup));
            }
            finally
            {
                Object.DestroyImmediate(anchorGo);
                UIAuthoringTestFactory.Destroy(popup.gameObject);
            }
        }
    }
}
