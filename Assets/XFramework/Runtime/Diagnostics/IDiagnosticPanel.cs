namespace XFramework.XDiagnostics
{
    /// <summary>
    /// 诊断页签契约：一块「能自我描述」的运行时状态。
    /// <para>实现本接口，再用 <see cref="DiagnosticsManager.Register(IDiagnosticPanel)"/> 显式登记，
    /// 诊断窗口（或任何渲染方）就会在刷新时调用 <see cref="Collect(IDiagnosticReport)"/>，
    /// 把写进报告的内容显示出来。框架自己的模块页签与第三方页签走的是同一条路径，没有特例。</para>
    /// <para><b>写侧契约（实现者必读）</b>：</para>
    /// <para>① <b>只在主线程调用</b>，与渲染方同线程（框架的运行时状态本身都只有主线程可读）。</para>
    /// <para>② <b>可重复调用</b>：每次刷新都会调。实现必须无副作用——不得改框架状态、不得启停定时器
    /// 或发消息、不得在 <see cref="Collect(IDiagnosticReport)"/> 内再触发一次采集。</para>
    /// <para>③ <b>不得抛异常</b>：渲染方会兜底捕获，但抛出会让本次报告整份作废，只显示一条错误提示。</para>
    /// <para>④ <b>允许分配、允许遍历全表</b>：这是诊断路径，不适用「每帧路径零分配」那套约定；
    /// 但<b>不得被每帧调用</b>——调用频率由渲染方决定（编辑器窗口默认 0.5 秒一次）。</para>
    /// <para>⑤ <b>模块未初始化时不得抛</b>：请在报告里写一行「未初始化」之类的说明，而不是让
    /// 查询异常逃逸。</para>
    /// </summary>
    public interface IDiagnosticPanel
    {
        /// <summary>
        /// 页签标题。显示在页签列表里，也是 <see cref="DiagnosticsManager"/> 判断「标题重复」的依据。
        /// <para>null 或空白会在登记时被拒绝——没有标题就无法渲染页签，早失败比晚显示好。</para>
        /// </summary>
        string Title { get; }

        /// <summary>
        /// 展示顺序，升序；同值按注册先后（稳定）。<see cref="DiagnosticOrders"/> 给出了建议取值带。
        /// </summary>
        int Order { get; }

        /// <summary>
        /// 采集：把「此刻的状态」写进 <paramref name="report"/>。
        /// <para>契约见 <see cref="IDiagnosticPanel"/> 的接口文档（主线程、可重复调用、无副作用、不得抛）。</para>
        /// </summary>
        /// <param name="report">要写入的报告。由渲染方提供并复用；实现只写不读。</param>
        void Collect(IDiagnosticReport report);
    }
}
