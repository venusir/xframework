using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XMessage;

namespace XFramework.XUI.Authoring.Tests
{
    [TestFixture]
    public sealed class UIDeclarativePanelTests
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

        private UIDeclarativePanel CreatePanel()
        {
            UIDeclarativePanel panel = UIAuthoringTestFactory.CreatePanel();
            _created.Add(panel.gameObject);
            return panel;
        }

        [Test]
        public async Task Open_BindsLiteralTextAndCloseButton()
        {
            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition();
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_title", Literal = "Hello" });
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new List<UIAction> { new UIAction { Kind = UIActionKind.CloseSelf } },
            });

            Text text = UIAuthoringTestFactory.CreateChild(panel.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();
            Button button = UIAuthoringTestFactory.CreateChild(panel.transform, "btn_close", typeof(Button))
                .GetComponent<Button>();

            var runtime = new FakeActionRuntime();
            panel.SetDefinitionForTest(definition);
            panel.SetRuntimeForTest(runtime);

            await panel.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Hello"));

            button.onClick.Invoke();
            await UniTask.Yield();
            Assert.That(runtime.CloseCalls, Is.EqualTo(1));

            await panel.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(panel);
        }

        [Test]
        public async Task Open_KindMismatch_SkipsBinding()
        {
            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(UIPanelDefinitionKind.Dialog);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_title", Literal = "Hello" });
            Text text = UIAuthoringTestFactory.CreateChild(panel.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();

            panel.SetDefinitionForTest(definition);
            panel.SetRuntimeForTest(new FakeActionRuntime());

            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task Open_LocalizationWithoutKey_DoesNotThrow()
        {
            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition();
            definition.TextBindings.Add(new UITextBinding
            {
                NodeName = "txt_title",
                UseLocalization = true,
                LocalizationKey = string.Empty,
            });
            Text text = UIAuthoringTestFactory.CreateChild(panel.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();

            panel.SetDefinitionForTest(definition);
            panel.SetRuntimeForTest(new FakeActionRuntime());

            await panel.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task Open_NoDefinition_DoesNotThrow()
        {
            UIDeclarativePanel panel = CreatePanel();
            panel.SetRuntimeForTest(new FakeActionRuntime());

            await panel.OpenForTestAsync();
            await panel.CloseForTestAsync();
        }
    }
}
