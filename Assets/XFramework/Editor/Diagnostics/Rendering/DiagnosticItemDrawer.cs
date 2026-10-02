using UnityEditor;
using UnityEngine;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 逐项渲染原语：报告条目（小节 / 文本 / 键值 / 表格 / 提示）各自的画法。
    /// <para><b>无状态纯绘制</b>：每个方法都可以单独调用——第三方实现
    /// <see cref="IDiagnosticPanelView"/> 自绘时，用它们复用标准件即可与默认视图观感一致。</para>
    /// <para><b>IMGUI 的硬约束</b>：Layout 与 Repaint 两趟必须产出相同布局，因此这里的一切宽度都由
    /// 内容确定性地推导（表格列宽走 <see cref="DiagnosticTableLayout"/>），不允许依赖事件类型或随机值。</para>
    /// </summary>
    public static class DiagnosticItemDrawer
    {
        #region Constants

        /// <summary>表格单列下限（像素）：再窄就只剩省略号，不如让整表横向溢出。</summary>
        private const float TableMinColumnWidth = 40f;

        /// <summary>表格列间距（像素）。</summary>
        private const float TableColumnSpacing = 8f;

        #endregion

        #region Public API

        /// <summary>按 <see cref="DiagnosticItem.Kind"/> 分发到对应的单项原语。</summary>
        /// <param name="item">要画的条目。</param>
        public static void Draw(in DiagnosticItem item)
        {
            switch (item.Kind)
            {
                case DiagnosticItemKind.Section:
                    DrawSection(item.Text);
                    break;

                case DiagnosticItemKind.Text:
                    DrawText(item.Text);
                    break;

                case DiagnosticItemKind.KeyValue:
                    DrawKeyValue(item.Text, item.Value);
                    break;

                case DiagnosticItemKind.Table:
                    DrawTable(item.Table);
                    break;

                case DiagnosticItemKind.Notice:
                    DrawNotice(item.Level, item.Text);
                    break;
            }
        }

        /// <summary>小节标题：加粗单行。</summary>
        /// <param name="title">标题。</param>
        public static void DrawSection(string title)
        {
            EditorGUILayout.LabelField(title ?? string.Empty, EditorStyles.boldLabel);
        }

        /// <summary>说明文本：自动换行。</summary>
        /// <param name="text">正文，可含换行。</param>
        public static void DrawText(string text)
        {
            EditorGUILayout.LabelField(text ?? string.Empty, EditorStyles.wordWrappedLabel);
        }

        /// <summary>键值行：左列是名（按编辑器标签宽度），右列是值（自动换行）。</summary>
        /// <param name="label">名。</param>
        /// <param name="value">值。</param>
        public static void DrawKeyValue(string label, string value)
        {
            EditorGUILayout.LabelField(label ?? string.Empty, value ?? string.Empty, EditorStyles.wordWrappedLabel);
        }

        /// <summary>提示：按级别用 <see cref="MessageType"/> 着色。</summary>
        /// <param name="level">级别。</param>
        /// <param name="text">正文。</param>
        public static void DrawNotice(DiagnosticLevel level, string text)
        {
            MessageType type;
            switch (level)
            {
                case DiagnosticLevel.Error:
                    type = MessageType.Error;
                    break;

                case DiagnosticLevel.Warning:
                    type = MessageType.Warning;
                    break;

                default:
                    type = MessageType.Info;
                    break;
            }

            EditorGUILayout.HelpBox(text ?? string.Empty, type);
        }

        /// <summary>
        /// 表格：可选标题 + 表头 + 细分隔线 + 数据行。
        /// <para>列宽取「各列内容的最大测量宽度」，再按当前视图宽度收缩
        /// （策略见 <see cref="DiagnosticTableLayout"/>）。</para>
        /// </summary>
        /// <param name="table">要画的表格；null 或零列时什么都不画。</param>
        public static void DrawTable(DiagnosticTable table)
        {
            if (table == null || table.ColumnCount == 0)
                return;

            if (!string.IsNullOrEmpty(table.Title))
                EditorGUILayout.LabelField(table.Title, EditorStyles.miniBoldLabel);

            int columns = table.ColumnCount;
            float[] natural = MeasureColumns(table, columns);
            float available = EditorGUIUtility.currentViewWidth
                              - EditorGUIUtility.standardVerticalSpacing * 2f
                              - TableColumnSpacing * (columns - 1);
            float[] widths = DiagnosticTableLayout.Allocate(natural, available, TableMinColumnWidth);

            DrawTableRow(table, columns, widths, -1);
            DrawSeparator();

            for (int r = 0; r < table.RowCount; r++)
                DrawTableRow(table, columns, widths, r);
        }

        #endregion

        #region Private

        private static float[] MeasureColumns(DiagnosticTable table, int columns)
        {
            var natural = new float[columns];
            var content = new GUIContent();

            for (int c = 0; c < columns; c++)
            {
                content.text = CellText(table.Columns[c]);
                natural[c] = EditorStyles.label.CalcSize(content).x;

                for (int r = 0; r < table.RowCount; r++)
                {
                    content.text = CellText(table.GetCell(r, c));
                    float width = EditorStyles.label.CalcSize(content).x;
                    if (width > natural[c])
                        natural[c] = width;
                }
            }

            return natural;
        }

        private static void DrawTableRow(DiagnosticTable table, int columns, float[] widths, int row)
        {
            EditorGUILayout.BeginHorizontal();

            for (int c = 0; c < columns; c++)
            {
                if (c > 0)
                    GUILayout.Space(TableColumnSpacing);

                string cell = row < 0 ? table.Columns[c] : table.GetCell(row, c);
                var style = row < 0 ? EditorStyles.miniBoldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(CellText(cell), style, GUILayout.Width(widths[c]));
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSeparator()
        {
            Rect rect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(rect, EditorGUIUtility.isProSkin
                ? new Color(1f, 1f, 1f, 0.12f)
                : new Color(0f, 0f, 0f, 0.12f));
        }

        /// <summary>
        /// 格子文本的单行化：IMGUI 的行布局是按单行标签算的，含换行的格子会顶破行高。
        /// 与文本渲染（<see cref="DiagnosticReportFormatter"/>）同一口径：换行换成空格。
        /// </summary>
        private static string CellText(string cell)
        {
            if (string.IsNullOrEmpty(cell))
                return string.Empty;

            if (cell.IndexOf('\n') < 0 && cell.IndexOf('\r') < 0)
                return cell;

            return cell.Replace('\r', ' ').Replace('\n', ' ');
        }

        #endregion
    }
}
