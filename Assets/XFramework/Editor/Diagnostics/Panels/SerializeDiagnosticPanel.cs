using XFramework.XDiagnostics;
using XFramework.XSerialize;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Serialize 页签：已注册格式与默认格式。
    /// <para>回答「这个项目能用哪些序列化格式、默认是哪个」。</para>
    /// </summary>
    internal sealed class SerializeDiagnosticPanel : IDiagnosticPanel
    {
        #region Constants

        /// <summary>探测格式注册情况时列出的常见格式名（框架内置两个，其余由项目注册）。</summary>
        private static readonly string[] ProbeFormats = { "json", "bson", "msgpack", "xml", "binary" };

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Serialize";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 140;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", Serializer.IsInitialized ? "是" : "否");

            if (!Serializer.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info,
                    "Serializer 尚未初始化（它通常随 Data 模块的初始化一起就绪）。");
                return;
            }

            var fallback = Serializer.Default;
            report.KeyValue("默认格式", fallback != null ? fallback.ToString() : "(无)");

            report.Section("常见格式是否已注册（TryGet）");
            var table = report.BeginTable(null, "格式", "已注册");
            for (int i = 0; i < ProbeFormats.Length; i++)
                table.AddRow(ProbeFormats[i], Serializer.TryGet(ProbeFormats[i], out _) ? "是" : "-");
        }

        #endregion
    }
}
