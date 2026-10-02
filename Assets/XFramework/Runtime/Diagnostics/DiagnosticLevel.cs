namespace XFramework.XDiagnostics
{
    /// <summary>
    /// <see cref="IDiagnosticReport.Notice(DiagnosticLevel, string)"/> 的级别：渲染方据此着色或加前缀，
    /// 文本渲染下表现为 <c>[INFO]</c> / <c>[WARN]</c> / <c>[ERROR]</c>。
    /// <para>它只影响显示，不影响任何行为——诊断报告里的「错误」不等于框架出错。</para>
    /// </summary>
    public enum DiagnosticLevel
    {
        /// <summary>普通信息。</summary>
        Info = 0,

        /// <summary>值得注意：数值异常、降级路径、配置缺失等。</summary>
        Warning = 1,

        /// <summary>需要处理：不一致状态、被吞掉的失败、本该有却没有的东西。</summary>
        Error = 2,
    }
}
