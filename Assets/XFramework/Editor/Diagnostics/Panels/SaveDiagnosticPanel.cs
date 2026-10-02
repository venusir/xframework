using XFramework.XDiagnostics;
using XFramework.XSave;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Save 页签：当前玩家、存档版本与忙碌状态。
    /// <para><b>槽位列表不在本轮</b>：<c>GetSlotMetasAsync</c> 是异步磁盘 IO（逐文件读取），
    /// 不适合按刷新节拍轮询；要做得配「手动按钮 + 独立异步入口」，另议。</para>
    /// </summary>
    internal sealed class SaveDiagnosticPanel : IDiagnosticPanel
    {
        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Save";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 110;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", SaveManager.IsInitialized ? "是" : "否");

            if (!SaveManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "SaveManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            report.KeyValue("当前玩家", SaveManager.CurrentPlayerId ?? "(未设置)");
            report.KeyValue("当前版本", SaveManager.CurrentVersion.ToString());
            report.KeyValue("忙碌中", SaveManager.IsBusy ? "是" : "否");

            if (SaveManager.IsBusy)
                report.Notice(DiagnosticLevel.Info, "有存档操作在进行中（IsBusy）——此刻发起新的读/写会被拒绝或排队。");
        }

        #endregion
    }
}
