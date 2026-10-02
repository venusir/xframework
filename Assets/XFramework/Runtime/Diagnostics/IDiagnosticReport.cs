namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 报告采集器（写侧）：<see cref="IDiagnosticPanel.Collect(IDiagnosticReport)"/> 拿到的就是这个。
    /// <para>它只提供「往里加内容」的方法，实现拿不到已经写好的东西——一根筋往下写即可。</para>
    /// <para><b>顺序即显示顺序</b>：条目按调用顺序排列；<see cref="Section(string)"/> 之后的内容
    /// 显示在该小节之下，直到下一个 <see cref="Section(string)"/>。</para>
    /// <para>所有方法都只允许在采集线程（主线程）调用，且只应在
    /// <see cref="IDiagnosticPanel.Collect(IDiagnosticReport)"/> 执行期间调用。</para>
    /// </summary>
    public interface IDiagnosticReport
    {
        /// <summary>开始一个新小节。后续条目显示在该小节之下。</summary>
        /// <param name="title">小节标题；null 按空串处理。</param>
        void Section(string title);

        /// <summary>一段说明文本，可含换行。null 按空串处理。</summary>
        /// <param name="text">正文。</param>
        void Text(string text);

        /// <summary>一行键值（属性）。</summary>
        /// <param name="label">名称；null 按空串处理。</param>
        /// <param name="value">值；null 按空串处理。</param>
        void KeyValue(string label, string value);

        /// <summary>一条带级别的提示，渲染方据此着色或加前缀。</summary>
        /// <param name="level">级别。</param>
        /// <param name="text">正文；null 按空串处理。</param>
        void Notice(DiagnosticLevel level, string text);

        /// <summary>
        /// 开始一张表格并返回填写句柄：先声明列，再对返回的 <see cref="DiagnosticTable"/> 反复
        /// <see cref="DiagnosticTable.AddRow(string[])"/>。
        /// <para>返回的表格与报告共享同一个实例——<b>请在本次采集结束前填完</b>，采集返回后再改动它
        /// 属于未定义行为。</para>
        /// </summary>
        /// <param name="title">表格标题，可为空串（渲染方会略过标题行）。</param>
        /// <param name="columns">列标题，至少一列。</param>
        /// <returns>要填写的表格。</returns>
        /// <exception cref="System.ArgumentNullException"><paramref name="columns"/> 为 null。</exception>
        /// <exception cref="System.ArgumentException"><paramref name="columns"/> 为空数组（没有列的表格无法渲染）。</exception>
        DiagnosticTable BeginTable(string title, params string[] columns);
    }
}
