using System.Collections.Generic;
using XFramework.XDiagnostics;
using XFramework.XSettings;

namespace XFramework.Editor.Diagnostics.Panels
{
    /// <summary>
    /// Settings 页签：已注册的设置类型与未提交改动。
    /// <para>回答「注册了哪些设置类型、谁还挂着改动没提交」——此前只能按已知类型问
    /// <c>IsRegistered&lt;T&gt;()</c> / <c>IsDirty&lt;T&gt;()</c>。</para>
    /// <para><c>未提交改动</c> 的口径与 <c>ISettingsManager&lt;T&gt;.IsDirty</c> 一致：
    /// 有改动待提交，<b>不等于已落盘</b>。</para>
    /// </summary>
    internal sealed class SettingsDiagnosticPanel : IDiagnosticPanel
    {
        #region Private Fields

        /// <summary>复用缓冲区：本页签是单例，一次分配长期使用。</summary>
        private readonly List<SettingsTypeInfo> _types = new List<SettingsTypeInfo>();

        #endregion

        #region IDiagnosticPanel

        /// <inheritdoc />
        public string Title => "Settings";

        /// <inheritdoc />
        public int Order => DiagnosticOrders.Framework + 20;

        /// <inheritdoc />
        public void Collect(IDiagnosticReport report)
        {
            report.Section("概览");
            report.KeyValue("已初始化", SettingsManager.IsInitialized ? "是" : "否");
            report.KeyValue("已注册类型数", SettingsManager.RegisteredTypeCount.ToString());

            if (!SettingsManager.IsInitialized)
            {
                report.Notice(DiagnosticLevel.Info, "SettingsManager 尚未初始化：进入播放模式后这里才有数据。");
                return;
            }

            SettingsManager.CopyRegisteredTypes(_types);

            report.Section("已注册类型");
            if (_types.Count == 0)
            {
                report.Text("(还没有注册任何设置类型)");
                return;
            }

            var table = report.BeginTable(null, "类型", "未提交改动");
            for (int i = 0; i < _types.Count; i++)
                table.AddRow(_types[i].SettingsType.Name, _types[i].IsDirty ? "有" : "-");
        }

        #endregion
    }
}
