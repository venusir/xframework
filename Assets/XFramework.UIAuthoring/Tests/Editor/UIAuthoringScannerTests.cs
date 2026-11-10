using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using XFramework.XUI.Authoring.Editor;

namespace XFramework.XUI.Authoring.Editor.Tests
{
    [TestFixture]
    public sealed class UIAuthoringScannerTests
    {
        private const string TempFolder = "Assets/__XfwUIAuthoringTests__";

        [SetUp]
        public void SetUp()
        {
            DeleteTempFolder();
        }

        [TearDown]
        public void TearDown()
        {
            DeleteTempFolder();
        }

        [Test]
        public void Scan_DeclarativePrefab_IsCollected()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__XfwUIAuthoringTests__");

            var definition = ScriptableObject.CreateInstance<UIPanelDefinition>();
            definition.Kind = UIPanelDefinitionKind.Panel;
            definition.TextBindings.Add(new UITextBinding { NodeName = "missing_node", Literal = "x" });
            string definitionPath = TempFolder + "/PanelDefinition.asset";
            AssetDatabase.CreateAsset(definition, definitionPath);

            var go = new GameObject("Panel", typeof(RectTransform));
            UIDeclarativePanel panel = go.AddComponent<UIDeclarativePanel>();
            panel.SetDefinitionForTest(definition);
            string prefabPath = TempFolder + "/Panel.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "临时预制体未导入");
            Assert.That(prefab.GetComponent<UIDeclarativePanel>(), Is.Not.Null);

            List<UIAuthoringScanResult> results = UIAuthoringScanner.Scan(new[] { TempFolder });

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].AssetPath, Is.EqualTo(prefabPath));
            Assert.That(Has(results[0].Issues, UIAuthoringRuleIds.TextNodeMissing), Is.True,
                "扫描器必须真正读到 _definition，否则定义相关规则会被整体跳过");
        }

        [Test]
        public void Scan_DeclarativeHudPrefab_IsCollected()
        {
            if (!AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.CreateFolder("Assets", "__XfwUIAuthoringTests__");

            var definition = ScriptableObject.CreateInstance<UIPanelDefinition>();
            definition.Kind = UIPanelDefinitionKind.Hud;
            string definitionPath = TempFolder + "/HudDefinition.asset";
            AssetDatabase.CreateAsset(definition, definitionPath);

            var go = new GameObject("Hud", typeof(RectTransform));
            UIDeclarativeHudItem hud = go.AddComponent<UIDeclarativeHudItem>();
            hud.SetDefinitionForTest(definition);
            string prefabPath = TempFolder + "/Hud.prefab";
            PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            Assert.That(prefab, Is.Not.Null, "临时 HUD 预制体未导入");
            Assert.That(prefab.GetComponent<UIDeclarativeHudItem>(), Is.Not.Null);

            List<UIAuthoringScanResult> results = UIAuthoringScanner.Scan(new[] { TempFolder });

            Assert.That(results.Count, Is.EqualTo(1));
            Assert.That(results[0].AssetPath, Is.EqualTo(prefabPath));
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

        private static void DeleteTempFolder()
        {
            if (AssetDatabase.IsValidFolder(TempFolder))
                AssetDatabase.DeleteAsset(TempFolder);
            if (Directory.Exists(TempFolder))
                Directory.Delete(TempFolder, true);
        }
    }
}
