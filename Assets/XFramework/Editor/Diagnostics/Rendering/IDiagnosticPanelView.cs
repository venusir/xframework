using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics
{
    /// <summary>
    /// 页签自绘的**可选能力接口**：页签想自己画，就实现它；诊断窗口渲染内容区前会做一次
    /// <c>is</c> 探测，命中则整块委托（含滚动），未命中走默认的 <see cref="DiagnosticReportView"/>。
    /// <para><b>这是本仓「可选能力接口 + 探测」那条规矩在渲染侧的落地</b>——它不污染
    /// <see cref="IDiagnosticPanel"/> 那份纯数据契约，第三方想要按钮、折叠、图表、树，都在这里做。</para>
    /// <para><b>契约</b>：</para>
    /// <para>① <see cref="IDiagnosticPanel.Collect(IDiagnosticReport)"/> <b>仍会被调用</b>，报告照常采集——
    /// 「复制为文本」与将来的运行时覆盖层走的还是那份数据，自绘只替换显示。</para>
    /// <para>② 绘制异常与采集异常同等隔离：窗口捕获后显示错误提示，不会打崩窗口，其他页签照常。</para>
    /// <para>③ 只在主线程调用（IMGUI 本就在主线程）。</para>
    /// <para>④ 想要标准件（小节 / 键值 / 表格 / 提示）就调 <see cref="DiagnosticItemDrawer"/> 的单项原语，
    /// 与默认视图观感一致；<b>不要</b>把整份报告再交给 <see cref="DiagnosticReportView"/>——它自带滚动，
    /// 嵌在自绘内容里会形成嵌套滚动。</para>
    /// <para><b>写在哪</b>：本接口在 Editor 程序集，因此自绘页签必须写在编辑器代码里
    /// （第三方通常放在自己的 Editor 程序集 / <c>Assembly-CSharp-Editor</c>）。</para>
    /// </summary>
    public interface IDiagnosticPanelView
    {
        /// <summary>
        /// 画该页签的内容区。由窗口在采集完成后调用，<paramref name="report"/> 是本次采集的结果。
        /// </summary>
        /// <param name="report">本次采集产出的报告（只读用途）。</param>
        void Draw(DiagnosticReport report);
    }
}
