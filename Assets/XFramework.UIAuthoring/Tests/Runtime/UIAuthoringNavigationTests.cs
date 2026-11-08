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
    }
}
