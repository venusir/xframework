using System.Collections.Generic;
using XFramework.XData;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Data 页签：已注册数据块与脏块清单。
    /// <para>回答「哪些块还没存」——存档前想确认「没有遗漏的改动」时看它。</para>
    /// </summary>
    internal sealed class DataDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<IDataBlock> _blocks = new List<IDataBlock>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Data";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 90;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", DataManager.IsInitialized ? "是" : "否");

            if (!DataManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "DataManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            report.KeyValue("有未提交改动", DataManager.HasDirtyBlocks ? "是" : "否");

            _blocks.Clear();
            DataManager.ForEachBlock(_blocks.Add);

            List<IDataBlock> dirty = DataManager.GetDirtyBlocks();

            report.Section("已注册数据块");
            if (_blocks.Count == 0)
            {
                report.Text("(没有已注册的数据块)");
                return;
            }

            var table = report.BeginTable(null, "块名", "脏");
            for (int i = 0; i < _blocks.Count; i++)
                table.AddRow(_blocks[i].BlockName, dirty.Contains(_blocks[i]) ? "是" : "-");
        }

        #endregion
    }
}
