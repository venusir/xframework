using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XMessage;
using XFramework.XReactive;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 声明式弹窗（阶段 4）：Kind 匹配绑定、CloseSelf、数据绑定与回池释放。
    /// </summary>
    [TestFixture]
    public sealed class UIDeclarativePopupPanelTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UIPanelDefinition> _definitions = new List<UIPanelDefinition>();
        private readonly List<System.IDisposable> _disposables = new List<System.IDisposable>();

        [SetUp]
        public void SetUp()
        {
            MessageManager.Clear();
            UIDataProviderRegistry.ResetForNewSession();
        }

        [TearDown]
        public void TearDown()
        {
            UIDataProviderRegistry.ResetForNewSession();

            for (int i = _created.Count - 1; i >= 0; i--)
                UIAuthoringTestFactory.Destroy(_created[i]);
            _created.Clear();

            for (int i = _definitions.Count - 1; i >= 0; i--)
            {
                if (_definitions[i] != null)
                    Object.DestroyImmediate(_definitions[i]);
            }
            _definitions.Clear();

            for (int i = _disposables.Count - 1; i >= 0; i--)
                _disposables[i]?.Dispose();
            _disposables.Clear();

            MessageManager.Clear();
        }

        private UIDeclarativePopupPanel CreatePopup()
        {
            UIDeclarativePopupPanel popup = UIAuthoringTestFactory.CreatePopup();
            _created.Add(popup.gameObject);
            return popup;
        }

        private UIPanelDefinition CreateDefinition(UIPanelDefinitionKind kind = UIPanelDefinitionKind.Popup)
        {
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(kind);
            _definitions.Add(definition);
            return definition;
        }

        [Test]
        public async Task Open_BindsLiteralTextAndCloseButton()
        {
            UIDeclarativePopupPanel popup = CreatePopup();
            UIPanelDefinition definition = CreateDefinition();
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_title", Literal = "Menu" });
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new List<UIAction> { new UIAction { Kind = UIActionKind.CloseSelf } },
            });

            Text text = UIAuthoringTestFactory.CreateChild(popup.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();
            Button button = UIAuthoringTestFactory.CreateChild(popup.transform, "btn_close", typeof(Button))
                .GetComponent<Button>();

            var runtime = new FakeActionRuntime();
            popup.SetDefinitionForTest(definition);
            popup.SetRuntimeForTest(runtime);

            await popup.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Menu"));

            button.onClick.Invoke();
            await UniTask.Yield();
            Assert.That(runtime.CloseCalls, Is.EqualTo(1));

            await popup.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(popup);
        }

        [Test]
        public async Task Open_KindMismatch_SkipsBinding()
        {
            UIDeclarativePopupPanel popup = CreatePopup();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_title", Literal = "Menu" });
            Text text = UIAuthoringTestFactory.CreateChild(popup.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();

            popup.SetDefinitionForTest(definition);
            popup.SetRuntimeForTest(new FakeActionRuntime());

            await popup.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));
        }

        [Test]
        public async Task Open_DataBinding_Applies()
        {
            var provider = new FakeDataProvider();
            ReactiveProperty<string> title = new ReactiveProperty<string>("Popup");
            _disposables.Add(title);
            provider.Values["demo.title"] = title;
            UIDataProviderRegistry.Register(provider);

            UIDeclarativePopupPanel popup = CreatePopup();
            UIPanelDefinition definition = CreateDefinition();
            definition.DataBindings.Add(new UIDataBinding
            {
                NodeName = "txt_title",
                Kind = UIDataBindingKind.Text,
                DataKey = "demo.title",
            });
            Text text = UIAuthoringTestFactory.CreateChild(popup.transform, "txt_title", typeof(Text))
                .GetComponent<Text>();

            popup.SetDefinitionForTest(definition);

            await popup.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Popup"));

            title.Value = "Next";
            Assert.That(text.text, Is.EqualTo("Next"));

            await popup.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(popup);
            Assert.That(title.SubscriptionCount, Is.EqualTo(0), "回池后订阅必须释放");
        }

        [Test]
        public async Task Open_NoDefinition_DoesNotThrow()
        {
            UIDeclarativePopupPanel popup = CreatePopup();
            popup.SetRuntimeForTest(new FakeActionRuntime());

            await popup.OpenForTestAsync();
            await popup.CloseForTestAsync();
        }
    }
}
