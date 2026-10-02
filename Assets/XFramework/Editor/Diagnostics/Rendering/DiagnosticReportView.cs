using UnityEditor;
using UnityEngine;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 报告视图：把一份 <see cref="DiagnosticReport"/> 画出来（小节 / 文本 / 键值 / 表格 / 提示）。
    /// <para>实例持有视图状态（滚动位置）——一个使用点一个实例。</para>
    /// <para><b>自带滚动视图</b>：不要再嵌进另一个滚动视图，也不要在自绘页签里套用它
    /// （那时用 <see cref="DiagnosticItemDrawer"/> 的单项原语）。</para>
    /// <para><b>用途不止诊断窗口</b>：任何编辑器窗口都可以 <c>new DiagnosticReportView()</c> 之后
    /// 直接画一份自己填的报告，观感与诊断窗口一致。</para>
    /// </summary>
    public sealed class DiagnosticReportView
    {
        #region Private Fields

        private Vector2 _scroll;

        #endregion

        #region Public API

        /// <summary>画整份报告（含滚动）。空报告显示一行「(空报告)」。</summary>
        /// <param name="report">要画的报告；null 等价于空报告。</param>
        public void Draw(DiagnosticReport report)
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawContent(report);
            EditorGUILayout.EndScrollView();
        }

        #endregion

        #region Private

        private static void DrawContent(DiagnosticReport report)
        {
            if (report == null || report.IsEmpty)
            {
                EditorGUILayout.LabelField("(空报告)", EditorStyles.miniLabel);
                return;
            }

            var items = report.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    EditorGUILayout.Space(2f);

                DiagnosticItemDrawer.Draw(items[i]);
            }
        }

        #endregion
    }
}
