using UnityEditor;
using UnityEngine;
using XFramework.XUI;

namespace XFramework.Editor
{
    /// <summary>
    /// UI 子系统状态窗口：实时显示活动面板、显示栈、遮罩持有数与在途打开。
    /// <para>排查「面板漏关了」「遮罩怎么还亮着」「哪个面板卡在打开中」这类只能靠翻运行时
    /// 状态定位的问题。数据来自 <see cref="UIManager.DumpState"/>，故与代码里读到的是同一份真相。</para>
    /// <para>打开方式：菜单 <c>Tools/XFramework/UI State</c>。</para>
    /// </summary>
    public sealed class UIStateWindow : EditorWindow
    {
        #region Constants

        /// <summary>刷新间隔（秒）。0.5 秒足够看清状态变化，又不至于每帧重绘。</summary>
        private const double RefreshInterval = 0.5;

        #endregion

        #region Fields

        private string _dump = "(UIManager 尚未初始化)";
        private Vector2 _scroll;
        private double _nextRefresh;

        #endregion

        #region Menu

        [MenuItem("Tools/XFramework/UI State")]
        private static void Open()
        {
            var window = GetWindow<UIStateWindow>("UI State");
            window.minSize = new Vector2(360f, 240f);
            window.Show();
        }

        #endregion

        #region Editor Lifecycle

        private void OnEnable()
        {
            _nextRefresh = 0d;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup < _nextRefresh)
                return;

            _nextRefresh = EditorApplication.timeSinceStartup + RefreshInterval;
            Repaint();
        }

        private void OnGUI()
        {
            Refresh();

            DrawHeader();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            EditorGUILayout.TextArea(_dump, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Private

        private void Refresh()
        {
            // 未初始化时 IsInitialized 为 false，不要在这里抛异常——窗口应当安静地显示「未初始化」
            if (!UIManager.IsInitialized)
            {
                _dump = "(UIManager 尚未初始化)";
                return;
            }

            _dump = UIManager.DumpState();
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            EditorGUILayout.LabelField(
                UIManager.IsInitialized ? UIManager.GetState().ToString() : "not initialized",
                EditorStyles.miniLabel);

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField($"LOD drivers: {UIManager.LodDriverCount}", EditorStyles.miniLabel);

            if (GUILayout.Button("Copy", EditorStyles.toolbarButton, GUILayout.Width(48f)))
                EditorGUIUtility.systemCopyBuffer = _dump;

            EditorGUILayout.EndHorizontal();
        }

        #endregion
    }
}
