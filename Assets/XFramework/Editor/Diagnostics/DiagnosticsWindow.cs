using System;
using UnityEditor;
using UnityEngine;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 框架诊断窗口：把 <see cref="DiagnosticsManager"/> 里登记的页签列出来，采集并显示它们的报告。
    /// <para>窗口本身只做三件事——生命周期、转发 <see cref="DiagnosticsWindowModel"/>、调用 GUILayout；
    /// 判断对错的逻辑都在那个可测的核心里。</para>
    /// <para>数据是**拉**出来的：每次刷新现调页签的 <see cref="IDiagnosticPanel.Collect(IDiagnosticReport)"/>，
    /// 因此窗口显示的与代码里读到的是同一份真相，不存在第二本账。</para>
    /// <para>打开方式：菜单 <c>Tools/XFramework/Diagnostics</c>。</para>
    /// </summary>
    public sealed class DiagnosticsWindow : EditorWindow
    {
        #region Constants

        /// <summary>左栏页签列表宽度（像素）。</summary>
        private const float TabListWidth = 170f;

        #endregion

        #region Fields

        private readonly DiagnosticsWindowModel _model = new DiagnosticsWindowModel();
        private readonly DiagnosticReportView _view = new DiagnosticReportView();

        private Vector2 _tabScroll;
        private string _filter = string.Empty;

        #endregion

        #region Menu

        [MenuItem("Tools/XFramework/Diagnostics")]
        private static void Open()
        {
            var window = GetWindow<DiagnosticsWindow>("Diagnostics");
            window.minSize = new Vector2(520f, 320f);
            window.Show();
        }

        #endregion

        #region Editor Lifecycle

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            _model.Tick(EditorApplication.timeSinceStartup, manual: true);
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // 采集只在这里发生（旧 UI State 窗口在 OnGUI 里取数，一帧 Layout/Repaint 会跑多次）
            if (_model.Tick(EditorApplication.timeSinceStartup, manual: false))
                Repaint();
        }

        #endregion

        #region GUI

        private void OnGUI()
        {
            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawTabList();
            DrawContent();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            _model.AutoRefresh = GUILayout.Toggle(_model.AutoRefresh, "自动刷新", EditorStyles.toolbarButton,
                GUILayout.Width(72f));

            if (GUILayout.Button("刷新", EditorStyles.toolbarButton, GUILayout.Width(48f)))
            {
                _model.Tick(EditorApplication.timeSinceStartup, manual: true);
                Repaint();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField($"页签 {_model.PanelCount}", EditorStyles.miniLabel, GUILayout.Width(72f));

            if (GUILayout.Button("复制", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                EditorGUIUtility.systemCopyBuffer = _model.BuildCopyText();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTabList()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(TabListWidth));

            _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);

            _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll);

            if (!_model.HasPanels)
                EditorGUILayout.LabelField("(暂无页签)", EditorStyles.miniLabel);

            int shown = 0;
            for (int i = 0; i < _model.PanelCount; i++)
            {
                string title = _model.GetTitle(i);
                if (!MatchesFilter(title))
                    continue;

                shown++;
                bool selected = i == _model.SelectedIndex;
                bool nowSelected = GUILayout.Toggle(selected, title, EditorStyles.miniButton);
                if (nowSelected != selected)
                    _model.Select(i);
            }

            if (_model.HasPanels && shown == 0)
                EditorGUILayout.LabelField("(无匹配)", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawContent()
        {
            EditorGUILayout.BeginVertical();

            if (!_model.HasPanels)
            {
                EditorGUILayout.HelpBox(
                    "尚未登记任何诊断页签。\n" +
                    "· 框架与第三方页签都经 DiagnosticsManager.Register 显式登记；\n" +
                    "· 多数模块的页签要进入播放模式（模块初始化）后才会出现。",
                    MessageType.Info);
            }
            else if (_model.Failed || !_model.TryGetCustomView(out var customView))
            {
                // 采集失败时绕开自绘：报告里放的是错误说明，用统一视图显示
                _view.Draw(_model.Report);
            }
            else
            {
                try
                {
                    customView.Draw(_model.Report);
                }
                catch (Exception ex)
                {
                    // 自绘异常与采集异常同等隔离。注意 IMGUI 的界限：异常发生在 Begin/End 之间时，
                    // 这一趟的布局可能已经不平衡——本 catch 只保证窗口不崩，不保证本帧画得对。
                    EditorGUILayout.HelpBox(
                        $"页签 '{_model.GetTitle(_model.SelectedIndex)}' 自绘时抛出异常：\n{ex}", MessageType.Error);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private bool MatchesFilter(string title)
        {
            return string.IsNullOrEmpty(_filter)
                   || title.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        #endregion
    }
}
