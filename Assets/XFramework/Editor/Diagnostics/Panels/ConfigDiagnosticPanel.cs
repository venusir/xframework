using System.Collections.Generic;
using XFramework.XConfig;
using XFramework.XDiagnostics;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Config 页签：已加载的配置表 / 全局配置清单。
    /// <para>回答「这个项目到底加载了哪些表、各自多少行、从哪个资源路径来」——此前只有按类型问的
    /// <c>IsLoaded&lt;T&gt;()</c>，不知道类型就问不出来。</para>
    /// </summary>
    internal sealed class ConfigDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<ConfigLoadedInfo> _loaded = new List<ConfigLoadedInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Config";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 20;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", ConfigManager.IsInitialized ? "是" : "否");
            report.KeyValue("在途加载任务", ConfigManager.InFlightLoadCount.ToString());

            if (!ConfigManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "ConfigManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            ConfigManager.CopyLoaded(_loaded);

            report.Section("已加载配置");
            if (_loaded.Count == 0)
            {
                report.Text("(还没有加载任何配置)");
                return;
            }

            var table = report.BeginTable(null, "类型", "种类", "条目", "资源路径");
            for (int i = 0; i < _loaded.Count; i++)
            {
                var info = _loaded[i];
                table.AddRow(
                    info.ConfigType.Name,
                    info.Kind == ConfigLoadKind.Table ? "表" : "全局",
                    info.EntryCount.ToString(),
                    info.AssetPath ?? "(直接登记)");
            }
        }

        #endregion
    }
}
