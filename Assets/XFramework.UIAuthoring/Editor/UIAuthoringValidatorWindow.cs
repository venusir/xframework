using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace XFramework.XUI.Authoring.Editor
{
    /// <summary>
    /// 声明式定义校验窗口。只读，不修改预制体/资产。
    /// </summary>
    internal sealed class UIAuthoringValidatorWindow : EditorWindow
    {
        private string _root = "Assets";
        private List<UIAuthoringScanResult> _results = new List<UIAuthoringScanResult>();
        private readonly HashSet<string> _expanded = new HashSet<string>();
        private Vector2 _scroll;

        [MenuItem("Tools/XFramework/UI Authoring/Validate Declarative Panels")]
        private static void Open()
        {
            UIAuthoringValidatorWindow window = GetWindow<UIAuthoringValidatorWindow>(
                "Declarative Panel Validator");
            window.minSize = new Vector2(560f, 360f);
        }

        private void OnGUI()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            _root = EditorGUILayout.TextField(_root, GUILayout.MinWidth(200f));
            if (GUILayout.Button("Scan", EditorStyles.toolbarButton, GUILayout.Width(80f)))
                Scan();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.HelpBox(
                "只读校验声明式定义与节点。核心 UI 预制体规则请另外运行 Tools/XFramework/UI/Validate UI Prefabs。",
                MessageType.Info);

            int errors = 0;
            int warnings = 0;
            for (int i = 0; i < _results.Count; i++)
            {
                for (int j = 0; j < _results[i].Issues.Count; j++)
                {
                    UIAuthoringIssueSeverity severity = _results[i].Issues[j].Severity;
                    if (severity == UIAuthoringIssueSeverity.Error) errors++;
                    else if (severity == UIAuthoringIssueSeverity.Warning) warnings++;
                }
            }

            EditorGUILayout.LabelField(
                "候选 " + _results.Count + " 个：错误 " + errors + "、警告 " + warnings + "。",
                EditorStyles.boldLabel);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            for (int i = 0; i < _results.Count; i++)
            {
                UIAuthoringScanResult result = _results[i];
                bool expanded = _expanded.Contains(result.AssetPath);
                bool next = EditorGUILayout.Foldout(
                    expanded,
                    result.AssetPath + "（" + result.Issues.Count + "）");

                if (next != expanded)
                {
                    if (next)
                        _expanded.Add(result.AssetPath);
                    else
                        _expanded.Remove(result.AssetPath);
                }

                if (!next)
                    continue;

                EditorGUI.indentLevel++;
                if (result.Issues.Count == 0)
                {
                    EditorGUILayout.LabelField("无问题");
                }
                else
                {
                    for (int j = 0; j < result.Issues.Count; j++)
                    {
                        UIAuthoringIssue issue = result.Issues[j];
                        EditorGUILayout.LabelField(
                            issue.Severity + "  " + issue.RuleId + "：" + issue.Message);
                        if (!string.IsNullOrEmpty(issue.NodePath))
                            EditorGUILayout.LabelField("    节点：" + issue.NodePath);
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            _results = UIAuthoringScanner.Scan(new[] { _root });
            _expanded.Clear();
        }
    }
}
