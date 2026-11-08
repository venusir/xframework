using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace XFramework.Editor.UI
{
    /// <summary>
    /// UI 预制体校验窗口。
    /// <para>只负责选择范围、触发扫描与渲染报告；判断逻辑全部在 <see cref="UIPrefabScanner"/> 与
    /// <see cref="UIPrefabValidator"/>，IMGUI 层不参与规则判定。</para>
    /// <para>打开方式：菜单 <c>Tools/XFramework/UI/Validate UI Prefabs</c>。扫描只读，不修改预制体。</para>
    /// </summary>
    internal sealed class UIPrefabValidatorWindow : EditorWindow
    {
        #region Constants

        private const string DefaultScope = "Assets";

        #endregion

        #region Fields

        private readonly List<string> _folders = new List<string>();
        private readonly HashSet<string> _expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private Vector2 _scroll;
        private bool _checkAddresses = true;
        private UIPrefabValidationReport _report;

        #endregion

        #region Menu

        [MenuItem("Tools/XFramework/UI/Validate UI Prefabs")]
        private static void Open()
        {
            var window = GetWindow<UIPrefabValidatorWindow>("UI Prefab Validator");
            window.minSize = new Vector2(560f, 360f);
            window.Show();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawToolbar();
            DrawNotices();
            DrawResults();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("使用当前选中文件夹", GUILayout.Width(150f)))
                SetFolders(GetSelectedFolders());

            if (GUILayout.Button("重置为 Assets", GUILayout.Width(110f)))
                SetFolders(null);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("扫描", GUILayout.Width(80f)))
                Scan();

            EditorGUILayout.EndHorizontal();

            var scope = _folders.Count == 0
                ? $"{DefaultScope}（默认）"
                : string.Join(", ", _folders);
            EditorGUILayout.LabelField("扫描范围", scope);

            _checkAddresses = EditorGUILayout.ToggleLeft(
                "校验 YooAsset 地址（需要工程内唯一的采集配置）",
                _checkAddresses);

            GUILayout.Space(4f);
        }

        private void DrawNotices()
        {
            if (_report == null)
                return;

            for (int i = 0; i < _report.Notices.Count; i++)
            {
                var notice = _report.Notices[i];
                EditorGUILayout.HelpBox(notice.Message, ToMessageType(notice.Severity));
            }

            if (_report.Notices.Count > 0)
                GUILayout.Space(4f);
        }

        private void DrawResults()
        {
            if (_report == null)
            {
                EditorGUILayout.HelpBox("点击「扫描」开始。扫描只读，不会修改预制体。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(_report.BuildSummary(), EditorStyles.boldLabel);

            if (_report.Entries.Count == 0)
            {
                EditorGUILayout.HelpBox("扫描范围内没有 UI 候选预制体。", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            for (int i = 0; i < _report.Entries.Count; i++)
                DrawEntry(_report.Entries[i]);

            EditorGUILayout.EndScrollView();
        }

        private void DrawEntry(UIPrefabValidationEntry entry)
        {
            var expanded = _expanded.Contains(entry.AssetPath);
            var title = $"{entry.AssetPath}    [{Describe(entry)}]";
            var next = EditorGUILayout.Foldout(expanded, title, true);

            if (next != expanded)
            {
                if (next)
                    _expanded.Add(entry.AssetPath);
                else
                    _expanded.Remove(entry.AssetPath);
            }

            if (!next)
                return;

            EditorGUI.indentLevel++;

            if (GUILayout.Button("选中预制体", GUILayout.Width(90f)))
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>(entry.AssetPath);
                Selection.activeObject = asset;
                if (asset != null)
                    EditorGUIUtility.PingObject(asset);
            }

            for (int i = 0; i < entry.Addresses.Count; i++)
            {
                var address = entry.Addresses[i];
                var text = address.HasAddress
                    ? $"地址：{address.PackageName} / {address.Address}"
                    : $"地址：{address.PackageName} / （无可寻址地址，按资源路径加载）";
                EditorGUILayout.LabelField(text, EditorStyles.miniLabel);
            }

            if (entry.Issues.Count == 0)
            {
                EditorGUILayout.LabelField("无问题。", EditorStyles.miniLabel);
            }
            else
            {
                for (int i = 0; i < entry.Issues.Count; i++)
                    DrawIssue(entry.Issues[i]);
            }

            EditorGUI.indentLevel--;
            GUILayout.Space(4f);
        }

        private static void DrawIssue(UIPrefabIssue issue)
        {
            EditorGUILayout.LabelField(
                $"{SeverityLabel(issue.Severity)} {issue.RuleId}",
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(issue.Message, EditorStyles.wordWrappedLabel);

            if (!string.IsNullOrEmpty(issue.NodePath))
                EditorGUILayout.LabelField($"节点：{issue.NodePath}", EditorStyles.miniLabel);
        }

        private void Scan()
        {
            try
            {
                var provider = new YooAssetUIPrefabAddressProvider();
                _report = UIPrefabScanner.Scan(_folders, _checkAddresses, provider, OnProgress);
                _expanded.Clear();
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            Repaint();
        }

        private static bool OnProgress(float progress, string description)
        {
            return !EditorUtility.DisplayCancelableProgressBar(
                "UI Prefab Validator",
                description,
                progress);
        }

        #endregion

        #region Helpers

        private void SetFolders(IReadOnlyList<string> folders)
        {
            _folders.Clear();
            if (folders != null)
                _folders.AddRange(folders);

            // 范围变了就丢掉旧报告，避免报告与扫描范围对不上。
            _report = null;
            _expanded.Clear();
        }

        private static List<string> GetSelectedFolders()
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var guids = Selection.assetGUIDs;

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (string.IsNullOrEmpty(path))
                    continue;

                if (AssetDatabase.IsValidFolder(path))
                {
                    if (seen.Add(path))
                        result.Add(path);
                    continue;
                }

                var directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory))
                    continue;

                directory = directory.Replace('\\', '/');
                if (seen.Add(directory))
                    result.Add(directory);
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }

        private static string Describe(UIPrefabValidationEntry entry)
        {
            if (!entry.HasIssues)
                return "无问题";

            return $"错误 {entry.Count(UIPrefabIssueSeverity.Error)}，"
                + $"警告 {entry.Count(UIPrefabIssueSeverity.Warning)}，"
                + $"提示 {entry.Count(UIPrefabIssueSeverity.Info)}";
        }

        private static string SeverityLabel(UIPrefabIssueSeverity severity)
        {
            switch (severity)
            {
                case UIPrefabIssueSeverity.Error:
                    return "错误";
                case UIPrefabIssueSeverity.Warning:
                    return "警告";
                default:
                    return "提示";
            }
        }

        private static MessageType ToMessageType(UIPrefabIssueSeverity severity)
        {
            switch (severity)
            {
                case UIPrefabIssueSeverity.Error:
                    return MessageType.Error;
                case UIPrefabIssueSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
            }
        }

        #endregion
    }
}
