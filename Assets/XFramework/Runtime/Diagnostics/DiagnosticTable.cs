using System;
using System.Collections.Generic;

namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 一张诊断表格：表头固定、行只增不改。由 <see cref="IDiagnosticReport.BeginTable(string, string[])"/>
    /// 创建并交给页签填写。
    /// <para><b>没有删除行的方法</b>：诊断表格描述的是「此刻有什么」，重填靠下一次采集，不靠增量修改。</para>
    /// <para><b>不做对齐计算</b>：列宽由渲染方决定——文本渲染（<see cref="DiagnosticReportFormatter"/>）
    /// 与编辑器渲染各自算各自的，表格本身只保存内容。</para>
    /// <para>非线程安全；生命周期与所属的那次采集一致。</para>
    /// </summary>
    public sealed class DiagnosticTable
    {
        #region Private Fields

        private readonly List<string> _columns;
        private readonly List<string[]> _rows;

        #endregion

        #region Public Properties

        /// <summary>表格标题，可能为空串（渲染方会略过标题行）。</summary>
        public string Title { get; }

        /// <summary>列标题（只读视图）。</summary>
        public IReadOnlyList<string> Columns => _columns;

        /// <summary>列数。</summary>
        public int ColumnCount => _columns.Count;

        /// <summary>已添加的行数。</summary>
        public int RowCount => _rows.Count;

        #endregion

        #region Construction

        internal DiagnosticTable(string title, string[] columns)
        {
            Title = title;
            _columns = new List<string>(columns);
            _rows = new List<string[]>();
        }

        #endregion

        #region Public API

        /// <summary>
        /// 追加一行。
        /// <para><paramref name="cells"/> 会被复制一份，调用方手里的数组可以继续复用。
        /// 少于列数的行，缺的格子在 <see cref="GetCell"/> 处返回 <c>null</c>（渲染成空格子）；
        /// 多于列数的部分被忽略。</para>
        /// </summary>
        /// <param name="cells">本行各列的值；null 等价于一行全空的格子。</param>
        public void AddRow(params string[] cells)
        {
            if (cells == null || cells.Length == 0)
            {
                _rows.Add(Array.Empty<string>());
                return;
            }

            // 多于列数的部分真的被丢掉（不是"存着但渲染时忽略"）——否则 GetCell 会越过 ColumnCount
            // 读到它们，"多余被忽略"这条文档承诺就成了假的。
            int count = cells.Length < _columns.Count ? cells.Length : _columns.Count;
            var copy = new string[count];
            Array.Copy(cells, copy, count);
            _rows.Add(copy);
        }

        /// <summary>
        /// 取一个格子的值。越界（行或列超出范围）返回 <c>null</c> 而不抛异常——渲染方因此不必先做边界判断。
        /// </summary>
        /// <param name="row">行下标（0 起）。</param>
        /// <param name="column">列下标（0 起）。</param>
        /// <returns>格子内容；越界或该行未填到这一列时为 <c>null</c>。</returns>
        public string GetCell(int row, int column)
        {
            if (row < 0 || row >= _rows.Count)
                return null;

            var cells = _rows[row];
            if (column < 0 || column >= cells.Length)
                return null;

            return cells[column];
        }

        #endregion
    }
}
