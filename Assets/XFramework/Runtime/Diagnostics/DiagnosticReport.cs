using System;
using System.Collections.Generic;

namespace XFramework.XDiagnostics
{
    /// <summary>
    /// <see cref="IDiagnosticReport"/> 的默认实现：一个可清空、可复用的条目列表。
    /// <para><b>给渲染方</b>：持有一个实例，每次刷新做 <see cref="Clear"/> +
    /// <see cref="IDiagnosticPanel.Collect(IDiagnosticReport)"/> 重填，就不必每次重新分配。
    /// 渲染读 <see cref="Items"/>。</para>
    /// <para>非线程安全；只允许在采集线程（主线程）使用，且只在采集期间与采集后立即读取。</para>
    /// </summary>
    public sealed class DiagnosticReport : IDiagnosticReport
    {
        #region Private Fields

        private readonly List<DiagnosticItem> _items = new List<DiagnosticItem>();

        #endregion

        #region Public Properties

        /// <summary>按写入顺序排列的条目（实时视图，不是快照）。</summary>
        public IReadOnlyList<DiagnosticItem> Items => _items;

        /// <summary>是否没有任何条目。</summary>
        public bool IsEmpty => _items.Count == 0;

        #endregion

        #region Public API

        /// <summary>清空全部条目，供渲染方复用本实例。</summary>
        public void Clear()
        {
            _items.Clear();
        }

        /// <inheritdoc />
        public void Section(string title)
        {
            _items.Add(DiagnosticItem.CreateSection(title ?? string.Empty));
        }

        /// <inheritdoc />
        public void Text(string text)
        {
            _items.Add(DiagnosticItem.CreateText(text ?? string.Empty));
        }

        /// <inheritdoc />
        public void KeyValue(string label, string value)
        {
            _items.Add(DiagnosticItem.CreateKeyValue(label ?? string.Empty, value ?? string.Empty));
        }

        /// <inheritdoc />
        public void Notice(DiagnosticLevel level, string text)
        {
            _items.Add(DiagnosticItem.CreateNotice(level, text ?? string.Empty));
        }

        /// <inheritdoc />
        public DiagnosticTable BeginTable(string title, params string[] columns)
        {
            if (columns == null)
                throw new ArgumentNullException(nameof(columns));
            if (columns.Length == 0)
                throw new ArgumentException("表格至少需要一列。", nameof(columns));

            var table = new DiagnosticTable(title ?? string.Empty, columns);
            _items.Add(DiagnosticItem.CreateTable(table));
            return table;
        }

        #endregion
    }
}
