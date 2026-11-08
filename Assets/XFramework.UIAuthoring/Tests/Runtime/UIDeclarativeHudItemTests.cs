using System.Collections.Generic;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI.Authoring;

namespace XFramework.XUI.Authoring.Tests
{
    /// <summary>
    /// 声明式 HUD（阶段 2b）：只绑定文本、不绑定动作；生命周期由核心 <c>UIHudItem</c> 负责，
    /// 这里用 internal 测试钩子驱动 <c>OnOpenImpl</c>/<c>OnCloseImpl</c> 并手动模拟回池。
    /// </summary>
    [TestFixture]
    public sealed class UIDeclarativeHudItemTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UIPanelDefinition> _definitions = new List<UIPanelDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();

            for (int i = _definitions.Count - 1; i >= 0; i--)
            {
                if (_definitions[i] != null)
                    Object.DestroyImmediate(_definitions[i]);
            }
            _definitions.Clear();
        }

        private UIDeclarativeHudItem CreateHud()
        {
            UIDeclarativeHudItem hud = UIAuthoringTestFactory.CreateHud();
            _created.Add(hud.gameObject);
            return hud;
        }

        private UIPanelDefinition CreateDefinition(UIPanelDefinitionKind kind = UIPanelDefinitionKind.Hud)
        {
            UIPanelDefinition definition = UIAuthoringTestFactory.CreateDefinition(kind);
            _definitions.Add(definition);
            return definition;
        }

        private Text CreateText(Transform parent, string name = "txt_name")
        {
            Transform node = UIAuthoringTestFactory.CreateChild(parent, name, typeof(Text));
            return node.GetComponent<Text>();
        }

        [Test]
        public async Task Open_LiteralText_BindsAndDisablesRaycaster()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition();
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_name", Literal = "NPC" });
            Text text = CreateText(hud.transform);

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo("NPC"));
            Assert.That(hud.Raycaster.enabled, Is.False, "HUD 默认不可交互，Raycaster 应关闭");

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        [Test]
        public async Task Open_LocalizationWithoutKey_DoesNotThrow()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition();
            definition.TextBindings.Add(new UITextBinding
            {
                NodeName = "txt_name",
                UseLocalization = true,
                LocalizationKey = string.Empty,
            });
            Text text = CreateText(hud.transform);

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        [Test]
        public async Task Open_KindMismatch_SkipsBinding()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_name", Literal = "NPC" });
            Text text = CreateText(hud.transform);

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();

            Assert.That(text.text, Is.EqualTo(string.Empty));

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        [Test]
        public async Task Open_NoDefinition_DoesNotThrow()
        {
            UIDeclarativeHudItem hud = CreateHud();

            await hud.OpenForTestAsync();
            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        [Test]
        public async Task Open_ActionBindings_AreNotBound()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition();
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_name", Literal = "NPC" });
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new System.Collections.Generic.List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.CloseSelf },
                },
            });
            CreateText(hud.transform);
            Button button = UIAuthoringTestFactory
                .CreateChild(hud.transform, "btn_close", typeof(Button))
                .GetComponent<Button>();

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();

            // 若动作被误绑定，点击会走到未初始化的 UIManager 并记 Error；Unity 测试默认把
            // 意外 Error 当失败。HUD 在 2b 不绑定动作，这里应当安静无事。
            button.onClick.Invoke();
            await UniTask.Yield();

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }

        [Test]
        public async Task Reopen_AfterPoolRecycle_RebindsText()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition();
            UITextBinding binding = new UITextBinding { NodeName = "txt_name", Literal = "First" };
            definition.TextBindings.Add(binding);
            Text text = CreateText(hud.transform);

            hud.SetDefinitionForTest(definition);
            await hud.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("First"));

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);

            binding.Literal = "Second";
            await hud.OpenForTestAsync();
            Assert.That(text.text, Is.EqualTo("Second"));

            await hud.CloseForTestAsync();
            UIAuthoringTestFactory.InvokePoolRecycle(hud);
        }
    }
}
