using System;
using System.Text;

namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 把一份报告渲染成纯文本——诊断窗口的「复制」按钮与一切「把状态贴出去」的场景都走这里。
    /// <para><b>格式</b>（逐条对应 <see cref="DiagnosticItemKind"/>）：</para>
    /// <para>· 小节：<c>== 标题 ==</c>；· 文本：原样；· 键值：<c>名: 值</c>；
    /// · 提示：<c>[INFO|WARN|ERROR] 正文</c>。</para>
    /// <para>· 表格：标题行 <c>--- 标题 ---</c>（标题为空则略过）+ 表头 + <c>-+-</c> 分隔线 + 各行，
    /// 列按字符数右补空格对齐（末列不补，避免行尾空白）；格子里的换行会被替换成空格以保持对齐。</para>
    /// <para><b>纯函数</b>：只读入参，不依赖任何框架状态，可在任意线程调用。空报告输出空串。
    /// 多次调用请复用 <see cref="ToText(DiagnosticReport, StringBuilder)"/> 重载，避免每次分配。</para>
    /// </summary>
    public static class DiagnosticReportFormatter
    {
        #region Constants

        /// <summary>统一用 '\n' 而不是 Environment.NewLine：输出要跨平台可读、可精确断言。</summary>
        private const char NewLine = '\n';

        #endregion

        #region Public API

        /// <summary>渲染成一段文本。空报告返回空串（不带换行）。</summary>
        /// <param name="report">要渲染的报告。</param>
        /// <returns>纯文本。</returns>
        /// <exception cref="ArgumentNullException"><paramref name="report"/> 为 null。</exception>
        public static string ToText(DiagnosticReport report)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));

            var builder = new StringBuilder();
            ToText(report, builder);
            return builder.ToString();
        }

        /// <summary>
        /// 渲染进调用方提供的 <see cref="StringBuilder"/>（追加，不先清空）。
        /// </summary>
        /// <param name="report">要渲染的报告。</param>
        /// <param name="builder">目标缓冲区。</param>
        /// <exception cref="ArgumentNullException">任一参数为 null。</exception>
        public static void ToText(DiagnosticReport report, StringBuilder builder)
        {
            if (report == null)
                throw new ArgumentNullException(nameof(report));
            if (builder == null)
                throw new ArgumentNullException(nameof(builder));

            var items = report.Items;
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0)
                    builder.Append(NewLine);

                AppendItem(builder, items[i]);
            }
        }

        #endregion

        #region Private

        private static void AppendItem(StringBuilder builder, in DiagnosticItem item)
        {
            switch (item.Kind)
            {
                case DiagnosticItemKind.Section:
                    builder.Append("== ").Append(item.Text).Append(" ==");
                    break;

                case DiagnosticItemKind.Text:
                    builder.Append(item.Text);
                    break;

                case DiagnosticItemKind.KeyValue:
                    builder.Append(item.Text).Append(": ").Append(item.Value);
                    break;

                case DiagnosticItemKind.Notice:
                    builder.Append('[').Append(TagOf(item.Level)).Append("] ").Append(item.Text);
                    break;

                case DiagnosticItemKind.Table:
                    AppendTable(builder, item.Table);
                    break;
            }
        }

        private static string TagOf(DiagnosticLevel level)
        {
            switch (level)
            {
                case DiagnosticLevel.Warning:
                    return "WARN";
                case DiagnosticLevel.Error:
                    return "ERROR";
                default:
                    return "INFO";
            }
        }

        private static void AppendTable(StringBuilder builder, DiagnosticTable table)
        {
            if (table == null)
                return;

            int columns = table.ColumnCount;
            if (columns <= 0)
                return;

            if (!string.IsNullOrEmpty(table.Title))
                builder.Append("--- ").Append(table.Title).Append(" ---").Append(NewLine);

            // 列宽 = 表头与本列所有格子的最大字符数（按字符数而非显示宽度——CJK 的等宽对齐
            // 需要东亚宽度表，本仓不做：诊断表格以类型名与数字为主，误差只出现在中文表头）。
            var widths = new int[columns];
            for (int c = 0; c < columns; c++)
                widths[c] = CellWidth(table.Columns[c]);

            var cells = new string[columns];
            for (int r = 0; r < table.RowCount; r++)
            {
                for (int c = 0; c < columns; c++)
                {
                    int width = CellWidth(table.GetCell(r, c));
                    if (width > widths[c])
                        widths[c] = width;
                }
            }

            for (int c = 0; c < columns; c++)
                cells[c] = table.Columns[c];
            AppendRow(builder, cells, widths, columns);

            builder.Append(NewLine);
            for (int c = 0; c < columns; c++)
            {
                if (c > 0)
                    builder.Append("-+-");
                builder.Append('-', widths[c]);
            }

            for (int r = 0; r < table.RowCount; r++)
            {
                builder.Append(NewLine);
                for (int c = 0; c < columns; c++)
                    cells[c] = table.GetCell(r, c);
                AppendRow(builder, cells, widths, columns);
            }
        }

        private static void AppendRow(StringBuilder builder, string[] cells, int[] widths, int columns)
        {
            // 行尾空白会污染复制出去的内容：从最后一个非空格子之后就不再输出分隔与补白。
            // 列是左对齐的，截掉尾部空列不影响其余列的对位。
            int last = columns - 1;
            while (last > 0 && string.IsNullOrEmpty(SingleLine(cells[last])))
                last--;

            for (int c = 0; c <= last; c++)
            {
                if (c > 0)
                    builder.Append(" | ");

                string cell = SingleLine(cells[c]);
                builder.Append(cell);

                // 末列不补空格（同上：行尾空白）。
                if (c < last)
                    builder.Append(' ', widths[c] - cell.Length);
            }
        }

        private static int CellWidth(string cell)
        {
            return SingleLine(cell).Length;
        }

        private static string SingleLine(string cell)
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
