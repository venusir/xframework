using NUnit.Framework;

namespace XFramework.XUI.Authoring.Tests
{
    [TestFixture]
    public sealed class UIAuthoringActionMessageTests
    {
        [Test]
        public void Message_ExposesFields()
        {
            var message = new UIAuthoringActionMessage("shop.buy", "42");

            Assert.That(message.ActionId, Is.EqualTo("shop.buy"));
            Assert.That(message.Payload, Is.EqualTo("42"));
        }
    }
}
