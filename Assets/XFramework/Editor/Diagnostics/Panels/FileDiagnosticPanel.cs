using XFramework.XDiagnostics;
using XFramework.XFileManager;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// File 页签：初始化状态、加密提供者与根目录。
    /// <para>回答「文件到底落在哪」「加密有没有生效」——路径由 <c>FileManager.GetPhysicalPath</c>
    /// 给出，是排查「写不进去」的第一站。</para>
    /// </summary>
    internal sealed class FileDiagnosticPanel : IDiagnosticPanel
    {
        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "File";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 120;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", FileManager.IsInitialized ? "是" : "否");

            if (!FileManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info,
                    "FileManager 尚未初始化（它是懒初始化：第一次使用时自动就绪）。");
                return;
            }

            var crypto = FileManager.GetCryptoProvider();
            report.KeyValue("加密提供者", crypto != null ? crypto.GetType().Name : "(未启用)");
        }

        #endregion
    }
}
