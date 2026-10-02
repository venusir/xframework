namespace XFramework.XDiagnostics
{
    /// <summary>
    /// <see cref="IDiagnosticPanel.Order"/> 的建议取值带。
    /// <para><b>是建议，不是约束</b>（与 <c>UILayers</c> 的层号同一体例）：登记表按 Order 升序排，
    /// 同值按注册先后；用任何 int 都合法，这些常量只是让「框架的页签在前、第三方的在后」不必靠猜。</para>
    /// </summary>
    public static class DiagnosticOrders
    {
        /// <summary>框架自带页签的起点（0 起）。框架页签占用 <c>[0, 1000)</c>。</summary>
        public const int Framework = 0;

        /// <summary>第三方页签的默认起点。远远靠后，保证排在全部框架页签之后。</summary>
        public const int User = 1000;
    }
}
