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
