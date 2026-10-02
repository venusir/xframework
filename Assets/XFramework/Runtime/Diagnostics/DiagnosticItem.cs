namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 报告里的一条内容（<see cref="DiagnosticReport.Items"/> 的元素）。
    /// <para><b>各字段按 <see cref="Kind"/> 取用，其余为默认值</b>：</para>
    /// <para>· <see cref="DiagnosticItemKind.Section"/> / <see cref="DiagnosticItemKind.Text"/>：
    /// 用 <see cref="Text"/>。</para>
    /// <para>· <see cref="DiagnosticItemKind.KeyValue"/>：用 <see cref="Text"/>（名）与 <see cref="Value"/>（值）。</para>
    /// <para>· <see cref="DiagnosticItemKind.Notice"/>：用 <see cref="Level"/> 与 <see cref="Text"/>。</para>
    /// <para>· <see cref="DiagnosticItemKind.Table"/>：用 <see cref="Table"/>。</para>
    /// <para>渲染方按这条规则分发即可，不必理解别的语义。</para>
    /// </summary>
    public readonly struct DiagnosticItem
    {
        /// <summary>本条的种类。</summary>
        public readonly DiagnosticItemKind Kind;

        /// <summary>标题 / 正文 / 键值行的「名」。</summary>
        public readonly string Text;

        /// <summary>键值行的「值」，其余种类为 null。</summary>
        public readonly string Value;

        /// <summary>提示级别，仅 <see cref="DiagnosticItemKind.Notice"/> 有意义。</summary>
        public readonly DiagnosticLevel Level;

        /// <summary>表格，仅 <see cref="DiagnosticItemKind.Table"/> 非 null。</summary>
        public readonly DiagnosticTable Table;

        private DiagnosticItem(DiagnosticItemKind kind, string text, string value, DiagnosticLevel level, DiagnosticTable table)
        {
            Kind = kind;
            Text = text;
            Value = value;
            Level = level;
            Table = table;
        }

        internal static DiagnosticItem CreateSection(string title)
            => new DiagnosticItem(DiagnosticItemKind.Section, title, null, DiagnosticLevel.Info, null);

        internal static DiagnosticItem CreateText(string text)
            => new DiagnosticItem(DiagnosticItemKind.Text, text, null, DiagnosticLevel.Info, null);

        internal static DiagnosticItem CreateKeyValue(string label, string value)
            => new DiagnosticItem(DiagnosticItemKind.KeyValue, label, value, DiagnosticLevel.Info, null);

        internal static DiagnosticItem CreateNotice(DiagnosticLevel level, string text)
            => new DiagnosticItem(DiagnosticItemKind.Notice, text, null, level, null);

        internal static DiagnosticItem CreateTable(DiagnosticTable table)
            => new DiagnosticItem(DiagnosticItemKind.Table, null, null, DiagnosticLevel.Info, table);
    }
}
