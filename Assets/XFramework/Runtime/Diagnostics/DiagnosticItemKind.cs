namespace XFramework.XDiagnostics
{
    /// <summary>
    /// <see cref="DiagnosticItem"/> 的种类，决定渲染方怎么画它。
    /// <para>取值即 <see cref="IDiagnosticReport"/> 上五个写入方法的对应关系，二者一一对应。</para>
    /// </summary>
    public enum DiagnosticItemKind
    {
        /// <summary>小节标题（<see cref="IDiagnosticReport.Section(string)"/>）。</summary>
        Section = 0,

        /// <summary>说明文本（<see cref="IDiagnosticReport.Text(string)"/>）。</summary>
        Text = 1,

        /// <summary>键值行（<see cref="IDiagnosticReport.KeyValue(string, string)"/>）。</summary>
        KeyValue = 2,

        /// <summary>表格（<see cref="IDiagnosticReport.BeginTable(string, string[])"/>）。</summary>
        Table = 3,

        /// <summary>带级别的提示（<see cref="IDiagnosticReport.Notice(DiagnosticLevel, string)"/>）。</summary>
        Notice = 4,
    }
}
