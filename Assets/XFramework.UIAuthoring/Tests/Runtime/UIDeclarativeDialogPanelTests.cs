using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XMessage;

namespace XFramework.XUI.Authoring.Tests
{
    [TestFixture]
    public sealed class UIDeclarativeDialogPanelTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
                UIAuthoringTestFactory.Destroy(_created[i]);
            _created.Clear();
            MessageManager.Clear();
        }

        [Test]
        public async Task Open_BindsText_And_CloseWithResult()
        {
            UIDeclarativeDialogPanel dialog = UIAuthoringTestFactory.CreateDialogPanel();
            _created.Add(dialog.gameObject);
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(
                UIPanelDefinitionKind.Dialog);
            definition.TextBindings.Add(new UITextBinding
            {
                NodeName = "txt_message",
                Literal = "Are you sure?",
            });
            Text text = UIAuthoringTestFactory.CreateChild(dialog.transform, "txt_message", typeof(Text))
                .GetComponent<Text>();

            var runtime = new FakeActionRuntime();
            dialog.SetDefinitionForTest(definition);
            dialog.SetRuntimeForTest(runtime);

            await dialog.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Are you sure?"));

            await dialog.CloseWithResultAsync("confirm");
            Assert.That(runtime.CloseCalls, Is.EqualTo(1));

            await dialog.CloseForTestAsync();
            string result = await dialog.WaitResultAsync();
            Assert.That(result, Is.EqualTo("confirm"));
        }

        [Test]
        public async Task Open_NoDefinition_DoesNotThrow()
        {
            UIDeclarativeDialogPanel dialog = UIAuthoringTestFactory.CreateDialogPanel();
            _created.Add(dialog.gameObject);
            dialog.SetRuntimeForTest(new FakeActionRuntime());

            await dialog.OpenForTestAsync();
            await dialog.CloseForTestAsync();
        }
    }
}
