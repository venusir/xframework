using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using XFramework.XUI.Authoring.Editor;
using XFramework.XUI.View;

namespace XFramework.XUI.Authoring.Editor.Tests
{
    [TestFixture]
    public sealed class UIAuthoringValidatorTests
    {
        private readonly List<GameObject> _created = new List<GameObject>();
        private readonly List<UIPanelDefinition> _definitions = new List<UIPanelDefinition>();

        [TearDown]
        public void TearDown()
        {
            for (int i = _created.Count - 1; i >= 0; i--)
            {
                if (_created[i] != null)
                    UnityEngine.Object.DestroyImmediate(_created[i]);
            }
            _created.Clear();

            for (int i = _definitions.Count - 1; i >= 0; i--)
            {
                if (_definitions[i] != null)
                    UnityEngine.Object.DestroyImmediate(_definitions[i]);
            }
            _definitions.Clear();
        }

        private sealed class TestCSharpPanel : UIPanelBase
        {
            protected override UniTask OnOpen(object userData) => UniTask.CompletedTask;

            protected override UniTask OnClose() => UniTask.CompletedTask;
        }

        private UIDeclarativePanel CreatePanel()
        {
            var go = new GameObject("Panel", typeof(RectTransform));
            _created.Add(go);
            return go.AddComponent<UIDeclarativePanel>();
        }

        private UIDeclarativeDialogPanel CreateDialog()
        {
            var go = new GameObject("Dialog", typeof(RectTransform));
            _created.Add(go);
            return go.AddComponent<UIDeclarativeDialogPanel>();
        }

        private UIDeclarativeHudItem CreateHud()
        {
            var go = new GameObject("Hud", typeof(RectTransform));
            _created.Add(go);
            return go.AddComponent<UIDeclarativeHudItem>();
        }

        private UIPanelDefinition CreateDefinition(UIPanelDefinitionKind kind)
        {
            var definition = ScriptableObject.CreateInstance<UIPanelDefinition>();
            definition.Kind = kind;
            _definitions.Add(definition);
            return definition;
        }

        private Transform CreateChild(Transform parent, string name, params Type[] components)
        {
            var go = new GameObject(name, typeof(RectTransform));
            _created.Add(go);
            go.transform.SetParent(parent, false);
            for (int i = 0; i < components.Length; i++)
                go.AddComponent(components[i]);
            return go.transform;
        }

        private static UIAuthoringAddressCatalog CreateCatalog(
            Dictionary<string, GameObject> targets)
        {
            var catalog = new UIAuthoringAddressCatalog
            {
                Status = UIAuthoringAddressStatus.Available,
            };
            foreach (KeyValuePair<string, GameObject> pair in targets)
                catalog.AddressToPath[pair.Key] = "Assets/" + pair.Key + ".prefab";
            return catalog;
        }

        private static List<UIAuthoringIssue> Validate(
            GameObject root, UIPanelDefinition definition, UIAuthoringAddressCatalog catalog,
            Dictionary<string, GameObject> targets)
        {
            return UIAuthoringValidator.Validate(
                root,
                definition,
                catalog,
                catalog != null && catalog.IsAvailable,
                path =>
                {
                    string key = path.Substring("Assets/".Length, path.Length - "Assets/".Length - ".prefab".Length);
                    return targets != null && targets.TryGetValue(key, out GameObject target) ? target : null;
                });
        }

        private static bool Has(List<UIAuthoringIssue> issues, string ruleId)
        {
            for (int i = 0; i < issues.Count; i++)
            {
                if (issues[i].RuleId == ruleId)
                    return true;
            }
            return false;
        }

        [Test]
        public void RootComponentMissing_Reports()
        {
            var root = new GameObject("Root", typeof(RectTransform));
            _created.Add(root);
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);

            List<UIAuthoringIssue> issues = Validate(root, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.RootComponentMissing), Is.True);
        }

        [Test]
        public void KindMismatch_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Dialog);

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.KindMismatch), Is.True);
        }

        [Test]
        public void TextNodeMissing_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.TextBindings.Add(new UITextBinding { NodeName = "missing", Literal = "x" });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.TextNodeMissing), Is.True);
        }

        [Test]
        public void TextTargetMissing_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "txt_title");
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_title", Literal = "x" });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.TextTargetMissing), Is.True);
        }

        [Test]
        public void ButtonMissing_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_close");
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new List<UIAction> { new UIAction { Kind = UIActionKind.CloseSelf } },
            });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.ButtonMissing), Is.True);
        }

        [Test]
        public void CloseSelfNotLast_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_close", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.CloseSelf },
                    new UIAction { Kind = UIActionKind.Custom, ActionId = "next" },
                },
            });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.ActionOrder), Is.True);
        }

        [Test]
        public void OpenTargetDeclarative_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_open", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_open",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.Open, AssetPath = "decl" },
                },
            });

            var target = new GameObject("decl", typeof(RectTransform));
            _created.Add(target);
            target.AddComponent<UIDeclarativePanel>();
            var targets = new Dictionary<string, GameObject> { ["decl"] = target };

            List<UIAuthoringIssue> issues = Validate(
                panel.gameObject, definition, CreateCatalog(targets), targets);

            Assert.That(Has(issues, UIAuthoringRuleIds.TargetInvalid), Is.True);
        }

        [Test]
        public void OpenTargetCSharp_NoTargetError()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_open", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_open",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.Open, AssetPath = "csharp" },
                },
            });

            var target = new GameObject("csharp", typeof(RectTransform));
            _created.Add(target);
            target.AddComponent<TestCSharpPanel>();
            var targets = new Dictionary<string, GameObject> { ["csharp"] = target };

            List<UIAuthoringIssue> issues = Validate(
                panel.gameObject, definition, CreateCatalog(targets), targets);

            Assert.That(Has(issues, UIAuthoringRuleIds.TargetInvalid), Is.False);
        }

        [Test]
        public void ReplaceTargetCSharp_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_replace", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_replace",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.Replace, AssetPath = "csharp" },
                },
            });

            var target = new GameObject("csharp", typeof(RectTransform));
            _created.Add(target);
            target.AddComponent<TestCSharpPanel>();
            var targets = new Dictionary<string, GameObject> { ["csharp"] = target };

            List<UIAuthoringIssue> issues = Validate(
                panel.gameObject, definition, CreateCatalog(targets), targets);

            Assert.That(Has(issues, UIAuthoringRuleIds.TargetInvalid), Is.True);
        }

        [Test]
        public void ShowDialogTargetPanel_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_dialog", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_dialog",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.ShowDialog, AssetPath = "decl", ActionId = "confirm" },
                },
            });

            var target = new GameObject("decl", typeof(RectTransform));
            _created.Add(target);
            target.AddComponent<UIDeclarativePanel>();
            var targets = new Dictionary<string, GameObject> { ["decl"] = target };

            List<UIAuthoringIssue> issues = Validate(
                panel.gameObject, definition, CreateCatalog(targets), targets);

            Assert.That(Has(issues, UIAuthoringRuleIds.TargetInvalid), Is.True);
        }

        [Test]
        public void CustomEmptyActionId_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_custom", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_custom",
                Actions = new List<UIAction> { new UIAction { Kind = UIActionKind.Custom } },
            });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.ActionResultInvalid), Is.True);
        }

        [Test]
        public void GlobalAction_Warns()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_layer", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_layer",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.SetLayerVisibility, Layer = 200, BoolValue = false },
                },
            });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.GlobalAction), Is.True);
        }

        [Test]
        public void CloseWithResultOnPanel_Reports()
        {
            UIDeclarativePanel panel = CreatePanel();
            CreateChild(panel.transform, "btn_result", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Panel);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_result",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.CloseWithResult, ResultId = "confirm" },
                },
            });

            List<UIAuthoringIssue> issues = Validate(panel.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.ActionNotAllowed), Is.True);
        }

        [Test]
        public void HudDefinition_TextBinding_NoHudRuleViolation()
        {
            UIDeclarativeHudItem hud = CreateHud();
            CreateChild(hud.transform, "txt_name", typeof(UnityEngine.UI.Text));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Hud);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_name", Literal = "NPC" });

            List<UIAuthoringIssue> issues = Validate(hud.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.HudActionNotSupported), Is.False);
            Assert.That(Has(issues, UIAuthoringRuleIds.HudTabNotSupported), Is.False);
            Assert.That(Has(issues, UIAuthoringRuleIds.TextNodeMissing), Is.False);
        }

        [Test]
        public void HudDefinitionWithActions_Reports()
        {
            UIDeclarativeHudItem hud = CreateHud();
            CreateChild(hud.transform, "txt_name", typeof(UnityEngine.UI.Text));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Hud);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_close",
                Actions = new List<UIAction> { new UIAction { Kind = UIActionKind.CloseSelf } },
            });

            List<UIAuthoringIssue> issues = Validate(hud.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.HudActionNotSupported), Is.True);
        }

        [Test]
        public void HudDefinitionWithTabs_Warns()
        {
            UIDeclarativeHudItem hud = CreateHud();
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Hud);
            definition.TabGroups.Add(new UITabGroup { GroupName = "tabs", NodeNames = new List<string> { "page_a" } });

            List<UIAuthoringIssue> issues = Validate(hud.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.HudTabNotSupported), Is.True);
        }

        [Test]
        public void HudTextNodeTooDeep_ReportsDepth()
        {
            UIDeclarativeHudItem hud = CreateHud();
            Transform node = hud.transform;
            for (int depth = 1; depth <= 6; depth++)
                node = CreateChild(node, "layer_" + depth);
            CreateChild(node, "txt_deep", typeof(UnityEngine.UI.Text));

            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Hud);
            definition.TextBindings.Add(new UITextBinding { NodeName = "txt_deep", Literal = "x" });

            List<UIAuthoringIssue> issues = Validate(hud.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.HudNodeDepthExceeded), Is.True);
        }

        [Test]
        public void DialogDefinition_AllowsCloseWithResult()
        {
            UIDeclarativeDialogPanel dialog = CreateDialog();
            CreateChild(dialog.transform, "btn_confirm", typeof(Button));
            UIPanelDefinition definition = CreateDefinition(UIPanelDefinitionKind.Dialog);
            definition.ActionBindings.Add(new UIActionBinding
            {
                ButtonNodeName = "btn_confirm",
                Actions = new List<UIAction>
                {
                    new UIAction { Kind = UIActionKind.CloseWithResult, ResultId = "confirm" },
                },
            });

            List<UIAuthoringIssue> issues = Validate(dialog.gameObject, definition, null, null);

            Assert.That(Has(issues, UIAuthoringRuleIds.ActionNotAllowed), Is.False);
        }
    }
}
