using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XFramework.XUI.Authoring.Editor
{
    internal sealed class UIAuthoringScanResult
    {
        internal UIAuthoringScanResult(string assetPath, List<UIAuthoringIssue> issues)
        {
            AssetPath = assetPath;
            Issues = issues ?? new List<UIAuthoringIssue>();
        }

        internal string AssetPath { get; }

        internal List<UIAuthoringIssue> Issues { get; }
    }

    /// <summary>
    /// 扫描 <c>Assets</c> 下带声明式组件的预制体，逐个调用纯规则引擎。
    /// </summary>
    internal static class UIAuthoringScanner
    {
        internal static List<UIAuthoringScanResult> Scan(IReadOnlyList<string> folders)
        {
            var provider = new YooAssetUIAuthoringAddressProvider();
            UIAuthoringAddressCatalog catalog = provider.Query();
            bool addressCheckPerformed = catalog.IsAvailable;

            string[] searchFolders = folders == null || folders.Count == 0
                ? new[] { "Assets" }
                : CopyFolders(folders);

            string[] guids = AssetDatabase.FindAssets("t:Prefab", searchFolders);
            var results = new List<UIAuthoringScanResult>(guids.Length);

            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                if (prefab.GetComponent<UIDeclarativePanel>() == null
                    && prefab.GetComponent<UIDeclarativeDialogPanel>() == null)
                {
                    continue;
                }

                UIPanelDefinition definition = ReadDefinition(prefab);
                List<UIAuthoringIssue> issues = UIAuthoringValidator.Validate(
                    prefab,
                    definition,
                    catalog,
                    addressCheckPerformed,
                    assetPath => AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));

                results.Add(new UIAuthoringScanResult(path, issues));
            }

            return results;
        }

        private static UIPanelDefinition ReadDefinition(GameObject prefab)
        {
            var serialized = new SerializedObject(prefab);
            SerializedProperty property = serialized.FindProperty("_definition");
            return property != null ? property.objectReferenceValue as UIPanelDefinition : null;
        }

        private static string[] CopyFolders(IReadOnlyList<string> folders)
        {
            var result = new string[folders.Count];
            for (int i = 0; i < folders.Count; i++)
                result[i] = folders[i];
            return result;
        }
    }
}
