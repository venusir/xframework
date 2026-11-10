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
                    && prefab.GetComponent<UIDeclarativeDialogPanel>() == null
                    && prefab.GetComponent<UIDeclarativeHudItem>() == null)
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

        /// <summary>
        /// 从预制体根上的声明式组件读取定义资产。
        /// <para><b>必须对组件而不是 GameObject 建 <see cref="SerializedObject"/></b>：
        /// GameObject 的序列化面只有 m_Name / m_Component 等自身字段，找不到组件上的
        /// <c>_definition</c>——对 GameObject 调用会让定义恒为 null，校验被静默跳过。</para>
        /// </summary>
        private static UIPanelDefinition ReadDefinition(GameObject prefab)
        {
            Component component = prefab.GetComponent<UIDeclarativePanel>();
            if (component == null)
                component = prefab.GetComponent<UIDeclarativeDialogPanel>();
            if (component == null)
                component = prefab.GetComponent<UIDeclarativeHudItem>();

            if (component == null)
                return null;

            var serialized = new SerializedObject(component);
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
